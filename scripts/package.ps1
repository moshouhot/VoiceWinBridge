[CmdletBinding()]
param([string]$ExecutablePath, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $ExecutablePath) { $ExecutablePath = & (Join-Path $PSScriptRoot 'build.ps1') }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'dist' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($ExecutablePath).ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'A semantic release version is required.' }
$package = 'VoiceWinBridge-v' + $version + '-windows-x64'
$folder = Join-Path $OutputDirectory $package
$zip = Join-Path $OutputDirectory ($package + '.zip')
if ((Test-Path -LiteralPath $folder) -or (Test-Path -LiteralPath $zip)) { throw 'Release output already exists. Choose a new OutputDirectory; existing files are not deleted.' }
New-Item -ItemType Directory -Path $folder -Force | Out-Null
Copy-Item -LiteralPath $ExecutablePath -Destination (Join-Path $folder 'VoiceWinBridge.exe')
# Public package is an explicit whitelist, never a copy of the installation directory.
foreach ($name in '使用说明.txt','install.ps1','uninstall.ps1') {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $folder $name)
}
if (Test-Path -LiteralPath (Join-Path $root 'LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination (Join-Path $folder 'LICENSE.txt')
}
Compress-Archive -LiteralPath $folder -DestinationPath $zip -CompressionLevel Optimal
Write-Output $zip
