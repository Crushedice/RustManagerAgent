using System.Text.Json;
using RustOpsAgent.Core.Contracts;
using RustOpsAgent.Core.Interaction;
using RustOpsAgent.Infrastructure;
using RustOpsAgent.Infrastructure.GitOps;
using RustOpsAgent.Infrastructure.Memory;

namespace RustOpsAgent.Core;

internal sealed class AgentRuntime
{
    private readonly AgentConfig _config;
    private readonly IIntentClassifier _classifier;
    private readonly IActionExecutor _executor;
    private readonly IResponseComposer _composer;
    private readonly NeoCortexStore _neoCortex;
    private readonly LegacyAgentStateStore _legacyState;
    private readonly IGitOpsService _gitOps;
    private volatile bool _stop;

    public AgentRuntime(
        AgentConfig config,
        IIntentClassifier classifier,
        IActionExecutor executor,
        IResponseComposer composer,
        NeoCortexStore neoCortex,
        LegacyAgentStateStore legacyState,
        IGitOpsService gitOps)
    {
        _config = config;
        _classifier = classifier;
        _executor = executor;
        _composer = composer;
        _neoCortex = neoCortex;
        _legacyState = legacyState;
        _gitOps = gitOps;
    }

    public void RequestStop() => _stop = true;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[agent] Runtime started.");
        var tick = 0;
        while (!_stop && !cancellationToken.IsCancellationRequested)
        {
            _legacyState.UpdateRuntimeStatus(_config.Llm);
            await ProcessFeedbackInboxAsync(cancellationToken);
            await ProcessDecisionInboxAsync(cancellationToken);

            var chatFiles = Directory.Exists(_config.Inbox.ChatInboxPath)
                ? Directory.GetFiles(_config.Inbox.ChatInboxPath, "*.json").Length
                : 0;
            if (chatFiles > 0 || tick % 5 == 0)
                Console.WriteLine($"[agent] Tick {tick}: chat-inbox={chatFiles} file(s)");

            await ProcessChatInboxAsync(cancellationToken);
            _legacyState.Save();
            tick++;
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _config.Monitor.PollSeconds)), cancellationToken);
        }
        Console.WriteLine("[agent] Runtime stopped.");
    }

    private async Task ProcessFeedbackInboxAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_config.Inbox.FeedbackInboxPath))
        {
            return;
        }

        foreach (var file in EnumerateInboxFiles(_config.Inbox.FeedbackInboxPath))
        {
            if (_stop || cancellationToken.IsCancellationRequested)
                return;
            try
            {
                var payload = JsonSerializer.Deserialize<FeedbackInboxItem>(await File.ReadAllTextAsync(file, cancellationToken), JsonDefaults.Default);
                if (payload is null)
                {
                    continue;
                }

                _legacyState.RecordFeedback(payload.AdminId, payload.ActionId, payload.Verdict, payload.Note, payload.ServerName);

                // Allow admins to teach the log filter using partial-match directives.
                var note = payload.Note ?? payload.Preference;
                if (!string.IsNullOrWhiteSpace(note) && note.StartsWith("ignore ", StringComparison.OrdinalIgnoreCase))
                {
                    var pattern = note["ignore ".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(pattern))
                    {
                        var ignore = _neoCortex.LoadIgnoreFeedback();
                        if (!ignore.PartialMatches.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                        {
                            ignore.PartialMatches.Add(pattern);
                            _neoCortex.SaveIgnoreFeedback(ignore);
                        }

                        var logs = _neoCortex.LoadLogs();
                        if (!logs.IgnorePatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                        {
                            logs.IgnorePatterns.Add(pattern);
                            _neoCortex.SaveLogs(logs);
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(payload.AdminId))
                {
                    WriteOutbox(payload.AdminId!, "Feedback received and applied.", payload.ActionId, payload.ServerName);
                }
            }
            catch (Exception ex)
            {
                _legacyState.RecordAgentError($"feedback inbox processing failed: {ex.Message}");
            }
            finally
            {
                TryDelete(file);
            }
        }
    }

    private async Task ProcessDecisionInboxAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_config.Inbox.DecisionInboxPath))
        {
            return;
        }

        foreach (var file in EnumerateInboxFiles(_config.Inbox.DecisionInboxPath))
        {
            if (_stop || cancellationToken.IsCancellationRequested)
                return;
            try
            {
                var payload = JsonSerializer.Deserialize<DecisionInboxItem>(await File.ReadAllTextAsync(file, cancellationToken), JsonDefaults.Default);
                if (payload is null)
                {
                    continue;
                }

                _legacyState.RecordFeedback(payload.AdminId, payload.ActionId, payload.Decision, payload.Note, null);
                if (!string.IsNullOrWhiteSpace(payload.AdminId))
                {
                    var action = string.IsNullOrWhiteSpace(payload.ActionId) ? "(unknown action)" : payload.ActionId;
                    var decision = string.IsNullOrWhiteSpace(payload.Decision) ? "noted" : payload.Decision;
                    WriteOutbox(payload.AdminId!, $"Decision received for {action}: {decision}.", payload.ActionId, null);
                }
            }
            catch (Exception ex)
            {
                _legacyState.RecordAgentError($"decision inbox processing failed: {ex.Message}");
            }
            finally
            {
                TryDelete(file);
            }
        }
    }

    private async Task ProcessChatInboxAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_config.Inbox.ChatInboxPath))
        {
            return;
        }

        foreach (var file in EnumerateInboxFiles(_config.Inbox.ChatInboxPath))
        {
            if (_stop || cancellationToken.IsCancellationRequested)
                return;

            ChatInboxItem? item = null;
            try
            {
                item = JsonSerializer.Deserialize<ChatInboxItem>(File.ReadAllText(file), JsonDefaults.Default);
                if (item is null || string.IsNullOrWhiteSpace(item.Message))
                {
                    continue;
                }

                var actionId = string.IsNullOrWhiteSpace(item.RequestId) ? item.Id : item.RequestId!;

                var selection = _neoCortex.LoadSelection();
                var state = selection.Conversations.FirstOrDefault(c => string.Equals(c.AdminId, item.AdminId, StringComparison.OrdinalIgnoreCase));
                if (state is null)
                {
                    state = new ConversationSelectionState { AdminId = item.AdminId };
                    selection.Conversations.Add(state);
                }

                var route = await _classifier.ClassifyAsync(item.Message, state, cancellationToken);
                var context = new ToolExecutionContext(item.AdminId, item.Message, route, state, DateTime.UtcNow);
                var result = await _executor.ExecuteAsync(context, cancellationToken);
                var reply = _composer.Compose(context, result);

                UpdateSelectionState(state, route, result);
                _neoCortex.SaveSelection(selection);

                var operations = _neoCortex.LoadOperations();
                operations.RuntimeStatus = new RuntimeStatus
                {
                    LlmEnabled = _config.Llm.Enabled,
                    LlmProvider = _config.Llm.Provider,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                operations.RecentActions.Add(new ActionRecord
                {
                    Intent = route.Intent.ToString(),
                    Result = result.Success ? "success" : (result.ErrorCode ?? "failed"),
                    ServerName = result.SelectedServer ?? route.Slots.ServerName,
                    TimestampUtc = DateTime.UtcNow
                });
                operations.RecentActions = operations.RecentActions.TakeLast(100).ToList();
                _neoCortex.SaveOperations(operations);

                _legacyState.RecordAction(
                    actionId,
                    route.Intent.ToString().ToLowerInvariant(),
                    result.Success,
                    result.Message,
                    result.SelectedServer ?? route.Slots.ServerName,
                    "chat");

                if (!result.Success)
                {
                    var incident = new EvolutionIncidentRecord
                    {
                        Request = item.Message,
                        IntendedOutcome = route.Intent.ToString(),
                        FailureReason = result.Message,
                        MissingCapability = result.ErrorCode ?? "unknown",
                        RecurrencePrevention = "Improve handler coverage and routing slots.",
                        Classification = result.ErrorCode ?? "execution_failure",
                        Timestamp = DateTime.UtcNow,
                        Resolved = false
                    };
                    await _neoCortex.RecordIncidentAsync(incident, cancellationToken);
                    _legacyState.RecordIncident(result.SelectedServer ?? route.Slots.ServerName, result.ErrorCode ?? "execution_failure", result.Message);

                    if (_config.GitOps.Enabled && _config.GitOps.AllowPush)
                    {
                        _ = TryProposeIncidentBranchAsync(incident, cancellationToken);
                    }
                }

                WriteOutbox(item.AdminId, reply, actionId, result.SelectedServer ?? route.Slots.ServerName);
            }
            catch (Exception ex)
            {
                await _neoCortex.RecordIncidentAsync(new EvolutionIncidentRecord
                {
                    Request = item?.Message ?? "<invalid inbox item>",
                    IntendedOutcome = "processing",
                    FailureReason = ex.Message,
                    MissingCapability = "processing_error",
                    RecurrencePrevention = "Validate chat inbox payloads before processing.",
                    Classification = "processing_error",
                    Timestamp = DateTime.UtcNow,
                    Resolved = false
                }, cancellationToken);

                _legacyState.RecordAgentError(ex.Message);
                _legacyState.RecordIncident(null, "processing_error", ex.Message);
                WriteOutbox(item?.AdminId ?? "admin", $"Failed to process request: {ex.Message}", item?.RequestId ?? item?.Id, null);
            }
            finally
            {
                TryDelete(file);
            }
        }
    }

    private async Task TryProposeIncidentBranchAsync(EvolutionIncidentRecord incident, CancellationToken cancellationToken)
    {
        try
        {
            var slug = $"incident-{incident.Classification}";
            var branch = await _gitOps.EnsureAgentBranchAsync(slug, cancellationToken);
            var title = $"[agent] Incident: {incident.Classification}";
            var body = $"Request: {incident.Request}\nFailure: {incident.FailureReason}\nMissing: {incident.MissingCapability}\nPrevention: {incident.RecurrencePrevention}";
            await _gitOps.CommitAsync($"incident: record {incident.Id}", cancellationToken);
            await _gitOps.PushAsync(branch, cancellationToken);
            await _gitOps.CreatePrAsync(branch, title, body, cancellationToken);
        }
        catch (Exception ex)
        {
            _legacyState.RecordAgentError($"GitOps incident PR failed: {ex.Message}");
        }
    }

    private IEnumerable<string> EnumerateInboxFiles(string path)
    {
        return Directory
            .GetFiles(path, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void UpdateSelectionState(ConversationSelectionState state, AdminIntentRoute route, ToolExecutionResult result)
    {
        state.LastIntent = route.Intent.ToString();
        state.LastServerName = result.SelectedServer ?? route.Slots.ServerName ?? state.LastServerName;
        state.LastCommandText = route.Slots.CommandText ?? state.LastCommandText;
        state.LastTimeRange = route.Slots.TimeRange ?? state.LastTimeRange;
        state.UpdatedAtUtc = DateTime.UtcNow;
    }

    private void WriteOutbox(string adminId, string message, string? actionId, string? serverName)
    {
        var payload = new AdapterMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            AdminId = adminId,
            Kind = "chat-reply",
            Audience = "admins",
            TargetAdminId = adminId,
            ServerName = serverName ?? string.Empty,
            ActionId = actionId,
            Message = message,
            CreatedAtUtc = DateTime.UtcNow
        };

        Directory.CreateDirectory(_config.Outbox.MessageOutboxPath);
        var path = Path.Combine(_config.Outbox.MessageOutboxPath, $"{payload.CreatedAtUtc:yyyyMMddHHmmssfff}-chat-reply-{payload.Id}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonDefaults.Default));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
