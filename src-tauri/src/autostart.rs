use std::fs;
use std::process::Command;

const TASK_NAME: &str = "iMini Tuner";
const LEGACY_TASK_NAME: &str = "imini Control Center";

fn hidden(mut command: Command) -> Command {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x08000000);
    }
    command
}

fn xml_escape(value: &str) -> String {
    value
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
}

fn current_identity() -> Result<String, String> {
    #[cfg(windows)]
    {
        use windows_sys::Win32::Security::Authentication::Identity::{
            GetUserNameExW, NameSamCompatible,
        };
        let mut length = 0u32;
        unsafe {
            let _ = GetUserNameExW(NameSamCompatible, std::ptr::null_mut(), &mut length);
        }
        if length > 0 {
            let mut buffer = vec![0u16; length as usize];
            let ok = unsafe {
                GetUserNameExW(NameSamCompatible, buffer.as_mut_ptr(), &mut length)
            };
            if ok != 0 {
                buffer.truncate(length as usize);
                return Ok(String::from_utf16_lossy(&buffer));
            }
        }
    }
    Err("无法确定当前系统账户；已拒绝创建开机启动任务".into())
}

fn task_xml() -> Result<String, String> {
    let exe = std::env::current_exe().map_err(|e| format!("无法定位程序: {e}"))?;
    let exe = exe.to_string_lossy();
    let identity = current_identity()?;
    Ok(format!(
        r#"<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>iMini Tuner 后台服务（开机启动）</Description>
    <URI>\{task}</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{identity}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>{identity}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>"{exe}"</Command>
      <Arguments>--minimized</Arguments>
    </Exec>
  </Actions>
</Task>"#,
        task = xml_escape(TASK_NAME),
        identity = xml_escape(&identity),
        exe = xml_escape(&exe)
    ))
}

fn schtasks(args: &[&str]) -> std::process::Output {
    let exe = crate::runtime::system32("schtasks.exe").expect("Windows system directory");
    hidden(Command::new(exe))
        .args(args)
        .output()
        .unwrap_or_else(|e| {
            let cmd = crate::runtime::system32("cmd.exe").expect("Windows system directory");
            let mut fallback = hidden(Command::new(cmd))
                .args(["/C", "exit", "1"])
                .output()
                .expect("cmd");
            fallback.stderr = e.to_string().into_bytes();
            fallback
        })
}

pub fn is_enabled() -> bool {
    schtasks(&["/Query", "/TN", TASK_NAME, "/FO", "LIST"])
        .status
        .success()
}

pub fn set_enabled(enabled: bool) -> Result<(), String> {
    if !enabled {
        for task in [TASK_NAME, LEGACY_TASK_NAME] {
            if schtasks(&["/Query", "/TN", task, "/FO", "LIST"])
                .status
                .success()
            {
                let output = schtasks(&["/Delete", "/TN", task, "/F"]);
                if !output.status.success() {
                    return Err(format!(
                        "删除开机启动任务失败: {}",
                        String::from_utf8_lossy(&output.stderr).trim()
                    ));
                }
            }
        }
        return Ok(());
    }

    if cfg!(feature = "portable") {
        return Err("便携版不提供开机自启；请使用安装版".into());
    }
    let exe = std::env::current_exe().map_err(|e| format!("无法定位程序: {e}"))?;
    let program_files = crate::runtime::program_files()?;
    if !exe.starts_with(&program_files) {
        return Err("为安全起见，只有安装在 Program Files 中的版本才能设置开机自启".into());
    }

    let xml = task_xml()?;
    let stamp = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos();
    let path = crate::runtime::dir()?.join(format!("task-{stamp}.xml"));
    let mut bytes = Vec::with_capacity(xml.len() * 2 + 2);
    bytes.extend_from_slice(&[0xFF, 0xFE]);
    for unit in xml.encode_utf16() {
        bytes.extend_from_slice(&unit.to_le_bytes());
    }
    fs::write(&path, bytes).map_err(|e| format!("无法写入任务文件: {e}"))?;
    let path_text = path.to_string_lossy().to_string();
    let output = schtasks(&[
        "/Create",
        "/TN",
        TASK_NAME,
        "/XML",
        path_text.as_str(),
        "/F",
    ]);
    let _ = fs::remove_file(&path);
    if output.status.success() && is_enabled() {
        let _ = schtasks(&["/Delete", "/TN", LEGACY_TASK_NAME, "/F"]);
        Ok(())
    } else {
        Err(format!(
            "创建开机启动任务失败: {} {}",
            String::from_utf8_lossy(&output.stdout).trim(),
            String::from_utf8_lossy(&output.stderr).trim()
        ))
    }
}
