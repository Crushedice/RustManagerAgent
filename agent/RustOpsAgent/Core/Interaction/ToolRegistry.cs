internal sealed class ToolRegistry
{
    private static readonly Dictionary<AdminIntentType, IReadOnlyList<string>> EligibleTools = new()
    {
        [AdminIntentType.server_control] = new[] { "start_server", "stop_server", "restart_server" },
        [AdminIntentType.player_lookup] = new[] { "get_server_players" },
        [AdminIntentType.rcon_command] = new[] { "execute_server_command" },
        [AdminIntentType.status_check] = new[] { "get_server_health", "get_server_events" },
        [AdminIntentType.troubleshooting] = new[] { "get_server_health", "inspect_host_network", "validate_oxide" },
        [AdminIntentType.chat] = Array.Empty<string>(),
        [AdminIntentType.clarification] = Array.Empty<string>()
    };

    public IReadOnlyList<string> GetEligibleTools(AdminIntentRoute route)
        => EligibleTools.TryGetValue(route.Intent, out var tools) ? tools : Array.Empty<string>();
}
