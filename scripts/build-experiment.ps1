$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory (Join-Path $root 'artifacts\preview') -ExecutableName 'VoiceWinBridgePreview.exe'
