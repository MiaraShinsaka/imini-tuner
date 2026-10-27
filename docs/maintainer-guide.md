# 维护者手册

这份文档写给未来的你：项目公开后，问题会从“能不能用”变成“为什么我的机器不行”“什么时候支持 XX 型号”“你能不能现在修”。下面是一套尽量省时间、又不失礼貌的处理方式。

## 1. 先划边界

在 README 和 Issue 模板里反复写清楚：

- 只对 **imini Pro 830 / IMINI-ARB32H-HPT** 做过完整实测。
- 其他机型只保证“可以只读检测”，不保证写入安全。
- 这是个人项目，不提供商业支持、不承诺响应时间。
- 固件写入有风险，用户自行承担。
- 不接收官方安装包、反编译源码、官方 DLL。

边界写在前面，后面拒绝的时候就不需要反复解释。

## 2. Issue 分级

建议至少这些标签：

| 标签 | 用途 |
|---|---|
| `bug` | 程序本身可复现的错误 |
| `compatibility` | 其他机型/BIOS 的适配请求或报告 |
| `enhancement` | 新功能 |
| `question` | 使用问题 |
| `documentation` | 文档错误 |
| `security` | 安全问题（公开 Issue 里不要放细节） |
| `needs-info` | 等信息，超时关闭 |
| `hardware-risk` | 涉及风扇/温度/死机，优先处理 |
| `confirmed` | 已复现 |
| `wontfix` | 明确不做 |

处理顺序：

1. **安全**：出现死机、风扇失控、无法开机、蓝屏 → 先让用户停止写入、切回原模式、断电，再谈复现。
2. **阻断性 bug**：程序打不开、切模式报错、自启失败。
3. **兼容性**：其他机型。
4. **功能/文档**。

## 3. 每个 Issue 先要这五样

没有这些信息，基本无法判断：

```text
机型 / 主板：
CPU：
BIOS 版本：
Windows 版本：
--status 的脱敏 JSON：
```

再加：

- 是否以管理员身份运行；
- 做了什么操作、期望什么、实际什么；
- `%LOCALAPPDATA%\iminiControlCenter\app.log` 的相关片段；
- 如果涉及界面，附截图。

可以做一个置顶的「提问前请先看」Discussion，把这些贴进去，Issue 里只回链接。

## 4. 常用回复模板

### 兼容性请求

> 这台机器还没有人验证过。请先不要写入模式，只用管理员权限运行 `--status`，把脱敏后的 JSON 贴上来。确认 `connected=true`、`modeCount` 和温度/风扇读数合理后，再决定要不要试切一次。即使接口同名，模式值和风扇通道也可能不同。

### 信息不足

> 目前缺少机型、BIOS、Windows 版本和 `--status` 输出，无法判断。请补充后我再继续看。若 7 天内没有补充，我会先关闭这个 Issue，之后可以随时重开。

### 官方程序相关

> 这个项目不修改官方程序，也不分发官方文件。我们只调用 BIOS 暴露的 ACPI-WMI 接口。官方控制中心的问题请向机械革命反馈。

### 安全问题

> 感谢报告。请不要在公开 Issue 里放利用细节，请通过 Security → Advisories 私密提交，我会在那里回复。

### 要求立刻支持某机型

> 我没有这台机器，无法验证写入行为。可以通过以下方式推进：1）你提供只读 `--status` 和 `--diagnostics`；2）你愿意做一次可回退的实测并记录；3）有人寄测或提供远程环境。否则我只能把它留在 compatibility 列表里，不能凭空猜测模式值。

## 5. 发布流程

每次发版按这个清单走：

1. 更新版本号：`src-tauri/Cargo.toml`、`src-tauri/tauri.conf.json`、`installer/setup.iss`、`CHANGELOG.md`。
2. 运行 `build-release.ps1`，它会依次编译完整版、压缩包便携版、单文件便携版；若存在 `certs\imini-tuner-selfsigned.pfx` 会自动签名，再调用 Inno Setup 生成安装版并签安装包，最后把四个文件放进 `release\`。
3. 实机启动三个版本，确认模式切换、托盘、热键（`Win+Alt+Space` 录制）、设置保存和关闭行为。
4. 更新 `docs/screenshot-light.png` / `docs/screenshot-dark.png` 以及 `docs/tuner-*.png`。
5. 检查 `release\SHA256SUMS.txt`，把三个发布文件的 SHA-256 写进 Release Notes；同时附上 `release\iMiniTuner-SelfSigned.cer`。
6. 提交、打 tag、推送：

   ```bat
   git add -A
   git commit -m "release: v2.2.0"
   git tag -a v2.2.0 -m "v2.2.0"
   git push origin main --tags
   ```

7. GitHub Releases 附上四个文件：`iMiniTuner-Setup.exe`、`iMiniTuner-Lite.exe`、`iMiniTuner-Portable.zip`、`iMiniTuner-SelfSigned.cer`（不要附官方安装包），写变更、校验值、已知问题。
8. 旧版本保留，方便用户回退。

安装包只通过 `installer/setup.iss` 维护，不再自己写安装器逻辑；需要改安装行为时优先用 Inno 的 `[Tasks]`、`[Icons]`、`[Run]`、`[UninstallRun]` 声明。

## 6. 兼容性怎么扩张

不要因为“接口同名”就把新机型写进支持列表。建议分三档：

| 状态 | 含义 |
|---|---|
| 维护者实测 | 你本人在该机型上完成三模式切换 + 回退 |
| 社区报告通过 | 用户提供完整日志和可回退实测，你 review 过 |
| 仅接口存在 | 只读检测通过，写入未验证 |

README 的兼容性表只写前两档；第三档放 `docs/compatibility.md`，并明确“未验证写入”。

新增机型时优先做“只读探测”，不要直接加模式映射。模式值不是通用协议，不同模具可能完全不同。

## 7. 面对后续问题的态度

- **不要承诺时间**。可以说“我会看”，不要说“今晚修”。
- **不要被“我是小白”绑架**。给步骤，但不替用户远程操作。
- **不要接受“你先支持，我再测”**。没有数据就不猜。
- **不要和厂商支持争论**。用户应同时向官方反馈固件问题。
- **可以关闭**：重复 Issue、纯情绪发泄、要求分发官方文件、要求绕过安全限制。
- **保留证据**：涉及硬件异常的 Issue 不要删，打上 `hardware-risk`，方便后续判断。
- **保护隐私**：用户贴的日志可能含用户名/路径，提醒脱敏；不要把这些内容转到别处。

## 8. 法务与品牌

- 仓库里不要出现机械革命官方安装包、官方 DLL、完整反编译源码。逆向文档只描述接口、方法名、返回值和自己的观察。
- README 写明“非官方项目，与机械革命无隶属关系”，不要使用官方 Logo 作为项目 Logo。
- 使用 MIT 许可证；如未来想换许可证，需要所有贡献者同意。
- 如果厂商发来删除请求，先确认对方具体指向哪些文件，再决定替换/删除；不要把争论公开化。

## 9. 可以逐步加的自动化

- GitHub Actions：每次 push 编译一次，防止源码坏掉（已提供 `.github/workflows/build.yml`）。
- Issue 模板：强制填写机型/BIOS/`--status`（已提供）。
- 发布检查脚本：扫描待提交文件，拦截 `.exe`、`.dll`、`extracted/`、`decompiled_*/`。
- 可选：stale bot，30 天无回复的 `needs-info` 自动关闭。

## 10. 最后

这个项目的价值不是“支持所有机械革命”，而是“把 imini Pro 830 这一个机型做对，并把方法写清楚”。守住这个范围，后续问题会少很多；范围一旦被拉大，维护成本会迅速失控。
