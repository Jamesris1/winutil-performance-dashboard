param(
    [Parameter(Mandatory = $true)][string] $ScriptPath,
    [Parameter(Mandatory = $true)][string] $ScriptHash,
    [Parameter(Mandatory = $true)][string] $ExtensionPath,
    [Parameter(Mandatory = $true)][string] $ExtensionHash,
    [Parameter(Mandatory = $true)][string] $SessionPath,
    [string] $Config,
    [ValidateSet('Standard', 'Minimal', 'Advanced', '')][string] $Preset,
    [switch] $Offline
)

# This isolated elevated worker executes the verified release in memory. The cached
# upstream bytes stay intact; two narrow hooks supply a log path and the error panel.
function Assert-CompanionNormalPath([string] $Path, [switch] $Directory) {
    $full = [IO.Path]::GetFullPath($Path)
    $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    if ($Directory -and -not $item.PSIsContainer) { throw "Expected a directory: $full" }
    if (-not $Directory -and $item.PSIsContainer) { throw "Expected a file: $full" }
    while ($null -ne $item) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Redirected companion paths are not supported: $($item.FullName)"
        }
        $parent = if ($item.PSIsContainer) { $item.Parent } else { $item.Directory }
        $item = $parent
    }
    return $full
}

function global:Write-CompanionStatus {
    param([string] $State = 'Running', [string] $Message, [int] $ErrorCount = 0,
        [string] $LastError = '', $ExitCode = $null)
    $folder = $env:WINUTIL_COMPANION_SESSION
    if ([string]::IsNullOrWhiteSpace($folder)) { return }
    $statusPath = Join-Path $folder 'status.json'
    $temporary = Join-Path $folder ('.status-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $report = [ordered]@{
        state = $State; message = $Message; errorCount = $ErrorCount; lastError = $LastError
        exitCode = $ExitCode; updatedUtc = [DateTime]::UtcNow.ToString('o')
    }
    try {
        [IO.File]::WriteAllText($temporary, ($report | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
        if ([IO.File]::Exists($statusPath)) { [IO.File]::Replace($temporary, $statusPath, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temporary, $statusPath) }
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

function global:Write-CompanionFailure {
    param([string] $Detail)
    $path = Join-Path $env:WINUTIL_COMPANION_SESSION 'errors.log'
    [IO.File]::AppendAllText($path, ([DateTime]::UtcNow.ToString('o') + ' ' + $Detail + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))
}

function Get-CompanionFileHash([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
}

$exitCode = 1
$previousSession = $env:WINUTIL_COMPANION_SESSION
$previousHeadless = $env:WINUTIL_HEADLESS_CHILD
try {
    $SessionPath = Assert-CompanionNormalPath -Path $SessionPath -Directory
    $env:WINUTIL_COMPANION_SESSION = $SessionPath
    # In-memory vendor invocation returns its code instead of exiting this wrapper.
    $env:WINUTIL_HEADLESS_CHILD = '0'
    Write-CompanionStatus -State 'Starting' -Message 'Verifying the WinUtil release and companion extension.'
    foreach ($required in @(
        @{ Path = $ScriptPath; Hash = $ScriptHash },
        @{ Path = $ExtensionPath; Hash = $ExtensionHash }
    )) {
        $required.Path = Assert-CompanionNormalPath -Path $required.Path
        if ((Get-CompanionFileHash -Path $required.Path) -ne $required.Hash) {
            throw "Integrity check failed: $($required.Path)"
        }
    }
    # A failed bootstrap must remain readable even if the vendor transcript never starts.
    [IO.File]::AppendAllText((Join-Path $SessionPath 'session.log'),
        ([DateTime]::UtcNow.ToString('o') + ' WinUtil Performance 1.1.0 session started.' + [Environment]::NewLine),
        (New-Object Text.UTF8Encoding($false)))

    . $ExtensionPath
    $source = [IO.File]::ReadAllText($ScriptPath)
    $logAnchor = '$sync.transcriptPath = $sync.logPath'
    $uiAnchor = '$sync["Form"].ShowDialog() | Out-Null'
    if (($source.Split(@($logAnchor), [StringSplitOptions]::None).Length - 1) -ne 1 -or
        ($source.Split(@($uiAnchor), [StringSplitOptions]::None).Length - 1) -ne 1) {
        throw 'The verified vendor release does not match the companion extension anchors.'
    }
    $logHook = '$sync.logPath = Join-Path $env:WINUTIL_COMPANION_SESSION ''session.log''' + "`r`n" +
        '$sync.CompanionSessionPath = $env:WINUTIL_COMPANION_SESSION' + "`r`n" + $logAnchor
    $uiHook = 'Initialize-WinUtilCompanionErrors -Sync $sync' + "`r`n    " + $uiAnchor
    $source = $source.Replace($logAnchor, $logHook).Replace($uiAnchor, $uiHook)
    $vendor = [ScriptBlock]::Create($source)
    $vendorParameters = @{}
    if ($Config) { $vendorParameters.Config = $Config }
    if ($Preset) { $vendorParameters.Preset = $Preset }
    if ($Offline) { $vendorParameters.Offline = $true }
    Write-CompanionStatus -State 'Running' -Message $(if ($Config -or $Preset) { 'Applying the selected WinUtil profile.' } else { 'Full WinUtil is open. Monitoring continues in the dashboard.' })
    $global:LASTEXITCODE = 0
    . $vendor @vendorParameters | Out-Null
    $exitCode = [int]$global:LASTEXITCODE
    $count = if ($sync -and $sync.LoggedErrors) { [int]$sync.LoggedErrors.Count } else { 0 }
    $detail = ''
    if ($count -gt 0) { $detail = [string]$sync.LoggedErrors[$count - 1] }
    # Retain additional error details discovered by the panel's transcript parser.
    $reportPath = Join-Path $SessionPath 'status.json'
    if (Test-Path -LiteralPath $reportPath) {
        try {
            $prior = Get-Content -LiteralPath $reportPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ([int]$prior.errorCount -gt $count) { $count = [int]$prior.errorCount; $detail = [string]$prior.lastError }
        } catch { }
    }
    if ($exitCode -eq 0) {
        $message = if ($count -gt 0) { "WinUtil finished with $count reported error(s). Open the log for details." } else { 'WinUtil finished. The session log is available.' }
        Write-CompanionStatus -State 'Completed' -Message $message -ErrorCount $count -LastError $detail -ExitCode 0
    } else {
        Write-CompanionStatus -State 'Failed' -Message "WinUtil returned exit code $exitCode. Open the log for details." -ErrorCount ([Math]::Max(1, $count)) -LastError $detail -ExitCode $exitCode
    }
} catch {
    $caught = $_
    $detail = ($caught | Out-String).Trim() + [Environment]::NewLine + $caught.ScriptStackTrace
    if ($env:WINUTIL_COMPANION_SESSION) {
        try { Write-CompanionFailure -Detail $detail } catch { }
        try { Write-CompanionStatus -State 'Failed' -Message $caught.Exception.Message -ErrorCount 1 -LastError $detail -ExitCode 1 } catch { Write-Warning "The startup status could not be saved: $($_.Exception.Message)" }
    }
    Write-Error $detail -ErrorAction Continue
    $exitCode = 1
} finally {
    # Vendor normally stops its transcript. A bootstrap/UI exception may leave it open.
    try { Stop-Transcript -ErrorAction Stop | Out-Null } catch { }
    $env:WINUTIL_COMPANION_SESSION = $previousSession
    $env:WINUTIL_HEADLESS_CHILD = $previousHeadless
}
exit $exitCode
