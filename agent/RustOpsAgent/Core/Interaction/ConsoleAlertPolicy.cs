using RustOpsAgent.Core.Contracts;

namespace RustOpsAgent.Core.Interaction;

// Pure decision logic for console-error escalation: whether a given batch of errors is worth
// telling the admin about, given what we've already reported. Kept free of I/O so it can be
// unit-tested and reasoned about in isolation from the runtime.
internal static class ConsoleAlertPolicy
{
    internal enum AlertDecision
    {
        Suppress, // same known signature, recently reported, not escalating
        New,      // signature never alerted before
        Spike,    // known signature, but error volume jumped materially
        Reissue   // known signature, quiet for long enough to re-surface
    }

    // Order-independent fingerprint of the top error messages, so the same set of recurring errors
    // maps to one signature regardless of which is momentarily most frequent.
    internal static string Signature(IEnumerable<string> messages)
    {
        var keys = messages
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();
        return keys.Count == 0 ? "none" : string.Join(" || ", keys);
    }

    internal static AlertDecision Decide(
        AlertedSignatureState? prior, int alertCount, DateTime nowUtc, double reissueHours, double spikeFactor)
    {
        if (prior is null)
            return AlertDecision.New;

        if (prior.LastAlertedCount > 0 && alertCount >= prior.LastAlertedCount * Math.Max(1.0, spikeFactor))
            return AlertDecision.Spike;

        if (nowUtc - prior.LastAlertedAtUtc >= TimeSpan.FromHours(Math.Max(0.1, reissueHours)))
            return AlertDecision.Reissue;

        return AlertDecision.Suppress;
    }

    internal static string FormatAgo(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return "moments";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
        return $"{(int)span.TotalDays}d";
    }
}
