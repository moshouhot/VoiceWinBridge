# 构建与发布

## 本地构建不影响已安装实例

在 Windows 64 位系统安装 .NET Framework 4.8 Developer Pack 后，于仓库根目录运行：

```powershell
.\scripts\build.ps1
.\scripts\test.ps1 -ExecutablePath .\artifacts\build\VoiceWinBridge.exe
```

输出为 `artifacts/build/VoiceWinBridge.exe`。编译器使用系统 Framework64 下的 `csc.exe`，引用 Developer Pack 的 UIAutomation/WindowsBase 程序集；不需要 NuGet 或 Python。
`AssemblyInfo.cs` 是版本来源。PE 图标与托盘/窗口图标均来自 `assets/VoiceWinBridge.ico`，包含 16–256 像素尺寸。

可选的预览构建为 `scripts/build-experiment.ps1`，输出到 `artifacts/preview/`，使用独立互斥锁；不要与正式实例同时操作语音。

## 创建干净的便携包

```powershell
.\scripts\package.ps1 -ExecutablePath .\artifacts\build\VoiceWinBridge.exe
```

`dist/VoiceWinBridge-v<版本>-windows-x64/` 仅包含 EXE、使用说明、当前用户自启脚本，以及存在时的 LICENSE.txt。旁边生成同名 ZIP。已存在的输出不会被删除或覆盖；重新打包时传入另一个 `-OutputDirectory`。

发布前检查 ZIP 文件清单、EXE 版本、原生图标资源和解压后的启动情况。源码由 GitHub tag 自动提供，不将本机安装目录直接压缩发布。
`.local/` 存放本机恢复备份、旧目录和诊断材料；`artifacts/`、`dist/`、个人 `settings.json` 和 `debug.log*` 均不进入 Git。

## 原创图标

构建直接使用已提交的 ICO。仅需重新生成图标时，运行 `python scripts/create-icon.py`，此可选开发工具依赖 Pillow；不影响普通构建与运行依赖。

## 安装路径与恢复

本机更新时保留原 EXE 所在目录及 `settings.json`，因此登录自启路径不变。替换前备份原 EXE，确认没有语音会话，再停止这一安装路径的准确进程；构建脚本不负责部署。
恢复时退出新版本、放回该备份 EXE 并从原路径启动，不覆盖个人设置。
