$ErrorActionPreference = 'Stop'

$releaseHash = 'EEAE69922FBE6354EA4E2A37F705EAA17BDAF45CC7889618A313178A513B13EE'
$vendorScript = Join-Path $PSScriptRoot 'vendor\winutil.ps1'
if ((Get-FileHash -LiteralPath $vendorScript -Algorithm SHA256).Hash -ne $releaseHash) {
    throw 'The vendored script differs from the official WinUtil 26.10.07 release. Review the upstream release before changing its pinned hash.'
}

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The .NET Framework 4.x C# compiler is required. Build on Windows with .NET Framework enabled.'
}

$outputDirectory = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$sessionWrapper = Join-Path $PSScriptRoot 'src\Run-WinUtilSession.ps1'
$extension = Join-Path $PSScriptRoot 'src\WinUtilExtension.ps1'
$helperHashes = [ordered]@{
    'WinUtil.SessionWrapper' = (Get-FileHash -LiteralPath $sessionWrapper -Algorithm SHA256).Hash
    'WinUtil.Extension' = (Get-FileHash -LiteralPath $extension -Algorithm SHA256).Hash
}
$helperManifest = Join-Path $outputDirectory 'helper-hashes.json'
[IO.File]::WriteAllText($helperManifest, ($helperHashes | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
$arguments = @(
    '/nologo', '/noconfig', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-',
    ('/out:' + (Join-Path $outputDirectory 'WinUtil-Performance.exe')),
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\launcher.manifest')),
    ('/win32icon:' + (Join-Path $PSScriptRoot 'assets\dashboard.ico')),
    ('/resource:' + $vendorScript + ',WinUtil.Script'),
    ('/resource:' + (Join-Path $PSScriptRoot 'vendor\WINUTIL-LICENSE.txt') + ',WinUtil.License'),
    ('/resource:' + (Join-Path $PSScriptRoot 'LICENSE') + ',WinUtil.DashboardLicense'),
    ('/resource:' + (Join-Path $PSScriptRoot 'assets\dashboard.ico') + ',WinUtil.Icon'),
    ('/resource:' + $sessionWrapper + ',WinUtil.SessionWrapper'),
    ('/resource:' + $extension + ',WinUtil.Extension'),
    ('/resource:' + $helperManifest + ',WinUtil.HelperHashes'),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll',
    (Join-Path $PSScriptRoot 'src\launcher.cs'),
    (Join-Path $PSScriptRoot 'src\Dashboard.cs'),
    (Join-Path $PSScriptRoot 'src\PerformanceTelemetry.cs')
    (Join-Path $PSScriptRoot 'src\VendorSession.cs')
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw ('C# compiler failed with exit code ' + $LASTEXITCODE) }
Write-Host ('Built ' + (Join-Path $outputDirectory 'WinUtil-Performance.exe'))
