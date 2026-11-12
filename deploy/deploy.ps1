<#
.SYNOPSIS
    Builds osu-rotate and deploys it to the Raspberry Pi over SSH.

.DESCRIPTION
    Publishes a self-contained linux-arm64 build, uploads it with scp and installs it with
    deploy/pi-install.sh. The first deploy also provisions the Pi. Every deploy goes into a new
    release folder and is health-checked; if it doesn't come up, the previous release is restored.

    The terminal stays interactive so sudo on the Pi can ask for your password.

.EXAMPLE
    .\deploy\deploy.ps1 -SshHost pi@raspberrypi.local

.EXAMPLE
    .\deploy\deploy.ps1 -SshHost pi@raspberrypi.local -Rollback
#>
[CmdletBinding()]
param(
    # SSH destination, e.g. pi@raspberrypi.local
    [Parameter(Mandatory = $true)]
    [string] $SshHost,

    [int] $Port = 22,

    # Switch back to the previous release instead of deploying
    [switch] $Rollback,

    # Build and pack only, without connecting to the Pi
    [switch] $PackOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$runtime = 'linux-arm64'

# Runs a native command and stops the script if it fails.
function Invoke-Native([string] $Exe, [string[]] $Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "'$Exe' failed with exit code $LASTEXITCODE." }
}

# Short commit hash for the release folder name, so you can tell what is deployed.
function Get-ReleaseLabel {
    $ErrorActionPreference = 'Continue'   # git writes to stderr outside a repo; not fatal here
    $sha = & git -C $repoRoot rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $sha) { return 'local' }
    if (& git -C $repoRoot status --porcelain 2>$null) { return "$sha-dirty" }
    return "$sha"
}

foreach ($tool in 'ssh', 'scp', 'dotnet') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "'$tool' was not found on PATH." }
}
# Prefer Windows' own tar: Git's GNU tar would treat "C:\..." as a remote host.
$tar = 'tar'
if ($env:SystemRoot -and (Test-Path (Join-Path $env:SystemRoot 'System32\tar.exe'))) {
    $tar = Join-Path $env:SystemRoot 'System32\tar.exe'
}

if ($Rollback) {
    Invoke-Native ssh @('-p', "$Port", '-t', $SshHost, 'sudo /opt/osu-rotate/pi-install.sh rollback')
    return
}

$label = Get-ReleaseLabel
$outDir = Join-Path $repoRoot "artifacts\deploy\$runtime"
$bundleDir = Join-Path $outDir 'bundle'

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $bundleDir 'deploy') -Force | Out-Null

Write-Host "==> Publishing self-contained $runtime build ($label)"
Invoke-Native dotnet @('publish', (Join-Path $repoRoot 'src\OsuRotate.Web\OsuRotate.Web.csproj'), '-c', 'Release', '-r', $runtime,
    '--self-contained', '-o', (Join-Path $bundleDir 'app'), '-nologo')

Write-Host '==> Packing'
foreach ($file in 'pi-install.sh', 'osu-rotate.service', 'osu-rotate.env.example') {
    Copy-Item (Join-Path $PSScriptRoot $file) (Join-Path $bundleDir 'deploy')
}
Copy-Item (Join-Path $PSScriptRoot 'pi-install.sh') (Join-Path $outDir 'osu-rotate-pi-install.sh')
Invoke-Native $tar @('-czf', (Join-Path $outDir 'osu-rotate-bundle.tar.gz'), '-C', $bundleDir, 'app', 'deploy')

if ($PackOnly) {
    Write-Host "==> Packed $outDir\osu-rotate-bundle.tar.gz"
    return
}

Write-Host "==> Uploading to $SshHost"
Push-Location $outDir
try {
    # Plain file names keep drive letters out of scp's host:path parsing.
    Invoke-Native scp @('-P', "$Port", 'osu-rotate-bundle.tar.gz', 'osu-rotate-pi-install.sh', "${SshHost}:")
}
finally {
    Pop-Location
}

Write-Host "==> Installing on $SshHost"
$remote = 'sed -i ''s/\r$//'' ~/osu-rotate-pi-install.sh && sudo bash ~/osu-rotate-pi-install.sh install ~/osu-rotate-bundle.tar.gz ' +
    $label + '; rc=$?; rm -f ~/osu-rotate-bundle.tar.gz ~/osu-rotate-pi-install.sh; exit $rc'
Invoke-Native ssh @('-p', "$Port", '-t', $SshHost, $remote)

Write-Host "==> Deployed $label. Check it with: ssh $SshHost curl -s http://127.0.0.1:5180/healthz"
