using System.Text.Json;
using System.Text.Json.Nodes;
internal sealed class FocusedNetworkInspector
{
    private static readonly HashSet<string> AllowedInterfaces = new(StringComparer.OrdinalIgnoreCase) { "eth0", "wt1", "wg1" };

    public JsonArray FilterInterfaces(JsonElement interfaces)
    {
        var result = new JsonArray();
        foreach (var iface in interfaces.EnumerateArray())
        {
            var name = iface.GetProperty("name").GetString();
            if (!string.IsNullOrWhiteSpace(name) && AllowedInterfaces.Contains(name))
                result.Add(JsonNode.Parse(iface.GetRawText()));
        }

        return result;
    }
}
