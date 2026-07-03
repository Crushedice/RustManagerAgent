using RustOpsAgent.Core.Contracts;

namespace RustOpsAgent.Infrastructure.Memory;

internal sealed class NullMemoryStore : IInspectableMemoryStore
{
    public Task UpsertAsync(MemoryRecord record, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemorySearchRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemorySearchResult>>(Array.Empty<MemorySearchResult>());

    public Task<MemoryRecord?> GetByIdAsync(string id, CancellationToken cancellationToken) => Task.FromResult<MemoryRecord?>(null);

    public Task DeleteAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task MarkAccessedAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<int> CompactOrPruneAsync(CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<MemoryDebugStats> GetDebugStatsAsync(CancellationToken cancellationToken) => Task.FromResult(new MemoryDebugStats());

    public Task<IReadOnlyList<MemoryRecord>> ListRecentAsync(int maxResults, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemoryRecord>>(Array.Empty<MemoryRecord>());

    public Task<IReadOnlyList<MemoryRecord>> ListByApprovalStateAsync(MemoryApprovalState approvalState, int maxResults, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemoryRecord>>(Array.Empty<MemoryRecord>());

    public Task<IReadOnlyList<MemoryRecord>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MemoryRecord>>(Array.Empty<MemoryRecord>());

    public Task<bool> ExistsByContentHashAsync(string contentHash, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> ReinforceAsync(
        string id, double importanceDelta, double confidenceDelta, bool markVerified, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<int> PromotePendingAsync(
        IReadOnlyCollection<MemoryRecordType> types, double minConfidence, int maxToPromote, CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<int> DecayUnusedAsync(
        double importanceStep, int olderThanDays, int maxToDecay, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
