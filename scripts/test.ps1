[CmdletBinding()]
param([string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts\tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
if (-not $ExecutablePath) { $ExecutablePath = & (Join-Path $PSScriptRoot 'build.ps1') }
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$unit = Join-Path $out 'InputMethodRestoreTests.exe'
& $compiler /nologo /warnaserror+ /target:exe "/out:$unit" (Join-Path $root 'tests\InputMethodRestoreTests.cs') (Join-Path $root 'src\InputMethodRestore.cs')
if ($LASTEXITCODE -ne 0) { throw 'Unit test compilation failed.' }
& $unit
if ($LASTEXITCODE -ne 0) { throw 'Input-method guard tests failed.' }
$smokeSource = Join-Path $root 'tests\ReleaseSmokeTests.cs'
if (Test-Path -LiteralPath $smokeSource) {
    $smoke = Join-Path $out 'ReleaseSmokeTests.exe'
    & $compiler /nologo /warnaserror+ /target:exe /reference:System.Runtime.Serialization.dll /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/out:$smoke" $smokeSource
    if ($LASTEXITCODE -ne 0) { throw 'Release test compilation failed.' }
    & $smoke $ExecutablePath $out
    if ($LASTEXITCODE -ne 0) { throw 'Release smoke tests failed.' }
}
Write-Host 'Tests passed. No microphone or startup settings were changed.'
