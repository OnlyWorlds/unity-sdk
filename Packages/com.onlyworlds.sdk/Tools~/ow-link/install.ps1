# install.ps1: registers ow-link.ps1 as this Windows user's handler for ow:// links, or removes it.
#
#   powershell -ExecutionPolicy Bypass -File install.ps1 [-Config <your.json>] [-Force] [-DryRun]
#   powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall [-DryRun]
#
# Per user only: it copies the forwarder to %LOCALAPPDATA%\OnlyWorlds\ow-link and writes HKCU\Software\Classes\ow.
# It never writes HKLM and never asks for an administrator. It refuses to replace an ow: handler that is not its own
# unless -Force. -Config copies your config in as ow-link.json; without it, an existing ow-link.json is kept, and a
# fresh install starts from the example, which you then edit. -Uninstall removes the key (only when it is this
# forwarder's) and the folder, config included. -DryRun prints what it would do and changes nothing.

[CmdletBinding()]
param(
    [string]$Config,
    [switch]$Uninstall,
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$InstallDir = Join-Path $env:LOCALAPPDATA 'OnlyWorlds\ow-link'
$Forwarder = Join-Path $InstallDir 'ow-link.ps1'
$Key = 'HKCU:\Software\Classes\ow'
$CommandKey = "$Key\shell\open\command"

function Step([string]$Text) {
    if ($DryRun) { Write-Output "would $Text" } else { Write-Output $Text }
}

# The command line the key holds today, or $null when there is no ow: handler for this user.
function Get-CurrentCommand {
    if (-not (Test-Path -LiteralPath $CommandKey)) { return $null }
    return (Get-Item -LiteralPath $CommandKey).GetValue('')
}

function Test-Ours([string]$Command) {
    return $Command -and $Command.IndexOf($Forwarder, [StringComparison]::OrdinalIgnoreCase) -ge 0
}

$current = Get-CurrentCommand

if ($Uninstall) {
    if ($current -and -not (Test-Ours $current) -and -not $Force) {
        Write-Error "The ow: handler is not this forwarder's ($current). Left alone; -Force removes it anyway."
    }
    if (Test-Path -LiteralPath $Key) {
        Step "remove $Key"
        if (-not $DryRun) { Remove-Item -LiteralPath $Key -Recurse }
    }
    if (Test-Path -LiteralPath $InstallDir) {
        Step "remove $InstallDir"
        if (-not $DryRun) { Remove-Item -LiteralPath $InstallDir -Recurse }
    }
    Write-Output 'ow: links are no longer handled for this user.'
    exit 0
}

if ($current -and -not (Test-Ours $current) -and -not $Force) {
    Write-Error "Another ow: handler is registered for this user ($current). Left alone; -Force replaces it."
}

if ($Config -and -not (Test-Path -LiteralPath $Config -PathType Leaf)) { Write-Error "No config at $Config." }

# The copies: the forwarder and this installer (so -Uninstall works once the package is gone), then the config.
Step "copy the forwarder to $InstallDir"
if (-not $DryRun) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ow-link.ps1') -Destination $Forwarder -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination (Join-Path $InstallDir 'install.ps1') -Force
}

$installedConfig = Join-Path $InstallDir 'ow-link.json'
if ($Config) {
    Step "copy $Config to $installedConfig"
    if (-not $DryRun) { Copy-Item -LiteralPath $Config -Destination $installedConfig -Force }
} elseif (-not (Test-Path -LiteralPath $installedConfig)) {
    Step "start $installedConfig from the example: edit its ports and player"
    if (-not $DryRun) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ow-link.example.json') -Destination $installedConfig }
}

# Windows PowerShell, which every Windows has, whichever shell runs this installer. conhost's headless mode keeps
# the window from showing at all, where the default terminal would otherwise open one; older Windows lacks it.
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$conhost = Join-Path $env:SystemRoot 'System32\conhost.exe'
$run = "`"$powershell`" -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$Forwarder`" `"%1`""
if ((Test-Path -LiteralPath $conhost) -and [Environment]::OSVersion.Version.Build -ge 19041) {
    $run = "`"$conhost`" --headless $run"
}

Step "write $Key (URL Protocol) with the command: $run"
if (-not $DryRun) {
    foreach ($path in @($Key, "$Key\shell", "$Key\shell\open", $CommandKey)) {
        # New-Item -Force on a key that exists would clear it; create only what is missing.
        if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path | Out-Null }
    }
    Set-Item -LiteralPath $Key -Value 'URL:OnlyWorlds address'
    Set-ItemProperty -LiteralPath $Key -Name 'URL Protocol' -Value ''
    Set-Item -LiteralPath $CommandKey -Value $run
}

Write-Output "ow: links now go to $Forwarder, which reads $installedConfig."
Write-Output "Remove it with: powershell -ExecutionPolicy Bypass -File `"$(Join-Path $InstallDir 'install.ps1')`" -Uninstall"
