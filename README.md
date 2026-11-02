# VoiceWinBridge

<img src="assets/VoiceWinBridge.png" width="88" height="88" alt="VoiceWinBridge 图标">

**长按说话，松开落字，结束后回到原输入法。**

Windows 托盘小工具：用 **Win** 或 **右 Alt** 调用已安装的豆包输入法语音识别。可以继续使用搜狗等输入法进行键盘输入，不必将豆包设为日常输入法。

## 下载与使用

从 [Releases](https://github.com/moshouhot/VoiceWinBridge/releases) 下载 `VoiceWinBridge-v1.0.0-windows-x64.zip`，解压到可写的固定目录，双击 `VoiceWinBridge.exe`。无需 Python，也不需要编译源码。

1. 先安装并确认豆包输入法的语音识别可正常使用。
2. 在目标输入框中选好日常输入法，例如搜狗。
3. 单独长按 Win 或右 Alt，说话后松开，等待文字写入。
4. 双击托盘图标打开设置；右键可以暂停或退出。

短按 Win 仍打开开始菜单，Win+E、Win+R 等组合键保留原行为。设置支持分别开关两种触发键、调整 300–1500 毫秒的触发阈值、探测豆包目录和当前用户登录自启。默认阈值为 520 毫秒。

## 输入法恢复的范围

语音期间可能切成豆包；**本版在语音结束后恢复调用前的具体输入法**，不承诺全程不切换。

恢复前检查原窗口、进程和线程仍在前台，并且当前仍是豆包。离开原窗口或手动改选其他非豆包输入法时跳过。主动选择豆包与语音自动切换无法区分。

Windows 的恢复接口作用于当前桌面，也可能改变其他窗口的输入法选择；不修改默认输入法或 profile 启用状态。正常结束、取消、RPC 错误和正常退出共用清理路径，强制结束进程无法保证恢复。

## 环境与验证

- 面向 Windows 10/11 64 位，运行需要 .NET Framework 4.8。
- 已在本机豆包输入法 **v0.9.3.20**、搜狗拼音和英文 US 键盘环境验证。
- 现场确认语音正常落字，结束后回到搜狗；跨进程对照覆盖手动改选、切窗、取消、退出及连续会话。
- 豆包 RPC 属于非公开接口，版本更新可能影响兼容性。其他输入法、自绘输入框及提权应用需要在实际环境复测。

语音识别由已安装的豆包输入法提供，联网与账号要求遵循它自身的规则。本项目不包含豆包安装包、`rpc.dll`、识别模型或账号数据，也不是豆包官方产品。

## 源码与构建

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1 -ExecutablePath .\artifacts\build\VoiceWinBridge.exe
```

需要 .NET Framework 4.8 Developer Pack。构建只写入 `artifacts/`，不会停止或替换正在运行的程序。发布包输出到 `dist/`，采用文件白名单，不包含个人设置、日志、备份或豆包自身文件。

| 目录 | 内容 |
| --- | --- |
| `src/` | WinForms 托盘、按键处理、语音 RPC、输入法恢复 |
| `assets/` | 原创多尺寸 ICO、PNG 和 SVG 图标 |
| `scripts/` | 构建、测试和白名单打包 |
| `tests/` | 身份/恢复条件测试、资源/UI 冒烟及现场对照工具 |
| `docs/` | 构建和发布说明 |

详见 [构建说明](docs/BUILD.md)、[测试说明](tests/README.md) 与 [版本记录](CHANGELOG.md)。

## 许可证

本项目源码及原创图标采用 [MIT License](LICENSE.txt)。豆包输入法及其组件不属于本仓库，遵循各自的许可。
