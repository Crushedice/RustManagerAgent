internal interface IRconClient : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, string password, CancellationToken cancellationToken = default);
    Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default);
    event Action<string>? UnsolicitedMessage;
}
