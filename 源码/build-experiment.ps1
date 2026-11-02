$ErrorActionPreference = 'Stop'
$bridgeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $bridgeDir 'artifacts\live-preview'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found.' }
$executable = Join-Path $outDir 'VoiceWinBridgePreview.exe'
$frameworkRef = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$automationClient = Join-Path $frameworkRef 'UIAutomationClient.dll'
$automationTypes = Join-Path $frameworkRef 'UIAutomationTypes.dll'
$windowsBase = Join-Path $frameworkRef 'WindowsBase.dll'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ `
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  /reference:System.Runtime.Serialization.dll "/reference:$windowsBase" "/reference:$automationClient" "/reference:$automationTypes" `
  "/out:$executable" `
  (Join-Path $bridgeDir 'VoiceWinBridge.cs') `
  (Join-Path $bridgeDir 'DoubaoVoiceClient.cs') `
  (Join-Path $bridgeDir 'BridgeSettings.cs') `
  (Join-Path $bridgeDir 'SettingsForm.cs') `
  (Join-Path $bridgeDir 'VoicePreviewOverlay.cs') `
  (Join-Path $bridgeDir 'InputMethodRestore.cs')
if ($LASTEXITCODE -ne 0) { throw "Experimental compilation failed with exit code $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $bridgeDir 'settings.json') -Destination (Join-Path $outDir 'settings.json') -Force -ErrorAction SilentlyContinue
Write-Host "Experimental build completed: $executable"
