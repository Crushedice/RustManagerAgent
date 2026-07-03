using RustOpsAgent.Core.Contracts;
using RustOpsAgent.Infrastructure.Memory;

namespace RustOpsAgent.Tests;

// Covers the memory "maturation" learning loop: outcome reinforcement, confidence-gated
// promotion of Pending knowledge, decay of unused noise, and failure->procedure synthesis.
public class MemoryLearningTests
{
    [Fact]
    public async Task ReinforceAsync_Adjusts_Clamps_And_Stamps_Verified()
    {
        var (store, _) = NewStore();
        var record = Record("recall me", type: MemoryRecordType.Fix, importance: 0.5, confidence: 0.9);
        await store.UpsertAsync(record, CancellationToken.None);
        Assert.Null((await store.GetByIdAsync(record.Id, CancellationToken.None))!.LastVerifiedUtc);

        var updated = await store.ReinforceAsync(record.Id, importanceDelta: 0.3, confidenceDelta: 0.4, markVerified: true, CancellationToken.None);

        Assert.True(updated);
        var after = await store.GetByIdAsync(record.Id, CancellationToken.None);
        Assert.Equal(0.8, after!.Importance, 3);
        Assert.Equal(1.0, after.Confidence, 3); // 0.9 + 0.4 clamped to 1.0
        Assert.NotNull(after.LastVerifiedUtc);
    }

    [Fact]
    public async Task ReinforceAsync_Floors_Importance_At_Zero_And_Leaves_Verified_When_Not_Asked()
    {
        var (store, _) = NewStore();
        var record = Record("decaying", importance: 0.1, confidence: 0.5);
        await store.UpsertAsync(record, CancellationToken.None);

        await store.ReinforceAsync(record.Id, importanceDelta: -0.5, confidenceDelta: 0.0, markVerified: false, CancellationToken.None);

        var after = await store.GetByIdAsync(record.Id, CancellationToken.None);
        Assert.Equal(0.0, after!.Importance, 3);
        Assert.Null(after.LastVerifiedUtc);
    }

    [Fact]
    public async Task PromotePendingAsync_Promotes_Only_Allowed_Types_Above_Confidence()
    {
        var (store, _) = NewStore();
        var ripeFact = Record("ripe fact", type: MemoryRecordType.Fact, confidence: 0.85, approval: MemoryApprovalState.Pending);
        var lowFact = Record("low-confidence fact", type: MemoryRecordType.Fact, confidence: 0.50, approval: MemoryApprovalState.Pending);
        var wrongType = Record("noisy exception", type: MemoryRecordType.Exception, confidence: 0.95, approval: MemoryApprovalState.Pending);
        await store.UpsertAsync(ripeFact, CancellationToken.None);
        await store.UpsertAsync(lowFact, CancellationToken.None);
        await store.UpsertAsync(wrongType, CancellationToken.None);

        var promoted = await store.PromotePendingAsync(
            new[] { MemoryRecordType.Fact }, minConfidence: 0.80, maxToPromote: 10, CancellationToken.None);

        Assert.Equal(1, promoted);
        Assert.Equal(MemoryApprovalState.Active, (await store.GetByIdAsync(ripeFact.Id, CancellationToken.None))!.ApprovalState);
        Assert.Equal(MemoryApprovalState.Pending, (await store.GetByIdAsync(lowFact.Id, CancellationToken.None))!.ApprovalState);
        Assert.Equal(MemoryApprovalState.Pending, (await store.GetByIdAsync(wrongType.Id, CancellationToken.None))!.ApprovalState);
    }

    [Fact]
    public async Task PromotePendingAsync_Respects_MaxPerRun_Limit()
    {
        var (store, _) = NewStore();
        for (var i = 0; i < 5; i++)
            await store.UpsertAsync(Record($"fact {i}", type: MemoryRecordType.Fact, confidence: 0.9, approval: MemoryApprovalState.Pending), CancellationToken.None);

        var promoted = await store.PromotePendingAsync(new[] { MemoryRecordType.Fact }, 0.80, maxToPromote: 2, CancellationToken.None);

        Assert.Equal(2, promoted);
    }

    [Fact]
    public async Task DecayUnusedAsync_Only_Touches_Aged_And_Unaccessed_Records()
    {
        var (store, _) = NewStore();
        var old = Record("old unused noise", importance: 0.5, createdAtUtc: DateTime.UtcNow.AddDays(-30));
        var recent = Record("recent unused", importance: 0.5, createdAtUtc: DateTime.UtcNow.AddDays(-1));
        var oldButAccessed = Record("old but useful", importance: 0.5, createdAtUtc: DateTime.UtcNow.AddDays(-30));
        await store.UpsertAsync(old, CancellationToken.None);
        await store.UpsertAsync(recent, CancellationToken.None);
        await store.UpsertAsync(oldButAccessed, CancellationToken.None);
        await store.MarkAccessedAsync(oldButAccessed.Id, CancellationToken.None);

        var decayed = await store.DecayUnusedAsync(importanceStep: 0.2, olderThanDays: 21, maxToDecay: 100, CancellationToken.None);

        Assert.Equal(1, decayed);
        Assert.Equal(0.3, (await store.GetByIdAsync(old.Id, CancellationToken.None))!.Importance, 3);
        Assert.Equal(0.5, (await store.GetByIdAsync(recent.Id, CancellationToken.None))!.Importance, 3);
        Assert.Equal(0.5, (await store.GetByIdAsync(oldButAccessed.Id, CancellationToken.None))!.Importance, 3);
    }

    [Fact]
    public async Task RunMaturationAsync_Promotes_Pending_And_Decays_Noise()
    {
        var root = NewRoot();
        var service = NewService(root, out var store);
        await store.UpsertAsync(Record("seeded ops fact", type: MemoryRecordType.Fact, confidence: 0.9, approval: MemoryApprovalState.Pending), CancellationToken.None);
        await store.UpsertAsync(Record("stale noise", importance: 0.5, createdAtUtc: DateTime.UtcNow.AddDays(-40)), CancellationToken.None);

        var report = await service.RunMaturationAsync(CancellationToken.None);

        Assert.True(report.Ran);
        Assert.True(report.Promoted >= 1);
        Assert.True(report.Decayed >= 1);
    }

    [Fact]
    public async Task RunMaturationAsync_Disabled_Is_A_Noop()
    {
        var root = NewRoot();
        var settings = Settings(root);
        settings.Learning.Enabled = false;
        var store = new SqliteMemoryStore(settings.DatabasePath, settings);
        var service = new SemanticMemoryService(settings, store, new FakeEmbeddingProvider(),
            Path.Combine(root, "legacy.json"), Path.Combine(root, "NeoCortex"));
        await store.UpsertAsync(Record("seeded fact", type: MemoryRecordType.Fact, confidence: 0.9, approval: MemoryApprovalState.Pending), CancellationToken.None);

        var report = await service.RunMaturationAsync(CancellationToken.None);

        Assert.False(report.Ran);
        Assert.False(report.HasActivity);
    }

    [Fact]
    public async Task RunMaturationAsync_Synthesizes_Procedure_From_Repeated_Failure_Then_Fix()
    {
        var root = NewRoot();
        var service = NewService(root, out var store);
        const string prefix = "ServerControl|rust.server.control|cotton";
        var lastFailure = DateTime.UtcNow.AddMinutes(-30);

        for (var i = 0; i < 3; i++)
        {
            await store.UpsertAsync(Record(
                $"Failure: ServerControl on cotton - update timed out #{i}",
                type: MemoryRecordType.Failure,
                createdAtUtc: lastFailure.AddMinutes(-i),
                metadata: new()
                {
                    ["actionFingerprint"] = $"{prefix}|api_error",
                    ["intent"] = "ServerControl",
                    ["selectedServer"] = "cotton",
                    ["errorCode"] = "api_error"
                }), CancellationToken.None);
        }

        await store.UpsertAsync(Record(
            "Success: ServerControl on cotton - Restart initiated for cotton",
            type: MemoryRecordType.Fix,
            createdAtUtc: DateTime.UtcNow,
            metadata: new()
            {
                ["actionFingerprint"] = prefix,
                ["intent"] = "ServerControl",
                ["selectedServer"] = "cotton",
                ["success"] = "True"
            }), CancellationToken.None);

        var report = await service.RunMaturationAsync(CancellationToken.None);

        Assert.Equal(1, report.ProceduresSynthesized);
        var all = await store.GetAllAsync(CancellationToken.None);
        var procedures = all.Where(r => r.Type == MemoryRecordType.Procedure).ToList();
        var procedure = Assert.Single(procedures);
        Assert.Equal(MemoryApprovalState.Active, procedure.ApprovalState);
        Assert.Contains("cotton", procedure.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("server:cotton", procedure.Tags);
        Assert.NotNull(procedure.LastVerifiedUtc);

        // Idempotent: a second pass dedups on content hash and synthesizes nothing new.
        var second = await service.RunMaturationAsync(CancellationToken.None);
        Assert.Equal(0, second.ProceduresSynthesized);
    }

    [Fact]
    public async Task RunMaturationAsync_Does_Not_Synthesize_When_No_Resolution_Exists()
    {
        var root = NewRoot();
        var service = NewService(root, out var store);
        for (var i = 0; i < 4; i++)
        {
            await store.UpsertAsync(Record(
                $"Failure: ServerControl on sandbox - kill failed #{i}",
                type: MemoryRecordType.Failure,
                createdAtUtc: DateTime.UtcNow.AddMinutes(-i),
                metadata: new()
                {
                    ["actionFingerprint"] = "ServerControl|rust.server.control|sandbox|api_error",
                    ["intent"] = "ServerControl",
                    ["selectedServer"] = "sandbox",
                    ["errorCode"] = "api_error"
                }), CancellationToken.None);
        }

        var report = await service.RunMaturationAsync(CancellationToken.None);

        Assert.Equal(0, report.ProceduresSynthesized);
        var all = await store.GetAllAsync(CancellationToken.None);
        Assert.DoesNotContain(all, r => r.Type == MemoryRecordType.Procedure);
    }

    // --- helpers ---

    private static (SqliteMemoryStore Store, string Root) NewStore()
    {
        var root = NewRoot();
        var settings = Settings(root);
        return (new SqliteMemoryStore(settings.DatabasePath, settings), root);
    }

    private static SemanticMemoryService NewService(string root, out SqliteMemoryStore store)
    {
        var settings = Settings(root);
        store = new SqliteMemoryStore(settings.DatabasePath, settings);
        return new SemanticMemoryService(settings, store, new FakeEmbeddingProvider(),
            Path.Combine(root, "legacy.json"), Path.Combine(root, "NeoCortex"));
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rustops-learning-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static MemorySettings Settings(string root) => new()
    {
        DatabasePath = Path.Combine(root, "memory.db"),
        SearchEnabled = true,
        WriteEnabled = true,
        SimilarityThreshold = 0.1,
        MaxRetrievedMemoriesPerStep = 6,
        MaxSearchCandidates = 200,
        MaxWritesPerWorkflowStep = 5,
        DebugLoggingEnabled = false
    };

    private static MemoryRecord Record(
        string summary,
        MemoryRecordType type = MemoryRecordType.Fact,
        MemoryScope scope = MemoryScope.Project,
        double importance = 0.6,
        double confidence = 0.7,
        MemoryApprovalState approval = MemoryApprovalState.Active,
        DateTime? createdAtUtc = null,
        Dictionary<string, string>? metadata = null)
    {
        var record = new MemoryRecord
        {
            Type = type,
            Scope = scope,
            Source = MemorySource.AgentAction,
            Summary = summary,
            Text = summary,
            Importance = importance,
            Confidence = confidence,
            ApprovalState = approval,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            Embedding = new[] { 1f, 0f, 0f, 0f },
            EmbeddingModel = "test",
            Metadata = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        record.Normalize();
        return record;
    }

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public string ModelName => "test-embedding";
        public int? Dimensions => 4;

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult(Embed(text));

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

        private static float[] Embed(string text)
        {
            var vector = new float[4];
            foreach (var ch in (text ?? string.Empty).ToLowerInvariant())
                vector[ch % 4] += ch;
            return vector;
        }
    }
}
