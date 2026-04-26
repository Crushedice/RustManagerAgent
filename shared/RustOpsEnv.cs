using System.Text.RegularExpressions;

internal static partial class RustOpsEnv
{
    private static readonly Regex PlaceholderPattern = CreatePlaceholderPattern();

    public static void LoadFromDefaultLocations(string? anchorPath = null)
    {
        foreach (var candidate in GetCandidatePaths(anchorPath))
        {
            if (!File.Exists(candidate))
                continue;

            LoadFromFile(candidate);
            return;
        }
    }

    public static string? FirstNonEmptyEnvironment(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    public static bool GetBoolean(string name, bool fallback)
    {
        var value = FirstNonEmptyEnvironment(name);
        if (value is null)
            return fallback;

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    public static int GetInt32(string name, int fallback)
    {
        var value = FirstNonEmptyEnvironment(name);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    public static double GetDouble(string name, double fallback)
    {
        var value = FirstNonEmptyEnvironment(name);
        return double.TryParse(value, out var parsed) ? parsed : fallback;
    }

    public static string ResolvePlaceholders(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? string.Empty;

        return PlaceholderPattern.Replace(value, match =>
        {
            var name = match.Groups[1].Value.Trim();
            return Environment.GetEnvironmentVariable(name) ?? match.Value;
        });
    }

    public static List<string> GetCsvValues(string name)
    {
        var value = FirstNonEmptyEnvironment(name);
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    public static string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? string.Empty;

        return value
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    public static string ResolveConfiguredPath(string? configuredPath, string baseDir, string fallback)
    {
        var raw = ResolvePlaceholders(configuredPath);
        if (string.IsNullOrWhiteSpace(raw))
            raw = fallback;

        var normalized = NormalizePath(raw);
        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(baseDir, normalized));
    }

    public static bool HasUnresolvedPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return PlaceholderPattern.IsMatch(value);
    }

    private static void LoadFromFile(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                continue;

            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static IEnumerable<string> GetCandidatePaths(string? anchorPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var fullPath = Path.GetFullPath(NormalizePath(path));
            if (seen.Add(fullPath))
                candidates.Add(fullPath);
        }

        Add(FirstNonEmptyEnvironment("RUSTOPS_ENV_FILE"));

        foreach (var baseDir in GetBaseDirectories(anchorPath))
        {
            Add(Path.Combine(baseDir, "config.env"));
            Add(Path.Combine(baseDir, "..", "config.env"));
            Add(Path.Combine(baseDir, "..", "..", "config.env"));
            // Legacy fallback paths kept for backward compatibility during migration
            Add(Path.Combine(baseDir, "rustops.env"));
            Add(Path.Combine(baseDir, "config", "rustops.env"));
            Add(Path.Combine(baseDir, "..", "config", "rustops.env"));
            Add(Path.Combine(baseDir, "..", "..", "config", "rustops.env"));
        }

        return candidates;
    }

    private static IEnumerable<string> GetBaseDirectories(string? anchorPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var fullPath = Path.GetFullPath(NormalizePath(path));
            if (seen.Add(fullPath))
                directories.Add(fullPath);
        }

        Add(Directory.GetCurrentDirectory());
        Add(AppContext.BaseDirectory);

        if (!string.IsNullOrWhiteSpace(anchorPath))
        {
            var fullAnchorPath = Path.GetFullPath(anchorPath);
            Add(Path.GetDirectoryName(fullAnchorPath));
        }

        return directories;
    }

    [GeneratedRegex(@"\$\{([^}]+)\}", RegexOptions.Compiled)]
    private static partial Regex CreatePlaceholderPattern();
}
