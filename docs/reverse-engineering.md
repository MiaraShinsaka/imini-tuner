# imini Pro 830 控制中心逆向记录

本文记录 2026-10-26 对机械革命官方控制中心的逆向过程，以及 `iMini Tuner` 所依赖的固件接口。目标是让后来者能复现、审查，而不是把控制逻辑藏在二进制里。

## 1. 样本与环境

| 文件 | SHA-256 |
|---|---|
| `机械革命控制中心V1.4.exe` | `48F1FD372CC6117666987C7AA6F4BD53409CCC0CC6BA78FE09B454B3741A16C2` |
| `ImagesShowSetUpOSDV2.4.exe` | `5E7521C113795C0CAB68B4173D16AF1AC5A6B220EE79AB6DECA139C413B89C52` |

测试机：

```text
机型      MECHREVO IMINI Series / IMINI-ARB32H-HPT
CPU       AMD Ryzen 7 8745H w/ Radeon 780M Graphics
BIOS      AMI 1.12
系统      Windows 11 26200
```

两个安装包均为 Inno Setup。解包后：

- `ControlCenter.exe`：PyInstaller 打包，Python 3.6；
- `OsdShowV2.4.exe`：Qt OSD 程序，不包含 CPU 模式 WMI 逻辑。

> 本仓库不包含上述安装包、解包目录或反编译产物，只保留接口结论和最小复现步骤。请自行从官方渠道获取样本，并遵守当地法律与软件许可。

## 2. 解包与反编译

```powershell
# Inno Setup
innoextract -e -d ControlCenter.exe_extracted "机械革命控制中心V1.4.exe"

# PyInstaller
python pyinstxtractor.py ControlCenter.exe
# 得到 ControlCenter.exe_extracted\ControlCenter.pyc（Python 3.6）
uncompyle6 -o decompiled_cc ControlCenter.pyc
```

反编译产物：

- `decompiled_cc\ControlCenter.py`（156,728 字节，主界面与模式逻辑）
- `decompiled_cc\CPUGPUMemorynew.py`（温度/内存/磁盘圆环控件）
- `decompiled_cc\btn1.py`

## 3. 关键结论：CPU 模式只走 ACPI-WMI

官方代码里没有任何 `powercfg`、注册表电源计划或 TDP 写入。模式切换只调用一个 WMI 方法：

```python
# decompiled_cc\ControlCenter.py:2628-2704
SetPowerMode(2)   # 静音
SetPowerMode(0)   # 均衡
SetPowerMode(1)   # 狂暴
```

WMI 目标：

```text
namespace  root\WMI
class      PowerSwitchInterface
instance   ACPI\PNP0C14\IP3POWERSWITCH_0
```

`ACPI\PNP0C14` 是 Microsoft 定义的 ACPI-WMI 映射设备；具体方法由厂商 BIOS/EC 的 ASL 提供。因此这条路径与显卡无关，也不需要加载官方 DLL。

### 方法表

| 方法 | 入参 | 出参 | 说明 |
|---|---|---|---|
| `GetPowerMode` | 无 | `CurrentPowerMode` (uint8) | 当前模式，实测 0/1/2 |
| `SetPowerMode` | `PowerMode` (uint8) | `ResultStatus` (uint32) | 0 表示成功 |
| `GetFeatureValue` | `Reserved1..4` (uint8) | `ResultStatus` (uint32) | `Reserved1` 为特征号 |
| `SetFeatureValue` | `Reserved1..4` (uint8) | `ResultStatus` (uint32) | `Reserved1` 特征号，`Reserved2` 值 |
| `GetHwTemp` | `HwTempType` (uint8) | `Temp` (uint32) | 本机仅类型 1 有效 |
| `GetFanControl` | `FanNumber` (uint8) | `FanDuty` (uint32) | 低 16 位 fan1 RPM，高 16 位 fan2 RPM |
| `SetFanControl` | `FanNumber`、`FanDuty` | `ResultStatus` | 101 = 自动；20–100 = 手动百分比 |

官方风扇写入位于 `decompiled_cc\ControlCenter.py:304-328`：`SetFanControl(FanDuty, FanNumber)`，滑块范围 35–100，另有 101 表示固件自动。本机只有 1 路风扇。

`GetFeatureValue` 的特征号（来自 `ControlCenter.py:141-233` 与实测）：

| 特征号 | 含义 | 本机值 |
|---|---|---|
| 1 | OSD/功能位 | 1 |
| 2 | 支持的模式数量 | 3 |
| 3 | 风扇数量 | 1 |
| 4 | GPU/功能位 | 15 |
| 5 | CPU Turbo 支持 | 255 |

### 实测读数

管理员权限下对 `PowerSwitchInterface` 逐项调用，结果（`probe\probe_result2.json`）：

```json
{
  "instance": "ACPI\\PNP0C14\\IP3POWERSWITCH_0",
  "feature1": 1,
  "feature2": 3,
  "feature3": 1,
  "feature4": 15,
  "feature5": 255,
  "fanCount": 1,
  "powerMode": 2,
  "fan1": 1189,
  "fan2": 2147483647,
  "tempType1": 57,
  "tempType2": 2147483647,
  "tempType3": 2147483647,
  "tempType4": 2147483647,
  "tempType5": 2147483647,
  "tempType6": 2147483647
}
```

写入测试：

```text
SetPowerMode(2) -> ResultStatus 0, 读回 2
SetPowerMode(0) -> ResultStatus 0, 读回 0
SetPowerMode(1) -> ResultStatus 0, 读回 1
SetPowerMode(0) -> ResultStatus 0, 读回 0   # 恢复
```

`2147483647`（0x7FFFFFFF）是无效/不支持的哨兵值，不能当作温度或转速显示。

## 4. 权限模型

普通权限（medium integrity）下：

- 可以打开 `root\WMI` 命名空间；
- 可以读到 `PowerSwitchInterface` 实例；
- 调用 `GetPowerMode` / `SetPowerMode` 返回 `Access denied` / `0x80070005`。

本机管理员组处于 deny-only（UAC 拆分令牌），因此必须提升到 high integrity 才能调用方法。这也是本程序在 manifest 中声明 `requireAdministrator` 的原因，而不是为了别的权限。

计划任务使用 `RunLevel=HighestAvailable`，登录时以完整令牌启动，因此开机自启不会再次弹 UAC。

## 5. 官方程序的其他发现

- `DllCallMethod.dll` 只包含 AMD ADL 显示相关导出，与 CPU 模式无关。
- `GetHWTemperture()`（`ControlCenter.py:1372-1379`）实际调用 `GetPowerMode`，却读取不存在的 `CurrentHWTemperture` 字段，属于错误实现/死代码。
- 官方风扇 RPM 读取（`ControlCenter.py:282-300`）把 `FanDuty` 拆成两个 16 位值；与本工具实现一致。
- OSD 程序 `OsdShowV2.4.exe` 只负责屏幕提示，没有 `PowerSwitchInterface` 调用。

## 6. 本工具的实现边界

只做以下调用：

```text
GetPowerMode
SetPowerMode(0/1/2)
GetFeatureValue(1..5)
GetHwTemp(1)
GetFanControl(1)
SetFanControl(20..100 或 101, 1)
```

风扇写入是本工具在 2.1 新增的能力：默认最低 35%，解除限制后硬下限 20%；退出时恢复 101（自动），温度保护默认在 88°C 时恢复自动。不写 `SetFeatureValue`、不修改电源计划、不加载官方 DLL。模式切换的暴露面仍限于官方程序已经使用的三个值；风扇写入选在官方滑块的有效范围内，并额外限制更低的 20%。

## 7. 复现命令

本仓库不附带官方样本或 probe 脚本。复现时先用第 2 节的方法自行解包，或直接用管理员 PowerShell 读取同一 WMI 实例；本工具自身不做 WMI 写操作诊断，风扇/模式结果可在界面读回：

```powershell
# 以管理员身份运行
"dist\iMini Tuner.exe" --show
Get-Content "$env:LOCALAPPDATA\iminiControlCenter\app.log" -Tail 40
```

## 8. 参考

- [OpenRevo（40/50 系笔记本）](https://github.com/faintonce/open-revo)
- [JiaoLongControl（蛟龙 16 Pro）](https://github.com/GaoXanSheng/JiaoLongControl)
- Microsoft ACPI-WMI 映射规范：`ACPI\PNP0C14` / `root\WMI`
