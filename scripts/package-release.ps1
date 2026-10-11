param(
    [Parameter(Mandatory)][string]$InputDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)]
    [ValidateSet('linux-x64', 'linux-arm64', 'win-x64', 'win-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier
)
$ErrorActionPreference = 'Stop'
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$windowsTarget = $RuntimeIdentifier.StartsWith('win-')
$binary = if ($windowsTarget) { 'skillsync.exe' } else { 'skillsync' }
$files = @($binary, 'README.md', 'LICENSE')
foreach ($file in $files) {
    if (-not [IO.File]::Exists((Join-Path $InputDirectory $file))) { throw "Missing release file: $file" }
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$extension = if ($windowsTarget) { '.zip' } else { '.tar.gz' }
$asset = "skillsync-$RuntimeIdentifier$extension"
$destination = Join-Path $OutputDirectory $asset
if ($windowsTarget) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $InputDirectory $file), $file) | Out-Null
        }
    }
    finally { $archive.Dispose() }
}
else {
    & tar -czf $destination -C $InputDirectory $binary README.md LICENSE
    if ($LASTEXITCODE -ne 0) { throw "Cannot package $RuntimeIdentifier" }
}
$hash = (Get-FileHash $destination -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$destination.sha256", "$hash  $asset" + [char]10, [Text.Encoding]::ASCII)
Write-Output $destination
