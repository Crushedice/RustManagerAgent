using RustOpsAgent.Core.Contracts;

namespace RustOpsAgent.Domains.Rust;

internal sealed class RustChatToolHandler : IToolHandler
{
    public string Name => "rust.chat.reply";
    public IReadOnlyCollection<AdminIntentType> EligibleIntents => new[] { AdminIntentType.Chat, AdminIntentType.Clarification };

    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var reply = context.Route.Intent switch
        {
            AdminIntentType.Clarification => context.Route.ClarificationQuestion ?? "Please clarify what action you want and which server it targets.",
            _ => "Ready. Ask for status, server control, player lookup, RCON command, logs, plugins, or focused network inspection."
        };

        return Task.FromResult(new ToolExecutionResult(true, reply, context.SelectionState.LastServerName));
    }
}
