internal sealed class RustLogManager
{
    public IEnumerable<string> ResolveLogPaths(string serverName)
    {
        yield return $"/srv/{serverName}/Log.txt";
        var oxideLogs = $"/srv/{serverName}/oxide/logs";
        if (Directory.Exists(oxideLogs))
        {
            foreach (var file in Directory.GetFiles(oxideLogs, "*.txt", SearchOption.TopDirectoryOnly))
                yield return file;
        }
    }
}
