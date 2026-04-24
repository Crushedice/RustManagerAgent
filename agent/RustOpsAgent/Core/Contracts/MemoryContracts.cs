using System.Text.Json.Serialization;

namespace RustOpsAgent.Core.Contracts;

internal sealed class ActiveOperationalState
{
    [JsonPropertyName("runtimeStatus")] public RuntimeStatus RuntimeStatus { get; set; } = new();
    [JsonPropertyName("recentActions")] public List<ActionRecord> RecentActions { get; set; } = new();
    [JsonPropertyName("llmInteractions")] public List<LlmInteractionRecord> LlmInteractions { get; set; } = new();
}

internal sealed class RuntimeStatus
{
    [JsonPropertyName("llmEnabled")] public bool LlmEnabled { get; set; }
    [JsonPropertyName("llmProvider")] public string? LlmProvider { get; set; }
    [JsonPropertyName("lastLlmInteractionAtUtc")] public DateTime? LastLlmInteractionAtUtc { get; set; }
    [JsonPropertyName("updatedAtUtc")] public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class ActionRecord
{
    [JsonPropertyName("timestampUtc")] public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("intent")] public string Intent { get; set; } = string.Empty;
    [JsonPropertyName("result")] public string Result { get; set; } = string.Empty;
    [JsonPropertyName("serverName")] public string? ServerName { get; set; }
}

internal sealed class LlmInteractionRecord
{
    [JsonPropertyName("atUtc")] public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("type")] public string Type { get; set; } = "intent-routing";
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("context")] public string? Context { get; set; }
    [JsonPropertyName("responsePreview")] public string? ResponsePreview { get; set; }
}

internal sealed class SelectionSessionState
{
    [JsonPropertyName("conversations")] public List<ConversationSelectionState> Conversations { get; set; } = new();
}

internal sealed class LogKnowledgeState
{
    [JsonPropertyName("ignorePatterns")] public List<string> IgnorePatterns { get; set; } = new();
    [JsonPropertyName("importanceRules")] public List<string> ImportanceRules { get; set; } = new();
    [JsonPropertyName("recentEntries")] public List<LogObservation> RecentEntries { get; set; } = new();
}

internal sealed class LogObservation
{
    [JsonPropertyName("serverName")] public string ServerName { get; set; } = string.Empty;
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("connector")] public string? Connector { get; set; }
    [JsonPropertyName("level")] public string? Level { get; set; }
    [JsonPropertyName("line")] public string Line { get; set; } = string.Empty;
    [JsonPropertyName("importance")] public int Importance { get; set; }
    [JsonPropertyName("capturedAtUtc")] public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class IgnoreFeedbackState
{
    [JsonPropertyName("partialMatches")] public List<string> PartialMatches { get; set; } = new();
}

internal sealed class DomainCacheState
{
    [JsonPropertyName("serverCache")] public Dictionary<string, string> ServerCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class EvolutionIncidentRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("request")] public string Request { get; set; } = string.Empty;
    [JsonPropertyName("intendedOutcome")] public string IntendedOutcome { get; set; } = string.Empty;
    [JsonPropertyName("failureReason")] public string FailureReason { get; set; } = string.Empty;
    [JsonPropertyName("missingCapability")] public string MissingCapability { get; set; } = string.Empty;
    [JsonPropertyName("recurrencePrevention")] public string RecurrencePrevention { get; set; } = string.Empty;
    [JsonPropertyName("classification")] public string Classification { get; set; } = "unknown";
    [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("resolved")] public bool Resolved { get; set; }
}

internal sealed class EvolutionReviewResult
{
    public List<EvolutionIncidentRecord> OpenIncidents { get; set; } = new();
    public List<EvolutionIncidentRecord> RecentlyResolved { get; set; } = new();
}

internal interface IEvolutionStore
{
    Task RecordIncidentAsync(EvolutionIncidentRecord incident, CancellationToken cancellationToken);
    Task<EvolutionReviewResult> ReviewAsync(CancellationToken cancellationToken);
}

// Maintenance Tracking: Track Wartungsarbeiten tickets and device patch progress
internal sealed class MaintenanceTrackingState
{
    [JsonPropertyName("organizations")] public Dictionary<string, OrgMaintenanceState> Organizations { get; set; } = new();
    [JsonPropertyName("updatedAtUtc")] public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class OrgMaintenanceState
{
    [JsonPropertyName("organizationId")] public string OrganizationId { get; set; } = string.Empty;
    [JsonPropertyName("organizationName")] public string OrganizationName { get; set; } = string.Empty;
    [JsonPropertyName("ticketId")] public string TicketId { get; set; } = string.Empty;
    [JsonPropertyName("ticketName")] public string TicketName { get; set; } = "Wartungsarbeiten";
    [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("targetPatchVersion")] public string? TargetPatchVersion { get; set; }
    [JsonPropertyName("affectedDevices")] public Dictionary<string, DevicePatchState> AffectedDevices { get; set; } = new();
    [JsonPropertyName("completionStatus")] public string CompletionStatus { get; set; } = "Pending"; // Pending, InProgress, Completed, DryRun-Completed
    [JsonPropertyName("completedAtUtc")] public DateTime? CompletedAtUtc { get; set; }
    [JsonPropertyName("lastPolledAtUtc")] public DateTime LastPolledAtUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("dryRunMode")] public bool DryRunMode { get; set; } = true;
}

internal sealed class DevicePatchState
{
    [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = string.Empty;
    [JsonPropertyName("hostname")] public string Hostname { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = "Pending"; // Pending, InProgress, Completed, Failed
    [JsonPropertyName("lastStartAtUtc")] public DateTime? LastStartAtUtc { get; set; }
    [JsonPropertyName("lastEndAtUtc")] public DateTime? LastEndAtUtc { get; set; }
    [JsonPropertyName("patchType")] public string? PatchType { get; set; }
    [JsonPropertyName("lastUpdatedAtUtc")] public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

// Severity Rule State: Track learned rules for warning classification
internal sealed class SeverityRuleState
{
    [JsonPropertyName("rules")] public List<SeverityRule> Rules { get; set; } = new();
    [JsonPropertyName("updatedAtUtc")] public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class SeverityRule
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("pattern")] public string Pattern { get; set; } = string.Empty; // Pattern to match (regex or keyword)
    [JsonPropertyName("severity")] public string Severity { get; set; } = "Medium"; // Critical, High, Medium, Low
    [JsonPropertyName("action")] public string Action { get; set; } = "CreateTicket"; // CreateTicket, AlertAdmin, AutoResolve
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("isSystemDefault")] public bool IsSystemDefault { get; set; } = false;
    [JsonPropertyName("lastLearnedFromAtUtc")] public DateTime? LastLearnedFromAtUtc { get; set; }
    [JsonPropertyName("confidence")] public double Confidence { get; set; } = 0.5; // 0.0 to 1.0
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// Warnings Knowledge State: Store researched knowledge about standard IT warnings
internal sealed class WarningsKnowledgeState
{
    [JsonPropertyName("warnings")] public Dictionary<string, WarningKnowledge> Warnings { get; set; } = new();
    [JsonPropertyName("lastResearchedAtUtc")] public DateTime? LastResearchedAtUtc { get; set; }
    [JsonPropertyName("updatedAtUtc")] public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

internal sealed class WarningKnowledge
{
    [JsonPropertyName("warningType")] public string WarningType { get; set; } = string.Empty; // CPU, Disk, Memory, Patch, Security, etc.
    [JsonPropertyName("meaning")] public string Meaning { get; set; } = string.Empty; // What this warning typically means
    [JsonPropertyName("normalThresholds")] public Dictionary<string, object> NormalThresholds { get; set; } = new(); // e.g., { "cpu": "< 80%", "disk": "< 90%" }
    [JsonPropertyName("typicalRemediation")] public string? TypicalRemediation { get; set; }
    [JsonPropertyName("urgencyLevel")] public string UrgencyLevel { get; set; } = "Medium"; // Critical, High, Medium, Low
    [JsonPropertyName("relatedTicketType")] public string? RelatedTicketType { get; set; }
    [JsonPropertyName("sources")] public List<string> Sources { get; set; } = new(); // Where this knowledge came from (LLM, vendor docs, etc.)
    [JsonPropertyName("researchedAtUtc")] public DateTime ResearchedAtUtc { get; set; } = DateTime.UtcNow;
}

// Conversation context for selection/feedback
internal sealed class ConversationSelectionState
{
    [JsonPropertyName("adminId")] public string AdminId { get; set; } = string.Empty;
    [JsonPropertyName("lastServerName")] public string? LastServerName { get; set; }
    [JsonPropertyName("lastIntent")] public string? LastIntent { get; set; }
    [JsonPropertyName("context")] public Dictionary<string, string> Context { get; set; } = new();
}
