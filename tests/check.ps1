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
    $process = Start-Process -FilePath $executable -ArgumentList $quoted -PassThru -Wait -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if ($process.ExitCode -ne $expectedExit) { throw ($name + ': unexpected exit code ' + $process.ExitCode) }
    $result = Get-Content -LiteralPath $stdout -Raw | ConvertFrom-Json
    if ($null -eq $result) { throw ($name + ': inspection JSON is missing.') }
    return $result
}
function Assert([bool] $condition, [string] $message) { if (-not $condition) { throw $message } }

$plain = Invoke-Inspection 'self-test' @('--self-test')
Assert ($plain.success -and $plain.sha256MatchesOfficialRelease) 'Embedded upstream resource did not verify.'
Assert ($plain.companionVersion -eq '1.0.0' -and $plain.version -eq '26.10.07') 'Unexpected companion/upstream version.'
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

$assembly = [Reflection.Assembly]::LoadFile($executable)
$resourceNames = $assembly.GetManifestResourceNames()
Assert ($resourceNames -contains 'WinUtil.Script' -and $resourceNames -contains 'WinUtil.License' -and $resourceNames -contains 'WinUtil.DashboardLicense') 'Required upstream or companion resources are missing.'
Assert (@($resourceNames | Where-Object { $_ -like 'WinUtil.Observation.*' }).Count -eq 0) 'Personal baseline observations must not be embedded.'

$parseTokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile((Join-Path $projectRoot 'vendor\winutil.ps1'), [ref] $parseTokens, [ref] $parseErrors) | Out-Null
Assert ($parseErrors.Count -eq 0) 'Vendored PowerShell script has parser errors.'

Write-Host 'Build, embedded-resource, version, parser, and CLI checks passed. No WinUtil actions or UAC requests were executed.'
