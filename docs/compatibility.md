# 兼容性说明

## 已实测

| 机型 | 主板 | CPU | BIOS | 系统 | 结果 |
|---|---|---|---|---|---|
| MECHREVO IMINI Series | IMINI-ARB32H-HPT | Ryzen 7 8745H | AMI 1.12 | Windows 11 26200 | 三模式切换、风扇自动/手动、温度保护、自启均通过 |

## 其他机型

程序查找的是：

```text
namespace: root\WMI
class:     PowerSwitchInterface
instance:  ACPI\PNP0C14\IP3POWERSWITCH_0
```

只要实例存在，理论上可以读取状态；但**不要假设模式值和风扇通道一定相同**。不同模具可能：

- 模式数量不同（`GetModeNum` 返回值不同）；
- 静音/均衡/狂暴对应的数值不同；
- 风扇数量、温度通道不同；
- `SetPowerMode` 返回成功，但 EC 实际不响应。

因此其他机型请先只读验证，再决定是否写入。

## 只读验证

以管理员身份运行（桥接程序位于本次运行的 `%ProgramData%\iMiniTuner\run-*` 目录，需管理员权限访问）：

```powershell
$run = Get-ChildItem "$env:ProgramData\iMiniTuner" -Directory |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
& "$($run.FullName)\imini-power-210.exe" --status
& "$($run.FullName)\imini-power-210.exe" --diagnostics
```

`--status` 直接输出 JSON；`--diagnostics` 把接口、实例、方法返回值和原始状态输出到标准输出。

风扇写入请只在确认机型支持后尝试：`--fan 101` 恢复自动，`--fan 35..100` 设置手动百分比（程序硬下限 20）。不同机型的风扇通道和有效范围可能不同，写入前先只读确认 `fanCount`、`fanRpm` 和温度通道。

重点看：

- `connected` 是否为 `true`；
- `modeCount` 是否合理；
- `temperature`、`fanRpm` 是否在合理范围；
- `error` 是否为空。

`--diagnostics` 会把接口、实例、方法返回值和原始状态写到 `diagnostics.txt`。提交 Issue 前请删除用户名、计算机名、序列号等隐私信息。

## 提交兼容性报告

请使用仓库的「兼容性报告」Issue 模板，附上：

- 机型、主板型号、CPU；
- BIOS 版本；
- Windows 版本；
- `imini-power-210.exe --status` 的脱敏 JSON；
- 是否实际切换过模式，以及切换后的温度/风扇/噪音变化；
- 是否出现蓝屏、死机、风扇失控、无法开机等异常。

如果写入后出现异常，请立即切回原模式并断开电源，不要反复尝试。
