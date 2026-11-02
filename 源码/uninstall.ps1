$ErrorActionPreference = 'Stop'

Get-Process -Name 'VoiceWinBridge' -ErrorAction SilentlyContinue | Stop-Process
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'DoubaoVoiceWinBridge' -ErrorAction SilentlyContinue

Write-Host 'VoiceWinBridge stopped and removed from startup.'
