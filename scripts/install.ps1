param(
    [string]$Version = 'latest',
    [string]$InstallDir = '',
    [switch]$NoPathUpdate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SkillSyncPath([string]$Existing, [string]$Directory) {
    foreach ($entry in $Existing.Split(';')) {
        if ($entry.TrimEnd('\') -ieq $Directory.TrimEnd('\')) { return $Existing }
    }
    if ([string]::IsNullOrEmpty($Existing)) { return $Directory }
    return "$Existing;$Directory"
}

function Invoke-SkillSyncInstall([string]$ReleaseVersion, [string]$Directory, [bool]$SkipPath) {
    if ($ReleaseVersion -ne 'latest' -and $ReleaseVersion -cnotmatch '^v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9]+([.-][A-Za-z0-9]+)*)?$') {
        throw 'Version must be latest or a tag such as v0.1.0-preview.4.'
    }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git is required. Install Git, then run this installer again.'
    }
    if ($env:OS -ne 'Windows_NT') { throw 'Use install.sh on Linux and macOS.' }
    $machineArchitecture = $env:PROCESSOR_ARCHITEW6432
    if (-not $machineArchitecture) { $machineArchitecture = $env:PROCESSOR_ARCHITECTURE }
    $architecture = switch ($machineArchitecture) {
        'AMD64' { 'x64' }
        'ARM64' { 'arm64' }
        default { throw 'Supported architectures: x64 and arm64.' }
    }
    if (-not $Directory) { $Directory = Join-Path $env:LOCALAPPDATA 'SkillSync\bin' }
    $Directory = [IO.Path]::GetFullPath($Directory)
    if ($Directory -match '[;\x00-\x1f\x7f]') { throw 'Invalid install directory.' }
    $baseUrl = $env:SKILLSYNC_RELEASE_BASE_URL
    if (-not $baseUrl) { $baseUrl = 'https://github.com/Walfhand/SkillSync/releases' }
    $releaseUri = [Uri]$baseUrl
    if ($releaseUri.Scheme -ne 'https' -and -not ($releaseUri.Scheme -eq 'http' -and $releaseUri.IsLoopback)) {
        throw 'Release URL must use HTTPS (HTTP is allowed only for a local test mirror).'
    }
    $releasePath = "download/$ReleaseVersion"
    if ($ReleaseVersion -eq 'latest') { $releasePath = 'latest/download' }
    $asset = "skillsync-win-$architecture.zip"
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('skillsync-' + [Guid]::NewGuid().ToString('N'))
    $candidate = $null
    New-Item -ItemType Directory $stage | Out-Null
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $archivePath = Join-Path $stage 'archive.zip'
        $checksumPath = Join-Path $stage 'checksum'
        Invoke-WebRequest -UseBasicParsing "$baseUrl/$releasePath/$asset" -OutFile $archivePath -TimeoutSec 120
        Invoke-WebRequest -UseBasicParsing "$baseUrl/$releasePath/$asset.sha256" -OutFile $checksumPath -TimeoutSec 120
        $checksum = [IO.File]::ReadAllText($checksumPath).TrimEnd([char]13, [char]10)
        if ($checksum -cnotmatch '^([a-f0-9]{64})  ([^\r\n]+)$' -or $Matches[2] -cne $asset) {
            throw 'Invalid SHA-256 checksum file.'
        }
        if ((Get-FileHash $archivePath -Algorithm SHA256).Hash -ine $Matches[1]) {
            throw 'SHA-256 mismatch; the existing installation was kept.'
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
        $stagedBinary = Join-Path $stage 'skillsync.exe'
        try {
            $entries = @($archive.Entries | Where-Object { $_.FullName -ceq 'skillsync.exe' })
            if ($entries.Count -ne 1 -or $entries[0].Length -eq 0) { throw 'Archive does not contain the skillsync executable.' }
            $inputStream = $entries[0].Open()
            $outputStream = [IO.File]::Create($stagedBinary)
            try { $inputStream.CopyTo($outputStream) }
            finally { $inputStream.Dispose(); $outputStream.Dispose() }
        }
        finally { $archive.Dispose() }
        New-Item -ItemType Directory -Force $Directory | Out-Null
        $candidate = Join-Path $Directory ('.skillsync-' + [Guid]::NewGuid().ToString('N'))
        [IO.File]::Copy($stagedBinary, $candidate)
        if (-not $SkipPath) {
            $existing = [Environment]::GetEnvironmentVariable('Path', 'User')
            $updated = Get-SkillSyncPath $existing $Directory
            if ($updated -cne $existing) { [Environment]::SetEnvironmentVariable('Path', $updated, 'User') }
        }
        $destination = Join-Path $Directory 'skillsync.exe'
        if ([IO.File]::Exists($destination)) { [IO.File]::Replace($candidate, $destination, $null) }
        else { [IO.File]::Move($candidate, $destination) }
        $candidate = $null
        if (-not $SkipPath) { $env:Path = Get-SkillSyncPath $env:Path $Directory }
        Write-Output "Installed SkillSync $ReleaseVersion at $destination. Open a new terminal to use it everywhere."
    }
    finally {
        if ($candidate -and [IO.File]::Exists($candidate)) { [IO.File]::Delete($candidate) }
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}

# Dot-sourcing exposes the pure PATH helper without installing anything.
if ($MyInvocation.InvocationName -ne '.') {
    try { Invoke-SkillSyncInstall $Version $InstallDir $NoPathUpdate.IsPresent }
    catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
}
