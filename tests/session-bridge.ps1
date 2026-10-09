$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $project 'test-results\session-bridge'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$wrapper = Join-Path $project 'src\Run-WinUtilSession.ps1'
$extension = Join-Path $project 'src\WinUtilExtension.ps1'

function Quote-BridgeArgument([string] $value) {
    $escaped = [regex]::Replace($value, '(\\*)"', '$1$1\"')
    '"' + [regex]::Replace($escaped, '(\\+)$', '$1$1') + '"'
}
function Invoke-SyntheticSession([string] $name, [string] $source, [bool] $badHash = $false) {
    $session = Join-Path $folder ($name + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $session | Out-Null
    $scriptPath = Join-Path $session 'inert-test-script.ps1'
    [IO.File]::WriteAllText($scriptPath, $source, (New-Object Text.UTF8Encoding($false)))
    $hash = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash
    $expected = if ($badHash) { '0' * 64 } else { $hash }
    $arguments = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $wrapper,
        '-ScriptPath', $scriptPath, '-ScriptHash', $expected,
        '-ExtensionPath', $extension, '-ExtensionHash', (Get-FileHash -LiteralPath $extension -Algorithm SHA256).Hash,
        '-SessionPath', $session)
    # No -Verb RunAs, real vendor script, preset or config is used in these fixtures.
    $child = Start-Process -FilePath $powershell -ArgumentList @($arguments | ForEach-Object { Quote-BridgeArgument $_ }) -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $session 'stdout.txt') -RedirectStandardError (Join-Path $session 'stderr.txt')
    $report = Get-Content -LiteralPath (Join-Path $session 'status.json') -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash -ne $hash) { throw 'The wrapper modified cached source bytes.' }
    return @{ Path = $session; Report = $report; ExitCode = $child.ExitCode }
}

$inert = @'
param([string] $Config, [string] $Preset, [switch] $Offline)
$sync = @{ LoggedErrors = (New-Object Collections.ArrayList); logPath = 'unused' }
$sync.transcriptPath = $sync.logPath
[IO.File]::AppendAllText($sync.logPath, "Synthetic harmless session output`r`n")
if ($false) { $sync["Form"].ShowDialog() | Out-Null }
$global:LASTEXITCODE = 0
'@
$success = Invoke-SyntheticSession 'success' $inert
if ($success.ExitCode -ne 0 -or $success.Report.state -ne 'Completed' -or $success.Report.exitCode -ne 0) { throw 'Synthetic wrapper completion was not reported.' }
if ((Get-Content -LiteralPath (Join-Path $success.Path 'session.log') -Raw) -notmatch 'Synthetic harmless session output') { throw 'Synthetic session output was lost.' }

$rejected = Invoke-SyntheticSession 'hash-rejected' $inert $true
if ($rejected.ExitCode -ne 1 -or $rejected.Report.state -ne 'Failed' -or $rejected.Report.errorCount -ne 1) { throw 'Integrity failure was not reported as an error.' }
if ((Get-Content -LiteralPath (Join-Path $rejected.Path 'errors.log') -Raw) -notmatch 'Integrity check failed') { throw 'Bootstrap integrity failure details were lost.' }
if ((Get-Content -LiteralPath (Join-Path $rejected.Path 'stdout.txt') -Raw) -match 'Synthetic harmless session output') { throw 'Rejected code executed.' }

$fault = Invoke-SyntheticSession 'exception' ($inert.Replace('$global:LASTEXITCODE = 0', 'throw "Synthetic startup exception with full details"'))
if ($fault.ExitCode -ne 1 -or $fault.Report.state -ne 'Failed' -or $fault.Report.lastError -notmatch 'Synthetic startup exception with full details') { throw 'Unhandled startup exception did not reach the status report.' }
if ((Get-Content -LiteralPath (Join-Path $fault.Path 'errors.log') -Raw) -notmatch 'Synthetic startup exception with full details') { throw 'Unhandled startup exception details were not persisted.' }
Write-Host 'Inert worker fixtures passed: completion, hash rejection, source preservation, startup exception and persistent detailed logs.'
