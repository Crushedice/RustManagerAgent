using System.Reflection;
using Sentry;

internal static class RustOpsSentry
{
    private const string DefaultDsn = "https://8e1bbf090b73a90ebf5183bba0c7b07e@logging.rusticaland.ovh/7";
    private static bool _globalHandlersRegistered;

    public static IDisposable Initialize(string serviceName)
    {
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
        });

        RegisterGlobalHandlers(serviceName);
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

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AddBreadcrumb("Unobserved task exception captured.", "runtime");
            SentrySdk.CaptureException(args.Exception);
            args.SetObserved();
        };

        AddBreadcrumb($"Sentry initialized for {serviceName}.", "startup");
    }

    public static void AddBreadcrumb(string message, string category)
    {
        SentrySdk.AddBreadcrumb(message, category);
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
