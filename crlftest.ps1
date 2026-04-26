$content = Get-Content ".\" -Raw
$crlf = ([regex]::Matches($content, "`r`n")).Count
$lf   = ([regex]::Matches($content, "(?<!`r)`n")).Count

"CRLF: $crlf"
"LF only: $lf"