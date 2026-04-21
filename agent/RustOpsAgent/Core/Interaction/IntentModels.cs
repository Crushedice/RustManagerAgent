internal enum AdminIntentType
{
    chat,
    server_control,
    player_lookup,
    rcon_command,
    file_edit,
    status_check,
    troubleshooting,
    clarification
}

internal sealed class AdminIntentRoute
{
    public AdminIntentType Intent { get; set; } = AdminIntentType.chat;
    public string? ServerName { get; set; }
    public string? PlayerName { get; set; }
    public string? CommandText { get; set; }
    public string? TimeRange { get; set; }
    public string? Severity { get; set; }
    public bool NeedsClarification { get; set; }
}

internal sealed record AgentInteractionContext(
    string AdminId,
    string Message,
    DateTime UtcNow,
    List<ServerSnapshot> Servers,
    AdminConversationState Conversation,
    AdminPreference Preference);

internal sealed class RoutedReply
{
    public string Reply { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public string? ServerName { get; set; }
    public bool PendingClarification { get; set; }
    public string Notes { get; set; } = string.Empty;
    public IReadOnlyList<string>? UsedTools { get; set; }
}
