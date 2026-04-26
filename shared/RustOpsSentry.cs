using System.Reflection;
using Sentry;

internal static class RustOpsSentry
{
    private const string DefaultDsn = "https://8e1bbf090b73a90ebf5183bba0c7b07e@logging.rusticaland.ovh/7";
    private static bool _globalHandlersRegistered;
    private static string? _serviceName;

    public static IDisposable Initialize(string serviceName)
    {
        _serviceName = serviceName;
        var dsn = RustOpsEnv.FirstNonEmptyEnvironment("RUSTOPS_SENTRY_DSN", "SENTRY_DSN") ?? DefaultDsn;
        var environment = RustOpsEnv.FirstNonEmptyEnvironment("RUSTOPS_SENTRY_ENVIRONMENT", "SENTRY_ENVIRONMENT") ?? "production";
        var release = RustOpsEnv.FirstNonEmptyEnvironment("RUSTOPS_SENTRY_RELEASE", "SENTRY_RELEASE") ?? BuildReleaseName(serviceName);
        var debug = RustOpsEnv.GetBoolean("RUSTOPS_SENTRY_DEBUG", RustOpsEnv.GetBoolean("SENTRY_DEBUG", false));
        var tracesSampleRate = RustOpsEnv.GetDouble("RUSTOPS_SENTRY_TRACES_SAMPLE_RATE",
            RustOpsEnv.GetDouble("SENTRY_TRACES_SAMPLE_RATE", 0.1));
        var maxBreadcrumbs = RustOpsEnv.GetInt32("RUSTOPS_SENTRY_MAX_BREADCRUMBS", 100);

        var handle = SentrySdk.Init(options =>
        {
            options.Dsn = dsn;
            options.Debug = debug;
            options.AutoSessionTracking = true;
            options.AttachStacktrace = true;
            options.Environment = environment;
            options.Release = release;
            options.MaxBreadcrumbs = maxBreadcrumbs;
            options.TracesSampleRate = tracesSampleRate;
        });

        SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag("service", serviceName);
            scope.SetTag("machine", Environment.MachineName);
            scope.SetTag("process.id", Environment.ProcessId.ToString());
            scope.SetTag("process.name", Environment.ProcessPath is null ? "unknown" : Path.GetFileName(Environment.ProcessPath));
            scope.SetTag("runtime.version", Environment.Version.ToString());
            scope.SetExtra("currentDirectory", Environment.CurrentDirectory);
            scope.SetExtra("processPath", Environment.ProcessPath ?? string.Empty);
            scope.SetExtra("commandLine", Environment.CommandLine);
        });

        RegisterGlobalHandlers(serviceName);
        AddBreadcrumb($"Sentry initialized for {serviceName}.", "startup");
        return handle;
    }

    public static void RegisterGlobalHandlers(string serviceName)
    {
        if (_globalHandlersRegistered)
            return;

        _globalHandlersRegistered = true;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AddBreadcrumb("Unhandled AppDomain exception captured.", "runtime");
                SentrySdk.CaptureException(exception);
                SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            }
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            AddBreadcrumb("Process exit detected. Flushing Sentry.", "runtime");
            SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AddBreadcrumb("Unobserved task exception captured.", "runtime");
            SentrySdk.CaptureException(args.Exception);
            args.SetObserved();
        };
    }

    public static void AddBreadcrumb(string message, string category)
    {
        SentrySdk.AddBreadcrumb(message, category);
    }

    public static void ConfigureScope(Action<Scope> configure)
    {
        SentrySdk.ConfigureScope(configure);
    }

    public static void CaptureException(
        Exception exception,
        string context,
        string category,
        IReadOnlyDictionary<string, string?>? tags = null,
        IReadOnlyDictionary<string, object?>? extras = null)
    {
        AddBreadcrumb(context, category);
        SentrySdk.CaptureException(exception, scope =>
        {
            scope.SetTag("error.context", context);
            if (!string.IsNullOrWhiteSpace(_serviceName))
            {
                scope.SetTag("service", _serviceName);
            }

            if (tags is not null)
            {
                foreach (var entry in tags)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                    {
                        scope.SetTag(entry.Key, entry.Value!);
                    }
                }
            }

            if (extras is not null)
            {
                foreach (var entry in extras)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key) && entry.Value is not null)
                    {
                        scope.SetExtra(entry.Key, entry.Value);
                    }
                }
            }

        });
    }

    public static void CaptureMessage(
        string message,
        string category,
        SentryLevel level = SentryLevel.Info,
        IReadOnlyDictionary<string, string?>? tags = null,
        IReadOnlyDictionary<string, object?>? extras = null)
    {
        AddBreadcrumb(message, category);
        SentrySdk.CaptureMessage(message, scope =>
        {
            if (!string.IsNullOrWhiteSpace(_serviceName))
            {
                scope.SetTag("service", _serviceName);
            }

            if (tags is not null)
            {
                foreach (var entry in tags)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                    {
                        scope.SetTag(entry.Key, entry.Value!);
                    }
                }
            }

            if (extras is not null)
            {
                foreach (var entry in extras)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Key) && entry.Value is not null)
                    {
                        scope.SetExtra(entry.Key, entry.Value);
                    }
                }
            }

        }, level);
    }

    public static async Task FlushAsync()
    {
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
    }

    private static string BuildReleaseName(string serviceName)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "dev";
        return $"{serviceName}@{version}";
    }
}
