using System.Diagnostics;
using System.Text;
using RustOpsAgent.Core.Contracts;

namespace RustOpsAgent.Infrastructure;

internal sealed class AutoPullService
{
    private readonly AutoPullSettings _settings;
    private DateTime _lastRunAtUtc = DateTime.MinValue;
    private AutoPullStatus _lastStatus = new("idle", null, null, DateTime.MinValue);

    public AutoPullService(AutoPullSettings settings)
    {
        _settings = settings;
    }

    public AutoPullStatus LastStatus => _lastStatus;

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
            return;

        var interval = TimeSpan.FromMinutes(Math.Max(5, _settings.IntervalMinutes));
        if (DateTime.UtcNow - _lastRunAtUtc < interval)
            return;

        await TriggerAsync(cancellationToken);
    }

    public async Task<AutoPullStatus> TriggerAsync(CancellationToken cancellationToken)
    {
        _lastRunAtUtc = DateTime.UtcNow;
        Console.WriteLine("[autopull] Starting pull cycle.");

        var output = new StringBuilder();
        try
        {
            var gitEnv = BuildGitEnv();
            var pullOut = await RunAsync("git", $"pull {_settings.RemoteName} {_settings.BranchName}", _settings.RepoPath, gitEnv, cancellationToken);
            output.AppendLine(pullOut);
            Console.WriteLine($"[autopull] git pull: {pullOut.Trim()}");

            if (pullOut.Contains("Already up to date", StringComparison.OrdinalIgnoreCase))
            {
                _lastStatus = new AutoPullStatus("up-to-date", pullOut.Trim(), null, DateTime.UtcNow);
                return _lastStatus;
            }

            if (_settings.BuildEnabled && !string.IsNullOrWhiteSpace(_settings.BuildScript))
            {
                Console.WriteLine($"[autopull] Running build script: {_settings.BuildScript}");
                var buildScript = Path.IsPathRooted(_settings.BuildScript)
                    ? _settings.BuildScript
                    : Path.Combine(_settings.RepoPath, _settings.BuildScript);

                var buildOut = await RunAsync("bash", $"\"{buildScript}\"", _settings.RepoPath, null, cancellationToken);
                output.AppendLine(buildOut);
                Console.WriteLine($"[autopull] build: {buildOut.Trim()}");
            }

            if (_settings.RestartEnabled && !string.IsNullOrWhiteSpace(_settings.ServiceName))
            {
                Console.WriteLine($"[autopull] Restarting service: {_settings.ServiceName}");
                var restartOut = await RunAsync("systemctl", $"restart {_settings.ServiceName}", "/", null, cancellationToken);
                output.AppendLine(restartOut);
            }

            _lastStatus = new AutoPullStatus("updated", output.ToString().Trim(), null, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[autopull] Failed: {ex.Message}");
            RustOpsSentry.CaptureException(ex, "AutoPull cycle failed.", "agent.autopull");
            _lastStatus = new AutoPullStatus("error", output.ToString().Trim(), ex.Message, DateTime.UtcNow);
        }

        return _lastStatus;
    }

    private Dictionary<string, string>? BuildGitEnv()
    {
        if (string.IsNullOrWhiteSpace(_settings.GithubToken))
            return null;

        var token = _settings.GithubToken!;
        // Use GIT_CONFIG_* env vars (git 2.31+) to inject the PAT via URL rewrite,
        // avoiding the need for a ~/.git-credentials file under the service user account.
        return new Dictionary<string, string>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_CONFIG_COUNT"] = "2",
            ["GIT_CONFIG_KEY_0"] = $"url.https://oauth2:{token}@github.com/.insteadOf",
            ["GIT_CONFIG_VALUE_0"] = "https://github.com/",
            ["GIT_CONFIG_KEY_1"] = "credential.helper",
            ["GIT_CONFIG_VALUE_1"] = ""
        };
    }

    private static async Task<string> RunAsync(string fileName, string arguments, string workingDirectory, Dictionary<string, string>? env, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (env != null)
        {
            foreach (var kvp in env)
                psi.Environment[kvp.Key] = kvp.Value;
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start process: {fileName}");
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {arguments} exited {process.ExitCode}: {stdErr.Trim()}");
        }

        return string.IsNullOrWhiteSpace(stdOut) ? stdErr.Trim() : stdOut.Trim();
    }
}

internal sealed record AutoPullStatus(
    string Phase,
    string? Output,
    string? Error,
    DateTime LastRunAtUtc);
