$root = ".\"

Get-ChildItem -Path $root -Recurse -File | ForEach-Object {
    try {
        # crude binary check: skip if null bytes present
        $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
        if ($bytes -contains 0) { return }

        $content = [System.Text.Encoding]::UTF8.GetString($bytes)
        if ($content.Contains("`r")) {
            $content = $content -replace "`r", ""
            [System.IO.File]::WriteAllText($_.FullName, $content, [System.Text.Encoding]::UTF8)
        }
    } catch {
        # ignore locked/inaccessible files
    }
}