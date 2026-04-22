namespace RustOpsAgent.Domains.Rust.Rcon;

internal sealed class PersistentRconSession : IAsyncDisposable
{
    private readonly Uri _uri;
    private readonly string _password;
    private readonly RconRollingLogMonitor _monitor = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IRconClient? _client;

    public PersistentRconSession(Uri uri, string password)
    {
        _uri = uri;
        _password = password;
    }

    public bool Matches(Uri uri, string password) =>
        Uri.Compare(_uri, uri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0 &&
        string.Equals(_password, password, StringComparison.Ordinal);

    public IReadOnlyList<string> Snapshot() => _monitor.Snapshot();

    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync(cancellationToken);

            try
            {
                return await _client!.SendCommandAsync(command, cancellationToken);
            }
            catch
            {
                await ResetClientAsync();
                await EnsureConnectedAsync(cancellationToken);
                return await _client!.SendCommandAsync(command, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await ResetClientAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return;
        }

        var client = new RustRconClient();
        _monitor.Attach(client);

        try
        {
            await client.ConnectAsync(_uri, _password, cancellationToken);
            _client = client;
        }
        catch
        {
            _monitor.Detach(client);
            await client.DisposeAsync();
            throw;
        }
    }

    private async Task ResetClientAsync()
    {
        if (_client is null)
        {
            return;
        }

        var client = _client;
        _client = null;
        _monitor.Detach(client);
        await client.DisposeAsync();
    }
}
