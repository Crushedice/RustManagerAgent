using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using RustOpsAgent.Core.Contracts;

namespace RustOpsAgent.Domains.Rust;

/// <summary>
/// Handles web lookup requests for uMod plugin docs, Rust convar info, and Oxide API references.
/// Keyless: tries DuckDuckGo's Instant Answer API first, then falls back to DuckDuckGo's HTML
/// results endpoint (real organic results) when the instant answer is empty — which is the common
/// case for player-support questions. No API key is required for either path.
/// </summary>
internal sealed class WebSearchToolHandler : IToolHandler
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "RustOpsAgent/1.0" } }
    };

    public string Name => "web.search";

    public IReadOnlyCollection<AdminIntentType> EligibleIntents => new[]
    {
        AdminIntentType.Chat,
        AdminIntentType.Troubleshooting,
        AdminIntentType.RconCommand
    };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query = ExtractSearchQuery(context.Message);
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ToolExecutionResult(false, "No search query could be extracted.", null, false, "no_query");
        }

        var scopeToRust = ShouldScopeToRust(context.Message, query);
        try
        {
            var result = await SearchAsync(query, scopeToRust, cancellationToken);
            if (string.IsNullOrWhiteSpace(result))
            {
                return new ToolExecutionResult(false,
                    $"No results found for: {query}. Try rephrasing or check umod.org directly.",
                    null, false, "no_results");
            }

            return new ToolExecutionResult(true, result, null, false, Payload: new { query, result });
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, $"Web search failed: {ex.Message}", null, false, "search_error");
        }
    }

    /// <summary>
    /// Reusable, keyless web search. Returns a plain-text block of findings suitable for injecting
    /// into an LLM prompt, or null/empty when nothing was found. Callers (e.g. the in-game stand-in
    /// admin) use this to give the model fresh context for questions it can't answer from memory.
    /// </summary>
    public static async Task<string?> SearchAsync(string query, bool scopeToRust, CancellationToken cancellationToken)
    {
        var scopedQuery = scopeToRust
            ? $"site:umod.org OR site:wiki.facepunch.com {query}"
            : query;

        var instant = await TryInstantAnswerAsync(scopedQuery, cancellationToken);
        if (!string.IsNullOrWhiteSpace(instant))
        {
            return instant;
        }

        // Instant Answer is abstract-only and empty for most real questions — fall back to the
        // keyless HTML results endpoint, which returns actual organic titles + snippets.
        return await TryHtmlResultsAsync(scopedQuery, cancellationToken);
    }

    private static async Task<string?> TryInstantAnswerAsync(string scopedQuery, CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(scopedQuery);
        var url = $"https://api.duckduckgo.com/?q={encoded}&format=json&no_html=1&skip_disambig=1";
        var response = await _http.GetStringAsync(url, cancellationToken);
        using var doc = JsonDocument.Parse(response);

        var abstractText = doc.RootElement.TryGetProperty("Abstract", out var absNode) ? absNode.GetString() : null;
        var abstractSource = doc.RootElement.TryGetProperty("AbstractSource", out var srcNode) ? srcNode.GetString() : null;
        var abstractUrl = doc.RootElement.TryGetProperty("AbstractURL", out var urlNode) ? urlNode.GetString() : null;

        var relatedTopics = doc.RootElement.TryGetProperty("RelatedTopics", out var rtNode) && rtNode.ValueKind == JsonValueKind.Array
            ? rtNode.EnumerateArray()
                .Take(3)
                .Where(t => t.TryGetProperty("Text", out _))
                .Select(t => t.GetProperty("Text").GetString())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList()
            : new List<string?>();

        if (string.IsNullOrWhiteSpace(abstractText) && relatedTopics.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(abstractText))
        {
            parts.Add(abstractText!);
            if (!string.IsNullOrWhiteSpace(abstractSource))
            {
                parts.Add($"Source: {abstractSource} - {abstractUrl}");
            }
        }

        if (relatedTopics.Count > 0)
        {
            parts.AddRange(relatedTopics.Where(t => t is not null).Select(t => $"- {t}"));
        }

        return string.Join("\n", parts);
    }

    private static readonly Regex SnippetRegex = new(
        "<a[^>]*class=\"result__snippet\"[^>]*>(?<snippet>.*?)</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

    private static async Task<string?> TryHtmlResultsAsync(string scopedQuery, CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(scopedQuery);
        var url = $"https://html.duckduckgo.com/html/?q={encoded}";
        var html = await _http.GetStringAsync(url, cancellationToken);

        var snippets = SnippetRegex.Matches(html)
            .Select(m => StripHtml(m.Groups["snippet"].Value))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .Take(4)
            .Select(s => $"- {s}")
            .ToList();

        return snippets.Count == 0 ? null : string.Join("\n", snippets);
    }

    private static string StripHtml(string raw)
    {
        var noTags = TagRegex.Replace(raw, string.Empty);
        return WebUtility.HtmlDecode(noTags).Trim();
    }

    private static string ExtractSearchQuery(string message)
    {
        var prefixes = new[] { "search for", "search", "look up", "lookup", "find", "what is", "how does", "tell me about", "docs for", "documentation for" };
        var trimmed = message.Trim();
        var lowered = trimmed.ToLowerInvariant();
        foreach (var prefix in prefixes)
        {
            if (lowered.StartsWith(prefix, StringComparison.Ordinal))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }

        return trimmed;
    }

    public static bool ShouldScopeToRust(string message, string query)
    {
        var lower = $"{message} {query}".ToLowerInvariant();
        return lower.Contains("plugin") || lower.Contains("oxide") || lower.Contains("umod")
            || lower.Contains("rust server") || lower.Contains("convar") || lower.Contains("permission");
    }
}
