$ErrorActionPreference = 'Stop'

$bridgeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifactRoot = Join-Path $bridgeDir 'artifacts\share'
$packageName = 'VoiceWinBridge-share-with-source-2026-07-31'
$packageDir = Join-Path $artifactRoot $packageName
$zipPath = Join-Path $artifactRoot ($packageName + '.zip')
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler was not found.'
}

$frameworkRef = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$references = @(
    'System.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Runtime.Serialization.dll',
    (Join-Path $frameworkRef 'WindowsBase.dll'),
    (Join-Path $frameworkRef 'UIAutomationClient.dll'),
    (Join-Path $frameworkRef 'UIAutomationTypes.dll')
)

if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

$compilerArguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+')
$compilerArguments += $references | ForEach-Object { '/reference:' + $_ }
$compilerArguments += '/out:' + (Join-Path $packageDir 'VoiceWinBridge.exe')
$compilerArguments += @(
    (Join-Path $bridgeDir 'VoiceWinBridge.cs'),
    (Join-Path $bridgeDir 'DoubaoVoiceClient.cs'),
    (Join-Path $bridgeDir 'BridgeSettings.cs'),
    (Join-Path $bridgeDir 'SettingsForm.cs'),
    (Join-Path $bridgeDir 'VoicePreviewOverlay.cs'),
    (Join-Path $bridgeDir 'InputMethodRestore.cs')
)

& $compiler $compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Share build failed with exit code $LASTEXITCODE"
}

$instructions = @(
    '豆包语音 Win 桥接（朋友测试版）',
    '',
    '使用方法：',
    '1. 需要先安装并能正常使用豆包输入法。',
    '2. 将整个文件夹解压到固定位置，不要直接在压缩包里运行。',
    '3. 双击 VoiceWinBridge.exe。',
    '4. 右下角托盘图标右键 → 设置，可以选择 Win / 右 Alt、触发时间和开机自动启动。',
    '5. 长按设置的按键，语音框出现后说话，松开后等待文字写入。',
    '',
    '说明：',
    '- 这是非正式测试包，不是豆包官方程序。',
    '- 首次运行若被 Windows 提示，请确认文件来自分享者后再选择运行。',
    '- 开机自启记录的是当前 EXE 路径；移动文件夹后请重新取消并勾选自启。',
    '- 退出程序：托盘图标右键 → 退出。'
)
$instructions | Set-Content -LiteralPath (Join-Path $packageDir '使用说明.txt') -Encoding UTF8

$sourceDir = Join-Path $packageDir '源码'
New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null
$sourceFiles = @(
    'VoiceWinBridge.cs',
    'DoubaoVoiceClient.cs',
    'BridgeSettings.cs',
    'SettingsForm.cs',
    'VoicePreviewOverlay.cs',
    'InputMethodRestore.cs',
    'build.ps1',
    'build-experiment.ps1',
    'package-share.ps1',
    'install.ps1',
    'uninstall.ps1',
    'README.md',
    'tests\README.md',
    'tests\InputMethodRestoreTests.cs',
    'tests\VoiceRpcProbe.cs',
    'tests\VoiceRpcAction.cs',
    '.gitignore'
)
foreach ($sourceFile in $sourceFiles) {
    $sourcePath = Join-Path $bridgeDir $sourceFile
    if (Test-Path -LiteralPath $sourcePath) {
        $destination = Join-Path $sourceDir $sourceFile
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destination -Force
    }
}
@(
    '这里包含生成本测试包时使用的完整桥接源码与构建脚本。',
    '本包没有包含个人 settings.json、debug.log、Git 历史或豆包输入法自身文件。',
    '重新构建当前分享包：在源码目录中运行 package-share.ps1。'
) | Set-Content -LiteralPath (Join-Path $sourceDir '源码说明.txt') -Encoding UTF8

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -LiteralPath $packageDir -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Share package completed: $zipPath"
