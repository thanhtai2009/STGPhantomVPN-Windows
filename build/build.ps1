<#
.SYNOPSIS
  Builds STG Phantom VPN on Windows: unit tests, Release build (net48), portable zip and, optionally, the
  Inno Setup installer. Same steps and options as build/build.sh.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\build.ps1
  powershell -ExecutionPolicy Bypass -File build\build.ps1 -Installer
  powershell -ExecutionPolicy Bypass -File build\build.ps1 -BundleOpenVpn -OutDir D:\out

.NOTES
  Needs the .NET SDK 8 or newer. -Installer needs Inno Setup 6 (ISCC.exe); it is looked up in the ISCC
  environment variable, the Inno Setup 6 uninstall registry entry and the default install folders.
  The version is read from <Version> in src\STGPhantomVPN\STGPhantomVPN.csproj.
  Works in Windows PowerShell 5.1 and PowerShell 7.
#>
[CmdletBinding()]
param(
    [switch]$Installer,
    [switch]$BundleOpenVpn,
    [string]$OutDir,
    [string]$PkgSuffix = $env:PKG_SUFFIX,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$Root = Split-Path -Parent $PSScriptRoot
$Src = Join-Path $Root 'src/STGPhantomVPN'
if (-not $OutDir) { $OutDir = if ($env:OUT_DIR) { $env:OUT_DIR } else { Join-Path $Root 'out' } }

function Step([string]$text) { Write-Host "== $text" -ForegroundColor Cyan }
function Fail([string]$text) { Write-Host $text -ForegroundColor Red; exit 1 }

# Runs a native command and stops on a non-zero exit code (PowerShell 5.1 does not do that by itself).
function Invoke-Native([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { Fail "$exe failed with exit code $LASTEXITCODE" }
}

function Write-Sha256([string]$file) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
    $line = "$hash  $(Split-Path -Leaf $file)`n"
    [IO.File]::WriteAllText("$file.sha256", $line, (New-Object Text.UTF8Encoding $false))
    Write-Host $line.Trim()
}

function Get-Pinned([string]$url, [string]$sha, [string]$dest) {
    $ok = (Test-Path -LiteralPath $dest) -and ((Get-FileHash -Algorithm SHA256 -LiteralPath $dest).Hash -eq $sha.ToUpperInvariant())
    if (-not $ok) {
        Write-Host "Downloading $url"
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile "$dest.part"
        Move-Item -Force -LiteralPath "$dest.part" -Destination $dest
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $dest).Hash -ne $sha.ToUpperInvariant()) {
        Remove-Item -Force -LiteralPath $dest
        Fail "SHA-256 mismatch for $url"
    }
}

function Find-Iscc {
    $candidates = @()
    if ($env:ISCC) { $candidates += $env:ISCC }
    foreach ($key in @('HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
                       'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
                       'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1')) {
        $item = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($item -and ($item.PSObject.Properties.Name -contains 'InstallLocation') -and $item.InstallLocation) {
            $candidates += (Join-Path $item.InstallLocation 'ISCC.exe')
        }
    }
    $bases = @(${env:ProgramFiles(x86)}, $env:ProgramFiles)
    if ($env:LOCALAPPDATA) { $bases += (Join-Path $env:LOCALAPPDATA 'Programs') }
    foreach ($base in $bases) {
        if ($base) { $candidates += (Join-Path $base 'Inno Setup 6\ISCC.exe') }
    }
    $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($found) { $candidates += $found.Source }
    foreach ($c in $candidates) { if ($c -and (Test-Path -LiteralPath $c)) { return $c } }
    return $null
}

# ---- Version (one place: the csproj) ----
[xml]$csproj = Get-Content -Raw -LiteralPath (Join-Path $Src 'STGPhantomVPN.csproj')
$Version = @($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $Version) { Fail 'No <Version> in STGPhantomVPN.csproj' }
$Name = "STGPhantomVPN-Windows-$Version$PkgSuffix"
Write-Host "STG Phantom VPN $Version -> $OutDir"

# ---- .NET SDK 8+ ----
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Fail '.NET SDK 8 or newer is required: https://dotnet.microsoft.com/download' }
$sdks = @(& dotnet --list-sdks 2>$null | ForEach-Object { ($_ -split ' ')[0] })
$best = $sdks | Where-Object { $_ -match '^\d+' } | ForEach-Object { [int](($_ -split '\.')[0]) } | Sort-Object -Descending | Select-Object -First 1
if (-not $best -or $best -lt 8) { Fail ".NET SDK 8 or newer is required (found: $($sdks -join ', '))" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

# ---- Tests ----
if (-not $SkipTests) {
    Step 'Unit tests'
    Invoke-Native dotnet @('run', '-c', 'Release', '--project', (Join-Path $Root 'tests/STGPhantomVPN.Tests'))
}

# ---- Build ----
Step 'Build'
$Bin = Join-Path $Src 'bin/Release/net48'
# A clean obj folder keeps the exe identical between builds of the same tree.
foreach ($d in @($Bin, (Join-Path $Src 'obj'))) { if (Test-Path -LiteralPath $d) { Remove-Item -Recurse -Force -LiteralPath $d } }
Invoke-Native dotnet @('build', (Join-Path $Src 'STGPhantomVPN.csproj'), '-c', 'Release', '-nologo', '-v', 'q')

# ---- Stage the portable folder ----
$Stage = Join-Path ([IO.Path]::GetTempPath()) ("stgvpn-" + [Guid]::NewGuid().ToString('N'))
$Pkg = Join-Path $Stage $Name
New-Item -ItemType Directory -Force -Path $Pkg | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $Bin 'STGPhantomVPN.exe'), (Join-Path $Bin 'STGPhantomVPN.exe.config') -Destination $Pkg
    Get-ChildItem -LiteralPath $Bin -Filter *.dll | Copy-Item -Destination $Pkg
    foreach ($d in @('vi', 'zh-CN', 'km', 'Assets')) { Copy-Item -Recurse -LiteralPath (Join-Path $Bin $d) -Destination $Pkg }
    Copy-Item -Recurse -LiteralPath (Join-Path $Root 'licenses') -Destination (Join-Path $Pkg 'licenses')
    Copy-Item -LiteralPath (Join-Path $Root 'README.md'), (Join-Path $Root 'README.vi.md') -Destination $Pkg

    if ($BundleOpenVpn) {
        Step 'Bundling OpenVPN MSI and GPL source'
        # Pinned downloads are defined once, in build/build.sh (and Core/PinnedDownloads.cs).
        $sh = Get-Content -Raw -LiteralPath (Join-Path $Root 'build/build.sh')
        $base = [regex]::Match($sh, 'OVPN_BASE="([^"]+)"').Groups[1].Value
        $srcUrl = [regex]::Match($sh, 'OVPN_SRC_URL="([^"]+)"').Groups[1].Value
        $srcSha = [regex]::Match($sh, 'OVPN_SRC_SHA=([0-9a-f]{64})').Groups[1].Value
        $cache = if ($env:CACHE_DIR) { $env:CACHE_DIR } else { Join-Path ([string]$env:LOCALAPPDATA) 'stgphantomvpn-cache' }
        New-Item -ItemType Directory -Force -Path $cache, (Join-Path $Pkg 'redist'), (Join-Path $Pkg 'source') | Out-Null
        foreach ($arch in @('amd64', 'arm64', 'x86')) {
            $sha = [regex]::Match($sh, "\[$arch\]=([0-9a-f]{64})").Groups[1].Value
            if (-not $sha) { Fail "No pinned SHA-256 for $arch in build.sh" }
            $f = "OpenVPN-2.6.23-I002-$arch.msi"
            Get-Pinned "$base/$f" $sha (Join-Path $cache $f)
            Copy-Item -LiteralPath (Join-Path $cache $f) -Destination (Join-Path $Pkg 'redist')
        }
        Get-Pinned $srcUrl $srcSha (Join-Path $cache 'openvpn-2.6.23.tar.gz')
        Copy-Item -LiteralPath (Join-Path $cache 'openvpn-2.6.23.tar.gz') -Destination (Join-Path $Pkg 'source')
        $Name = "$Name-with-openvpn"
        Rename-Item -LiteralPath $Pkg -NewName $Name
        $Pkg = Join-Path $Stage $Name
    }

    # ---- Zip ----
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $OutDir = (Resolve-Path -LiteralPath $OutDir).Path
    $Zip = Join-Path $OutDir "$Name.zip"
    if (Test-Path -LiteralPath $Zip) { Remove-Item -Force -LiteralPath $Zip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($Pkg, $Zip, [IO.Compression.CompressionLevel]::Optimal, $true)

    # ---- Installer ----
    if ($Installer) {
        Step 'Installer'
        $iscc = Find-Iscc
        if (-not $iscc) { Fail 'Inno Setup 6 (ISCC.exe) not found. Install it from https://jrsoftware.org/isdl.php or set ISCC=<path to ISCC.exe>.' }
        $setup = Join-Path $OutDir "STGPhantomVPN-Setup-$Version.exe"
        foreach ($f in @($setup, "$setup.sha256")) { if (Test-Path -LiteralPath $f) { Remove-Item -Force -LiteralPath $f } }
        Invoke-Native $iscc @('/Q', "/DAppVersion=$Version", "/DAppDir=$Pkg", "/DOutputDir=$OutDir", (Join-Path $Root 'installer/STGPhantomVPN.iss'))
        if (-not (Test-Path -LiteralPath $setup)) { Fail "ISCC produced no $setup" }
        Write-Sha256 $setup
    }
}
finally {
    if (Test-Path -LiteralPath $Stage) { Remove-Item -Recurse -Force -LiteralPath $Stage }
}

Write-Sha256 $Zip
Step 'Done'
Get-Item -LiteralPath $Zip | Format-Table -AutoSize Name, Length
