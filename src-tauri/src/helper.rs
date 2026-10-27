use serde::{Deserialize, Serialize};
use std::io::{BufRead, BufReader, Write};
use std::path::PathBuf;
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};
use std::sync::Mutex;

/// Small C# bridge for the ACPI-WMI firmware interface.  It is embedded in the
/// executable and kept alive for the lifetime of the app, so polling and fan
/// changes do not pay a process-start cost each time.  The WMI provider's
/// method metadata is not usable from the pure-Rust `wmi` crate on this BIOS,
/// so the proven System.Management calls remain in the bridge.
const HELPER: &[u8] = include_bytes!("../resources/imini-power.exe");

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Status {
    #[serde(default)]
    pub ok: bool,
    #[serde(default)]
    pub connected: bool,
    #[serde(default)]
    pub error: String,
    #[serde(default)]
    pub mode: i32,
    #[serde(default)]
    pub mode_name: String,
    #[serde(default)]
    pub temperature: i32,
    #[serde(default)]
    pub fan_rpm: i32,
    #[serde(default)]
    pub cpu_name: String,
    #[serde(default)]
    pub cpu_load: i32,
    #[serde(default)]
    pub clock_mhz: i32,
    #[serde(default)]
    pub mode_count: i32,
    #[serde(default)]
    pub fan_count: i32,
    #[serde(default)]
    pub feature1: i32,
    #[serde(default)]
    pub system_manufacturer: String,
    #[serde(default)]
    pub system_model: String,
    #[serde(default)]
    pub board_product: String,
    /// Filled by this process after the machine identity is read. Never trust
    /// a value supplied by the helper JSON itself.
    #[serde(default)]
    pub machine_verified: bool,
}

/// Only the board/product string is a useful fingerprint on these mini PCs:
/// Win32_ComputerSystem.Model is the generic "IMINI Series".
fn verified_machine(status: &Status) -> bool {
    let board = status.board_product.to_ascii_uppercase();
    board.contains("ARB32H") || board.contains("IMINI SERIES-ARB32H")
}

fn mark_machine(mut status: Status) -> Status {
    status.machine_verified = verified_machine(&status);
    status
}

struct Bridge {
    child: Child,
    stdin: ChildStdin,
    stdout: BufReader<ChildStdout>,
}

impl Bridge {
    fn start() -> Result<Self, String> {
        let path = helper_path()?;
        let dir = path
            .parent()
            .ok_or_else(|| "固件接口目录无效".to_string())?;
        let system_root = crate::runtime::windows_dir()?;
        let system32 = system_root.join("System32");
        let path_env = format!(
            "{};{}",
            system32.display(),
            system_root.display()
        );
        let mut command = Command::new(&path);
        command
            .arg("--serve")
            .current_dir(dir)
            .env_clear()
            .env("SystemRoot", &system_root)
            .env("WINDIR", &system_root)
            .env("PATH", path_env)
            .env("TEMP", dir)
            .env("TMP", dir)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null());
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            command.creation_flags(0x08000000); // CREATE_NO_WINDOW
        }
        let mut child = command
            .spawn()
            .map_err(|e| format!("无法启动固件接口: {e}"))?;
        let stdin = child.stdin.take().ok_or("固件接口输入不可用")?;
        let stdout = child.stdout.take().ok_or("固件接口输出不可用")?;
        Ok(Self {
            child,
            stdin,
            stdout: BufReader::new(stdout),
        })
    }

    fn request(&mut self, line: &str) -> Result<Status, String> {
        writeln!(self.stdin, "{line}").map_err(|e| format!("固件接口写入失败: {e}"))?;
        self.stdin
            .flush()
            .map_err(|e| format!("固件接口刷新失败: {e}"))?;
        let mut text = String::new();
        self.stdout
            .read_line(&mut text)
            .map_err(|e| format!("固件接口读取失败: {e}"))?;
        let trimmed = text.trim();
        if trimmed.is_empty() {
            return Err("固件接口无返回".into());
        }
        let status: Status =
            serde_json::from_str(trimmed).map_err(|e| format!("固件接口返回格式错误: {e}"))?;
        if !status.ok {
            return Err(if status.error.is_empty() {
                "固件接口拒绝了此操作".into()
            } else {
                status.error
            });
        }
        Ok(status)
    }
}

impl Drop for Bridge {
    fn drop(&mut self) {
        let _ = writeln!(self.stdin, "quit");
        let _ = self.stdin.flush();
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

static BRIDGE: Mutex<Option<Bridge>> = Mutex::new(None);

fn helper_path() -> Result<PathBuf, String> {
    crate::runtime::write("imini-power-210.exe", HELPER)
}

fn request(line: &str) -> Result<Status, String> {
    let mut guard = BRIDGE.lock().unwrap_or_else(|p| p.into_inner());
    if guard.is_none() {
        *guard = Some(Bridge::start()?);
    }
    let result = guard
        .as_mut()
        .ok_or("固件接口未启动")?
        .request(line);
    if result.is_err() {
        // Force a fresh process and WMI scope on the next call.
        *guard = None;
    }
    result
}

pub fn status() -> Result<Status, String> {
    request("status").map(mark_machine)
}

pub fn set_mode(mode: u8) -> Result<Status, String> {
    if mode > 2 {
        return Err("无效的模式".into());
    }
    request(&format!("mode {mode}")).map(mark_machine)
}

pub fn set_fan(duty: u8) -> Result<Status, String> {
    request(&format!("fan {duty}")).map(mark_machine)
}
