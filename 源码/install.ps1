$ErrorActionPreference = 'Stop'

$bridgeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$executable = Join-Path $bridgeDir 'VoiceWinBridge.exe'

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'VoiceWinBridge.exe is missing. Please extract the complete release package first.'
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name 'DoubaoVoiceWinBridge' -Value ('"{0}"' -f $executable)

$oldProcess = Get-Process -Name 'VoiceWinBridge' -ErrorAction SilentlyContinue
if ($oldProcess) {
    $oldProcess | Stop-Process -Force
    $oldProcess | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue
}

Unblock-File -LiteralPath $executable -ErrorAction SilentlyContinue
Start-Process -FilePath $executable

Write-Host 'VoiceWinBridge installed and started.'
