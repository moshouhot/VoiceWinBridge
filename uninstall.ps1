$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'VoiceWinBridge.exe'
foreach ($process in Get-Process -Name 'VoiceWinBridge' -ErrorAction SilentlyContinue) {
    if ($process.Path -eq $executable) { Stop-Process -Id $process.Id }
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$command = Get-ItemPropertyValue -Path $runKey -Name 'DoubaoVoiceWinBridge' -ErrorAction SilentlyContinue
if ($command -eq ('"{0}"' -f $executable)) {
    Remove-ItemProperty -Path $runKey -Name 'DoubaoVoiceWinBridge'
}
Write-Host 'This copy stopped; its startup entry removed. Settings and files were kept.'
