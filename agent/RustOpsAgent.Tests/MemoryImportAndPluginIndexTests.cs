using System.Net;
using RustOpsAgent.Core.Contracts;
using RustOpsAgent.Core.Interaction;
using RustOpsAgent.Domains.Rust;
using RustOpsAgent.Infrastructure;
using RustOpsAgent.Infrastructure.Memory;

namespace RustOpsAgent.Tests;

public class MemoryImportAndPluginIndexTests
{
    [Fact]
    public async Task MemoryImport_Imports_Markdown_Frontmatter_As_Active()
    {
        var root = TempRoot();
        var seed = Path.Combine(root, "knowledge", "verified");
        Directory.CreateDirectory(seed);
        var file = Path.Combine(seed, "infrastructure.md");
        await File.WriteAllTextAsync(file, """
        ---
        title: Rusticaland VPS routing
        category: infrastructure
        tags: [vps, routing, wireguard, rusticaland]
        confidence: 0.95
        importance: 0.9
        sourceType: ManualImport
        approval: Active
        lastVerifiedUtc: 2026-04-29T00:00:00Z
        ---

        # Infrastructure
        VPS public IP forwards to the Windows server.
        """);

        var (service, store, settings) = MakeMemory(root);
        settings.MemoryImport.TrustedSeedFolders = new List<string> { seed };
        settings.MemoryImport.NearDuplicateThreshold = 1.01;
        var importer = new MemoryImportService(settings, service, store);

        var report = await importer.ImportFolderAsync(new MemoryImportOptions { FolderPath = seed }, CancellationToken.None);
        var records = await service.ListRecentAsync(10, CancellationToken.None);

        Assert.Equal(1, report.Imported);
        var record = Assert.Single(records);
        Assert.Equal(MemoryApprovalState.Active, record.ApprovalState);
        Assert.Equal(MemorySource.ManualImport, record.Source);
        Assert.Equal("infrastructure", record.Category);
        Assert.Contains("vps", record.Tags);
        Assert.Equal(0.95, record.Confidence, 2);
    }

    [Fact]
    public async Task MemoryImport_Chunks_Ignores_Duplicates_And_Is_Idempotent()
    {
        var root = TempRoot();
        var seed = Path.Combine(root, "seed");
        Directory.CreateDirectory(seed);
        Directory.CreateDirectory(Path.Combine(seed, "bin"));
        await File.WriteAllTextAsync(Path.Combine(seed, "notes.md"), """
        # Infrastructure
        First useful fact.

        ## Routing
        Second useful fact.
        """);
        await File.WriteAllTextAsync(Path.Combine(seed, "bin", "ignored.md"), "ignored");

        var (service, store, settings) = MakeMemory(root);
        settings.MemoryImport.TrustedSeedFolders = new List<string> { seed };
        settings.MemoryImport.NearDuplicateThreshold = 1.01;
        var importer = new MemoryImportService(settings, service, store);

        var first = await importer.ImportFolderAsync(new MemoryImportOptions { FolderPath = seed, Trusted = true }, CancellationToken.None);
        var second = await importer.ImportFolderAsync(new MemoryImportOptions { FolderPath = seed, Trusted = true }, CancellationToken.None);
        var records = await service.ListRecentAsync(20, CancellationToken.None);

        Assert.Equal(2, first.Imported);
        Assert.True(second.Duplicates >= 2);
        Assert.Equal(2, records.Count);
        Assert.DoesNotContain(records, record => record.SourcePath.Contains("ignored", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(records, record => record.Metadata["headingPath"] == "Infrastructure");
        Assert.Contains(records, record => record.Metadata["headingPath"] == "Infrastructure > Routing");
    }

    [Fact]
    public async Task MemoryApproval_Excludes_Pending_Until_Approved()
    {
        var root = TempRoot();
        var (service, _, _) = MakeMemory(root);
        var pending = await service.AddManualMemoryAsync(new ManualMemoryInput
        {
            Summary = "Pending route fact",
            Text = "Pending route fact should not be recalled.",
            Confidence = 0.99,
            ApprovalState = MemoryApprovalState.Pending
        }, CancellationToken.None);
        await service.AddManualMemoryAsync(new ManualMemoryInput
        {
            Summary = "Active route fact",
            Text = "Active route fact should be recalled.",
            Confidence = 0.99,
            ApprovalState = MemoryApprovalState.Active
        }, CancellationToken.None);

        var before = await service.RecallForPlanningAsync("route fact", new ConversationSelectionState(), Array.Empty<string>(), CancellationToken.None);
        await service.SetApprovalStateAsync(pending.Id, MemoryApprovalState.Active, CancellationToken.None);
        var after = await service.RecallForPlanningAsync("route fact", new ConversationSelectionState(), Array.Empty<string>(), CancellationToken.None);

        Assert.DoesNotContain(before.Results, result => result.MemoryRecord.Id == pending.Id);
        Assert.Contains(after.Results, result => result.MemoryRecord.Id == pending.Id);
    }

    [Fact]
    public async Task AiGenerated_Import_Defaults_To_Pending()
    {
        var root = TempRoot();
        var seed = Path.Combine(root, "knowledge", "ai-generated");
        Directory.CreateDirectory(seed);
        await File.WriteAllTextAsync(Path.Combine(seed, "claude-review-import.json"), """[{ "title": "AI note", "content": "AI generated claim." }]""");

        var (service, store, settings) = MakeMemory(root);
        var importer = new MemoryImportService(settings, service, store);

        await importer.ImportFolderAsync(new MemoryImportOptions { FolderPath = seed }, CancellationToken.None);
        var pending = await service.ListPendingAsync(10, CancellationToken.None);

        Assert.NotEmpty(pending);
        Assert.All(pending, record => Assert.Equal(MemorySource.AiGeneratedImport, record.Source));
    }

    [Fact]
    public void PluginReferenceExtractor_Extracts_Common_Oxide_Patterns()
    {
        var source = """
        [Info("Kits", "Facepunch", "1.2.3")]
        [Description("Kit menu")]
        class Kits : RustPlugin
        {
            [ChatCommand("kit")]
            void KitCommand(BasePlayer player, string command, string[] args) {}
            [ConsoleCommand("inventory.give")]
            void Give(ConsoleSystem.Arg arg) {}
            [Command("help")]
            void Help(IPlayer player, string command, string[] args) {}
            void Init() { cmd.AddChatCommand("remove", this, nameof(CommandRemove)); AddCovalenceCommand("serverinfo", nameof(ServerInfoCommand)); permission.RegisterPermission("kits.admin", this); }
            void OnPlayerDeath(BasePlayer player, HitInfo info) {}
            bool CanLootEntity(BasePlayer player, BaseEntity entity) => true;
            void LoadDefaultConfig() { Config["SomeKey"] = true; GetConfig("OtherKey", true); }
            [JsonProperty("Some Config Key")] public bool Enabled;
        }
        """;

        var record = PluginReferenceExtractor.Extract("monthly", "Kits.cs", source);

        Assert.Contains(record.Commands, command => command.Command == "kit" && command.Type == "ChatCommand");
        Assert.Contains(record.Commands, command => command.Command == "inventory.give" && command.Type == "ConsoleCommand");
        Assert.Contains(record.Commands, command => command.Command == "help" && command.Type == "CovalenceCommand");
        Assert.Contains(record.Commands, command => command.Command == "remove");
        Assert.Contains(record.Commands, command => command.Command == "serverinfo");
        Assert.Contains("kits.admin", record.Permissions);
        Assert.Contains("OnPlayerDeath", record.Hooks);
        Assert.Contains("CanLootEntity", record.Hooks);
        Assert.Contains("SomeKey", record.ConfigKeys);
        Assert.Contains("Some Config Key", record.ConfigKeys);
    }

    [Fact]
    public async Task PluginReferenceIndex_Skips_Unchanged_And_Updates_On_Hash_Change()
    {
        var root = TempRoot();
        var store = new SqlitePluginReferenceIndexStore(Path.Combine(root, "plugin-index.db"));
        var first = PluginReferenceExtractor.Extract("monthly", "Backpacks.cs", "[Info(\"Backpacks\", \"A\", \"1.0.0\")] class Backpacks : RustPlugin { [ChatCommand(\"backpack\")] void B(){} }");
        await store.UpsertAsync(first, "source1", CancellationToken.None);

        var existing = await store.GetBySourcePathAsync("Backpacks.cs", CancellationToken.None);
        var changed = PluginReferenceExtractor.Extract("monthly", "Backpacks.cs", "[Info(\"Backpacks\", \"A\", \"1.0.1\")] class Backpacks : RustPlugin { [ChatCommand(\"backpack\")] void B(){} [ChatCommand(\"viewbackpack\")] void V(){} }");
        await store.UpsertAsync(changed, "source2", CancellationToken.None);
        var updated = await store.GetBySourcePathAsync("Backpacks.cs", CancellationToken.None);

        Assert.NotNull(existing);
        Assert.NotEqual(existing!.SourceHash, changed.SourceHash);
        Assert.NotNull(updated);
        Assert.Equal("1.0.1", updated!.Version);
        Assert.Contains(updated.Commands, command => command.Command == "viewbackpack");
    }

    [Fact]
    public async Task RustChatToolHandler_Routes_Player_Command_Query_To_Plugin_Index_Safely()
    {
        var root = TempRoot();
        var store = new SqlitePluginReferenceIndexStore(Path.Combine(root, "plugin-index.db"));
        var record = PluginReferenceExtractor.Extract("monthly", "Kits.cs", """
        [Info("Kits", "A", "1.0.0")]
        class Kits : RustPlugin
        {
            [ChatCommand("kit")] void Kit() {}
            [ChatCommand("adminkit")] void AdminKit() { permission.UserHasPermission("1", "kits.admin"); }
        }
        """);
        await store.UpsertAsync(record, "raw source should not appear", CancellationToken.None);

        var memory = new CommandMemoryService();
        var indexer = new PluginReferenceIndexer(new RustOpsApiClient(new ApiSettings { BaseUrl = "http://127.0.0.1:1", ApiKey = "test" }), store, memory);
        var handler = new RustChatToolHandler(MakeNeoCortex(root), memory, pluginReferenceIndexer: indexer);

        var result = await ExecuteChatCommandAsync(handler, "What commands can players use from Kits?");

        Assert.Contains("/kit", result.Message);
        Assert.DoesNotContain("adminkit", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw source", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RustChatToolHandler_Imports_Server_Catalog_With_Specific_Memory_Types()
    {
        var root = TempRoot();
        var variablesPath = Path.Combine(root, "ServerVariables.agent-readable.jsonl");
        var commandsPath = Path.Combine(root, "ServerCommands.agent-readable.jsonl");
        await File.WriteAllTextAsync(variablesPath, """
        {"convar":"server.pve","generated_on_start":true,"default_raw":"False","default_type":"boolean","description":"Enables PvE mode"}
        """);
        await File.WriteAllTextAsync(commandsPath, """
        {"command":"server.readcfg","generated_command_metadata":true,"description":"Reads and executes server config files","risk_level_inferred":"safe","tags":["config"]}
        """);

        var (service, _, _) = MakeMemory(root);
        var knowledge = new ServerKnowledgeCatalog(variablesPath, commandsPath);
        var handler = new RustChatToolHandler(MakeNeoCortex(root), service, knowledge: knowledge);

        var result = await ExecuteChatCommandAsync(handler, "memory import server catalog");
        var records = await service.ListRecentAsync(10, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(records, record => record.Type == MemoryRecordType.ServerConvar && record.Source == MemorySource.ServerCatalog && record.Summary.Contains("server.pve", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(records, record => record.Type == MemoryRecordType.ServerCommand && record.Source == MemorySource.ServerCatalog && record.Summary.Contains("server.readcfg", StringComparison.OrdinalIgnoreCase));
    }

    private static (SemanticMemoryService Service, IInspectableMemoryStore Store, MemorySettings Settings) MakeMemory(string root)
    {
        var settings = new MemorySettings
        {
            DatabasePath = Path.Combine(root, "memory.db"),
            SearchEnabled = true,
            WriteEnabled = true,
            SimilarityThreshold = 0.1,
            MinimumRecallConfidence = 0.55,
            MaxSearchCandidates = 100,
            MaxRetrievedMemoriesPerStep = 10,
            MaxInjectedMemoryCharacters = 4000
        };
        var store = new SqliteMemoryStore(settings.DatabasePath, settings);
        var service = new SemanticMemoryService(settings, store, new FakeEmbeddingProvider(), Path.Combine(root, "state.json"), Path.Combine(root, "NeoCortex"));
        return (service, store, settings);
    }

    private static Task<ToolExecutionResult> ExecuteChatCommandAsync(RustChatToolHandler handler, string message)
    {
        var route = new AdminIntentRoute(AdminIntentType.Chat, new AdminIntentSlots(null, null, null, null, null), 0.9, false, null, "rust.chat.reply");
        return handler.ExecuteAsync(new ToolExecutionContext("admin", message, route, new ConversationSelectionState { AdminId = "admin" }, DateTime.UtcNow), CancellationToken.None);
    }

    private static NeoCortexStore MakeNeoCortex(string root) =>
        new(Path.Combine(root, "NeoCortex"), Path.Combine(root, "legacy-state.json"));

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rustops-import-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public string ModelName => "fake";
        public int? Dimensions => 4;
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken) => Task.FromResult(Embed(text));
        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

        private static float[] Embed(string text)
        {
            var vector = new float[4];
            foreach (var ch in (text ?? string.Empty).ToLowerInvariant())
            {
                vector[ch % 4] += ch;
            }

            return vector;
        }
    }

    private sealed class CommandMemoryService : ISemanticMemoryService
    {
        public Task<WorkflowMemoryContext> RecallForPlanningAsync(string message, ConversationSelectionState state, IReadOnlyList<string> knownServers, CancellationToken cancellationToken) => Task.FromResult(WorkflowMemoryContext.Empty);
        public Task<WorkflowMemoryContext> RecallForExecutionAsync(ToolExecutionContext context, CancellationToken cancellationToken) => Task.FromResult(WorkflowMemoryContext.Empty);
        public Task RecordActionOutcomeAsync(ToolExecutionContext context, ToolExecutionResult result, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordUserInstructionAsync(string? adminId, string? serverName, string instruction, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordReflectionAsync(string summary, string detail, IReadOnlyList<string> tags, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordServerFactAsync(string serverName, string summary, string detail, IReadOnlyList<string> tags, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MemoryDebugStats> GetStatsAsync(CancellationToken cancellationToken) => Task.FromResult(new MemoryDebugStats());
        public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MemorySearchResult>>(Array.Empty<MemorySearchResult>());
        public Task<MemoryRecord?> GetByIdAsync(string id, CancellationToken cancellationToken) => Task.FromResult<MemoryRecord?>(null);
        public Task DeleteAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<MemoryRecord>> ListRecentAsync(int maxResults, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MemoryRecord>>(Array.Empty<MemoryRecord>());
        public Task<IReadOnlyList<IGrouping<string, MemoryRecord>>> ListRepeatedFailuresAsync(int minOccurrences, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IGrouping<string, MemoryRecord>>>(Array.Empty<IGrouping<string, MemoryRecord>>());
        public Task<MemoryRecord> AddManualMemoryAsync(ManualMemoryInput input, CancellationToken cancellationToken) => Task.FromResult(new MemoryRecord { Summary = input.Summary, Text = input.Text });
        public Task<MemoryImportDisposition> ImportRecordAsync(MemoryRecord record, CancellationToken cancellationToken) => Task.FromResult(MemoryImportDisposition.Imported);
        public Task<IReadOnlyList<MemoryRecord>> ListPendingAsync(int maxResults, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MemoryRecord>>(Array.Empty<MemoryRecord>());
        public Task<bool> SetApprovalStateAsync(string id, MemoryApprovalState approvalState, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> RebuildEmbeddingsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<MemoryMigrationReport> MigrateLegacyMemoryAsync(bool dryRun, CancellationToken cancellationToken) => Task.FromResult(new MemoryMigrationReport());
        public Task<int> PruneAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task ReinforceRecalledMemoriesAsync(WorkflowMemoryContext? recall, bool success, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MemoryMaturationReport> RunMaturationAsync(CancellationToken cancellationToken) => Task.FromResult(new MemoryMaturationReport());
    }
}
