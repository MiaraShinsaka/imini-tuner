use std::path::PathBuf;
use std::process::Command;
use std::sync::Mutex;

/// A new switch must replace the previous card, not stack behind it.  The
/// native toast process is deliberately tiny; terminating the previous one is
/// cheaper and more predictable than trying to reorder two topmost windows.
static LAST_TOAST_PID: Mutex<Option<u32>> = Mutex::new(None);

fn stop_previous() {
    let mut guard = LAST_TOAST_PID.lock().unwrap_or_else(|p| p.into_inner());
    if let Some(pid) = guard.take() {
        #[cfg(windows)]
        unsafe {
            use windows_sys::Win32::Foundation::{CloseHandle, FALSE};
            use windows_sys::Win32::System::Threading::{OpenProcess, TerminateProcess, PROCESS_TERMINATE};
            let handle = OpenProcess(PROCESS_TERMINATE, FALSE, pid);
            if !handle.is_null() {
                let _ = TerminateProcess(handle, 1);
                let _ = CloseHandle(handle);
            }
        }
        #[cfg(not(windows))]
        {
            let _ = pid;
        }
    }
}

/// The previous compact card, kept as a tiny native GUI process.  A separate
/// process means the card never shares a message loop with the firmware call or
/// the WebView, and it keeps working when the main window is hidden.
const TOAST: &[u8] = include_bytes!("../resources/imini-toast.exe");

fn toast_path() -> Result<PathBuf, String> {
    crate::runtime::write("imini-toast.exe", TOAST)
}

pub fn show_mode(_app: &tauri::AppHandle, mode: i32) {
    if cfg!(feature = "lite") {
        return;
    }
    let name = match mode {
        2 => "静音模式",
        0 => "均衡模式",
        _ => "狂暴模式",
    };
    let message = format!("已切换到“{}”", name);
    show_message(_app, &message);
}

pub fn show_message(_app: &tauri::AppHandle, message: &str) {
    if cfg!(feature = "lite") {
        return;
    }
    let result = (|| -> Result<(), String> {
        let path = toast_path()?;
        let dir = path
            .parent()
            .ok_or_else(|| "通知程序目录无效".to_string())?;
        let system_root = crate::runtime::windows_dir()?;
        let path_env = format!(
            "{};{}",
            system_root.join("System32").display(),
            system_root.display()
        );
        let mut command = Command::new(&path);
        command
            .arg(message)
            .current_dir(dir)
            .env_clear()
            .env("SystemRoot", &system_root)
            .env("WINDIR", &system_root)
            .env("PATH", path_env)
            .env("TEMP", dir)
            .env("TMP", dir);
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            command.creation_flags(0x08000000); // CREATE_NO_WINDOW
        }
        stop_previous();
        let child = command
            .spawn()
            .map_err(|e| format!("无法显示通知: {e}"))?;
        *LAST_TOAST_PID.lock().unwrap_or_else(|p| p.into_inner()) = Some(child.id());
        Ok(())
    })();
    if let Err(error) = result {
        crate::log(&format!("toast failed: {error}"));
    }
}