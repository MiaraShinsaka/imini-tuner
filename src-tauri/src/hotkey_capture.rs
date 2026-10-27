//! Shortcut recording is delegated to a small, embedded native helper.
//!
//! Windows delivers `WH_KEYBOARD_LL` callbacks only to the thread that
//! installed the hook, and that thread has to run a real Win32 message loop.
//! Tauri owns the main thread with tao's event loop, so installing the hook
//! here produced no callbacks at all (the shell kept seeing Win+Alt+Space).
//!
//! `imini-hotkey.exe` is a separate process whose only job is to own that
//! message loop.  This module keeps it resident, forwards its JSON events to
//! the WebView and turns capture on/off.  The helper exits as soon as its
//! stdin pipe closes, so it can never linger as an unnoticed keyboard hook.

use std::io::{BufRead, BufReader, Write};
use std::os::windows::process::CommandExt;
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};
use std::sync::mpsc::{self, Receiver, RecvTimeoutError, Sender};
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

use tauri::{AppHandle, Emitter};

const HELPER: &[u8] = include_bytes!("../resources/imini-hotkey.exe");
const HELPER_FILE: &str = "imini-hotkey-220.exe";
const CREATE_NO_WINDOW: u32 = 0x0800_0000;
const READY_TIMEOUT: Duration = Duration::from_secs(3);
const ACK_TIMEOUT: Duration = Duration::from_secs(2);

#[derive(Clone, Copy, PartialEq)]
enum AckKind {
    Ready,
    Started,
}

enum Ack {
    Ready,
    Started,
    Exited,
    Failed(String),
}

struct Helper {
    child: Child,
    stdin: ChildStdin,
    acks: Receiver<Ack>,
}

static STATE: OnceLock<Mutex<Option<Helper>>> = OnceLock::new();

fn state() -> &'static Mutex<Option<Helper>> {
    STATE.get_or_init(|| Mutex::new(None))
}

impl Helper {
    fn send(&mut self, command: &str) -> Result<(), String> {
        writeln!(self.stdin, "{command}")
            .and_then(|_| self.stdin.flush())
            .map_err(|error| format!("快捷键录制助手连接已断开: {error}"))
    }

    fn wait_for(&mut self, want: AckKind, timeout: Duration) -> Result<(), String> {
        let deadline = Instant::now() + timeout;
        loop {
            let left = deadline.saturating_duration_since(Instant::now());
            if left.is_zero() {
                return Err("快捷键录制助手未响应".into());
            }
            match self.acks.recv_timeout(left) {
                Ok(Ack::Ready) if want == AckKind::Ready => return Ok(()),
                Ok(Ack::Started) if want == AckKind::Started => return Ok(()),
                Ok(Ack::Failed(message)) => return Err(message),
                Ok(Ack::Exited) => return Err("快捷键录制助手意外退出".into()),
                Ok(_) => {}
                Err(RecvTimeoutError::Timeout) => return Err("快捷键录制助手未响应".into()),
                Err(RecvTimeoutError::Disconnected) => {
                    return Err("快捷键录制助手意外退出".into())
                }
            }
        }
    }

    /// Ask the helper to quit and make sure the process is gone.
    fn quit(mut self) {
        let _ = self.send("quit");
        drop(self.stdin);
        let deadline = Instant::now() + Duration::from_millis(600);
        while Instant::now() < deadline {
            if matches!(self.child.try_wait(), Ok(Some(_))) {
                return;
            }
            std::thread::sleep(Duration::from_millis(20));
        }
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

fn spawn(app: &AppHandle) -> Result<Helper, String> {
    let path = crate::runtime::write(HELPER_FILE, HELPER)?;
    let dir = crate::runtime::dir()?;

    let mut command = Command::new(&path);
    command
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .current_dir(&dir)
        // No console window: this process runs for the whole session.
        .creation_flags(CREATE_NO_WINDOW)
        .env_clear();
    for key in ["SystemRoot", "WINDIR", "PATH", "TEMP", "TMP"] {
        if let Some(value) = std::env::var_os(key) {
            command.env(key, value);
        }
    }

    let mut child = command
        .spawn()
        .map_err(|error| format!("无法启动快捷键录制助手: {error}"))?;
    let stdin = child
        .stdin
        .take()
        .ok_or_else(|| "无法连接快捷键录制助手输入".to_string())?;
    let stdout = child
        .stdout
        .take()
        .ok_or_else(|| "无法连接快捷键录制助手输出".to_string())?;

    let (tx, acks) = mpsc::channel();
    let handle = app.clone();
    std::thread::spawn(move || read_events(stdout, handle, tx));

    Ok(Helper {
        child,
        stdin,
        acks,
    })
}

fn read_events(stdout: ChildStdout, app: AppHandle, acks: Sender<Ack>) {
    let reader = BufReader::new(stdout);
    for line in reader.lines() {
        let Ok(line) = line else { break };
        let line = line.trim();
        if line.is_empty() {
            continue;
        }
        let Ok(value) = serde_json::from_str::<serde_json::Value>(line) else {
            if std::env::var_os("IMINI_HOTKEY_DEBUG").is_some() {
                crate::log(&format!("hotkey helper unparsed: {line}"));
            }
            continue;
        };
        if std::env::var_os("IMINI_HOTKEY_DEBUG").is_some() {
            crate::log(&format!("hotkey helper event: {line}"));
        }
        let name = value.get("event").and_then(|item| item.as_str()).unwrap_or("");
        match name {
            "ready" => {
                let _ = acks.send(Ack::Ready);
            }
            "started" => {
                let _ = acks.send(Ack::Started);
            }
            "preview" => {
                if let Some(keys) = value.get("keys").and_then(|item| item.as_str()) {
                    let _ = app.emit("hotkey-capture-preview", keys.to_string());
                }
            }
            "done" => {
                if let Some(keys) = value.get("keys").and_then(|item| item.as_str()) {
                    crate::log(&format!("hotkey capture done: {keys}"));
                    let _ = app.emit("hotkey-capture-done", keys.to_string());
                }
            }
            "cancel" => {
                let _ = app.emit("hotkey-capture-cancel", true);
            }
            "error" => {
                let message = value
                    .get("message")
                    .and_then(|item| item.as_str())
                    .unwrap_or("快捷键录制失败");
                crate::log(&format!("hotkey helper error: {message}"));
                let _ = app.emit("hotkey-capture-error", message.to_string());
                let _ = acks.send(Ack::Failed(message.to_string()));
            }
            "exit" => {
                let _ = acks.send(Ack::Exited);
                break;
            }
            // "stopped" and unknown events need no forwarding.
            _ => {}
        }
    }
    let _ = acks.send(Ack::Exited);
}

fn discard(slot: &mut Option<Helper>) {
    if let Some(mut helper) = slot.take() {
        let _ = helper.child.kill();
        let _ = helper.child.wait();
    }
}

/// Start the helper before the first capture so the first key press is not
/// missed.  Safe to call repeatedly.
pub fn prepare(app: &AppHandle) -> Result<(), String> {
    let mut slot = state()
        .lock()
        .map_err(|_| "快捷键录制状态已损坏".to_string())?;
    let alive = match slot.as_mut() {
        Some(helper) => matches!(helper.child.try_wait(), Ok(None)),
        None => false,
    };
    if alive {
        return Ok(());
    }
    discard(&mut slot);

    let mut helper = spawn(app)?;
    if let Err(error) = helper.wait_for(AckKind::Ready, READY_TIMEOUT) {
        let _ = helper.child.kill();
        let _ = helper.child.wait();
        crate::log(&format!("hotkey helper prepare failed: {error}"));
        return Err(error);
    }
    crate::log("hotkey helper ready");
    *slot = Some(helper);
    Ok(())
}

pub fn start(app: AppHandle) -> Result<(), String> {
    prepare(&app)?;
    let mut slot = state()
        .lock()
        .map_err(|_| "快捷键录制状态已损坏".to_string())?;
    let Some(helper) = slot.as_mut() else {
        return Err("快捷键录制助手未启动".into());
    };
    helper.send("start")?;
    if let Err(error) = helper.wait_for(AckKind::Started, ACK_TIMEOUT) {
        discard(&mut slot);
        crate::log(&format!("hotkey capture start failed: {error}"));
        return Err(error);
    }
    crate::log("hotkey capture started");
    Ok(())
}

pub fn stop() {
    if let Ok(mut slot) = state().lock() {
        if let Some(helper) = slot.as_mut() {
            crate::log("hotkey capture stop requested");
            let _ = helper.send("stop");
        }
    }
}

pub fn shutdown() {
    let helper = state().lock().ok().and_then(|mut slot| slot.take());
    if let Some(helper) = helper {
        helper.quit();
    }
}
