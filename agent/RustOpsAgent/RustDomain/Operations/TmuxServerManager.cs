internal sealed class TmuxServerManager
{
    private readonly RustMgrExecutor _executor;

    public TmuxServerManager(RustMgrExecutor executor)
    {
        _executor = executor;
    }

    public Task<RustMgrCommandResult> DiscoverSessionsAsync() => _executor.RunAsync("tmux list-sessions -F '#S'", 5000, 64000);

    public Task<RustMgrCommandResult> ReadConsoleAsync(string sessionName, int lines = 120)
        => _executor.RunAsync($"tmux capture-pane -pt {Escape(sessionName)} -S -{Math.Max(10, lines)}", 5000, 512000);

    public Task<RustMgrCommandResult> SendCommandAsync(string sessionName, string command)
        => _executor.RunAsync($"tmux send-keys -t {Escape(sessionName)} {Escape(command)} Enter", 5000, 64000);

    private static string Escape(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
