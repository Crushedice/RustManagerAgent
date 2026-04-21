internal sealed class ResponseComposer
{
    public RoutedReply Compose(AdminIntentRoute route, string reply, IReadOnlyList<string> toolsUsed)
    {
        return new RoutedReply
        {
            Reply = reply,
            Intent = route.Intent.ToString(),
            ServerName = route.ServerName,
            PendingClarification = route.NeedsClarification,
            Notes = route.NeedsClarification ? "Router requested explicit target clarification." : "Router executed intent path.",
            UsedTools = toolsUsed
        };
    }
}
