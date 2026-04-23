using System.Diagnostics;
using System.Runtime.InteropServices;
using Sentry;

internal sealed class RustMgrExecutor
{
    private readonly string _rustMgrPath;

    public RustMgrExecutor(string? rustMgrPath = null)
    {
        _rustMgrPath = string.IsNullOrWhiteSpace(rustMgrPath)
            ? (Environment.GetEnvironmentVariable("RUSTMGR_PATH") ?? "/opt/rust-manager/rustmgr.sh")
            : rustMgrPath;
    }

    public async Task<CommandExecutionResult> ExecuteAsync(params string[] args)
    {
        var action = args.FirstOrDefault() ?? "unknown";
        RustOpsSentry.AddBreadcrumb($"Executing rustmgr '{action}'.", "rustmgr");

        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = ResolveWorkingDirectory()
        };

        ConfigureProcessStartInfo(psi, args);

        try
        {
            using var process = new Process { StartInfo = psi };

            if (!process.Start())
            {
                return new CommandExecutionResult
                {
                    Ok = false,
                    ExitCode = -1,
                    Arguments = args,
                    StdErr = $"Failed to start '{_rustMgrPath}'."
                };
            }

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(GetTimeoutForAction(args.FirstOrDefault()));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(true);
                }
                catch
                {
                }

                return new CommandExecutionResult
                {
                    Ok = false,
                    ExitCode = -1,
                    Arguments = args,
                    TimedOut = true,
                    StdErr = $"rustmgr action '{args.FirstOrDefault() ?? "unknown"}' timed out after {GetTimeoutForAction(args.FirstOrDefault()).TotalSeconds:0}s"
                };
            }

            var result = new CommandExecutionResult
            {
                Ok = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Arguments = args,
                StdOut = (await stdOutTask).Trim(),
                StdErr = (await stdErrTask).Trim()
            };

            if (!result.Ok)
            {
                RustOpsSentry.AddBreadcrumb(
                    $"rustmgr '{action}' failed with exit code {result.ExitCode}.",
                    "rustmgr");
            }

            return result;
        }
        catch (Exception ex)
        {
            RustOpsSentry.CaptureException(
                ex,
                $"rustmgr '{action}' execution failed.",
                "rustmgr",
                tags: new Dictionary<string, string?> { ["rustmgr.action"] = action },
                extras: new Dictionary<string, object?>
                {
                    ["arguments"] = args.ToArray(),
                    ["workingDirectory"] = ResolveWorkingDirectory(),
                    ["rustMgrPath"] = _rustMgrPath
                });
            return new CommandExecutionResult
            {
                Ok = false,
                ExitCode = -1,
                Arguments = args,
                StdErr = ex.Message
            };
        }
    }

    private void ConfigureProcessStartInfo(ProcessStartInfo psi, IReadOnlyList<string> args)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            _rustMgrPath.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = "/usr/bin/env";
            psi.ArgumentList.Add("bash");
            psi.ArgumentList.Add(_rustMgrPath);
        }
        else
        {
            psi.FileName = _rustMgrPath;
        }

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
    }

    private string ResolveWorkingDirectory()
    {
        var directory = Path.GetDirectoryName(_rustMgrPath);
        return string.IsNullOrWhiteSpace(directory)
            ? Environment.CurrentDirectory
            : directory;
    }

    public async Task<CommandExecutionResult> ExecuteLifecycleAsync(string server, string operation)
    {
        if (RequiresConfigSync(operation))
        {
            var sync = await ExecuteAsync("sync-config", server);
            if (!sync.Ok)
                return sync;
        }

        var result = await ExecuteAsync(operation, server);
        if (result.Ok)
            return result;

        var expectedState = GetExpectedState(operation);
        if (expectedState is null)
            return result;

        var status = await GetStatusAsync(server);
        if (status is not null &&
            string.Equals(status.State, expectedState, StringComparison.OrdinalIgnoreCase))
        {
            result.Ok = true;
            result.Message = $"rustmgr '{operation}' returned exit code {result.ExitCode}, but status verification shows '{server}' is now '{status.State}'.";
        }

        return result;
    }

    public async Task<RustMgrStatusSnapshot?> GetStatusAsync(string server)
    {
        var result = await ExecuteAsync("status", server);
        if (!result.Ok)
            return null;

        return ParseStatus(server, result.StdOut);
    }

    public async Task<LifecycleVerificationResult?> VerifyExpectedServerStateAsync(string server, string operation)
    {
        var expectedState = GetExpectedState(operation);
        if (expectedState is null)
            return null;

        var (attempts, delaySeconds) = GetVerificationTiming(operation);
        RustMgrStatusSnapshot? lastStatus = null;
        var progressObserved = false;
        var processObserved = false;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var status = await GetStatusAsync(server);
            if (status is not null)
            {
                lastStatus = status;
                if (status.Pid.HasValue)
                    processObserved = true;

                if (string.Equals(status.State, expectedState, StringComparison.OrdinalIgnoreCase))
                {
                    return new LifecycleVerificationResult
                    {
                        ExpectedState = expectedState,
                        ReachedExpectedState = true,
                        ProgressObserved = true,
                        ProcessObserved = processObserved,
                        AttemptsUsed = attempt + 1,
                        LastStatus = status
                    };
                }

                if (IsMeaningfulLifecycleProgress(operation, status))
                    progressObserved = true;
            }

            if (attempt + 1 < attempts)
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }

        return new LifecycleVerificationResult
        {
            ExpectedState = expectedState,
            ReachedExpectedState = false,
            ProgressObserved = progressObserved,
            ProcessObserved = processObserved,
            AttemptsUsed = attempts,
            LastStatus = lastStatus
        };
    }

    public async Task<bool?> WaitForExpectedServerStateAsync(string server, string operation)
    {
        var verification = await VerifyExpectedServerStateAsync(server, operation);
        return verification?.ReachedExpectedState;
    }

    private static RustMgrStatusSnapshot ParseStatus(string server, string? stdout)
    {
        var output = stdout ?? string.Empty;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            fields[key] = value;
        }

        int? pid = null;
        if (fields.TryGetValue("pid", out var pidStr) && int.TryParse(pidStr, out var parsedPid))
            pid = parsedPid;

        fields.TryGetValue("state", out var state);
        fields.TryGetValue("autorestart", out var autoRestart);
        fields.TryGetValue("session", out var session);

        return new RustMgrStatusSnapshot
        {
            Name = server,
            State = state ?? "unknown",
            Online = string.Equals(state, "running", StringComparison.OrdinalIgnoreCase),
            AutoRestart = string.Equals(autoRestart, "yes", StringComparison.OrdinalIgnoreCase),
            Session = string.Equals(session, "yes", StringComparison.OrdinalIgnoreCase),
            Pid = pid,
            Raw = output
        };
    }

    private static bool RequiresConfigSync(string operation) =>
        operation is "start" or "restart";

    private static string? GetExpectedState(string operation) =>
        operation switch
        {
            "start" => "running",
            "restart" => "running",
            "stop" => "offline",
            _ => null
        };

    private static TimeSpan GetTimeoutForAction(string? action) =>
        (action ?? string.Empty).ToLowerInvariant() switch
        {
            "update" => TimeSpan.FromMinutes(25),
            "umod" => TimeSpan.FromMinutes(8),
            "start" => TimeSpan.FromMinutes(4),
            "restart" => TimeSpan.FromMinutes(5),
            "stop" => TimeSpan.FromMinutes(2),
            "kill" => TimeSpan.FromSeconds(30),
            "wipe" => TimeSpan.FromSeconds(30),
            "query" => TimeSpan.FromSeconds(20),
            "send" => TimeSpan.FromSeconds(20),
            "sync-config" => TimeSpan.FromSeconds(15),
            _ => TimeSpan.FromSeconds(45)
        };

    private static (int Attempts, int DelaySeconds) GetVerificationTiming(string operation) =>
        operation switch
        {
            "start" => (45, 2),
            "restart" => (45, 2),
            "stop" => (20, 1),
            _ => (5, 2)
        };

    private static bool IsMeaningfulLifecycleProgress(string operation, RustMgrStatusSnapshot status) =>
        operation switch
        {
            "start" or "restart" => status.Pid.HasValue ||
                                    string.Equals(status.State, "starting", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(status.State, "session-only", StringComparison.OrdinalIgnoreCase) ||
                                    status.Session ||
                                    status.AutoRestart,
            _ => false
        };
}

internal sealed class RustMgrStatusSnapshot
{
    public string Name { get; init; } = string.Empty;
    public string State { get; init; } = "unknown";
    public bool Online { get; init; }
    public bool AutoRestart { get; init; }
    public bool Session { get; init; }
    public int? Pid { get; init; }
    public string Raw { get; init; } = string.Empty;
}

internal sealed class LifecycleVerificationResult
{
    public string ExpectedState { get; init; } = string.Empty;
    public bool ReachedExpectedState { get; init; }
    public bool ProgressObserved { get; init; }
    public bool ProcessObserved { get; init; }
    public int AttemptsUsed { get; init; }
    public RustMgrStatusSnapshot? LastStatus { get; init; }
}

public sealed class CommandExecutionResult
{
    public bool Ok { get; set; }
    public int ExitCode { get; set; }
    public bool TimedOut { get; set; }
    public IEnumerable<string> Arguments { get; set; } = Array.Empty<string>();
    public string? Message { get; set; }
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
}
