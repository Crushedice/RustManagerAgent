using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RustOpsAgent.Core.Contracts;
using RustOpsAgent.Infrastructure;

namespace RustOpsAgent.Infrastructure.Memory;

internal sealed class MemoryImportService : IMemoryImportService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".txt", ".json" };
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj", "logs", "node_modules" };

    private readonly MemorySettings _settings;
    private readonly ISemanticMemoryService _semanticMemory;
    private readonly IInspectableMemoryStore _store;
    private readonly MemoryChunker _chunker;
    private readonly MemoryDeduplicator _deduplicator;

    public MemoryImportService(MemorySettings settings, ISemanticMemoryService semanticMemory, IInspectableMemoryStore store)
    {
        _settings = settings;
        _semanticMemory = semanticMemory;
        _store = store;
        _chunker = new MemoryChunker(settings.MemoryImport);
        _deduplicator = new MemoryDeduplicator(settings.MemoryImport, semanticMemory, store);
    }

    public async Task<MemoryImportReport> ImportFolderAsync(MemoryImportOptions options, CancellationToken cancellationToken)
    {
        var report = new MemoryImportReport();
        var root = Path.GetFullPath(options.FolderPath);
        if (!Directory.Exists(root))
        {
            report.Errors++;
            report.Messages.Add($"Folder not found: {root}");
            return report;
        }

        var trusted = options.Trusted || IsTrustedFolder(root);
        foreach (var path in EnumerateImportFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            report.FilesScanned++;

            try
            {
                var document = await ImportDocument.LoadAsync(path, root, cancellationToken);
                var chunks = _chunker.Chunk(document).ToList();
                report.ChunksDiscovered += chunks.Count;
                for (var i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    if (string.IsNullOrWhiteSpace(chunk.Text))
                    {
                        report.Skipped++;
                        continue;
                    }

                    var sourceType = ResolveSource(document.FrontMatter, path);
                    var approval = ResolveApproval(document.FrontMatter, sourceType, trusted);
                    var normalizedHash = MemoryTextHasher.HashNormalized(chunk.Text);
                    var record = new MemoryRecord
                    {
                        Type = MemoryRecordType.Fact,
                        Scope = MemoryScope.Project,
                        Source = sourceType,
                        ApprovalState = approval,
                        Title = document.Title,
                        Summary = BuildSummary(document, chunk),
                        Text = chunk.Text,
                        SourcePath = document.RelativePath,
                        SourceHash = normalizedHash,
                        ChunkIndex = i,
                        Category = document.Category,
                        LastVerifiedUtc = document.LastVerifiedUtc,
                        Importance = document.Importance,
                        Confidence = document.Confidence,
                        Tags = document.Tags.ToList(),
                        RelatedEntityIds = BuildRelatedIds(document, chunk),
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["sourceFileHash"] = document.FileHash,
                            ["headingPath"] = chunk.HeadingPath,
                            ["sourceRoot"] = root,
                            ["importedAtUtc"] = DateTime.UtcNow.ToString("O")
                        }
                    };

                    var duplicate = await _deduplicator.FindDuplicateAsync(record, chunk.Text, cancellationToken);
                    if (duplicate is not null)
                    {
                        report.Duplicates++;
                        await TryUpdateDuplicateMetadataAsync(duplicate, record, cancellationToken);
                        continue;
                    }

                    if (options.DryRun)
                    {
                        report.Skipped++;
                        continue;
                    }

                    var disposition = await _semanticMemory.ImportRecordAsync(record, cancellationToken);
                    if (disposition == MemoryImportDisposition.Imported)
                    {
                        report.Imported++;
                        if (approval == MemoryApprovalState.Pending)
                        {
                            report.Pending++;
                        }
                    }
                    else if (disposition == MemoryImportDisposition.Duplicate)
                    {
                        report.Duplicates++;
                    }
                    else
                    {
                        report.Skipped++;
                    }
                }
            }
            catch (Exception ex)
            {
                report.Errors++;
                report.Messages.Add($"{path}: {ex.Message}");
            }
        }

        return report;
    }

    private bool IsTrustedFolder(string root)
    {
        var trustedFolders = _settings.MemoryImport.TrustedSeedFolders ?? new List<string>();
        return trustedFolders
            .SelectMany(path => ResolveTrustedFolderCandidates(path))
            .Any(path => root.StartsWith(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ResolveTrustedFolderCandidates(string path)
    {
        if (Path.IsPathRooted(path))
        {
            yield return Path.GetFullPath(path);
            yield break;
        }

        yield return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static IEnumerable<string> EnumerateImportFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(directory)))
                {
                    pending.Push(directory);
                }
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                if (AllowedExtensions.Contains(Path.GetExtension(file)))
                {
                    yield return file;
                }
            }
        }
    }

    private static MemorySource ResolveSource(Dictionary<string, string> frontMatter, string path)
    {
        if (frontMatter.TryGetValue("sourceType", out var source) &&
            Enum.TryParse<MemorySource>(source, true, out var parsed))
        {
            return parsed;
        }

        return path.Contains($"{Path.DirectorySeparatorChar}ai-generated{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? MemorySource.AiGeneratedImport
            : MemorySource.SeededImport;
    }

    private MemoryApprovalState ResolveApproval(Dictionary<string, string> frontMatter, MemorySource source, bool trusted)
    {
        if (frontMatter.TryGetValue("approval", out var approval) &&
            Enum.TryParse<MemoryApprovalState>(approval, true, out var parsed))
        {
            if (source == MemorySource.AiGeneratedImport && !trusted && parsed == MemoryApprovalState.Active)
            {
                return MemoryApprovalState.Pending;
            }

            return parsed;
        }

        if (source == MemorySource.ManualImport || source == MemorySource.SeededImport || source == MemorySource.VerifiedFact)
        {
            return trusted ? MemoryApprovalState.Active : _settings.MemoryImport.DefaultApprovalState;
        }

        return source == MemorySource.AiGeneratedImport && !trusted
            ? MemoryApprovalState.Pending
            : _settings.MemoryImport.DefaultApprovalState;
    }

    private static string BuildSummary(ImportDocument document, MemoryChunk chunk)
    {
        var title = string.IsNullOrWhiteSpace(document.Title) ? Path.GetFileName(document.RelativePath) : document.Title;
        return string.IsNullOrWhiteSpace(chunk.HeadingPath)
            ? title
            : $"{title}: {chunk.HeadingPath}";
    }

    private static List<string> BuildRelatedIds(ImportDocument document, MemoryChunk chunk)
    {
        var ids = new List<string> { document.RelativePath, document.Category };
        if (!string.IsNullOrWhiteSpace(chunk.HeadingPath))
        {
            ids.AddRange(chunk.HeadingPath.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }

        ids.AddRange(document.Tags);
        return ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task TryUpdateDuplicateMetadataAsync(MemoryRecord existing, MemoryRecord incoming, CancellationToken cancellationToken)
    {
        if (existing.Source == MemorySource.VerifiedFact && incoming.Confidence <= existing.Confidence)
        {
            return;
        }

        var newerVerification = incoming.LastVerifiedUtc.HasValue &&
                                (!existing.LastVerifiedUtc.HasValue || incoming.LastVerifiedUtc > existing.LastVerifiedUtc);
        if (incoming.Confidence <= existing.Confidence && !newerVerification)
        {
            return;
        }

        existing.Importance = Math.Max(existing.Importance, incoming.Importance);
        existing.Confidence = Math.Max(existing.Confidence, incoming.Confidence);
        existing.LastVerifiedUtc = newerVerification ? incoming.LastVerifiedUtc : existing.LastVerifiedUtc;
        existing.Tags = existing.Tags.Concat(incoming.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        existing.RelatedEntityIds = existing.RelatedEntityIds.Concat(incoming.RelatedEntityIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var entry in incoming.Metadata)
        {
            existing.Metadata[entry.Key] = entry.Value;
        }

        existing.UpdatedAtUtc = DateTime.UtcNow;
        await _store.UpsertAsync(existing, cancellationToken);
    }
}

internal sealed class MemoryChunker
{
    private readonly MemoryImportSettings _settings;

    public MemoryChunker(MemoryImportSettings settings) => _settings = settings;

    public IEnumerable<MemoryChunk> Chunk(ImportDocument document)
    {
        if (Path.GetExtension(document.SourcePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var chunk in ChunkJson(document))
            {
                yield return chunk;
            }

            yield break;
        }

        var sections = SplitMarkdownSections(document.Body);
        foreach (var section in sections)
        {
            foreach (var chunk in SplitLargeText(section.Text, section.HeadingPath))
            {
                yield return chunk;
            }
        }
    }

    private IEnumerable<MemoryChunk> ChunkJson(ImportDocument document)
    {
        using var parsed = JsonDocument.Parse(document.Body);
        if (parsed.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in parsed.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return new MemoryChunk(item.GetRawText(), string.Empty);
                }
            }

            yield break;
        }

        yield return new MemoryChunk(parsed.RootElement.GetRawText(), string.Empty);
    }

    private IEnumerable<MemoryChunk> SplitLargeText(string text, string headingPath)
    {
        var normalized = text.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        var maxChars = Math.Max(200, _settings.ChunkMaxTokens * 4);
        if (normalized.Length <= maxChars)
        {
            yield return new MemoryChunk(normalized, headingPath);
            yield break;
        }

        var paragraphs = Regex.Split(normalized, @"\r?\n\s*\r?\n")
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
        var builder = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            if (builder.Length > 0 && builder.Length + paragraph.Length > maxChars)
            {
                yield return new MemoryChunk(builder.ToString().Trim(), headingPath);
                builder.Clear();
            }

            builder.AppendLine(paragraph.Trim());
            builder.AppendLine();
        }

        if (builder.Length > 0)
        {
            yield return new MemoryChunk(builder.ToString().Trim(), headingPath);
        }
    }

    private static IReadOnlyList<MemoryChunk> SplitMarkdownSections(string text)
    {
        var results = new List<MemoryChunk>();
        var headings = new List<string>();
        var builder = new StringBuilder();
        var currentHeading = string.Empty;

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var match = Regex.Match(line, @"^(?<level>#{1,6})\s+(?<title>.+?)\s*$");
            if (match.Success)
            {
                Flush();
                var level = match.Groups["level"].Value.Length;
                while (headings.Count >= level)
                {
                    headings.RemoveAt(headings.Count - 1);
                }

                headings.Add(match.Groups["title"].Value.Trim());
                currentHeading = string.Join(" > ", headings);
                continue;
            }

            builder.AppendLine(line);
        }

        Flush();
        return results;

        void Flush()
        {
            var section = builder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(section))
            {
                results.Add(new MemoryChunk(section, currentHeading));
            }

            builder.Clear();
        }
    }
}

internal sealed class MemoryDeduplicator
{
    private readonly MemoryImportSettings _settings;
    private readonly ISemanticMemoryService _semanticMemory;
    private readonly IInspectableMemoryStore _store;

    public MemoryDeduplicator(MemoryImportSettings settings, ISemanticMemoryService semanticMemory, IInspectableMemoryStore store)
    {
        _settings = settings;
        _semanticMemory = semanticMemory;
        _store = store;
    }

    public async Task<MemoryRecord?> FindDuplicateAsync(MemoryRecord incoming, string text, CancellationToken cancellationToken)
    {
        var all = await _store.GetAllAsync(cancellationToken);
        var normalizedHash = incoming.SourceHash;
        var exact = all.FirstOrDefault(record =>
            string.Equals(record.SourceHash, normalizedHash, StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(record.SourcePath, incoming.SourcePath, StringComparison.OrdinalIgnoreCase) &&
             string.Equals(record.SourceHash, normalizedHash, StringComparison.OrdinalIgnoreCase)));
        if (exact is not null)
        {
            return exact;
        }

        if (_settings.NearDuplicateThreshold <= 0)
        {
            return null;
        }

        try
        {
            var results = await _semanticMemory.SearchAsync(text, 5, cancellationToken);
            return results.FirstOrDefault(result => result.SimilarityScore >= _settings.NearDuplicateThreshold)?.MemoryRecord;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record MemoryChunk(string Text, string HeadingPath);

internal sealed class ImportDocument
{
    public string SourcePath { get; private set; } = string.Empty;
    public string RelativePath { get; private set; } = string.Empty;
    public string FileHash { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public IReadOnlyList<string> Tags { get; private set; } = Array.Empty<string>();
    public double Importance { get; private set; } = 0.7;
    public double Confidence { get; private set; } = 0.75;
    public DateTime? LastVerifiedUtc { get; private set; }
    public Dictionary<string, string> FrontMatter { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<ImportDocument> LoadAsync(string path, string root, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        var frontMatter = ParseFrontMatter(ref text);
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        var title = Read(frontMatter, "title") ?? ExtractFirstHeading(text) ?? Path.GetFileNameWithoutExtension(path);
        var tags = ParseTags(Read(frontMatter, "tags"));

        return new ImportDocument
        {
            SourcePath = path,
            RelativePath = relative,
            FileHash = MemoryTextHasher.HashRaw(text),
            Body = text.Trim(),
            Title = title.Trim(),
            Category = Read(frontMatter, "category") ?? string.Empty,
            Tags = tags,
            Importance = ParseDouble(Read(frontMatter, "importance"), 0.7),
            Confidence = ParseDouble(Read(frontMatter, "confidence"), 0.75),
            LastVerifiedUtc = DateTime.TryParse(Read(frontMatter, "lastVerifiedUtc"), out var verified) ? verified.ToUniversalTime() : null,
            FrontMatter = frontMatter
        };
    }

    private static Dictionary<string, string> ParseFrontMatter(ref string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalized = text.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return result;
        }

        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            return result;
        }

        var block = normalized[4..end];
        foreach (var line in block.Split('\n'))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0)
            {
                continue;
            }

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        text = normalized[(end + "\n---\n".Length)..];
        return result;
    }

    private static string? Read(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string? ExtractFirstHeading(string text)
    {
        var match = Regex.Match(text, @"^\s*#\s+(?<title>.+?)\s*$", RegexOptions.Multiline);
        return match.Success ? match.Groups["title"].Value.Trim() : null;
    }

    private static IReadOnlyList<string> ParseTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Trim().Trim('[', ']')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.Trim('"', '\''))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0.0, 1.0)
            : fallback;
}

internal static class MemoryTextHasher
{
    public static string HashNormalized(string text)
    {
        var normalized = Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();
        return HashRaw(normalized);
    }

    public static string HashRaw(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(bytes);
    }
}
