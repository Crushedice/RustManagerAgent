using System.Text.Json;
using Microsoft.SemanticKernel;
using RustOpsAgent.Core.Contracts;

namespace RustOpsAgent.Core.Interaction;

internal sealed class AdminIntentClassifier : IIntentClassifier
{
    private readonly Kernel? _kernel;

    public AdminIntentClassifier(Kernel? kernel)
    {
        _kernel = kernel;
    }

    public async Task<AdminIntentRoute> ClassifyAsync(string message, ConversationSelectionState state, CancellationToken cancellationToken)
    {
        if (_kernel is null)
        {
            return HeuristicFallback(message, state);
        }

        var prompt = $$"""
Return strict JSON only with keys:
intent, confidence, needsClarification, clarificationQuestion, targetRef, slots

intent enum:
chat, server_control, player_lookup, rcon_command, file_edit, status_check, troubleshooting, clarification

targetRef enum:
rust.server.control, rust.player.lookup, rust.rcon.command, rust.status.check, rust.logs.inspect, rust.plugins.verify, rust.network.inspect, rust.chat.reply

slots object keys:
serverName, playerName, commandText, timeRange, severity

Conversation context:
lastServer={{state.LastServerName ?? ""}}
lastIntent={{state.LastIntent ?? ""}}
lastCommand={{state.LastCommandText ?? ""}}

Admin message:
{{message}}
""";

        string raw;
        try
        {
            var response = await _kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            raw = response.GetValue<string>() ?? string.Empty;
        }
        catch
        {
            return HeuristicFallback(message, state);
        }

        var json = TryExtractJson(raw);
        if (json is null)
        {
            return HeuristicFallback(message, state);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var lowered = message.ToLowerInvariant();
            var intentText = root.TryGetProperty("intent", out var intentNode) ? intentNode.GetString() ?? "clarification" : "clarification";
            var intent = ParseIntent(intentText);
            var confidence = root.TryGetProperty("confidence", out var confidenceNode) && confidenceNode.ValueKind == JsonValueKind.Number
                ? confidenceNode.GetDouble()
                : 0.4;
            var needsClarification = root.TryGetProperty("needsClarification", out var needsNode) && needsNode.ValueKind == JsonValueKind.True;
            var clarification = root.TryGetProperty("clarificationQuestion", out var questionNode) ? questionNode.GetString() : null;
            var targetRef = root.TryGetProperty("targetRef", out var targetNode) ? targetNode.GetString() : null;

            string? serverName = null;
            string? playerName = null;
            string? commandText = null;
            string? timeRange = null;
            string? severity = null;

            if (root.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Object)
            {
                serverName = slots.TryGetProperty("serverName", out var sn) ? sn.GetString() : null;
                playerName = slots.TryGetProperty("playerName", out var pn) ? pn.GetString() : null;
                commandText = slots.TryGetProperty("commandText", out var cn) ? cn.GetString() : null;
                timeRange = slots.TryGetProperty("timeRange", out var tn) ? tn.GetString() : null;
                severity = slots.TryGetProperty("severity", out var sv) ? sv.GetString() : null;
            }

            if (string.IsNullOrWhiteSpace(serverName) && ShouldUseLastServer(message))
            {
                serverName = state.LastServerName;
            }

            targetRef = NormalizeTargetRef(targetRef) ?? InferTargetRef(intent, lowered);

            return new AdminIntentRoute(
                intent,
                new AdminIntentSlots(serverName, playerName, commandText, timeRange, severity),
                Math.Clamp(confidence, 0.0, 1.0),
                needsClarification,
                clarification,
                targetRef);
        }
        catch
        {
            return HeuristicFallback(message, state);
        }
    }

    private static AdminIntentRoute HeuristicFallback(string message, ConversationSelectionState state)
    {
        var lowered = message.ToLowerInvariant();
        AdminIntentType intent;
        if (lowered.Contains("network") || lowered.Contains("throughput") || lowered.Contains("latency") || lowered.Contains("eth0") || lowered.Contains("wg1") || lowered.Contains("wt1"))
            intent = AdminIntentType.StatusCheck;
        else if (lowered.Contains("plugin") || lowered.Contains("umod") || lowered.Contains("oxide"))
            intent = AdminIntentType.Troubleshooting;
        else if (lowered.Contains("restart") || lowered.Contains("start") || lowered.Contains("stop") || lowered.Contains("kill") || lowered.Contains("update"))
            intent = AdminIntentType.ServerControl;
        else if (lowered.Contains("player") || lowered.Contains("ban"))
            intent = AdminIntentType.PlayerLookup;
        else if (lowered.Contains("rcon") || lowered.Contains("command") || lowered.Contains("say ") || lowered.Contains("global."))
            intent = AdminIntentType.RconCommand;
        else if (lowered.Contains("status") || lowered.Contains("health") || lowered.Contains("logs"))
            intent = AdminIntentType.StatusCheck;
        else if (lowered.Contains("fix") || lowered.Contains("error") || lowered.Contains("fail"))
            intent = AdminIntentType.Troubleshooting;
        else
            intent = AdminIntentType.Chat;

        var serverName = ShouldUseLastServer(message) ? state.LastServerName : null;
        var targetRef = InferTargetRef(intent, lowered);

        return new AdminIntentRoute(
            intent,
            new AdminIntentSlots(serverName, null, null, null, null),
            0.4,
            false,
            null,
            targetRef);
    }

    private static bool ShouldUseLastServer(string message)
    {
        var lowered = message.ToLowerInvariant();
        // Only reuse the last server for explicit follow-up phrasing, not generic uses of "it" or "same".
        return lowered.Contains("that one") ||
               lowered.Contains("same server") ||
               lowered.Contains("same one") ||
               lowered.Contains("again") ||
               lowered.Contains("restart it") ||
               lowered.Contains("stop it") ||
               lowered.Contains("start it") ||
               lowered.Contains("kill it") ||
               lowered.Contains("update it") ||
               lowered.Contains("check it");
    }

    private static string? InferTargetRef(AdminIntentType intent, string loweredMessage) =>
        intent switch
        {
            AdminIntentType.ServerControl => "rust.server.control",
            AdminIntentType.PlayerLookup => "rust.player.lookup",
            AdminIntentType.RconCommand => "rust.rcon.command",
            AdminIntentType.Chat or AdminIntentType.Clarification => "rust.chat.reply",
            AdminIntentType.StatusCheck or AdminIntentType.Troubleshooting => InferDiagnosticsTarget(loweredMessage),
            _ => null
        };

    private static string InferDiagnosticsTarget(string loweredMessage)
    {
        if (loweredMessage.Contains("network") || loweredMessage.Contains("latency") || loweredMessage.Contains("throughput") || loweredMessage.Contains("eth0") || loweredMessage.Contains("wg1") || loweredMessage.Contains("wt1"))
        {
            return "rust.network.inspect";
        }

        if (loweredMessage.Contains("plugin") || loweredMessage.Contains("umod") || loweredMessage.Contains("oxide"))
        {
            return "rust.plugins.verify";
        }

        if (loweredMessage.Contains("log") || loweredMessage.Contains("error") || loweredMessage.Contains("exception") || loweredMessage.Contains("fail"))
        {
            return "rust.logs.inspect";
        }

        return "rust.status.check";
    }

    private static string? NormalizeTargetRef(string? targetRef)
    {
        if (string.IsNullOrWhiteSpace(targetRef))
        {
            return null;
        }

        return targetRef.Trim().ToLowerInvariant() switch
        {
            "network" or "network.inspect" => "rust.network.inspect",
            "plugins" or "plugins.verify" or "plugin" => "rust.plugins.verify",
            "logs" or "logs.inspect" => "rust.logs.inspect",
            "status" or "status.check" => "rust.status.check",
            "server_control" => "rust.server.control",
            "player_lookup" => "rust.player.lookup",
            "rcon_command" => "rust.rcon.command",
            "chat" or "clarification" => "rust.chat.reply",
            _ => targetRef
        };
    }

    private static AdminIntentType ParseIntent(string value) => value.ToLowerInvariant() switch
    {
        "chat" => AdminIntentType.Chat,
        "server_control" => AdminIntentType.ServerControl,
        "player_lookup" => AdminIntentType.PlayerLookup,
        "rcon_command" => AdminIntentType.RconCommand,
        "file_edit" => AdminIntentType.FileEdit,
        "status_check" => AdminIntentType.StatusCheck,
        "troubleshooting" => AdminIntentType.Troubleshooting,
        _ => AdminIntentType.Clarification
    };

    private static string? TryExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return raw[start..(end + 1)];
    }
}
