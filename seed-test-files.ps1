$root = "/tmp/browse"
New-Item -ItemType Directory -Force -Path $root | Out-Null
New-Item -ItemType Directory -Force -Path "$root/documents" | Out-Null
New-Item -ItemType Directory -Force -Path "$root/images" | Out-Null
New-Item -ItemType Directory -Force -Path "$root/logs" | Out-Null
New-Item -ItemType Directory -Force -Path "$root/data" | Out-Null
New-Item -ItemType Directory -Force -Path "$root/data/reports" | Out-Null

$extensions = @(".txt", ".log", ".csv", ".json", ".xml", ".md", ".html", ".css", ".js", ".yml")
$folders = @("$root", "$root/documents", "$root/images", "$root/logs", "$root/data", "$root/data/reports")

for ($i = 1; $i -le 1200; $i++) {
    $ext = $extensions[$i % $extensions.Count]
    $folder = $folders[$i % $folders.Count]
    $name = "file-{0:D3}{1}" -f $i, $ext
    $size = Get-Random -Minimum 10 -Maximum 5000
    $content = "x" * $size
    Set-Content -Path (Join-Path $folder $name) -Value $content
}

Write-Host "Created 1200 test files across $(($folders).Count) directories in $root"
