# Companion interface extension. The pinned upstream script remains unchanged on disk.
# The session runner calls this immediately before the original window's ShowDialog.

function Add-WinUtilCompanionErrorLine {
    param([hashtable] $State, [string] $Line)

    if ([string]::IsNullOrWhiteSpace($Line)) { return }
    if (-not $State.HasErrors) {
        $State.Box.Clear()
        $State.HasErrors = $true
    }
    $wasAtBottom = $State.Box.VerticalOffset -ge ($State.Box.ExtentHeight - $State.Box.ViewportHeight - 8)
    $State.Box.AppendText($Line + [Environment]::NewLine)
    # Keep the interface small even for a very long job; the file keeps the complete history.
    if ($State.Box.Text.Length -gt 200000) {
        $State.Box.Text = "[Earlier entries remain in the session log.]`r`n" + $State.Box.Text.Substring($State.Box.Text.Length - 150000)
    }
    if ($wasAtBottom -and $State.Box.SelectionLength -eq 0) { $State.Box.ScrollToEnd() }
    $State.LastError = $Line

    $sessionPath = [string]$State.Sync.CompanionSessionPath
    if (-not $sessionPath) { $sessionPath = $env:WINUTIL_COMPANION_SESSION }
    if ($sessionPath) {
        $writer = $null
        $file = $null
        try {
            $file = [IO.FileStream]::new((Join-Path $sessionPath 'errors.log'), [IO.FileMode]::Append,
                [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
            $writer = [IO.StreamWriter]::new($file, [Text.UTF8Encoding]::new($false))
            $writer.WriteLine($Line)
        } catch {
            # Reading the original transcript remains available if a second log cannot be written.
            $State.PersistenceWarning = 'The error summary could not be saved; open the full session log for details.'
        } finally {
            if ($writer) { $writer.Dispose() } elseif ($file) { $file.Dispose() }
        }
    }
}

function Read-WinUtilCompanionTranscript {
    param([hashtable] $State)
    $path = [string]$State.Sync.logPath
    if (-not $path -or -not [IO.File]::Exists($path)) { return }
    $file = $null
    try {
        $file = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        if ($State.Path -ne $path -or $file.Length -lt $State.Position) {
            $State.Path = $path
            $State.Position = [long]0
            $State.Pending = ''
            $State.Decoder = $null
            $State.Context.Clear()
            $State.RawBlock = $false
        }
        [void]$file.Seek($State.Position, [IO.SeekOrigin]::Begin)
        # Bound work per tick. Remaining bytes are read on the next tick without skipping anything.
        $buffer = New-Object byte[] 65536
        $read = $file.Read($buffer, 0, $buffer.Length)
        $State.ReadWarning = ''
        if ($read -eq 0) { return }
        $offset = 0
        if ($null -eq $State.Decoder) {
            $encoding = [Text.UTF8Encoding]::new($false)
            if ($read -ge 2 -and $buffer[0] -eq 255 -and $buffer[1] -eq 254) {
                $encoding = [Text.Encoding]::Unicode; $offset = 2
            } elseif ($read -ge 2 -and $buffer[0] -eq 254 -and $buffer[1] -eq 255) {
                $encoding = [Text.Encoding]::BigEndianUnicode; $offset = 2
            } elseif ($read -ge 3 -and $buffer[0] -eq 239 -and $buffer[1] -eq 187 -and $buffer[2] -eq 191) {
                $offset = 3
            }
            $State.Decoder = $encoding.GetDecoder()
        }
        $chars = New-Object char[] ($read + 2)
        $charCount = $State.Decoder.GetChars($buffer, $offset, $read - $offset, $chars, 0, $false)
        $State.Position += $read
        $State.Pending += [string]::new($chars, 0, $charCount)
        $lastNewline = $State.Pending.LastIndexOf("`n")
        if ($lastNewline -lt 0) { return }
        $complete = $State.Pending.Substring(0, $lastNewline + 1)
        $State.Pending = $State.Pending.Substring($lastNewline + 1)
        foreach ($line in ($complete.Substring(0, $complete.Length - 1) -split "`n")) {
            $line = $line.TrimEnd("`r")
            if ($line -match '^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)?\] \[(INFO|WARN|ERROR|DEBUG)\] \[') {
                $State.RawBlock = $false
                $State.StructuredError = $Matches[1] -eq 'ERROR'
                if ($Matches[1] -eq 'ERROR') { Add-WinUtilCompanionErrorLine -State $State -Line $line }
                $State.Context.Clear()
                continue
            }
            if ($State.StructuredError -and -not [string]::IsNullOrWhiteSpace($line)) {
                Add-WinUtilCompanionErrorLine -State $State -Line $line
                continue
            }
            $State.StructuredError = $false
            # Nonterminating PowerShell errors can bypass Write-WinUtilLog. Its transcript
            # includes the diagnostic markers below, so show the preceding message too.
            $rawMarker = $line -match '^\s*(At .+:\d+ char:\d+|\+\s+(CategoryInfo|FullyQualifiedErrorId)\s*:)'
            if ($rawMarker -and -not $State.RawBlock) {
                $State.RawBlock = $true
                $State.RawErrors++
                Add-WinUtilCompanionErrorLine -State $State -Line ('[' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff') + '] [ERROR] [PowerShell] Transcript diagnostic')
                foreach ($contextLine in $State.Context) { Add-WinUtilCompanionErrorLine -State $State -Line $contextLine }
                $State.Context.Clear()
            }
            if ($State.RawBlock) {
                if ([string]::IsNullOrWhiteSpace($line)) { $State.RawBlock = $false }
                else { Add-WinUtilCompanionErrorLine -State $State -Line $line }
            } elseif ([string]::IsNullOrWhiteSpace($line)) {
                $State.Context.Clear()
            } else {
                [void]$State.Context.Add($line)
                if ($State.Context.Count -gt 6) { $State.Context.RemoveAt(0) }
            }
        }
    } catch {
        $State.ReadWarning = 'The session log is temporarily unavailable; waiting for the next update.'
    } finally {
        if ($file) { $file.Dispose() }
    }
}

function Update-WinUtilCompanionErrors {
    param([hashtable] $State)
    Read-WinUtilCompanionTranscript -State $State
    if (-not [IO.File]::Exists([string]$State.Sync.logPath) -and $null -ne $State.Sync.LoggedErrors) {
        while ($State.FallbackCount -lt $State.Sync.LoggedErrors.Count) {
            Add-WinUtilCompanionErrorLine -State $State -Line ('[ERROR] [Early entry; original timestamp unavailable] ' + [string]$State.Sync.LoggedErrors[$State.FallbackCount])
            $State.FallbackCount++
        }
    }
    $loggedCount = 0
    if ($null -ne $State.Sync.LoggedErrors) { $loggedCount = $State.Sync.LoggedErrors.Count }
    # The upstream collection counts primary errors, excluding stack-frame detail entries.
    $count = $loggedCount + $State.RawErrors
    $State.Header.Text = 'Errors: ' + $count
    if ($count -gt 0 -or $State.HasErrors) {
        $State.Header.Foreground = [Windows.Media.Brushes]::OrangeRed
    } else {
        $State.Header.SetResourceReference([Windows.Controls.TextBlock]::ForegroundProperty, 'MainForegroundColor')
    }
    $job = [string]$State.Sync.ActiveJob
    $progressLabel = $State.Sync.WPFTweaksProgressLabel
    $progressText = if ($null -ne $progressLabel) { [string]$progressLabel.Text } else { '' }
    $State.Status.Text = if ($job) {
        if (-not [string]::IsNullOrWhiteSpace($progressText)) { $job + ': ' + $progressText }
        else { $job + ' is running' }
    } elseif ($count -gt 0) {
        'Review the details below. The complete session log is saved.'
    } else { 'Full WinUtil is ready. Details will appear here if an operation fails.' }
    if ($State.PersistenceWarning) { $State.Status.Text = $State.PersistenceWarning }
    elseif ($State.ReadWarning) { $State.Status.Text = $State.ReadWarning }
    if ($State.LastReportedCount -ne $count -or $State.LastReportedError -ne $State.LastError -or
        $State.LastReportedStatus -ne $State.Status.Text) {
        if (Get-Command Write-CompanionStatus -ErrorAction SilentlyContinue) {
            Write-CompanionStatus -State 'Running' -Message $State.Status.Text -ErrorCount $count -LastError $State.LastError
        }
        $State.LastReportedCount = $count
        $State.LastReportedError = $State.LastError
        $State.LastReportedStatus = $State.Status.Text
    }
}

function Initialize-WinUtilCompanionErrors {
    param([Parameter(Mandatory)] [hashtable] $Sync)
    if ($Sync.CompanionErrorPanel) { return }
    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
    $form = $Sync.Form
    $progress = $Sync.WPFTweaksProgressBar
    if ($null -eq $form -or $null -eq $progress -or $progress.Parent -isnot [Windows.Controls.Grid]) {
        throw 'The pinned WinUtil progress panel could not be found for the error-log extension.'
    }
    $root = $progress.Parent
    $row = [Windows.Controls.RowDefinition]::new()
    $row.Height = [Windows.GridLength]::Auto
    [void]$root.RowDefinitions.Add($row)

    $border = [Windows.Controls.Border]::new()
    $border.Name = 'CompanionErrorPanel'
    $border.Padding = [Windows.Thickness]::new(12, 8, 12, 10)
    $border.BorderThickness = [Windows.Thickness]::new(0, 1, 0, 0)
    $border.SetResourceReference([Windows.Controls.Border]::BackgroundProperty, 'MainBackgroundColor')
    $border.SetResourceReference([Windows.Controls.Border]::BorderBrushProperty, 'BorderColor')
    [Windows.Controls.Grid]::SetRow($border, $root.RowDefinitions.Count - 1)
    [void]$root.Children.Add($border)
    $panel = [Windows.Controls.Grid]::new()
    $border.Child = $panel
    foreach ($height in @('Auto', 'Auto', 'Auto')) {
        $definition = [Windows.Controls.RowDefinition]::new()
        $definition.Height = [Windows.GridLength]::Auto
        [void]$panel.RowDefinitions.Add($definition)
    }
    $toolbar = [Windows.Controls.DockPanel]::new()
    [void]$panel.Children.Add($toolbar)
    $header = [Windows.Controls.TextBlock]::new()
    $header.Text = 'Errors: 0'
    $header.FontSize = 13
    $header.FontWeight = [Windows.FontWeights]::SemiBold
    $header.SetResourceReference([Windows.Controls.TextBlock]::ForegroundProperty, 'MainForegroundColor')
    $header.VerticalAlignment = [Windows.VerticalAlignment]::Center
    [void]$toolbar.Children.Add($header)
    $buttons = [Windows.Controls.StackPanel]::new()
    $buttons.Orientation = [Windows.Controls.Orientation]::Horizontal
    [Windows.Controls.DockPanel]::SetDock($buttons, [Windows.Controls.Dock]::Right)
    $toolbar.LastChildFill = $false
    [void]$toolbar.Children.Add($buttons)
    foreach ($label in @('Copy errors', 'Open session log')) {
        $button = [Windows.Controls.Button]::new()
        $button.Content = $label
        $button.Margin = [Windows.Thickness]::new(10, 0, 0, 0)
        $button.Padding = [Windows.Thickness]::new(10, 3, 10, 3)
        [void]$buttons.Children.Add($button)
    }
    $status = [Windows.Controls.TextBlock]::new()
    $status.Text = 'No errors recorded. Details will appear here if an operation fails.'
    $status.FontSize = 11
    $status.TextWrapping = [Windows.TextWrapping]::Wrap
    $status.Margin = [Windows.Thickness]::new(0, 4, 0, 5)
    $status.SetResourceReference([Windows.Controls.TextBlock]::ForegroundProperty, 'MainForegroundColor')
    [Windows.Controls.Grid]::SetRow($status, 1)
    [void]$panel.Children.Add($status)
    $box = [Windows.Controls.TextBox]::new()
    $box.Name = 'CompanionErrorDetails'
    $box.Height = 96
    $box.IsReadOnly = $true
    $box.AcceptsReturn = $true
    $box.TextWrapping = [Windows.TextWrapping]::Wrap
    $box.VerticalScrollBarVisibility = [Windows.Controls.ScrollBarVisibility]::Auto
    $box.HorizontalScrollBarVisibility = [Windows.Controls.ScrollBarVisibility]::Disabled
    $box.FontFamily = [Windows.Media.FontFamily]::new('Consolas')
    $box.FontSize = 11
    $box.Padding = [Windows.Thickness]::new(6)
    $box.SetResourceReference([Windows.Controls.Control]::ForegroundProperty, 'MainForegroundColor')
    $box.SetResourceReference([Windows.Controls.Control]::BackgroundProperty, 'MainBackgroundColor')
    $box.SetResourceReference([Windows.Controls.Control]::BorderBrushProperty, 'BorderColor')
    $box.Text = 'Waiting for operation errors. You can select and copy any details shown here.'
    [Windows.Controls.Grid]::SetRow($box, 2)
    [void]$panel.Children.Add($box)

    $state = @{
        Sync = $Sync; Header = $header; Status = $status; Box = $box
        HasErrors = $false
        Path = ''; Position = [long]0; Pending = ''; Decoder = $null
        Context = [Collections.Generic.List[string]]::new(); RawBlock = $false; RawErrors = 0
        StructuredError = $false; FallbackCount = 0
        LastError = ''; LastReportedCount = -1; LastReportedError = ''; LastReportedStatus = ''
        ReadWarning = ''; PersistenceWarning = ''
    }
    $Sync.CompanionErrorPanel = $border
    $Sync.CompanionErrorState = $state
    $buttons.Children[0].Add_Click({
        if ($state.HasErrors) {
            try { [Windows.Clipboard]::SetText($state.Box.Text) }
            catch { $state.Status.Text = 'Clipboard unavailable. Select the text and press Ctrl+C to copy it.' }
        }
    }.GetNewClosure())
    $buttons.Children[1].Add_Click({
        $logPath = [string]$state.Sync.logPath
        if ($logPath -and [IO.File]::Exists($logPath)) {
            try {
                $start = [Diagnostics.ProcessStartInfo]::new($logPath)
                $start.UseShellExecute = $true
                [void][Diagnostics.Process]::Start($start)
            } catch { $state.Status.Text = 'Open this saved log manually: ' + $logPath }
        } else { $state.Status.Text = 'The session log has not been created yet.' }
    }.GetNewClosure())
    $timer = [Windows.Threading.DispatcherTimer]::new([Windows.Threading.DispatcherPriority]::Background, $form.Dispatcher)
    $timer.Interval = [TimeSpan]::FromMilliseconds(500)
    $timer.Add_Tick({ Update-WinUtilCompanionErrors -State $state }.GetNewClosure())
    $form.Add_Closed({ $timer.Stop() }.GetNewClosure())
    $Sync.CompanionErrorTimer = $timer
    Update-WinUtilCompanionErrors -State $state
    $timer.Start()
}
