# 贡献指南

感谢你愿意改进 iMini Tuner。这个项目很小，维护者也是普通用户，请尽量把问题描述清楚、把改动控制在小范围内。

## 先讨论，再写代码

- Bug、兼容性问题、功能建议：先开 Issue。
- 较大的改动（新增机型支持、改 WMI 调用方式、改 UI 框架）请先在 Issue 里说明方案，避免白做。
- 不要提交官方安装包、官方解包目录、反编译得到的完整源码、官方 DLL/PYD。只提交你自己写的代码、文档和脱敏后的日志。

## 开发环境

- Windows 10/11 x64
- [Visual Studio 2022 Build Tools](https://visualstudio.microsoft.com/visual-cpp-build-tools/)（“使用 C++ 的桌面开发” + Windows SDK）
- [Rust stable](https://rustup.rs)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php)（仅打安装包需要）
- .NET Framework 4.x（系统自带 `csc.exe`，用于编译 WMI 桥接程序，不需要 .NET SDK）

构建：

```bat
build-tauri.ps1
```

产物：`dist\iMini Tuner.exe`。完整发布构建：

```bat
build-release.ps1
```

## 代码约束

- UI 使用 Tauri V2 + 原生 HTML/CSS/JS，不引入前端打包器或 npm 依赖。
- Rust 侧只做窗口、托盘、热键、设置和进程编排；WMI 访问统一走 `helper\HelperProgram.cs` + `src\PowerSwitch.cs` 的桥接程序。
- WMI 桥接程序保持 C# 5 语法，保证能用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 编译。
- 不引入 NuGet 包、第三方 DLL 或 Python 运行时；安装包统一使用 Inno Setup 6。
- 不加入联网、遥测、自动更新、后台常驻服务。
- 所有需要管理员权限的操作必须显式说明原因。
- 修改 WMI 调用或模式值前，先在 Issue 中给出只读诊断证据。

## 提交前自检

1. `build-tauri.ps1` 通过。
2. 以管理员身份启动 `dist\iMini Tuner.exe`。
3. 依次切换静音 / 均衡 / 狂暴，确认读回状态、温度、风扇转速正常。
4. 检查托盘右键菜单、全局热键、关闭到托盘、隐藏托盘后用 `show-window.ps1` 唤回。
5. 确认没有把 `extracted/`、`decompiled_*/`、`tools/`、官方 `.exe` 或 `.dll` 加入提交。

## Pull Request

- 一个 PR 只做一件事。
- 说明改了什么、为什么改、怎么验证的。
- UI 改动请附截图。
- 兼容性改动请附机型、BIOS、Windows 版本和设置页截图（注意脱敏）。
- 维护者可能会要求你缩小范围或补充测试；这不是针对个人。

## 提交信息

建议使用简短的英文或中文祈使句：

```text
fix: keep background process when tray icon is hidden
docs: add compatibility report template
feat: add tray menu mode shortcuts
```
