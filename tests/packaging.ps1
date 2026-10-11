$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot '../scripts/package-release.ps1'
if (-not (Test-Path $script)) { throw 'Release packaging script is missing.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('skillsync-package-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'input with spaces'
$output = Join-Path $root 'output'
New-Item -ItemType Directory -Force $source | Out-Null
try {
    foreach ($name in @('skillsync', 'skillsync.exe', 'README.md', 'LICENSE', 'unrelated.txt')) {
        [IO.File]::WriteAllText((Join-Path $source $name), "fixture-$name")
    }
    foreach ($rid in @('win-x64', 'linux-arm64', 'osx-x64')) {
        & $script -InputDirectory $source -OutputDirectory $output -RuntimeIdentifier $rid
        $extension = if ($rid.StartsWith('win-')) { '.zip' } else { '.tar.gz' }
        $asset = "skillsync-$rid$extension"
        $archivePath = Join-Path $output $asset
        $expectedHash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $checksum = [IO.File]::ReadAllText("$archivePath.sha256").Trim()
        if ($checksum -cne "$expectedHash  $asset") { throw "Invalid checksum for $rid" }
        if ($extension -eq '.zip') {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
            try {
                $entries = @($archive.Entries | ForEach-Object { $_.FullName })
                $reader = [IO.StreamReader]::new($archive.GetEntry('skillsync.exe').Open())
                try {
                    if ($reader.ReadToEnd() -ne 'fixture-skillsync.exe') { throw 'ZIP changed executable bytes.' }
                }
                finally { $reader.Dispose() }
            }
            finally { $archive.Dispose() }
            $binary = 'skillsync.exe'
        }
        else {
            $entries = @(& tar -tzf $archivePath)
            if ($LASTEXITCODE -ne 0) { throw "Invalid archive for $rid" }
            $content = & tar -xOzf $archivePath -- skillsync
            if ($LASTEXITCODE -ne 0 -or $content -ne 'fixture-skillsync') { throw 'TAR changed executable bytes.' }
            $binary = 'skillsync'
        }
        $actualNames = ($entries | Sort-Object) -join ','
        $expectedNames = (@('LICENSE', 'README.md', $binary) | Sort-Object) -join ','
        if ($actualNames -cne $expectedNames) {
            throw "Archive entries must be flat and contain only the executable, README and license: $rid"
        }
    }
    Remove-Item -LiteralPath (Join-Path $source 'skillsync.exe')
    $rejected = $false
    try { & $script -InputDirectory $source -OutputDirectory $output -RuntimeIdentifier win-arm64 }
    catch { $rejected = $true }
    if (-not $rejected -or (Test-Path (Join-Path $output 'skillsync-win-arm64.zip'))) {
        throw 'Missing executable must fail without producing a release archive.'
    }
    Write-Output 'PASS release archive layout, binary content, SHA-256 and missing executable checks.'
}
finally { Remove-Item -LiteralPath $root -Recurse -Force }
