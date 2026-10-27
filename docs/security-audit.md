# iMini Tuner 安全审查与加固说明

> 审查对象：iMini Tuner 2.2.0（Tauri V2 + Rust，管理员权限运行，内嵌 C# ACPI-WMI 桥接）。
> 审查目标：本机低权限进程不能借助本程序的管理员令牌提权，或让本程序执行规定功能之外的代码。

## 1. 威胁模型

本程序必须管理员运行，才能访问固件 WMI 实例。最主要的现实威胁是：

- 同一台电脑上的**普通用户/中完整性进程**（恶意下载、浏览器漏洞、其他软件）提前在用户可写位置放置文件、目录联接或环境变量；
- 诱使已提升的 iMini Tuner 启动这些文件，或向用户可写路径执行高权限写入。

不在本审查范围内：已经拿到管理员权限的攻击者、内核/驱动级攻击、物理接触、供应链投毒、Windows 或 WebView2 自身的 0day。管理员本来就可以调试或注入本进程，这类攻击无法由应用层彻底阻止。

## 2. 发现与修复

### 2.1 高危：用户可写目录中的辅助程序替换（已修复）

**问题**：2.1.0 早期版本把内嵌的 `imini-power-210.exe`、`imini-toast.exe` 释放到
`%LOCALAPPDATA%\iminiControlCenter\`。该目录归当前用户所有；低权限进程可以在程序启动前
预先放入同名文件，或在“释放后、CreateProcess 前”替换文件，使管理员进程执行任意代码。

**修复**（`src-tauri/src/runtime.rs`）：

- 辅助程序改放到 `%ProgramData%\iMiniTuner\run-<时间戳>-<pid>\`；
- 创建后立刻用绝对路径的 `takeown.exe` / `icacls.exe` 收紧：
  - owner 改为 `NT AUTHORITY\SYSTEM`（`icacls /setowner *S-1-5-18`）；
  - 仅保留 `SYSTEM:(OI)(CI)F` 与 `BUILTIN\Administrators:(OI)(CI)F`；
  - `/reset` 清除预置 ACE，`/inheritance:r` 去掉继承 ACE；
- 拒绝重解析点（目录联接/符号链接）；对预先存在的 `run-*` 联接先删除再收紧；
- 写入后重新读取并逐字节比较，确认落盘内容就是内嵌字节；
- 启动辅助程序时使用绝对路径，并清空继承环境，仅保留 `SystemRoot`、`WINDIR`、最小 `PATH`、`TEMP/TMP`；
- 每次启动清理旧的 `run-*` 目录，程序退出时删除本次目录。

**验证**：中完整性 PowerShell 预先创建 `C:\ProgramData\iMiniTuner\run-evil\imini-power-210.exe`
后启动程序，程序会清理该目录并重建；中完整性进程随后访问 `C:\ProgramData\iMiniTuner`
返回“访问被拒绝”。ACL 实测 owner 为 `NT AUTHORITY\SYSTEM`，仅 SYSTEM/Administrators 有完全控制。

### 2.2 高危：开机自启任务被滥用（已修复）

**问题**：旧实现使用可预测的 `%TEMP%` XML 文件、裸 `schtasks` 名称查找，并允许任意路径创建
“最高权限”登录任务；攻击者可预先放置 XML/伪造 PATH，或把便携版复制到用户可写目录后注册
高权限自启动，形成持久化。

**修复**（`src-tauri/src/autostart.rs`）：

- 仅允许**安装在 `%ProgramFiles%` 下的程序**设置开机自启；便携版直接拒绝；
- 任务 XML 写入管理员专属的 `%ProgramData%\iMiniTuner\run-*` 目录，使用不可预测文件名；
- `schtasks.exe` 通过 `GetSystemDirectoryW` 解析绝对路径，不依赖 `PATH`；
- 任务中的用户身份使用 `GetUserNameExW(NameSamCompatible)` 获取；获取失败时**失败关闭**，不再信任 `USERDOMAIN/USERNAME` 环境变量；
- 只注册当前交互用户、`HighestAvailable` 的登录任务；
- 安装/卸载脚本使用固定任务名并清理旧版 `imini Control Center` 任务。

**验证**：静默安装到 `C:\Program Files\iMini Tuner` 后，任务 XML 的
`<UserId>` 为 `<计算机名>\<用户名>`（SAML 兼容格式），`<Command>` 为带引号的安装路径，`<Arguments>` 为
`--minimized`；关闭自启后任务被删除。

### 2.3 中危：WebView 内容与 IPC 攻击面（已收紧）

- `tauri.conf.json` 设置 CSP：仅允许本地资源、内联样式和 Tauri IPC；
- 关闭远程 URL 导航；窗口只加载打包的 `ui/` 资源；
- 前端不渲染固件返回的任意 HTML，只使用 `textContent`；
- 不启用 devtools 的远程调试端口；
- IPC 只暴露固定命令：状态、模式、风扇、设置、自启、退出；没有通用命令执行、文件读写或 shell。

### 2.4 低危：日志/设置重解析点与信息暴露（已处理）

- 设置保存前检查重解析点，拒绝写入符号链接；
- 日志目录/文件检查重解析点，避免高权限进程把日志追加到任意位置；
- 日志只记录模式/风扇/错误，不记录令牌、密码或完整环境；
- 设置文件仍在用户目录（非特权数据），但不再参与任何代码加载。

### 2.5 其他边界

- 不调用 `cmd.exe /c` 执行用户输入；不把用户字符串拼进命令行；
- 固件方法名、风扇编号、模式编号均为固定枚举/数值范围校验；
- 前端模式切换、风扇滑块和设置只能触发固定 Rust 命令；
- 安装包不把官方控制中心、Python 运行时或反编译产物带入发布包；
- 命名事件 `Local\iMiniTuner.Show` 仅用于唤回界面，低权限进程最多造成“窗口被显示”的干扰，不能借此执行代码。

## 3. 残余风险

以下风险仍然存在，发布说明中不应声称“绝对安全”：

1. **未做代码签名**：下载渠道被替换、或用户运行了被篡改的安装包时，UAC 仍会按用户确认放行。正式发布建议使用 Authenticode 证书签名，并发布 SHA-256。
2. **管理员级攻击者**：可调试、注入、替换 Program Files 或修改计划任务；应用层无法防御。
3. **WebView2/Rust 依赖供应链**：应定期更新 Tauri、WebView2 Runtime 和 Rust 依赖，并在 CI 运行 `cargo audit`。
4. **命名事件干扰**：同会话低权限进程可以唤出主窗口（拒绝服务/干扰），但不能借此执行代码或读取特权数据。
5. **固件/硬件风险**：风扇低转速、温度保护关闭、断电或强制结束进程仍可能造成过热；这是功能安全风险，不是提权漏洞。
6. **安装包升级路径**：安装器会静默卸载旧版 `imini Control Center`；若用户把旧版装到非默认目录，需手动卸载。

## 4. 复核清单

- [x] 中完整性进程无法读取/替换 `%ProgramData%\iMiniTuner` 中的辅助程序
- [x] 预置目录联接/伪造 helper 被清理，程序仍能连接固件
- [x] 便携版拒绝开机自启；安装版任务使用绝对路径与当前用户 SID
- [x] CSP 生效，界面无远程资源
- [x] 设置/日志拒绝重解析点
- [x] 发布构建通过 `cargo check --release`，安装/卸载 smoke test 通过
- [ ] 发布前用 Authenticode 签名（需要证书，当前未做）

## 5. 复现与验证命令

以管理员身份运行程序后，可用中完整性 PowerShell 验证：

```powershell
# 应被拒绝
Get-ChildItem 'C:\ProgramData\iMiniTuner' -Force

# 应显示 owner 为 NT AUTHORITY\SYSTEM，且只有 SYSTEM/Administrators
(Get-Acl 'C:\ProgramData\iMiniTuner').Owner
```

查看计划任务：

```powershell
schtasks /Query /TN "iMini Tuner" /XML
```

如果发现新的攻击面，请通过 `SECURITY.md` 中的私下渠道报告，不要在公开 Issue 中贴出可利用细节。
