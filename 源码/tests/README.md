# 输入法归还测试

## 先验证完整 profile，再看真实键盘输入

`InputMethodRestoreTests.cs` 检查 profile 身份、归还条件、SDK 常量及原生结构布局。
`VoiceRpcProbe.cs` 在独立输入框的 UI 线程采样完整 profile；`VoiceRpcAction.cs` 在另一进程调用实际豆包 RPC。
对照字段为类型、语言、CLSID、profile GUID、HKL，不能用中文 HKL 或空 GUID 单独推断输入法。

从源码目录构建测试程序：

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $PWD 'artifacts\profile-restore'
New-Item -ItemType Directory -Path $out -Force | Out-Null
& $csc /nologo /target:exe "/out:$out\InputMethodRestoreTests.exe" `
    .\tests\InputMethodRestoreTests.cs .\InputMethodRestore.cs
& "$out\InputMethodRestoreTests.exe"
foreach ($entry in 'VoiceRpcProbe', 'VoiceRpcAction') {
    & $csc /nologo /target:winexe "/main:$entry" `
        /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
        "/out:$out\$entry.exe" .\tests\VoiceRpcProbe.cs .\tests\VoiceRpcAction.cs `
        .\InputMethodRestore.cs .\DoubaoVoiceClient.cs
}
```

测试工具中的豆包目录和搜狗/豆包 profile GUID 是本机诊断夹具，不用于产品选择恢复目标。
运行 `VoiceRpcProbe.exe --external-commit` 会打开测试框、在该框中选择搜狗，并跨进程开始、停止语音。
其他模式为 `english`、`manual`、`manual-sogou`、`leave`、`cancel`、`shutdown`、`failure`、`repeat`。
必须保持测试框前台到测试动作开始；`leave` 会在语音中自动打开另一个测试窗口。
测试期间会调用麦克风识别服务，但不模拟 Win/Alt 按键，不修改默认输入法或登录自启。
关掉测试框后再开始下一项，避免争用同一语音 RPC 会话和日志文件。

## 2026-11-02 本机结果

环境：豆包输入法 v0.9.3.20、搜狗拼音、英文 US 键盘，启用了逐应用输入语言。

- 纯逻辑：13 项检查通过。
- `fresh-commit`：目标 UI 与读取值都是搜狗 → 豆包 → 搜狗。
- `fresh-english`：英文 → 豆包 → 英文；先向原窗口请求原布局即完成恢复。
- `fresh2-manual`：原搜狗，语音中改英文，结束后保留英文。
- `fresh2-manual-sogou`：原豆包，语音中改搜狗，结束后保留搜狗；覆盖同语种 TIP 交接。
- `fresh-leave`：切离原窗口后跳过恢复。
- `fresh2-cancel`、`fresh2-shutdown`：取消及正常退出后，目标 UI 回到搜狗。
- `fresh2-failure`：缺少 RPC DLL 时清理快照，目标仍是搜狗。
- `fresh2-repeat`：同一调用进程连续两轮，分别重新捕获并回到搜狗。

日志在 `artifacts/profile-restore/<上述名称>-action.log` 和 `-probe.log`。
最初 `fresh-manual`、`fresh-manual-sogou` 的初始中文选择因原语言是英文而返回错误，
不能用作该初始状态的证据；修正夹具先选择中文布局后，使用 `fresh2-*` 结果。

常驻 STA 的反例保留在 `manual-*`、`english-*`：目标 UI 已改变输入法，而调用线程仍读到旧 profile。
一次性 STA 在以上本机样本中与目标 UI 一致；这不是 Windows 跨进程读取接口的一般保证。

自动测试未说话，因此只证明这些会话状态和恢复路径。最终验收需在实际使用的输入框中：
先选搜狗，分别长按右 Alt、Win 说话并松开；确认落字正常，再用键盘输入拼音确认搜狗候选框。
每个触发键连续测试 2～3 次。主动改选豆包与语音自动切到豆包无法区分；
归还仍是已批准的桌面作用域，可能影响其他窗口，强制结束进程不能保证恢复。

## 现场验收已确认

2026-11-02，用户反馈：“落字正常，语音期间会切换豆包，语音输入完毕后会切换搜狗。”
当前安装入口为 `D:\Program Files\VoiceWinBridge\VoiceWinBridge.exe`，运行标记为 `snapshot=fresh-sta`。
现场日志的 10:54:20–10:54:30 两轮均捕获搜狗、结束时读到豆包，随后请求并确认恢复搜狗。
保留日志为 `artifacts/profile-restore/accepted-user-test.log`。当前版本按“结束后恢复原输入法”验收；
语音期间短暂切到豆包仍会发生。
