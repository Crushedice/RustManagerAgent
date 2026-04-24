using System.Text.Json;
using RustOpsAgent.Core.Contracts;
using RustOpsAgent.Infrastructure.Memory;

namespace RustOpsAgent.Domains.Integrations;

internal sealed class MaintenanceTrackerToolHandler : IToolHandler
{
    private readonly IConnectorLogSource[] _connectors;
    private readonly NeoCortexStore _memory;
    private readonly HttpClient _httpClient;

    public MaintenanceTrackerToolHandler(IConnectorLogSource[] connectors, NeoCortexStore memory, HttpClient? httpClient = null)
    {
        _connectors = connectors;
        _memory = memory;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string Name => "maintenance-tracker";

    public bool IsEligibleFor(AdminIntentRoute route) =>
        route.Intent is AdminIntentType.StatusCheck or AdminIntentType.Troubleshooting;

    public async Task<ToolExecutionResult> ExecuteAsync(AdminIntentRoute route, AgentConfig config, CancellationToken cancellationToken)
    {
        try
        {
            // Load current maintenance state
            var state = _memory.Load<MaintenanceTrackingState>("maintenance/tracking-state.json") ?? new MaintenanceTrackingState();

            // Query AutoTask for Wartungsarbeiten tickets (queue="Wartung", title starts with "Wartungsarbeiten")
            var tickets = await FetchWartungsarbeitenTicketsAsync(config, cancellationToken);
            if (!tickets.Any())
            {
                return new ToolExecutionResult(
                    true,
                    "Keine Wartungsarbeiten-Tickets gefunden.",
                    null,
                    false
                );
            }

            var progressReport = new List<string> { $"Überwache {tickets.Count} Wartungsarbeiten-Ticket(s):" };
            var changes = false;

            foreach (var ticket in tickets)
            {
                var orgId = ticket.CompanyId?.ToString() ?? ticket.CompanyName ?? "unknown";
                var ticketId = ticket.Id?.ToString() ?? "unknown";
                var ticketName = ticket.Title ?? "Wartungsarbeiten";

                // Get or create org state
                if (!state.Organizations.ContainsKey(orgId))
                {
                    state.Organizations[orgId] = new OrgMaintenanceState
                    {
                        OrganizationId = orgId,
                        OrganizationName = ticket.CompanyName ?? orgId,
                        TicketId = ticketId,
                        TicketName = ticketName,
                        DryRunMode = config.Maintenance.DryRunDefault
                    };
                    changes = true;
                }

                var orgState = state.Organizations[orgId];
                orgState.LastPolledAtUtc = DateTime.UtcNow;

                // Query devices for this org and their patch status
                var devicePatchStatus = await GetDevicePatchStatusAsync(orgId, config, cancellationToken);
                orgState.AffectedDevices = devicePatchStatus;

                // Compute completion percentage
                var total = devicePatchStatus.Count;
                var completed = devicePatchStatus.Count(d => d.Value.Status == "Completed");
                var completionPercent = total > 0 ? (completed / (double)total) : 0.0;

                progressReport.Add($"  {orgState.OrganizationName} ({orgId}): {completed}/{total} Geräte ({completionPercent:P0})");

                // Check if all devices completed
                if (completionPercent >= config.Maintenance.AutoCompleteThreshold && orgState.CompletionStatus != "Completed")
                {
                    if (orgState.DryRunMode)
                    {
                        progressReport.Add($"    [TROCKENTEST] Würde Ticket {ticketId} abschließen.");
                        orgState.CompletionStatus = "DryRun-Completed";
                    }
                    else
                    {
                        // Complete the ticket in AutoTask
                        var completed_result = await CompleteWartungsarbeitenTicketAsync(ticketId, config, cancellationToken);
                        if (completed_result)
                        {
                            progressReport.Add($"    ✓ Ticket {ticketId} abgeschlossen.");
                            orgState.CompletionStatus = "Completed";
                            orgState.CompletedAtUtc = DateTime.UtcNow;
                            changes = true;
                        }
                        else
                        {
                            progressReport.Add($"    ✗ Fehler beim Abschließen von Ticket {ticketId}.");
                        }
                    }
                }
            }

            // Save updated state
            if (changes)
            {
                state.UpdatedAtUtc = DateTime.UtcNow;
                _memory.Store("maintenance/tracking-state.json", state);
            }

            var message = string.Join("\n", progressReport);
            if (config.Maintenance.DryRunDefault)
            {
                message += "\n\n[⚠️ TROCKENTEST-MODUS AKTIV - Keine echten Änderungen werden vorgenommen]";
            }

            return new ToolExecutionResult(true, message, null, changes);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, $"Fehler beim Überwachen der Wartungsarbeiten: {ex.Message}", null, false, "maintenance-tracker-error");
        }
    }

    private async Task<List<(long? Id, string? CompanyId, string? CompanyName, string Title)>> FetchWartungsarbeitenTicketsAsync(
        AgentConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var autotaskSettings = config.Integrations.Autotask;
            if (!autotaskSettings.Enabled)
                return new();

            // Query AutoTask API for tickets in "Wartung" queue
            // The query is: tickets where queue.name = "Wartung"
            var uri = new Uri(autotaskSettings.BaseUrl.TrimEnd('/') + "/atservicesrest/v1.0/Tickets?filter=queue/name eq 'Wartung'");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyAutotaskAuthHeaders(request, autotaskSettings);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);

            var tickets = new List<(long?, string?, string?, string)>();
            if (doc.RootElement.TryGetProperty("pageDetails", out _) &&
                doc.RootElement.TryGetProperty("items", out var itemsElement))
            {
                foreach (var item in itemsElement.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : (long?)null;
                    var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                    var companyId = item.TryGetProperty("companyID", out var companyIdProp) ? companyIdProp.GetString() : null;
                    var companyName = item.TryGetProperty("companyName", out var companyNameProp) ? companyNameProp.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        tickets.Add((id, companyId, companyName, title));
                    }
                }
            }

            return tickets;
        }
        catch
        {
            return new();
        }
    }

    private async Task<Dictionary<string, DevicePatchState>> GetDevicePatchStatusAsync(string orgId, AgentConfig config, CancellationToken cancellationToken)
    {
        var deviceStatus = new Dictionary<string, DevicePatchState>();

        try
        {
            // Find ITGlue connector
            var itGlueConnector = _connectors.FirstOrDefault(c => c.GetType().Name == "ITGlueConnector");
            if (itGlueConnector == null)
                return deviceStatus;

            // Query DattoRMM for patch logs
            var dattoConnector = _connectors.FirstOrDefault(c => c.GetType().Name == "DattoRmmConnector");
            if (dattoConnector == null)
                return deviceStatus;

            // Fetch recent logs from DattoRMM
            var result = await dattoConnector.FetchRecentLogsAsync(cancellationToken);
            if (!result.Success || !result.Records.Any())
                return deviceStatus;

            // Parse patch schedule messages: look for "Patch Schedule: start" and "Patch Schedule: end"
            var patchLogs = result.Records
                .Where(r => r.Message.Contains("Patch Schedule", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.TimestampUtc)
                .GroupBy(r => r.Source)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in patchLogs)
            {
                var hostname = kvp.Key;
                var logs = kvp.Value;

                var deviceState = new DevicePatchState
                {
                    Hostname = hostname,
                    DeviceId = hostname,
                    LastUpdatedAtUtc = DateTime.UtcNow
                };

                // Find latest start and end
                var startLog = logs.FirstOrDefault(l => l.Message.Contains("start", StringComparison.OrdinalIgnoreCase));
                var endLog = logs.FirstOrDefault(l => l.Message.Contains("end", StringComparison.OrdinalIgnoreCase));

                if (startLog != null)
                    deviceState.LastStartAtUtc = startLog.TimestampUtc;

                if (endLog != null)
                    deviceState.LastEndAtUtc = endLog.TimestampUtc;

                // Determine status
                if (endLog != null && (startLog == null || endLog.TimestampUtc >= startLog.TimestampUtc))
                {
                    deviceState.Status = "Completed";
                }
                else if (startLog != null && endLog == null)
                {
                    deviceState.Status = "InProgress";
                }
                else
                {
                    deviceState.Status = "Pending";
                }

                deviceStatus[hostname] = deviceState;
            }

            return deviceStatus;
        }
        catch
        {
            return deviceStatus;
        }
    }

    private async Task<bool> CompleteWartungsarbeitenTicketAsync(string ticketId, AgentConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var autotaskSettings = config.Integrations.Autotask;
            if (!autotaskSettings.Enabled)
                return false;

            // Update ticket status to completed (status 30 = Completed in AutoTask)
            var uri = new Uri(autotaskSettings.BaseUrl.TrimEnd('/') + $"/atservicesrest/v1.0/Tickets/{ticketId}");
            var payload = JsonSerializer.Serialize(new { ticketStatus = 30 });

            using var request = new HttpRequestMessage(HttpMethod.Patch, uri)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };
            ApplyAutotaskAuthHeaders(request, autotaskSettings);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyAutotaskAuthHeaders(HttpRequestMessage request, ApiConnectorSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(settings.IntegrationCode))
        {
            request.Headers.TryAddWithoutValidation("ApiIntegrationcode", settings.IntegrationCode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(settings.Username) && !string.IsNullOrWhiteSpace(settings.Password))
        {
            var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
            request.Headers.TryAddWithoutValidation("Authorization", $"Basic {token}");
        }
    }
}
