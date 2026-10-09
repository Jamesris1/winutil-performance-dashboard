$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $projectRoot 'build.ps1')
$executable = Join-Path $projectRoot 'dist\WinUtil-Performance.exe'
$results = Join-Path $projectRoot 'test-results'
New-Item -ItemType Directory -Path $results -Force | Out-Null

# Redirecting a WinForms executable makes its inspection JSON available to CI.
function Quote-ProcessArgument([string] $value) {
    $escapedQuotes = [regex]::Replace($value, '(\\*)"', '$1$1\"')
    '"' + [regex]::Replace($escapedQuotes, '(\\+)$', '$1$1') + '"'
}
function Invoke-Inspection([string] $name, [string[]] $arguments, [int] $expectedExit = 0) {
    $stdout = Join-Path $results ($name + '.json')
    $stderr = Join-Path $results ($name + '.stderr.txt')
    $quoted = @($arguments | ForEach-Object { Quote-ProcessArgument $_ })
    # Give child-only inspection fixtures a writable test workspace even in restricted runners.
    $originalTemp = $env:TEMP; $originalTmp = $env:TMP
    try {
        $env:TEMP = $results; $env:TMP = $results
        $process = Start-Process -FilePath $executable -ArgumentList $quoted -PassThru -Wait -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    } finally { $env:TEMP = $originalTemp; $env:TMP = $originalTmp }
    if ($process.ExitCode -ne $expectedExit) { throw ($name + ': unexpected exit code ' + $process.ExitCode) }
    $result = Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json
    if ($null -eq $result) { throw ($name + ': inspection JSON is missing.') }
    return $result
}
function Assert([bool] $condition, [string] $message) { if (-not $condition) { throw $message } }

$plain = Invoke-Inspection 'self-test' @('--self-test')
Assert ($plain.success -and $plain.sha256MatchesOfficialRelease) 'Embedded upstream resource did not verify.'
Assert ($plain.companionVersion -eq '1.1.0' -and $plain.version -eq '26.10.07') 'Unexpected companion/upstream version.'
Assert ($plain.companionResourcesVerified -eq 2 -and $plain.capabilities.fullGuiErrorPanel -and $plain.capabilities.persistentSessionLogs) 'Companion logging resources are missing.'
Assert (-not $plain.vendorScriptExecuted -and -not $plain.elevationRequested -and -not $plain.cacheWritten) 'Inspection performed a mutation.'
Assert ($plain.requestedMode -eq 'performanceDashboard') 'Default launch mode should be the dashboard.'

$configFile = Join-Path $results 'config file with spaces.json'
Set-Content -LiteralPath $configFile -Value '[]' -Encoding UTF8
$automation = Invoke-Inspection 'config-preset-offline' @('--self-test', '-Config', $configFile, '-Preset', 'minimal', '-Offline')
Assert ($automation.requestedMode -eq 'vendorHeadlessAutomation') 'Explicit config/preset must select headless mode.'
Assert ($automation.vendorArguments.Count -eq 5) 'Arguments were lost.'
Assert ($automation.vendorArguments[1] -eq $configFile -and $automation.vendorArguments[3] -eq 'Minimal') 'File path or preset changed during parsing.'

$url = 'https://example.com/config.json?note=$(literal)&text="quoted"'
$urlResult = Invoke-Inspection 'literal-url' @('--self-test', '--config', $url)
Assert ($urlResult.vendorArguments[1] -eq $url) 'URL argument was treated as executable text or changed.'
$invalidPreset = Invoke-Inspection 'invalid-preset' @('--self-test', '--preset', 'Unsupported') 1
Assert (-not $invalidPreset.success) 'Unknown presets must be rejected.'
$missingConfig = Invoke-Inspection 'missing-config' @('--self-test', '--config') 1
Assert (-not $missingConfig.success) 'Missing config values must be rejected.'
$gaming = Invoke-Inspection 'gaming-preset' @('--self-test', '--preset', 'gaming')
Assert ($gaming.vendorArguments.Count -eq 2 -and $gaming.vendorArguments[0] -eq '-Config' -and $gaming.capabilities.gamingSelection -eq 'WPFToggleGameMode') 'Gaming must map to a Game Mode-only config instead of an unsupported vendor preset.'
$gamingMixed = Invoke-Inspection 'gaming-config-rejected' @('--self-test', '--preset', 'Gaming', '--config', $configFile) 1
Assert (-not $gamingMixed.success) 'Gaming cannot combine with unrelated config selections.'
$bridge = Invoke-Inspection 'bridge-self-test' @('--bridge-self-test')
Assert ($bridge.success -and $bridge.startupFailurePersisted -and $bridge.logDetailsReadable) 'Persistent session status/error bridge checks failed.'
Assert (-not $bridge.vendorScriptExecuted -and -not $bridge.elevationRequested -and -not $bridge.cacheWritten) 'Bridge inspection executed a vendor action.'
$ui = Invoke-Inspection 'ui-self-test' @('--ui-self-test')
Assert ($ui.success) 'Dashboard operation status, Gaming selection, layout or log-viewer checks failed.'

$assembly = [Reflection.Assembly]::LoadFile($executable)
$resourceNames = $assembly.GetManifestResourceNames()
Assert ($resourceNames -contains 'WinUtil.Script' -and $resourceNames -contains 'WinUtil.License' -and $resourceNames -contains 'WinUtil.DashboardLicense') 'Required upstream or companion resources are missing.'
Assert ($resourceNames -contains 'WinUtil.SessionWrapper' -and $resourceNames -contains 'WinUtil.Extension' -and $resourceNames -contains 'WinUtil.HelperHashes') 'Session wrapper/error panel resources are missing.'
Assert (@($resourceNames | Where-Object { $_ -like 'WinUtil.Observation.*' }).Count -eq 0) 'Personal baseline observations must not be embedded.'

$parseTokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile((Join-Path $projectRoot 'vendor\winutil.ps1'), [ref] $parseTokens, [ref] $parseErrors) | Out-Null
Assert ($parseErrors.Count -eq 0) 'Vendored PowerShell script has parser errors.'
foreach ($helper in @('Run-WinUtilSession.ps1', 'WinUtilExtension.ps1')) {
    $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $projectRoot ('src\' + $helper)), [ref] $parseTokens, [ref] $parseErrors) | Out-Null
    Assert ($parseErrors.Count -eq 0) ('Companion PowerShell parser error: ' + $helper)
}
& (Join-Path $PSScriptRoot 'session-bridge.ps1')
$windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$wpfArguments = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'full-ui-log.ps1'))
$wpfTest = Start-Process -FilePath $windowsPowerShell -ArgumentList @($wpfArguments | ForEach-Object { Quote-ProcessArgument $_ }) -PassThru -Wait -WindowStyle Hidden -RedirectStandardOutput (Join-Path $results 'full-ui-log.stdout.txt') -RedirectStandardError (Join-Path $results 'full-ui-log.stderr.txt')
Assert ($wpfTest.ExitCode -eq 0) 'Inert WPF error-panel checks failed; see test-results/full-ui-log.stderr.txt.'

Write-Host 'Build, embedded resources, CLI, Gaming scope, persistent error/status bridge, inert WPF error panel, and dashboard checks passed. No WinUtil actions or UAC requests were executed.'
