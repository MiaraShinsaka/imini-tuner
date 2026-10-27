# 把项目发布到 GitHub

这份文档按“第一次发开源项目”来写。先做完第 0 步，再选 GitHub Desktop 或命令行其中一种。

> 当前目录已经初始化了本地 git 仓库（分支 `main`），公开文件已按 `.gitignore` 暂存，但**还没有第一次提交**。签名私钥 `certs/`、构建产物 `dist*/`、`release/`、`build/`、`tools/`、官方安装包和反编译产物都不在暂存区。

## 0. 发布前必须确认

- [ ] `git ls-files` 里没有 `certs/`、`*.pfx`、`*.p12`、`*.key`、`*.exe`、`*.zip`、`*.dll`、`extracted/`、`*_extracted/`、`decompiled_*/`、`tools/`（`tools/hotkey-e2e/` 除外）、`build/`、`dist*/`、`release/`。
- [ ] **签名私钥只在本地**：`certs/imini-tuner-selfsigned.pfx` 和 `certs/pfx-password.txt` 一旦推上 GitHub，就必须视为泄露（别人可以签出“iMini Tuner”发布者的程序）。
- [ ] `LICENSE` 的版权人已改成你的名字或 GitHub ID（当前是 `iMini Tuner contributors`）。
- [ ] README 里保留了“非官方项目、与机械革命无隶属关系”的说明。
- [ ] 你接受：固件控制有风险，Issue 里会有人要求支持各种机型。

自查：

```bat
git ls-files
git status --short
```

只应看到源码、脚本、文档、图标、截图、`.github` 和 `tools/hotkey-e2e/`。

## 1. 建仓库

在 GitHub 网页：

1. 右上角 `+` → `New repository`。
2. 名称建议：`imini-tuner`。
3. 描述：`Unofficial CPU mode and fan control center for MECHREVO imini Pro 830 (Ryzen 7 8745H)`。
4. 选择 `Public`。
5. **不要**勾选 `Add a README file`、`.gitignore`、`license`，本地已经有了。
6. 创建后记下仓库地址，例如：

   ```text
   https://github.com/<your-name>/imini-tuner.git
   ```

## 2. 提交并推送

### 方式 A：命令行

没装 Git 也可以直接用项目自带的 MinGit：`tools\MinGit\cmd\git.exe`（`tools/` 已被忽略，不会进仓库）。

```bat
git config user.name "你的名字"
git config user.email "you@example.com"

git add -A
git status --short
git commit -m "iMini Tuner 2.2.0"
git remote add origin https://github.com/<your-name>/imini-tuner.git
git push -u origin main
```

推送时 GitHub 会要求登录：用 Git Credential Manager 浏览器登录，或创建 Personal Access Token（scope 只需 `repo`）。不要把 token 写进任何文件。

### 方式 B：GitHub Desktop

1. 安装 [GitHub Desktop](https://desktop.github.com/)，登录 GitHub 账号。
2. `File` → `Add local repository`，选择本项目文件夹。
3. 左侧 `Changes` 里逐项检查，确认没有 `certs/`、`extracted/`、`decompiled_*/`、`tools/`、官方 exe/dll/zip。
4. Commit message 填 `iMini Tuner 2.2.0`，点 `Commit to main`。
5. 点顶部 `Publish repository`，取消 `Keep this code private`，确认后发布。

## 3. 发布 v2.2.0

### 3.1 构建发布包

```bat
build-release.ps1
```

需要 Inno Setup 6（`ISCC.exe`）和 Windows SDK 的 `signtool.exe`。脚本会编译完整版、压缩包便携版、Lite，存在 `certs\imini-tuner-selfsigned.pfx` 时自动签名三个 exe 和安装包，然后生成：

```text
release\iMiniTuner-Setup.exe
release\iMiniTuner-Lite.exe
release\iMiniTuner-Portable.zip
release\iMiniTuner-SelfSigned.cer
release\SHA256SUMS.txt
```

没有 `certs\` 时脚本会跳过签名并继续构建，发布包就是无签名的（CI 构建同理）。

### 3.2 打 tag

```bat
git tag -a v2.2.0 -m "iMini Tuner 2.2.0"
git push origin v2.2.0
```

### 3.3 在 GitHub 创建 Release

1. 仓库页右侧 `Releases` → `Draft a new release`。
2. Tag：`v2.2.0`；标题：`iMini Tuner 2.2.0`。
3. 说明里写：
   - 支持机型、BIOS、Windows 版本；
   - 三个版本的区别（安装版 / 单文件便携版 / 压缩包便携版）；
   - 2.2.0 变更：快捷键录制改为独立原生辅助进程，可录制并吞掉 `Win+Alt+Space`；发布包自签名；
   - 四个文件的 SHA-256（见 `release\SHA256SUMS.txt`）；
   - 已知限制：自签名证书首次运行仍会触发 SmartScreen（点“更多信息 → 仍要运行”）、仅 imini Pro 830 完整实测、其他机型先只读验证；
   - 安装与卸载方法。
4. 上传 4 个文件：`iMiniTuner-Setup.exe`、`iMiniTuner-Lite.exe`、`iMiniTuner-Portable.zip`、`iMiniTuner-SelfSigned.cer`。**不要**上传官方安装包。
5. 勾选 `Set as the latest release`，发布。

装了 GitHub CLI 的话可以一条命令：

```bat
gh release create v2.2.0 --title "iMini Tuner 2.2.0" --notes-file CHANGELOG.md ^
  release\iMiniTuner-Setup.exe release\iMiniTuner-Lite.exe release\iMiniTuner-Portable.zip release\iMiniTuner-SelfSigned.cer
```

## 4. 仓库设置

- `About`：填描述、Topics：`mechrevo`, `imini-pro-830`, `mini-pc`, `tauri`, `rust`, `wmi`, `fan-control`, `power-management`, `hotkey`。
- `Settings` → `General`：启用 Issues、Discussions。
- `Settings` → `Security`：启用 private vulnerability reporting / Security Advisories。
- `Settings` → `Branches`：给 `main` 加保护，至少防止误 force-push。
- `Settings` → `Actions`：确认 workflow 有权限；`.github/workflows/build.yml` 只做无签名编译，不需要 secrets。
- 可以置顶一个 Discussion：`提问前请先看：机型 / BIOS / --status`。

## 5. 发布后自检

- 首页文件列表没有 `extracted`、`decompiled_cc`、`tools`、`probe`、`certs`。
- 仓库大小正常（源码 + 截图约 2 MB，不应是几十 MB）。
- Releases 里只有你的 4 个文件，没有官方 exe。
- README 的图片能显示，链接能跳转。
- Actions 的 build 是绿色（第一次跑可能需要按实际环境微调）。

本地可以用：

```bat
git ls-files
git count-objects -vH
```

检查是否有 `.exe`、`.dll`、`.zip`、`.pyd`、`.pfx` 被跟踪。

## 6. 如果误传了敏感文件

还没 push：

```bat
git rm --cached <file>
```

已经 push：

- 私钥、口令：立即把该证书视为作废，重新生成自签名证书并重签所有发布包；只删 commit 不够，GitHub 缓存和 fork 可能仍保留。
- 官方安装包、官方 DLL、反编译产物：把仓库设为 private 或删除重建，或用 `git filter-repo` 清洗历史。
- 官方文件不要用“再提交一次删除”来解决，历史里仍然存在。

最省事的做法：**发布前就把 `.gitignore` 和 `git ls-files` 检查做好**。

## 7. 后续版本

1. 改代码，更新 `CHANGELOG.md` 和版本号（`src-tauri/Cargo.toml`、`src-tauri/tauri.conf.json`、`installer/setup.iss`）。
2. 运行 `build-release.ps1`，实机检查三个版本；UI 有改动时更新截图。
3. 提交：`git add -A && git commit -m "fix: ..."`。
4. 打 tag：`git tag -a v2.2.1 -m "v2.2.1"`，push。
5. 新建 Release，附 4 个文件和 SHA-256。
6. 旧 Release 不要删，方便用户回退。

更完整的 Issue 处理方式见 [maintainer-guide.md](maintainer-guide.md)。
