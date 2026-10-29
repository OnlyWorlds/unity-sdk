# ow-link.ps1: the forwarder Windows runs for an ow:// link (install.ps1 registers it, per user).
#
# It offers the link to each port in its config, in order, with the OnlyWorlds SDK's link protocol (one line out,
# one line back over 127.0.0.1): a running Editor's OWLinkListener first, say, then a running player's. The first
# that answers "ok" has it. When none does, it starts the configured player with the link as an argument.
#
#   ow-link.ps1 <link> [-DryRun]
#
# The config is ow-link.json beside this script, or the file OW_LINK_CONFIG names. It is never an argument: the link
# arrives from whatever opened it, and an argument smuggled in beside it must not choose the config. -DryRun offers
# the link to the ports but prints the player's command line instead of starting it. Exit codes: 0 delivered, 1 player started (or, with -DryRun, would be),
# 2 the link was refused, 3 nowhere to deliver it (no player configured, or none at its path), 4 the config is unusable.
# The outcome is also written to last-run.txt beside the config, since the window is hidden.

[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Link,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$Config = $env:OW_LINK_CONFIG
if (-not $Config) { $Config = Join-Path $PSScriptRoot 'ow-link.json' }

# The longest line a listener reads (OWLinkListener.MaxLineBytes).
$MaxLineBytes = 4096

function Write-Outcome([int]$Code, [string]$Text) {
    Write-Output $Text
    try {
        $folder = Split-Path -Parent $Config
        if ($folder -and (Test-Path -LiteralPath $folder)) {
            $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + "  exit $Code  $Text"
            [System.IO.File]::WriteAllText((Join-Path $folder 'last-run.txt'), $line + "`n")
        }
    } catch {
        # The outcome is already on the output; a log that cannot be written changes nothing.
    }
    exit $Code
}

# Offers the link to 127.0.0.1:$Port. The listener's answer line, or $null when nothing answered.
function Send-OwLink([int]$Port, [string]$Text, [int]$TimeoutMs) {
    $client = New-Object System.Net.Sockets.TcpClient ([System.Net.Sockets.AddressFamily]::InterNetwork)
    try {
        $connecting = $client.BeginConnect([System.Net.IPAddress]::Loopback, $Port, $null, $null)
        if (-not $connecting.AsyncWaitHandle.WaitOne($TimeoutMs)) { return $null }
        $client.EndConnect($connecting)
        $client.SendTimeout = $TimeoutMs
        $client.ReceiveTimeout = $TimeoutMs

        $stream = $client.GetStream()
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text + "`n")
        $stream.Write($bytes, 0, $bytes.Length)
        $client.Client.Shutdown([System.Net.Sockets.SocketShutdown]::Send)

        $buffer = New-Object byte[] 256
        $count = 0
        while ($count -lt $buffer.Length) {
            $read = $stream.Read($buffer, $count, $buffer.Length - $count)
            if ($read -le 0) { break }
            $count += $read
            if ([Array]::IndexOf($buffer, [byte]10, 0, $count) -ge 0) { break }
        }

        $newline = [Array]::IndexOf($buffer, [byte]10, 0, $count)
        if ($newline -lt 0) { return $null }
        return [System.Text.Encoding]::UTF8.GetString($buffer, 0, $newline).TrimEnd("`r")
    } catch {
        return $null
    } finally {
        $client.Close()
    }
}

# One argument as Windows' command-line parser reads it back.
function ConvertTo-Argument([string]$Value) {
    if ($Value -ne '' -and $Value -notmatch '[\s"]') { return $Value }
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

# The link, as the browser handed it over. Only ow: links, and only characters an address can hold: nothing that
# could break out of an argument, a quote, a space or a backslash, ever reaches the player's command line.
if (-not $Link) { Write-Outcome 2 'refused: no link given' }
if ($Link -notmatch '^ow:') { Write-Outcome 2 'refused: not an ow: link' }
if ($Link.Length -gt $MaxLineBytes -or $Link -notmatch '^[A-Za-z0-9:/._~@?=&%+-]+$') {
    Write-Outcome 2 'refused: the link holds characters no address holds'
}

try {
    $settings = Get-Content -LiteralPath $Config -Raw -Encoding UTF8 | ConvertFrom-Json
} catch {
    Write-Outcome 4 "config unusable: $Config ($($_.Exception.Message))"
}

$timeout = 600
if ($settings.timeout_ms) { $timeout = [int]$settings.timeout_ms }

foreach ($port in @($settings.ports)) {
    if (-not $port) { continue }
    $answer = Send-OwLink ([int]$port) $Link $timeout
    if ($answer -eq 'ok') { Write-Outcome 0 "delivered to port $port" }
    if ($answer -and $answer.StartsWith('refused ') -and $answer -ne 'refused no_receiver') {
        # A listener read the link and refused it: every other one would too.
        Write-Outcome 2 "port $port answered: $answer"
    }
    # No answer, or a listener with nobody to take it: try the next.
}

$player = $settings.player
if (-not $player -or -not $player.path) { Write-Outcome 3 'no listener took it and no player is configured' }

$path = [Environment]::ExpandEnvironmentVariables([string]$player.path)
$arguments = @()
foreach ($argument in @($player.args)) {
    if ($null -eq $argument) { continue }
    $arguments += ConvertTo-Argument (([string]$argument).Replace('{link}', $Link))
}
$commandLine = $arguments -join ' '

if ($DryRun) { Write-Outcome 1 "would start: $(ConvertTo-Argument $path) $commandLine" }
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Write-Outcome 3 "no listener took it and the player is not at $path" }

$start = @{ FilePath = $path; WorkingDirectory = (Split-Path -Parent $path) }
if ($commandLine) { $start.ArgumentList = $commandLine }
Start-Process @start
Write-Outcome 1 "started $path"
