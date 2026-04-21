internal sealed class AdminIntentClassifier
{
    public AdminIntentRoute Classify(AgentInteractionContext context)
    {
        var message = context.Message.Trim();
        var lowered = message.ToLowerInvariant();
        var route = new AdminIntentRoute();

        if (lowered.Contains("restart") || lowered.Contains("start") || lowered.Contains("stop") || lowered.Contains("kill") || lowered.Contains("update"))
            route.Intent = AdminIntentType.server_control;
        else if (lowered.Contains("player") || lowered.Contains("steamid") || lowered.Contains("whois"))
            route.Intent = AdminIntentType.player_lookup;
        else if (lowered.Contains("rcon") || lowered.StartsWith("run command "))
            route.Intent = AdminIntentType.rcon_command;
        else if (lowered.Contains("status") || lowered.Contains("health") || lowered.Contains("logs"))
            route.Intent = AdminIntentType.status_check;
        else if (lowered.Contains("error") || lowered.Contains("failed") || lowered.Contains("issue"))
            route.Intent = AdminIntentType.troubleshooting;
        else
            route.Intent = AdminIntentType.chat;

        route.ServerName = ResolveServerNameFromText(context.Servers, message) ?? context.Conversation.LastServerName;
        route.CommandText = route.Intent == AdminIntentType.rcon_command ? message : null;
        route.NeedsClarification = (route.Intent == AdminIntentType.server_control || route.Intent == AdminIntentType.status_check || route.Intent == AdminIntentType.rcon_command)
            && string.IsNullOrWhiteSpace(route.ServerName);

        return route;
    }

    private static string? ResolveServerNameFromText(IEnumerable<ServerSnapshot> servers, string message)
    {
        foreach (var server in servers)
        {
            if (message.Contains(server.Name, StringComparison.OrdinalIgnoreCase))
                return server.Name;
        }

        return null;
    }
}
