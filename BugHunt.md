# RustOpsAgent Post-Refactor Audit
Analyzed against: Next_Steps.md (the original refactor plan)

---

## CRITICAL BUGS — WILL BREAK AT RUNTIME

---

### BUG-01 · GitOpsService is dead code
**File:** `Program.cs`
```csharp
_ = new GitOpsService(config.GitOps); // discarded immediately
```
The object is created and thrown away. `AgentRuntime` never receives it, no evolution path can call it, and the entire GitOps workflow (branch, commit, PR) is fully disconnected. This means the plan's requirement that code-changing paths use GitOps is not met — it's scaffolded but wired to nothing.

**Fix:** Inject `IGitOpsService` into `AgentRuntime`. Pass the instance instead of discarding it.

---

### BUG-02 · `git pr create` is not a git command
**File:** `Infrastructure/GitOps/GitOpsService.cs`
```csharp
var cmd = $"pr create --base {_settings.BaseBranch} ...";
return await RunGitAsync(cmd, cancellationToken); // FileName = "git"
```
`pr create` is a GitHub CLI (`gh`) command. `git pr create` does not exist. This throws `InvalidOperationException("git pr create failed: ...")` every time a PR is attempted.

**Fix:** Either change `FileName` to `"gh"` for this specific call, or refactor `RunGitAsync` to accept an optional override for the executable. Verify `gh` is installed in the deploy environment.

---

### BUG-03 · 3-minute countdown blocks the entire inbox loop
**File:** `Domains/Rust/RustToolHandlers.cs` — `RustServerControlToolHandler.ExecuteAsync`
```csharp
await Task.Delay(TimeSpan.FromMinutes(3), cancellationToken); // inline, not background
```
This runs inside `ProcessChatInboxAsync`, blocking all inbox processing (chat, feedback, decisions) for 3 full minutes. The inbox file is also held for deletion until after the delay. Any message sent during those 3 minutes will not be seen until the countdown completes.

**Fix:** Fire the countdown as a background task. Return immediately with a confirmation message. Example:
```csharp
_ = Task.Run(async () => {
    await Task.Delay(TimeSpan.FromMinutes(3), CancellationToken.None);
    await _api.PostAsync($"/servers/{Uri.EscapeDataString(server)}/restart", new { }, CancellationToken.None);
}, cancellationToken);
return new ToolExecutionResult(true, $"Restart countdown started for {server}. Server will restart in ~3 minutes.", server, true);
```

---

### BUG-04 · `JsonDocument` leak in countdown restart path
**File:** `Domains/Rust/RustToolHandlers.cs`
```csharp
await _api.PostAsync($"/servers/.../command", new { command = "say Server restart in 3 minutes" }, cancellationToken);
// returned JsonDocument never disposed
```
`PostAsync` returns a `JsonDocument` which is an `IDisposable`. The return value is discarded entirely. Repeated use causes unmanaged memory accumulation.

**Fix:** Add `using var _ =` or switch to a void-returning overload on `RustOpsApiClient` for fire-and-forget POST calls.

---

### BUG-05 · RconRollingLogMonitor accumulates dead event subscriptions
**File:** `Domains/Rust/RustToolHandlers.cs` — `RustRconToolHandler`
```csharp
private readonly RconRollingLogMonitor _rconLogMonitor = new(); // per handler instance

// inside TryExecuteDirectRconAsync, called every RCON command:
await using IRconClient client = new RustRconClient();
_rconLogMonitor.Attach(client); // subscribes to client.UnsolicitedMessage
await client.ConnectAsync(...);
// client disposed here — subscription still registered on _rconLogMonitor
```
Every call to `TryExecuteDirectRconAsync` attaches a new subscription and disposes the client. The `IRconClient.UnsolicitedMessage` event is subscribed to but never detached. `RconRollingLogMonitor` has no `Detach` method. The monitor grows in dead subscriptions across every RCON command.

**Fix:** Add a `Detach(IRconClient)` method to `RconRollingLogMonitor`. Call it before the client is disposed. Alternatively, the monitor should manage one persistent client instance rather than attaching to transient ones.

---

### BUG-06 · `RustRconClient.DisposeAsync` does not fault pending in-flight tasks
**File:** `Domains/Rust/Rcon/RustRconClient.cs`
```csharp
public async ValueTask DisposeAsync()
{
    try { _receiveCts?.Cancel(); } catch { }
    // _pending TCS entries NOT faulted or cancelled
}
```
If `SendCommandAsync` is awaiting a response when `DisposeAsync` is called, those `TaskCompletionSource`es hang until the internal 10-second timeout fires. In a fast connect→command→dispose cycle, this creates a 10-second stall per command before the task is finally cancelled.

**Fix:** Iterate `_pending` in `DisposeAsync` and call `TrySetCanceled` or `TrySetException` on all pending TCS entries before shutting down the socket.

---

## SIGNIFICANT ISSUES — WRONG BEHAVIOR OR DESIGN VIOLATIONS

---

### ISSUE-07 · `GitOpsService` constructor silently mutates shared config
**File:** `Infrastructure/GitOps/GitOpsService.cs`
```csharp
public GitOpsService(GitOpsSettings settings)
{
    _settings = settings;
    _settings.PushBranchPrefix = "agent/"; // mutates the shared reference
}
```
`Program.cs` already validates this before construction. The constructor then ignores the validated value and forcibly overwrites it. The shared `GitOpsSettings` object held by `config` is mutated as a side-effect of the constructor, which is unexpected and fragile.

**Fix:** Remove the assignment in the constructor. The config is already validated upstream. If a runtime guard is wanted, add a read-only assertion only.

---

### ISSUE-08 · `ShouldUseLastServer` triggers on "it " (any sentence)
**File:** `Core/Interaction/AdminIntentClassifier.cs` and `Domains/Rust/RustToolHandlers.cs`
```csharp
return lowered.Contains("that one") || lowered.Contains("again") || lowered.Contains("it ") || lowered.Contains("same");
```
`lowered.Contains("it ")` matches nearly every sentence that contains the word "it" as a subject or object: "check it", "what is it", "does it work", "fix it", etc. This causes unrelated messages to incorrectly inherit `lastServerName`, routing commands to the wrong server.

**Fix:** Either remove `"it "` from this list entirely, or replace it with stronger indicators like `"restart it"`, `"stop it"`, `"check it"` where a verb precedes "it" directly in an operational context.

---

### ISSUE-09 · `ToolRegistry.ResolveSingle` re-implements classifier keyword matching
**File:** `Core/Interaction/ToolRegistry.cs`
```csharp
if (message.Contains("network") || message.Contains("latency") || ...)
    return eligible.FirstOrDefault(h => h.Name == "rust.network.inspect") ?? eligible[0];
if (message.Contains("plugin") || message.Contains("umod") || ...)
    ...
```
The same keyword lists that exist in `AdminIntentClassifier.HeuristicFallback` are duplicated here for tool disambiguation. They will drift independently, creating inconsistencies between routing and execution.

**Fix:** The `AdminIntentRoute` already carries the `TargetRef` slot. Use a more specific intent type, or extend the `AdminIntentSlots` with a `ToolHint` field set by the classifier. The registry should select based on structured data from the route, not re-parse the raw message.

---

### ISSUE-10 · `FileEdit` intent is declared but has no real handler
**File:** `Domains/Rust/RustChatToolHandler.cs`
```csharp
public IReadOnlyCollection<AdminIntentType> EligibleIntents =>
    new[] { AdminIntentType.Chat, AdminIntentType.Clarification, AdminIntentType.FileEdit };
```
`FileEdit` is caught by the chat handler and returns a static string. There is no tool that actually performs file edits through GitOps. The plan required file edits to be gated through the evolution/GitOps workflow — that workflow exists on paper (GitOps classes exist) but nothing connects them.

**Fix:** Either remove `FileEdit` from the eligible intents until a real handler exists, or create a `RustFileEditToolHandler` that uses `IGitOpsService` properly. Do not leave a dead intent silently handled as chat.

---

### ISSUE-11 · `async Task` methods with no `await` — CS1998 warnings
**File:** `Core/AgentRuntime.cs`
```csharp
private async Task ProcessFeedbackInboxAsync(CancellationToken cancellationToken)
private async Task ProcessDecisionInboxAsync(CancellationToken cancellationToken)
```
Both methods are `async Task` but contain no `await` expressions. They read files and call synchronous methods only. The compiler generates CS1998 for each. They run synchronously but wrap in a state machine unnecessarily.

**Fix:** Either remove `async` and change the return to `void` or `Task` returned directly, or add actual async file I/O (`File.ReadAllTextAsync`).

---

### ISSUE-12 · `LegacyAgentState` retains dead self-repair schema
**File:** `Infrastructure/Memory/LegacyAgentStateStore.cs`
```csharp
public List<LegacySelfRepairHistoryEntry> SelfRepairHistory { get; set; } = new();
public List<LegacyCapabilityGapEntry> CapabilityGaps { get; set; } = new();
public List<LegacyLlmInteractionEntry> LlmInteractions { get; set; } = new();
```
The plan explicitly states: "The self-repair function is seemingly useless... Do not keep the concept named self-repair." These fields exist in the model, are written to disk on every `Save()`, and consume storage and serialization overhead. Nothing writes to them post-refactor.

**Fix:** Remove `SelfRepairHistory`, `CapabilityGaps`, and `LlmInteractions` from `LegacyAgentState` and delete the corresponding class definitions. If migration from existing state files is needed, handle in `EnsureMigrated()`.

---

### ISSUE-13 · `RconRollingLogMonitor` is per-command, not persistent — spec violation
The plan states: "The agent should have a small separate process that keeps a rolling Log from the Rcon available and monitors it."

The current implementation creates a brand new `RustRconClient` for each RCON command, attaches the monitor, executes one command, and disposes the client. The monitor never accumulates meaningful log data between commands because each connection only lives for the duration of a single command execution.

**Fix:** The `RconRollingLogMonitor` and a persistent `RustRconClient` connection should be owned at a higher level — either a background service started alongside the agent, or a long-lived component per server. The tool handler should read from the monitor's snapshot rather than creating transient connections.

---

### ISSUE-14 · `RustOpsApiClient` has no timeout — 100-second default
**File:** `Infrastructure/RustOpsApiClient.cs`
```csharp
_http = new HttpClient
{
    BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/")
};
// No Timeout set — defaults to 100 seconds
```
If the API is down or slow, every GET or POST stalls for up to 100 seconds. During a countdown or loop cycle, this compounds directly with BUG-03.

**Fix:** Set a reasonable timeout (e.g., 15–30 seconds) on the `HttpClient`:
```csharp
_http = new HttpClient { BaseAddress = ..., Timeout = TimeSpan.FromSeconds(20) };
```

---

### ISSUE-15 · `WaitForExpectedServerStateAsync` ignores its own parameters
**File:** `shared/RustMgrExecutor.cs`
```csharp
public async Task<bool?> WaitForExpectedServerStateAsync(string server, string operation, int attempts = 5, int delaySeconds = 2)
{
    var verification = await VerifyExpectedServerStateAsync(server, operation);
    // attempts and delaySeconds are completely ignored
    return verification?.ReachedExpectedState;
}
```
The method signature suggests customizable verification behavior, but the parameters are silently dropped. Any caller expecting different attempt counts or delays gets the hardcoded defaults from `GetVerificationTiming`.

**Fix:** Either pass `attempts` and `delaySeconds` through to `VerifyExpectedServerStateAsync` (requires a refactor of that method's signature), or remove the parameters from this method's signature entirely to avoid the false contract.

---

### ISSUE-16 · `NeoCortexStore.EnsureMigrated` overwrites existing files if marker is absent
**File:** `Infrastructure/Memory/NeoCortexStore.cs`
If the migration marker file (`.migration-complete`) does not exist but the target NeoCortex files already do (e.g., partial run, crash before marker write), the migration overwrites them with empty default objects, destroying any existing data.

**Fix:** Before writing each NeoCortex file during migration, check if it already exists and skip it:
```csharp
if (!File.Exists(_operationsPath)) SaveJson(_operationsPath, active);
```
This is exactly what `EnsureSeedFiles()` does — the migration path should behave the same way for files that already exist.

---

### ISSUE-17 · `_stop` flag not checked mid-inbox processing
**File:** `Core/AgentRuntime.cs`
The `while (!_stop)` guard only runs at the top of the loop. Inside `ProcessChatInboxAsync`, the `foreach` over inbox files does not re-check `_stop`. On a large or misbehaving inbox, `RequestStop()` will not be honored until all pending files are processed.

**Fix:** Replace `cancellationToken.ThrowIfCancellationRequested()` calls in the loop body with a check that also tests `_stop`:
```csharp
if (_stop || cancellationToken.IsCancellationRequested)
    return;
```

---

## MINOR / CODE QUALITY

---

### MINOR-18 · `ScoreImportance` accepts `string line` but silently expects lowercased input
**File:** `Domains/Rust/RustToolHandlers.cs`
```csharp
private static int ScoreImportance(string line, IEnumerable<string> dynamicRules)
{
    if (line.Contains("exception") || ...) return 3;
```
The caller always passes a lowercased string, but the method signature and internal checks don't communicate this. A future caller passing a non-lowercased string would silently miss matches.

**Fix:** Either lowercase inside the method, or rename the parameter to `lowercasedLine` to make the contract explicit.

---

### MINOR-19 · `RustPluginToolHandler` uses a static `HttpClient` that is never disposed
```csharp
private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
```
Fine for reuse, but since it's `static`, it outlives any individual handler instance with no disposal path. Not a runtime problem, but inconsistent with the rest of the codebase.

---

## SUMMARY TABLE

| ID | Severity | File | Description |
|----|----------|------|-------------|
| BUG-01 | **Critical** | Program.cs | GitOpsService discarded immediately, entire GitOps flow dead |
| BUG-02 | **Critical** | GitOpsService.cs | `git pr create` does not exist; should be `gh pr create` |
| BUG-03 | **Critical** | RustToolHandlers.cs | 3-min countdown blocks entire inbox loop |
| BUG-04 | **High** | RustToolHandlers.cs | JsonDocument leak in countdown path |
| BUG-05 | **High** | RustToolHandlers.cs | RconRollingLogMonitor accumulates dead event subscriptions |
| BUG-06 | **High** | RustRconClient.cs | In-flight TCS not faulted on dispose, 10s stall per command |
| ISSUE-07 | **Medium** | GitOpsService.cs | Constructor mutates shared config reference |
| ISSUE-08 | **Medium** | AdminIntentClassifier.cs | `"it "` triggers false last-server inheritance on any sentence |
| ISSUE-09 | **Medium** | ToolRegistry.cs | Duplicates classifier keyword lists, drift-prone |
| ISSUE-10 | **Medium** | RustChatToolHandler.cs | FileEdit intent is a dead-end with no real handler |
| ISSUE-11 | **Low** | AgentRuntime.cs | async methods with no await, CS1998 warnings |
| ISSUE-12 | **Low** | LegacyAgentStateStore.cs | Dead self-repair schema still in model (plan violation) |
| ISSUE-13 | **Medium** | RustToolHandlers.cs | RconRollingLogMonitor is per-command, spec requires persistent |
| ISSUE-14 | **Medium** | RustOpsApiClient.cs | No HTTP timeout, defaults to 100 seconds |
| ISSUE-15 | **Low** | RustMgrExecutor.cs | WaitForExpectedServerStateAsync ignores its own parameters |
| ISSUE-16 | **Medium** | NeoCortexStore.cs | EnsureMigrated overwrites existing data if marker absent |
| ISSUE-17 | **Low** | AgentRuntime.cs | _stop flag not checked mid-inbox iteration |
| MINOR-18 | **Low** | RustToolHandlers.cs | ScoreImportance silently expects pre-lowercased input |
| MINOR-19 | **Low** | RustToolHandlers.cs | Static HttpClient never disposed |

---

## RECOMMENDED FIX ORDER FOR AI EXECUTION

Phase A — Critical bugs (runtime failures):
1. BUG-02: Fix `git pr create` → `gh pr create` in `GitOpsService`
2. BUG-03: Move countdown restart to background task
3. BUG-04: Dispose JsonDocument from first PostAsync in countdown
4. BUG-01: Inject `IGitOpsService` into `AgentRuntime`, wire it for Evolution incidents
5. BUG-05: Add `Detach` to `RconRollingLogMonitor`, call before client dispose
6. BUG-06: Fault all pending TCS in `RustRconClient.DisposeAsync`

Phase B — Correctness and spec compliance:
7. ISSUE-14: Add HTTP timeout to `RustOpsApiClient`
8. ISSUE-16: Guard EnsureMigrated against overwriting existing files
9. ISSUE-08: Tighten `ShouldUseLastServer` — remove `"it "`
10. ISSUE-12: Remove dead self-repair fields from `LegacyAgentState`
11. ISSUE-13: Extract persistent RCON monitor as a background component
12. ISSUE-10: Remove `FileEdit` from `RustChatToolHandler.EligibleIntents` until a real handler exists

Phase C — Quality and cleanup:
13. ISSUE-07: Remove config mutation from `GitOpsService` constructor
14. ISSUE-09: Replace ToolRegistry keyword matching with route-derived data
15. ISSUE-11: Remove `async` from synchronous processing methods
16. ISSUE-15: Fix `WaitForExpectedServerStateAsync` parameter passthrough or drop parameters
17. ISSUE-17: Check `_stop` inside inbox processing loops
18. MINOR-18/19: Minor quality fixes
