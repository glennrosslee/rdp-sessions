[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'),
    [switch]$Install,
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'This build needs the 64-bit .NET Framework compiler included with Windows 11.'
}
$outputPath = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$exe = Join-Path $outputPath 'RDP Sessions.exe'
$sources = 'RdpTaskbarMenu.cs', 'ShortcutEditor.cs', 'ShortcutStore.cs', 'LauncherTheme.cs', 'MenuOrder.cs', 'MenuDragLayout.cs', 'RdpDocument.cs', 'RdpSettingsEditor.cs', 'RdpPropertyPages.cs' | ForEach-Object {
    Join-Path $PSScriptRoot $_
}
$references = @(
    "/reference:$framework\WPF\PresentationFramework.dll",
    "/reference:$framework\WPF\PresentationCore.dll",
    "/reference:$framework\WPF\WindowsBase.dll",
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll'
)
$icon = Join-Path $PSScriptRoot 'RdpSessions.ico'
& $compiler /nologo /warnaserror+ /optimize+ /debug- /platform:anycpu /target:winexe "/out:$exe" "/win32icon:$icon" "/resource:$icon,RdpSessions.ico" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }

if ($Installer) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.cmd') -Destination (Join-Path $outputPath 'Install.cmd') -Force
}

if ($Install) {
    $installDirectory = (New-Item -ItemType Directory -Path (Join-Path $env:LOCALAPPDATA 'RDP Sessions') -Force).FullName
    $installedExe = Join-Path $installDirectory 'RDP Sessions.exe'
    $staged = Join-Path $installDirectory ('.' + [guid]::NewGuid() + '.tmp')
    try {
        Copy-Item -LiteralPath $exe -Destination $staged
        if (Test-Path -LiteralPath $installedExe) {
            [IO.File]::Replace($staged, $installedExe, (Join-Path $installDirectory 'RDP Sessions.previous.exe'))
        } else {
            [IO.File]::Move($staged, $installedExe)
        }
    } catch {
        throw "Could not update the launcher. Close RDP Sessions and retry. $($_.Exception.Message)"
    } finally {
        [IO.File]::Delete($staged)
    }
    $setup = Start-Process -FilePath $installedExe -ArgumentList '--register' -Wait -PassThru -WindowStyle Hidden
    if ($setup.ExitCode -ne 0) { throw "Start shortcut or Jump List registration failed (exit $($setup.ExitCode))." }
    Write-Output "Installed $installedExe. Search Start for RDP Sessions, then choose Pin to taskbar."
} else {
    Write-Output $exe
}
