use std::ffi::OsString;
use std::fs;
use std::os::windows::ffi::OsStringExt;
use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::OnceLock;
use std::time::{SystemTime, UNIX_EPOCH};

use windows_sys::Win32::System::SystemInformation::{GetSystemDirectoryW, GetWindowsDirectoryW};

const CREATE_NO_WINDOW: u32 = 0x08000000;
const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x400;

static RUNTIME_DIR: OnceLock<Result<PathBuf, String>> = OnceLock::new();

/// Absolute path to a system executable.  Resolving this instead of relying on
/// PATH prevents a same-user directory from shadowing schtasks/takeown/icacls.
pub fn system32(name: &str) -> Result<PathBuf, String> {
    let mut buffer = vec![0u16; 32768];
    let len = unsafe { GetSystemDirectoryW(buffer.as_mut_ptr(), buffer.len() as u32) };
    if len == 0 || len as usize >= buffer.len() {
        return Err("无法定位 Windows 系统目录".into());
    }
    buffer.truncate(len as usize);
    Ok(PathBuf::from(OsString::from_wide(&buffer)).join(name))
}

pub fn windows_dir() -> Result<PathBuf, String> {
    let mut buffer = vec![0u16; 32768];
    let len = unsafe { GetWindowsDirectoryW(buffer.as_mut_ptr(), buffer.len() as u32) };
    if len == 0 || len as usize >= buffer.len() {
        return Err("无法定位 Windows 安装目录".into());
    }
    buffer.truncate(len as usize);
    Ok(PathBuf::from(OsString::from_wide(&buffer)))
}

fn program_data() -> Result<PathBuf, String> {
    let windows = windows_dir()?;
    windows
        .parent()
        .map(|drive| drive.join("ProgramData"))
        .ok_or_else(|| "无法定位 ProgramData 目录".to_string())
}

pub fn program_files() -> Result<PathBuf, String> {
    let windows = windows_dir()?;
    windows
        .parent()
        .map(|drive| drive.join("Program Files"))
        .ok_or_else(|| "无法定位 Program Files 目录".to_string())
}

fn is_reparse(path: &Path) -> bool {
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        if let Ok(metadata) = fs::symlink_metadata(path) {
            return metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0;
        }
    }
    false
}

fn run_tool(exe: &Path, args: &[OsString]) -> Result<(), String> {
    let output = Command::new(exe)
        .args(args)
        .creation_flags(CREATE_NO_WINDOW)
        .output()
        .map_err(|error| format!("无法运行 {}: {error}", exe.display()))?;
    if output.status.success() {
        Ok(())
    } else {
        Err(format!(
            "{} 失败: {}",
            exe.display(),
            String::from_utf8_lossy(&output.stderr).trim()
        ))
    }
}

/// Replace the inherited ACL with SYSTEM + Administrators only.  This is the
/// boundary that keeps a medium-integrity process from swapping the embedded
/// helper or toast executable between extraction and CreateProcess.
fn harden(dir: &Path) -> Result<(), String> {
    let takeown = system32("takeown.exe")?;
    let icacls = system32("icacls.exe")?;
    let dir_arg = dir.as_os_str().to_os_string();
    let _ = run_tool(
        &takeown,
        &[
            OsString::from("/F"),
            dir_arg.clone(),
            OsString::from("/R"),
            OsString::from("/D"),
            OsString::from("Y"),
        ],
    );
    // The owner can always rewrite the DACL, so ownership must be moved away
    // from the interactive user as well as the grants.
    run_tool(
        &icacls,
        &[
            dir_arg.clone(),
            OsString::from("/setowner"),
            OsString::from("*S-1-5-18"),
            OsString::from("/T"),
            OsString::from("/C"),
            OsString::from("/Q"),
        ],
    )?;
    // /reset discards any explicit ACE a lower-integrity process may have
    // planted on a pre-created directory; /inheritance:r then removes the
    // inherited ACEs before the narrow grant below.
    let _ = run_tool(
        &icacls,
        &[
            dir_arg.clone(),
            OsString::from("/reset"),
            OsString::from("/C"),
            OsString::from("/Q"),
        ],
    );
    run_tool(
        &icacls,
        &[
            dir_arg,
            OsString::from("/inheritance:r"),
            OsString::from("/grant:r"),
            OsString::from("*S-1-5-18:(OI)(CI)F"),
            OsString::from("*S-1-5-32-544:(OI)(CI)F"),
            OsString::from("/T"),
            OsString::from("/C"),
            OsString::from("/Q"),
        ],
    )
}

fn init() -> Result<PathBuf, String> {
    let root = program_data()?.join("iMiniTuner");

    // A pre-created junction must not be allowed to redirect this privileged
    // directory into a user-writable location.
    if is_reparse(&root) {
        fs::remove_dir(&root).map_err(|error| format!("无法清理程序数据目录: {error}"))?;
        if is_reparse(&root) {
            return Err("程序数据目录仍是重解析点，已停止".into());
        }
    }
    fs::create_dir_all(&root).map_err(|error| format!("无法创建程序数据目录: {error}"))?;
    if is_reparse(&root) {
        return Err("程序数据目录是重解析点，已停止".into());
    }
    // A pre-created junction inside the root would be followed by takeown/icacls
    // during the recursive hardening pass. Remove those entries first.
    if let Ok(entries) = fs::read_dir(&root) {
        for entry in entries.flatten() {
            let path = entry.path();
            if is_reparse(&path) {
                let _ = if path.is_dir() {
                    fs::remove_dir(&path)
                } else {
                    fs::remove_file(&path)
                };
            }
        }
    }
    harden(&root)?;
    if is_reparse(&root) {
        return Err("程序数据目录在初始化时被替换，已停止".into());
    }
    // Remove stale per-process directories from previous runs.  These are
    // admin-only, but keeping them would grow ProgramData indefinitely.
    if let Ok(entries) = fs::read_dir(&root) {
        for entry in entries.flatten() {
            if entry.file_name().to_string_lossy().starts_with("run-") {
                let _ = fs::remove_dir_all(entry.path());
            }
        }
    }

    let stamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos();
    let run = root.join(format!("run-{}-{}", stamp, std::process::id()));
    fs::create_dir(&run).map_err(|error| format!("无法创建运行目录: {error}"))?;
    if is_reparse(&run) {
        return Err("运行目录是重解析点，已停止".into());
    }
    harden(&run)?;
    if is_reparse(&run) {
        return Err("运行目录在初始化时被替换，已停止".into());
    }
    Ok(run)
}

/// Per-process, admin-only directory for the embedded firmware bridge and toast
/// helper.  It is deliberately not under LOCALAPPDATA, which is user-writable.
pub fn dir() -> Result<PathBuf, String> {
    RUNTIME_DIR.get_or_init(init).clone()
}

pub fn write(name: &str, bytes: &[u8]) -> Result<PathBuf, String> {
    let dir = dir()?;
    let path = dir.join(name);
    if is_reparse(&path) {
        let _ = fs::remove_file(&path);
    }
    // The helper can still be running and holding its image open: rewriting the
    // same bytes would fail with sharing violation on Windows.
    if let Ok(existing) = fs::read(&path) {
        if existing == bytes {
            return Ok(path);
        }
    }
    fs::write(&path, bytes).map_err(|error| format!("无法写入辅助程序: {error}"))?;
    if is_reparse(&path) {
        return Err("辅助程序路径是重解析点，已停止".into());
    }
    let written = fs::read(&path).map_err(|error| format!("无法校验辅助程序: {error}"))?;
    if written != bytes {
        return Err("辅助程序校验失败".into());
    }
    Ok(path)
}
