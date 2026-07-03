using RustOpsAgent.Core.Contracts;
using RustOpsAgent.Core.Interaction;
using RustOpsAgent.Domains.Rust;

namespace RustOpsAgent.Tests;

// Covers the two response-quality fixes: memory-aware console-alert dedup (so the agent stops
// re-broadcasting the same known errors) and humanising raw API errors before they reach chat.
public class ResponseQualityTests
{
    [Fact]
    public void Signature_Is_Order_Independent_And_Normalised()
    {
        var a = ConsoleAlertPolicy.Signature(new[] { "Sqlite handle exception", "NullReference in callback" });
        var b = ConsoleAlertPolicy.Signature(new[] { "nullreference in callback", "  SQLITE handle exception " });
        Assert.Equal(a, b);
    }

    [Fact]
    public void Signature_Changes_When_A_New_Error_Type_Appears()
    {
        var before = ConsoleAlertPolicy.Signature(new[] { "Sqlite handle exception" });
        var after = ConsoleAlertPolicy.Signature(new[] { "Sqlite handle exception", "OutOfMemory" });
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Decide_New_When_No_Prior()
    {
        Assert.Equal(ConsoleAlertPolicy.AlertDecision.New,
            ConsoleAlertPolicy.Decide(null, alertCount: 12, DateTime.UtcNow, reissueHours: 6, spikeFactor: 3));
    }

    [Fact]
    public void Decide_Suppress_When_Recent_And_Steady()
    {
        var now = DateTime.UtcNow;
        var prior = new AlertedSignatureState { LastAlertedAtUtc = now.AddMinutes(-30), LastAlertedCount = 12, TimesAlerted = 1 };
        Assert.Equal(ConsoleAlertPolicy.AlertDecision.Suppress,
            ConsoleAlertPolicy.Decide(prior, alertCount: 13, now, reissueHours: 6, spikeFactor: 3));
    }

    [Fact]
    public void Decide_Spike_When_Count_Jumps()
    {
        var now = DateTime.UtcNow;
        var prior = new AlertedSignatureState { LastAlertedAtUtc = now.AddMinutes(-30), LastAlertedCount = 12, TimesAlerted = 1 };
        Assert.Equal(ConsoleAlertPolicy.AlertDecision.Spike,
            ConsoleAlertPolicy.Decide(prior, alertCount: 40, now, reissueHours: 6, spikeFactor: 3));
    }

    [Fact]
    public void Decide_Reissue_After_Window_Elapses()
    {
        var now = DateTime.UtcNow;
        var prior = new AlertedSignatureState { LastAlertedAtUtc = now.AddHours(-7), LastAlertedCount = 12, TimesAlerted = 2 };
        Assert.Equal(ConsoleAlertPolicy.AlertDecision.Reissue,
            ConsoleAlertPolicy.Decide(prior, alertCount: 13, now, reissueHours: 6, spikeFactor: 3));
    }

    [Fact]
    public void HumanizeApiError_Explains_Remote_Server()
    {
        var ex = new InvalidOperationException(
            "API POST /servers/monthly/kill failed: 400 Bad Request {\n  \"code\": \"remote_server\",\n  \"message\": \"This operation requires a remote agent for this server.\"\n}");
        var msg = RustServerControlToolHandler.HumanizeApiError("monthly", ex);
        Assert.Contains("monthly", msg);
        Assert.Contains("remote", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("API POST", msg);
        Assert.DoesNotContain("{", msg);
    }

    [Fact]
    public void HumanizeApiError_Explains_Timeout()
    {
        var msg = RustServerControlToolHandler.HumanizeApiError("cotton", new TimeoutException("API POST /servers/cotton/update timed out after 30s."));
        Assert.Contains("cotton", msg);
        Assert.Contains("timed out", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HumanizeApiError_Extracts_Json_Message_And_Drops_Noise()
    {
        var ex = new InvalidOperationException(
            "API POST /servers/sandbox/start failed: 400 Bad Request {\"ok\":false,\"message\":\"invalid -silent-crashes flag\"}");
        var msg = RustServerControlToolHandler.HumanizeApiError("sandbox", ex);
        Assert.Equal("sandbox: invalid -silent-crashes flag", msg);
    }

    [Fact]
    public void HumanizeApiError_Strips_Prefix_When_No_Json()
    {
        var ex = new InvalidOperationException("API GET /servers/vanilla/oxide/validate failed: 404 Not Found");
        var msg = RustServerControlToolHandler.HumanizeApiError("vanilla", ex);
        Assert.StartsWith("vanilla:", msg);
        Assert.DoesNotContain("API GET", msg);
        Assert.DoesNotContain("404", msg);
        Assert.Contains("Not Found", msg);
    }

    // --- Intent routing: unban must not be swallowed by player_lookup's "ban" rule ---

    [Theory]
    [InlineData("unban hophop on cotton")]
    [InlineData("unban 76561199645683644 everywhere")]
    [InlineData("lift the ban on hophop")]
    [InlineData("remove ban for hophop on all servers")]
    [InlineData("pardon hophop")]
    public async Task Classifier_Routes_Unban_To_PlayerModeration(string message)
    {
        var classifier = new AdminIntentClassifier(kernel: null);
        var state = new ConversationSelectionState { AdminId = "admin" };

        var route = await classifier.ClassifyAsync(message, state, new[] { "cotton", "sandbox" }, CancellationToken.None);

        Assert.Equal(AdminIntentType.PlayerModeration, route.Intent);
    }

    [Theory]
    [InlineData("show me the ban list on cotton")]
    [InlineData("who is banned on sandbox")]
    [InlineData("give me the playerlist from cotton")]
    public async Task Classifier_Keeps_BanList_And_Playerlist_As_Lookup(string message)
    {
        var classifier = new AdminIntentClassifier(kernel: null);
        var state = new ConversationSelectionState { AdminId = "admin" };

        var route = await classifier.ClassifyAsync(message, state, new[] { "cotton", "sandbox" }, CancellationToken.None);

        Assert.Equal(AdminIntentType.PlayerLookup, route.Intent);
    }
}
