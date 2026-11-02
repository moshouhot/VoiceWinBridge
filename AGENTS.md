# VoiceWinBridge 项目约定

- 既有 C# / .NET Framework 4.8 WinForms 托盘程序，保持单 EXE 与现有 Win/右 Alt 行为。
- `src/VoiceWinBridge.cs`：键盘钩子与托盘；`DoubaoVoiceClient.cs`：串行 RPC 会话；`InputMethodRestore.cs`：具体 TSF profile 快照和恢复。
- COM 查询不能放进低级键盘钩子；停止、取消、异常和退出共用清理，不能让新会话抢用旧快照。
- 恢复只在原窗口/PID/TID 仍在前台且当前仍是豆包时进行；桌面作用域与手动选择限制必须保留在文档中。
- 构建：`scripts/build.ps1`。测试：`scripts/test.ps1`。打包：`scripts/package.ps1`。构建不得自动终止或替换安装实例。
- 版本源为 `src/AssemblyInfo.cs`；图标源为 `assets/VoiceWinBridge.ico`，PE 与运行时使用同一图标。
- `tests/VoiceRpcProbe.cs` 和 `VoiceRpcAction.cs` 是使用实际麦克风的现场诊断工具，不属于默认测试。
- `.local/`、个人设置、日志和豆包组件不能提交。发布包只能按白名单生成，不能压缩整个安装目录。
- 运行和开发备份均不属于公开源码；安装入口及 settings.json 路径不能因重组源码而改变。
