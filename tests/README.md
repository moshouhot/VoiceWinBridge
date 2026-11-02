# 测试

## 默认测试不调用麦克风

```powershell
.\scripts	est.ps1
```

- `InputMethodRestoreTests.cs`：13 项 profile 身份、恢复条件、SDK 标志和结构布局检查。
- `ReleaseSmokeTests.cs`：读取实际产物的版本和图标，实例化设置窗口并生成 `artifacts/tests/settings-preview.png`；不调用语音、不修改登录自启。

## 跨进程现场对照

`VoiceRpcProbe.cs` 在独立输入框的 UI 线程读取完整 profile，`VoiceRpcAction.cs` 在另一个进程调用实际豆包 RPC。必须同时比较类型、语言、CLSID、profile GUID、HKL，不能仅凭任务栏图标或中文 HKL 判断。

可用 Framework64 的 `csc.exe` 将两个文件与 `src/DoubaoVoiceClient.cs`、`src/InputMethodRestore.cs` 编译为两个独立 winexe（分别指定 `/main:VoiceRpcProbe` 与 `/main:VoiceRpcAction`），引用 System、System.Drawing 和 System.Windows.Forms。放在同一测试输出目录。

运行 `VoiceRpcProbe.exe --external-commit`，保持测试框前台。其他模式：`english`、`manual`、`manual-sogou`、`leave`、`cancel`、`shutdown`、`failure`、`repeat`。这些手动诊断模式会使用麦克风及实际语音会话，需先退出其他测试实例。

夹具中的豆包版本目录和搜狗/豆包 GUID 针对原本机测试环境；其他机器运行前须核对。它们不用于产品选择恢复目标。每轮 `probe.log` 会覆盖，`action.log` 追加；日志不随源码发布。

## 已验证的基线与限制

2026-11-02，在豆包 v0.9.3.20、搜狗拼音及英文 US 键盘、逐应用输入语言环境下：

- 搜狗 → 豆包 → 搜狗；英文 → 豆包 → 英文。
- 原搜狗，语音中改英文：保留英文。
- 原豆包，语音中改搜狗：保留搜狗，覆盖同语种 TIP 交接。
- 切离原窗口跳过恢复；取消、正常退出后恢复；缺少 DLL 时清理；同一进程连续两轮分别捕获和恢复。
- 现场实测语音正常落字，语音期间切到豆包，结束后回到搜狗。

常驻 STA 曾出现“目标已变、调用线程仍返回旧 profile”的反例；一次性 STA 在以上本机样本中与目标 UI 一致。这不是跨进程查询接口的一般保证。其他环境的最终验收仍需：在实际输入框中分别使用右 Alt、Win 语音，确认落字后键盘拼音的候选框属于原输入法。
