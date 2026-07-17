# Read MiniAppUI.cs
$content = Get-Content -Path "MiniApp/MiniAppUI.cs" -Raw

# Extract everything between <script> and </script>
# We look for the first occurrence of <script> and the next </script>
$startIndex = $content.IndexOf("<script>")
if ($startIndex -eq -1) {
    Write-Host "Could not find <script> tag"
    exit 1
}
$startIndex += "<script>".Length

$endIndex = $content.IndexOf("</script>", $startIndex)
if ($endIndex -eq -1) {
    Write-Host "Could not find </script> tag"
    exit 1
}

$jsText = $content.Substring($startIndex, $endIndex - $startIndex)

# Replace C# raw string interpolation placeholders or template literal symbols that might be raw in C#
# Specifically, check if there are raw {host} or double braces {{ }} in C#
# Since we just want syntax check, let's replace C# variables if they look like C# code.
# In C#, double braces {{ }} represent single braces in interpolated strings.
# Let's replace any double braces with single braces if the C# string uses them,
# or let's see: in MiniAppUI.cs, does it use interpolated string?
# Let's write the raw JS first and see node syntax check output.
$jsText = $jsText -replace '\$\{host\}', '"https://example.com"'

# Write to scratch/miniapp.js
New-Item -ItemType Directory -Force -Path "scratch"
$jsText | Out-File -FilePath "scratch/miniapp.js" -Encoding utf8

# Run node syntax check
$nodeOutput = & node -c scratch/miniapp.js 2>&1
Write-Host "Node Syntax Check Output:"
Write-Host $nodeOutput
