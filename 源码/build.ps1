$ErrorActionPreference = 'Stop'

$bridgeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $bridgeDir 'VoiceWinBridge.cs'
$voiceSource = Join-Path $bridgeDir 'DoubaoVoiceClient.cs'
$settingsSource = Join-Path $bridgeDir 'BridgeSettings.cs'
$settingsFormSource = Join-Path $bridgeDir 'SettingsForm.cs'
$previewSource = Join-Path $bridgeDir 'VoicePreviewOverlay.cs'
$restoreSource = Join-Path $bridgeDir 'InputMethodRestore.cs'
$executable = Join-Path $bridgeDir 'VoiceWinBridge.exe'
$stagedExecutable = Join-Path $bridgeDir 'VoiceWinBridge.new.exe'
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler was not found. Install .NET Framework 4.x developer tools.'
}

$frameworkRef = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$windowsBase = Join-Path $frameworkRef 'WindowsBase.dll'
$automationClient = Join-Path $frameworkRef 'UIAutomationClient.dll'
$automationTypes = Join-Path $frameworkRef 'UIAutomationTypes.dll'

& $compiler /nologo /target:winexe /platform:anycpu /optimize+ `
    /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.Runtime.Serialization.dll `
    "/reference:$windowsBase" "/reference:$automationClient" "/reference:$automationTypes" `
    "/out:$stagedExecutable" $source $voiceSource $settingsSource $settingsFormSource $previewSource $restoreSource
if ($LASTEXITCODE -ne 0) {
    throw "VoiceWinBridge compilation failed with exit code $LASTEXITCODE"
}

$oldProcesses = @(Get-Process -Name 'VoiceWinBridge' -ErrorAction SilentlyContinue)
if ($oldProcesses.Count -gt 0) {
    $oldProcesses | Stop-Process -Force
    $oldProcesses | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
}

Copy-Item -LiteralPath $stagedExecutable -Destination $executable -Force
Remove-Item -LiteralPath $stagedExecutable

if ($oldProcesses.Count -gt 0) {
    Start-Process -FilePath $executable
}

Write-Host 'VoiceWinBridge build completed.'
