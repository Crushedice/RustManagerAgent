using System.Text.Json;
internal sealed class UmodModule
{
    public bool TryValidateJsonConfig(string path, out string? error)
    {
        error = null;
        try
        {
            using var _ = JsonDocument.Parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
