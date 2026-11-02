[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [ValidatePattern('^[A-Za-z0-9.-]+\.exe$')][string]$ExecutableName = 'VoiceWinBridge.exe'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\build' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework 4.x x64 compiler is required.' }
$frameworkRef = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$references = @('System.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll')
foreach ($name in 'WindowsBase.dll','UIAutomationClient.dll','UIAutomationTypes.dll') {
    $path = Join-Path $frameworkRef $name
    if (-not (Test-Path -LiteralPath $path)) { throw '.NET Framework 4.8 Developer Pack is required.' }
    $references += $path
}
$icon = Join-Path $root 'assets\VoiceWinBridge.ico'
if (-not (Test-Path -LiteralPath $icon)) { throw 'App icon is missing.' }
$executable = Join-Path $OutputDirectory $ExecutableName
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/warnaserror+')
$arguments += $references | ForEach-Object { '/reference:' + $_ }
$arguments += "/win32icon:$icon"
$arguments += "/resource:$icon,VoiceWinBridge.AppIcon"
$arguments += "/out:$executable"
$arguments += Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }
# Build only: never stop or replace a running installation.
Write-Output $executable
