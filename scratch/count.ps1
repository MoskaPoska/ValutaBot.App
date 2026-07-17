$lines = Get-Content -Path "MiniApp/MiniAppUI.cs"
$balance = 0
for ($i = 2122; $i -lt 2450; $i++) {
    $line = $lines[$i]
    $opens = ([regex]::Matches($line, '\{')).Count
    $closes = ([regex]::Matches($line, '\}')).Count
    $balance += $opens - $closes
    if ($opens -gt 0 -or $closes -gt 0) {
        Write-Host ("Line {0}: {1} (opens: {2}, closes: {3}, balance: {4})" -f ($i + 1), $line.Trim(), $opens, $closes, $balance)
    }
}
