param([string] $ResultsDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'test-results'))
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run this WPF test in Windows PowerShell with -STA.' }
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\WinUtilExtension.ps1')
$script:reportedStatuses = [Collections.Generic.List[object]]::new()
function Write-CompanionStatus {
    param([string] $State, [string] $Message, [int] $ErrorCount, [string] $LastError)
    $script:reportedStatuses.Add(@{ state = $State; message = $Message; errorCount = $ErrorCount; lastError = $LastError })
}
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
$caseDirectory = Join-Path $ResultsDirectory ('full-ui-log-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $caseDirectory | Out-Null
$log = Join-Path $caseDirectory 'session.log'
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($log, "[2026-10-09 12:00:00.000] [INFO] [Harness] Synthetic test session`r`n[2026-10-09 12:00:01.001] [ERROR] [Harness] Early error before the interface loaded`r`n[2026-10-09 12:00:01.002] [ERROR] [Harness] Details: synthetic stack frame`r`n", $utf8)

$form = [Windows.Window]::new()
$form.Width = 1100; $form.Height = 680
$form.Title = 'WinUtil full interface - error panel test'
$form.Resources['MainBackgroundColor'] = [Windows.Media.BrushConverter]::new().ConvertFromString('#101923')
$form.Resources['MainForegroundColor'] = [Windows.Media.Brushes]::WhiteSmoke
$form.Resources['BorderColor'] = [Windows.Media.BrushConverter]::new().ConvertFromString('#334555')
$form.Background = $form.Resources['MainBackgroundColor']
$root = [Windows.Controls.Grid]::new()
$form.Content = $root
foreach ($height in @('Auto', 'Auto', '*', 'Auto')) {
    $row = [Windows.Controls.RowDefinition]::new()
    $row.Height = [Windows.GridLengthConverter]::new().ConvertFromString($height)
    [void]$root.RowDefinitions.Add($row)
}
$title = [Windows.Controls.TextBlock]::new()
$title.Text = 'Full WinUtil - Install / Tweaks / Config / Updates / Win11ISO'
$title.Foreground = [Windows.Media.Brushes]::WhiteSmoke
$title.FontSize = 20; $title.Margin = [Windows.Thickness]::new(20)
[void]$root.Children.Add($title)
$content = [Windows.Controls.TextBlock]::new()
$content.Text = 'Inert WPF harness. No original WinUtil actions, system changes, or elevation are performed.'
$content.Foreground = [Windows.Media.Brushes]::LightGray
$content.FontSize = 15; $content.Margin = [Windows.Thickness]::new(20)
[Windows.Controls.Grid]::SetRow($content, 2)
[void]$root.Children.Add($content)
$progress = [Windows.Controls.Border]::new()
$progress.Padding = [Windows.Thickness]::new(12)
$progressLabel = [Windows.Controls.TextBlock]::new()
$progressLabel.Text = 'Example operation finished with errors'
$progressLabel.Foreground = [Windows.Media.Brushes]::WhiteSmoke
$progress.Child = $progressLabel
[Windows.Controls.Grid]::SetRow($progress, 3)
[void]$root.Children.Add($progress)
$sync = [hashtable]::Synchronized(@{
    Form = $form; WPFTweaksProgressBar = $progress; logPath = $log; CompanionSessionPath = $caseDirectory
    LoggedErrors = [Collections.ArrayList]::Synchronized([Collections.ArrayList]::new()); ActiveJob = $null
})
[void]$sync.LoggedErrors.Add('[Harness] Early error before the interface loaded')
Initialize-WinUtilCompanionErrors -Sync $sync
$state = $sync.CompanionErrorState
function Assert([bool] $condition, [string] $message) { if (-not $condition) { throw $message } }
Assert ($state.Box.Text -like '*Early error before the interface loaded*') 'Early errors were lost.'
Assert ($state.Box.Text -like '*synthetic stack frame*') 'Error details were lost.'
Assert ($state.Header.Text -eq 'Errors: 1') 'Detail entries were incorrectly counted as primary errors.'
Assert ($state.Box.IsReadOnly -and $state.Box.VerticalScrollBarVisibility -eq 'Auto') 'Error text must be selectable and scrollable.'
Assert ([Windows.Controls.Grid]::GetRow($sync.CompanionErrorPanel) -gt [Windows.Controls.Grid]::GetRow($progress)) 'Error panel must be directly below the progress indicator.'

# Ensure a partially written line and a UTF-8 character split across ticks survive decoding.
$prefix = "[2026-10-09 12:00:02.001] [ERROR] [Harness] Unicode detail: caf"
$bytes = $utf8.GetBytes($prefix + [char]0xE9 + "`r`n")
$file = [IO.FileStream]::new($log, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
$file.Write($bytes, 0, $bytes.Length - 3); $file.Dispose()
Update-WinUtilCompanionErrors -State $state
Assert ($state.Box.Text -notlike '*Unicode detail*') 'A partial log line was displayed prematurely.'
$file = [IO.FileStream]::new($log, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
$file.Write($bytes, $bytes.Length - 3, 3); $file.Dispose()
[void]$sync.LoggedErrors.Add('[Harness] Unicode detail')
Update-WinUtilCompanionErrors -State $state
Assert ($state.Box.Text.Contains('caf' + [char]0xE9)) 'Split UTF-8 text was corrupted.'

# Test a raw PowerShell diagnostic that never used the upstream logger.
[IO.File]::AppendAllText($log, "[2026-10-09 12:00:03.000] [INFO] [Harness] Running next item`r`nSyntheticCommand : Access denied (test only)`r`nAt C:\Synthetic\test.ps1:20 char:1`r`n+ SyntheticCommand`r`n+ ~~~~~~~~~~~~~~~~`r`n    + CategoryInfo          : PermissionDenied`r`n    + FullyQualifiedErrorId : SyntheticError`r`n`r`n", $utf8)
Update-WinUtilCompanionErrors -State $state
Assert ($state.Box.Text -like '*Access denied (test only)*') 'Raw PowerShell error context was lost.'
Assert ($state.Box.Text -like '*FullyQualifiedErrorId*') 'Raw PowerShell diagnostic details were lost.'
Assert ($state.Header.Text -eq 'Errors: 3') 'Raw PowerShell error count was wrong.'
$saved = [IO.File]::ReadAllText((Join-Path $caseDirectory 'errors.log'), $utf8)
Assert ($saved -like '*Early error before the interface loaded*' -and $saved -like '*FullyQualifiedErrorId*') 'Persistent error history is incomplete.'
$before = $state.Box.Text
Update-WinUtilCompanionErrors -State $state
Assert ($state.Box.Text -eq $before) 'An unchanged transcript duplicated entries.'

# Operation status must move while the error count remains unchanged.
$reportedBefore = $script:reportedStatuses.Count
$sync.ActiveJob = 'Harness job'
$sync.WPFTweaksProgressLabel = [Windows.Controls.TextBlock]::new()
$sync.WPFTweaksProgressLabel.Text = 'Checking item 1 of 2'
Update-WinUtilCompanionErrors -State $state
Assert ($script:reportedStatuses.Count -eq ($reportedBefore + 1)) 'Starting an error-free job did not publish operation status.'
Assert ($script:reportedStatuses[$script:reportedStatuses.Count - 1].message -eq 'Harness job: Checking item 1 of 2') 'The actual progress caption was not reported.'
Assert ($script:reportedStatuses[$script:reportedStatuses.Count - 1].errorCount -eq 3) 'Operation status changed the error count.'
$sync.WPFTweaksProgressLabel.Text = 'Checking item 2 of 2'
Update-WinUtilCompanionErrors -State $state
Assert ($script:reportedStatuses.Count -eq ($reportedBefore + 2)) 'Progress with no new errors was not reported.'
$sync.ActiveJob = $null
Update-WinUtilCompanionErrors -State $state
Assert ($script:reportedStatuses.Count -eq ($reportedBefore + 3)) 'Returning to idle did not publish operation status.'
Assert ($script:reportedStatuses[$script:reportedStatuses.Count - 1].state -eq 'Running') 'An open full window was incorrectly marked completed.'

# Pump only this inert harness dispatcher. The installed timer must resolve its handler.
$frame = [Windows.Threading.DispatcherFrame]::new()
$stop = [Windows.Threading.DispatcherTimer]::new()
$stop.Interval = [TimeSpan]::FromMilliseconds(700)
$stop.Add_Tick({ $stop.Stop(); $frame.Continue = $false }.GetNewClosure())
$stop.Start()
[Windows.Threading.Dispatcher]::PushFrame($frame)
$sync.CompanionErrorTimer.Stop()

$root.Measure([Windows.Size]::new($form.Width, $form.Height))
$root.Arrange([Windows.Rect]::new(0, 0, $form.Width, $form.Height))
$root.UpdateLayout()
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(1100, 680, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($root)
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$imagePath = Join-Path $ResultsDirectory 'full-ui-errors.png'
$output = [IO.File]::Create($imagePath)
try { $encoder.Save($output) } finally { $output.Dispose() }
$result = @{ success = $true; originalVendorExecuted = $false; systemSettingsChanged = $false; earlyErrors = $true;
    unicodeSplit = $true; rawPowerShellErrors = $true; persisted = $true; operationStatusTransitions = $true;
    errorCount = 3; preview = $imagePath }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'full-ui-log.json') -Encoding UTF8
$result | ConvertTo-Json
