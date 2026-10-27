//! Real keyboard integration test for the shortcut-capture helper.
//!
//! Installs an independent low-level keyboard hook ("monitor") in this process
//! *before* spawning the helper, so the helper's newer hook runs first.  Then
//! it injects real system-queue keystrokes with `SendInput` and verifies:
//!
//!   1. idle keys pass through to the monitor hook,
//!   2. during a Win+Alt+Space capture the helper reports the combo AND the
//!      monitor sees zero events (the chord was swallowed, so the shell cannot
//!      open Start or the window system menu),
//!   3. swallowing ends after the keys are released,
//!   4. Esc cancels,
//!   5. `RegisterHotKey(Win+Alt+Space)` is available,
//!   6. the helper exits when its stdin pipe closes (never an orphan hook).
//!
//! Usage: `hotkey-selftest.exe [result.json]`.  Exit code 0 = all checks pass.
//!
//! Deliberately a console subsystem binary: the test runner waits for it and
//! reads its stdout directly.

use std::env;
use std::fs::{self, OpenOptions};
use std::io::{self, BufRead, BufReader, Write};
use std::os::windows::process::CommandExt;
use std::path::PathBuf;
use std::process::{Child, ChildStdin, Command, Stdio};
use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::mpsc::{self, Receiver, RecvTimeoutError, Sender};
use std::thread;
use std::time::{Duration, Instant};

use windows_sys::Win32::Foundation::HWND;
use windows_sys::Win32::Foundation::LPARAM;
use windows_sys::Win32::Foundation::LRESULT;
use windows_sys::Win32::Foundation::WPARAM;
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::Input::KeyboardAndMouse::*;
use windows_sys::Win32::UI::WindowsAndMessaging::*;

static MON_COUNT: [AtomicU32; 256] = [const { AtomicU32::new(0) }; 256];

fn wide(value: &str) -> Vec<u16> {
    value.encode_utf16().collect()
}

/// A foreground window owned by this process.  Low-level hooks only receive
/// injected input when the target window is at the same (or lower) integrity
/// level; this test binary runs at whatever level its parent has, so it makes
/// its own window the target instead of depending on the ambient foreground.
fn window_thread(ready: Sender<isize>) {
    unsafe {
        let class = wide("STATIC\0");
        let title = wide("iMini hotkey selftest\0");
        let hwnd = CreateWindowExW(
            0,
            class.as_ptr(),
            title.as_ptr(),
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            120,
            120,
            460,
            140,
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            std::ptr::null(),
        );
        if hwnd.is_null() {
            let _ = ready.send(0);
            return;
        }
        ShowWindow(hwnd, SW_SHOW);
        let _ = ready.send(hwnd as isize);

        let mut msg: MSG = std::mem::zeroed();
        while GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) > 0 {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
}

fn force_foreground(hwnd: HWND) -> bool {
    unsafe {
        ShowWindow(hwnd, SW_SHOW);
        SetWindowPos(
            hwnd,
            HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW,
        );
        SwitchToThisWindow(hwnd, 1);
        if GetForegroundWindow() != hwnd {
            // Clicking our own topmost window activates it even when the
            // current foreground app owns the foreground lock.
            let mut previous = std::mem::zeroed();
            GetCursorPos(&mut previous);
            SetCursorPos(320, 180);
            thread::sleep(Duration::from_millis(80));
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            thread::sleep(Duration::from_millis(150));
            SetCursorPos(previous.x, previous.y);
        }
        SetForegroundWindow(hwnd);
        GetForegroundWindow() == hwnd
    }
}

unsafe extern "system" fn monitor_proc(code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    if code == HC_ACTION as i32 {
        let kb = &*(lparam as *const KBDLLHOOKSTRUCT);
        let vk = (kb.vkCode as usize) % 256;
        let message = wparam as u32;
        if message == WM_KEYDOWN
            || message == WM_SYSKEYDOWN
            || message == WM_KEYUP
            || message == WM_SYSKEYUP
        {
            MON_COUNT[vk].fetch_add(1, Ordering::SeqCst);
        }
    }
    CallNextHookEx(std::ptr::null_mut(), code, wparam, lparam)
}

fn monitor_thread(ready: Sender<Result<(), String>>) {
    unsafe {
        let mut msg: MSG = std::mem::zeroed();
        PeekMessageW(&mut msg, std::ptr::null_mut(), 0, 0, PM_NOREMOVE);
        let hook = SetWindowsHookExW(
            WH_KEYBOARD_LL,
            Some(monitor_proc),
            GetModuleHandleW(std::ptr::null()),
            0,
        );
        if hook.is_null() {
            let _ = ready.send(Err(format!(
                "monitor SetWindowsHookExW failed: {}",
                io::Error::last_os_error()
            )));
            return;
        }
        let _ = ready.send(Ok(()));
        loop {
            let result = GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0);
            if result > 0 {
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            } else {
                break;
            }
        }
    }
}

fn mon(vk: u16) -> u32 {
    MON_COUNT[(vk as usize) % 256].load(Ordering::SeqCst)
}

fn send_key(vk: u16, up: bool) -> Result<(), String> {
    let input = INPUT {
        r#type: INPUT_KEYBOARD,
        Anonymous: INPUT_0 {
            ki: KEYBDINPUT {
                wVk: vk,
                wScan: 0,
                dwFlags: if up { KEYEVENTF_KEYUP } else { 0 },
                time: 0,
                dwExtraInfo: 0,
            },
        },
    };
    let sent = unsafe { SendInput(1, &input, std::mem::size_of::<INPUT>() as i32) };
    if sent != 1 {
        return Err(format!("SendInput failed: {}", io::Error::last_os_error()));
    }
    Ok(())
}

fn inject_combo(keys: &[u16]) -> Result<(), String> {
    for &vk in keys {
        send_key(vk, false)?;
        thread::sleep(Duration::from_millis(25));
    }
    thread::sleep(Duration::from_millis(80));
    for &vk in keys.iter().rev() {
        send_key(vk, true)?;
        thread::sleep(Duration::from_millis(25));
    }
    Ok(())
}

fn inject_tap(vk: u16) -> Result<(), String> {
    send_key(vk, false)?;
    thread::sleep(Duration::from_millis(30));
    send_key(vk, true)?;
    Ok(())
}

#[derive(Debug, Clone)]
struct HelperEvent {
    event: String,
    keys: Option<String>,
}

fn field(line: &str, name: &str) -> Option<String> {
    let pattern = format!("\"{name}\":\"");
    let start = line.find(&pattern)? + pattern.len();
    let rest = &line[start..];
    let end = rest.find('"')?;
    Some(rest[..end].to_string())
}

fn parse_event(line: &str) -> Option<HelperEvent> {
    let event = field(line, "event")?;
    Some(HelperEvent {
        event,
        keys: field(line, "keys"),
    })
}

struct Helper {
    child: Child,
    stdin: Option<ChildStdin>,
    events: Receiver<HelperEvent>,
}

impl Helper {
    fn spawn() -> Result<Helper, String> {
        let exe = env::current_exe()
            .map_err(|e| e.to_string())?
            .parent()
            .ok_or("no parent dir")?
            .join("imini-hotkey-helper.exe");
        if !exe.is_file() {
            return Err(format!("helper not found: {}", exe.display()));
        }
        let mut child = Command::new(&exe)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .creation_flags(0x0800_0000)
            .spawn()
            .map_err(|e| format!("spawn helper failed: {e}"))?;
        let stdin = child.stdin.take();
        let stdout = child.stdout.take().ok_or("helper stdout missing")?;
        let (tx, rx) = mpsc::channel();
        thread::spawn(move || {
            for line in BufReader::new(stdout).lines().filter_map(|line| line.ok()) {
                if let Some(event) = parse_event(&line) {
                    if tx.send(event).is_err() {
                        break;
                    }
                }
            }
        });
        Ok(Helper {
            child,
            stdin,
            events: rx,
        })
    }

    fn send(&mut self, command: &str) -> Result<(), String> {
        let stdin = self.stdin.as_mut().ok_or("helper stdin closed")?;
        writeln!(stdin, "{command}").map_err(|e| e.to_string())?;
        stdin.flush().map_err(|e| e.to_string())
    }

    fn wait_for(&self, wanted: &str, timeout: Duration) -> Option<HelperEvent> {
        let deadline = Instant::now() + timeout;
        loop {
            let left = deadline.checked_duration_since(Instant::now())?;
            match self.events.recv_timeout(left) {
                Ok(event) => {
                    if event.event == wanted {
                        return Some(event);
                    }
                    if event.event == "error" {
                        return None;
                    }
                }
                Err(RecvTimeoutError::Timeout) | Err(RecvTimeoutError::Disconnected) => {
                    return None
                }
            }
        }
    }

    fn drain(&self) {
        while self.events.try_recv().is_ok() {}
    }
}

fn trace(log: &Option<PathBuf>, message: &str) {
    let Some(path) = log else { return };
    if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(path) {
        let _ = writeln!(file, "{message}");
        let _ = file.flush();
    }
}

struct Check {
    name: &'static str,
    ok: bool,
    detail: String,
}

fn pass(name: &'static str, detail: impl Into<String>) -> Check {
    Check {
        name,
        ok: true,
        detail: detail.into(),
    }
}

fn fail(name: &'static str, detail: impl Into<String>) -> Check {
    Check {
        name,
        ok: false,
        detail: detail.into(),
    }
}

fn run(log: &Option<PathBuf>) -> Vec<Check> {
    let mut checks = Vec::new();
    trace(log, "start");

    let (win_tx, win_rx) = mpsc::channel();
    thread::spawn(move || window_thread(win_tx));
    let hwnd = match win_rx.recv_timeout(Duration::from_secs(3)) {
        Ok(0) | Err(_) => {
            checks.push(fail(
                "foreground_window",
                "could not create the selftest window",
            ));
            return checks;
        }
        Ok(raw) => raw as HWND,
    };
    if !force_foreground(hwnd) {
        checks.push(fail(
            "foreground_window",
            "could not bring the selftest window to the foreground",
        ));
        return checks;
    }
    checks.push(pass(
        "foreground_window",
        "own low-integrity window is foreground",
    ));
    trace(log, "foreground ready");

    let (ready_tx, ready_rx) = mpsc::channel();
    thread::spawn(move || monitor_thread(ready_tx));
    match ready_rx.recv_timeout(Duration::from_secs(3)) {
        Ok(Ok(())) => checks.push(pass("monitor_hook", "independent LL hook installed")),
        Ok(Err(error)) => {
            checks.push(fail("monitor_hook", error));
            return checks;
        }
        Err(_) => {
            checks.push(fail("monitor_hook", "monitor hook thread did not report ready"));
            return checks;
        }
    }
    trace(log, "monitor ready");

    let mut helper = match Helper::spawn() {
        Ok(helper) => helper,
        Err(error) => {
            checks.push(fail("helper_spawn", error));
            return checks;
        }
    };
    trace(log, "helper spawned");

    match helper.wait_for("ready", Duration::from_secs(5)) {
        Some(_) => checks.push(pass("helper_ready", "own message loop + WH_KEYBOARD_LL ready")),
        None => {
            checks.push(fail("helper_ready", "no ready event"));
            let _ = helper.child.kill();
            return checks;
        }
    }
    trace(log, "helper ready");

    // 1. Idle pass-through: the helper must not disturb normal typing.
    let before = mon(VK_F24);
    helper.drain();
    let idle = inject_tap(VK_F24).and_then(|_| {
        thread::sleep(Duration::from_millis(250));
        Ok(())
    });
    let after = mon(VK_F24);
    match idle {
        Ok(()) if after.saturating_sub(before) == 2 => checks.push(pass(
            "idle_passthrough",
            format!("F24 reached monitor hook {} times", after - before),
        )),
        Ok(()) => checks.push(fail(
            "idle_passthrough",
            format!("monitor saw {} of 2 F24 events", after - before),
        )),
        Err(error) => checks.push(fail("idle_passthrough", error)),
    }
    let mut stray_done = false;
    while let Ok(event) = helper.events.try_recv() {
        if event.event == "done" {
            stray_done = true;
        }
    }
    if stray_done {
        checks.push(fail("idle_no_capture", "helper recorded a combo while idle"));
    }
    trace(log, "idle test done");

    // 2. Win+Alt+Space must be recorded and fully swallowed.
    if let Err(error) = helper.send("start") {
        checks.push(fail("capture_start", error));
    } else if helper.wait_for("started", Duration::from_secs(2)).is_none() {
        checks.push(fail("capture_start", "no started event"));
    } else {
        checks.push(pass("capture_start", "started event received"));
        trace(log, "capture started");
        let win_before = mon(VK_LWIN);
        let alt_before = mon(VK_LMENU);
        let space_before = mon(VK_SPACE);
        helper.drain();

        let injected = inject_combo(&[VK_LWIN, VK_LMENU, VK_SPACE]);
        match injected {
            Ok(()) => match helper.wait_for("done", Duration::from_secs(3)) {
                Some(event) => {
                    let keys = event.keys.unwrap_or_default();
                    if keys == "Win+Alt+Space" {
                        checks.push(pass("record_combo", keys));
                    } else {
                        checks.push(fail("record_combo", format!("got '{keys}'")));
                    }
                }
                None => checks.push(fail("record_combo", "no done event")),
            },
            Err(error) => checks.push(fail("record_combo", error)),
        }

        thread::sleep(Duration::from_millis(300));
        let swallowed = mon(VK_LWIN) == win_before
            && mon(VK_LMENU) == alt_before
            && mon(VK_SPACE) == space_before;
        if swallowed {
            checks.push(pass(
                "swallow_chord",
                "monitor hook saw 0 of 6 Win/Alt/Space events",
            ));
        } else {
            checks.push(fail(
                "swallow_chord",
                format!(
                    "leaked Win {} Alt {} Space {}",
                    mon(VK_LWIN) - win_before,
                    mon(VK_LMENU) - alt_before,
                    mon(VK_SPACE) - space_before
                ),
            ));
        }
        trace(log, "capture test done");
    }

    // 3. Swallowing must end once everything is released.
    let before = mon(VK_F24);
    let released = inject_tap(VK_F24);
    thread::sleep(Duration::from_millis(300));
    match released {
        Ok(()) if mon(VK_F24).saturating_sub(before) == 2 => checks.push(pass(
            "swallow_released",
            "typing works again after capture",
        )),
        Ok(()) => checks.push(fail(
            "swallow_released",
            format!("monitor saw {} of 2 F24 events", mon(VK_F24) - before),
        )),
        Err(error) => checks.push(fail("swallow_released", error)),
    }

    // 4. Esc cancels.
    if helper.send("start").is_ok() && helper.wait_for("started", Duration::from_secs(2)).is_some()
    {
        helper.drain();
        match inject_tap(VK_ESCAPE) {
            Ok(()) => match helper.wait_for("cancel", Duration::from_secs(2)) {
                Some(_) => checks.push(pass("escape_cancel", "cancel event received")),
                None => checks.push(fail("escape_cancel", "no cancel event")),
            },
            Err(error) => checks.push(fail("escape_cancel", error)),
        }
    } else {
        checks.push(fail("escape_cancel", "could not start capture"));
    }
    thread::sleep(Duration::from_millis(250));

    // 5. The activation path used by the app must be able to claim the combo.
    let registered = unsafe {
        RegisterHotKey(
            std::ptr::null_mut(),
            0x6D31,
            MOD_WIN | MOD_ALT,
            VK_SPACE as u32,
        )
    };
    if registered != 0 {
        unsafe {
            UnregisterHotKey(std::ptr::null_mut(), 0x6D31);
        }
        checks.push(pass(
            "register_activation",
            "RegisterHotKey Win+Alt+Space succeeded",
        ));
    } else {
        checks.push(fail(
            "register_activation",
            format!(
                "RegisterHotKey Win+Alt+Space failed: {}",
                io::Error::last_os_error()
            ),
        ));
    }

    // 6. Closing the parent pipe must terminate the helper (no orphan hook).
    trace(log, "closing helper stdin");
    helper.stdin.take();
    let deadline = Instant::now() + Duration::from_secs(4);
    let mut exited = false;
    while Instant::now() < deadline {
        match helper.child.try_wait() {
            Ok(Some(_)) => {
                exited = true;
                break;
            }
            Ok(None) => thread::sleep(Duration::from_millis(50)),
            Err(_) => break,
        }
    }
    if exited {
        checks.push(pass("stdin_eof_exit", "helper exited on stdin close"));
    } else {
        checks.push(fail("stdin_eof_exit", "helper stayed alive"));
        let _ = helper.child.kill();
    }

    checks
}

fn main() {
    let result_path = env::args().nth(1);
    let log_path = result_path.as_ref().map(|path| PathBuf::from(format!("{path}.log")));
    trace(&log_path, "==== selftest start ====");
    let checks = run(&log_path);
    trace(&log_path, "run returned");
    let ok = checks.iter().all(|check| check.ok);
    let mut json = String::from("{\"ok\":");
    json.push_str(if ok { "true" } else { "false" });
    json.push_str(",\"checks\":[");
    for (index, check) in checks.iter().enumerate() {
        if index > 0 {
            json.push(',');
        }
        json.push_str(&format!(
            "{{\"name\":\"{}\",\"ok\":{},\"detail\":\"{}\"}}",
            check.name,
            check.ok,
            check
                .detail
                .replace('\\', "\\\\")
                .replace('"', "\\\"")
                .replace('\n', " ")
        ));
    }
    json.push_str("]}");
    if let Some(path) = &result_path {
        let _ = fs::write(path, &json);
    }
    println!("{json}");
    std::process::exit(if ok { 0 } else { 1 });
}

