$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'VoiceWinBridge.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Extract the complete release package first.' }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name 'DoubaoVoiceWinBridge' -Value ('"{0}"' -f $executable)
foreach ($process in Get-Process -Name 'VoiceWinBridge' -ErrorAction SilentlyContinue) {
    if ($process.Path -eq $executable) {
        Stop-Process -Id $process.Id
        Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
    }
}
Start-Process -FilePath $executable -WindowStyle Hidden
Write-Host 'VoiceWinBridge started. Current-user startup enabled.'
