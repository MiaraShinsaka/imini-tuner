//! Standalone shortcut-capture helper.
//!
//! Windows only delivers `WH_KEYBOARD_LL` callbacks to the thread that
//! installed the hook, and that thread must run a real Win32 message loop.
//! Tauri / tao own the main thread, so the hook used to be installed on the
//! UI thread and never produced a single callback.  This helper is a tiny,
//! separate process whose only job is to own that thread and message loop.
//!
//! Protocol (one JSON object per line):
//!   stdin : `start`, `stop`, `quit`
//!   stdout: ready | started | preview | done | cancel | stopped | error
//!
//! The hook procedure itself only touches atomics and pushes one event into a
//! channel; stdout is written by a different thread so a slow pipe can never
//! delay the hook (Windows unhooks a helper that exceeds LowLevelHooksTimeout).

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::io::{self, BufRead, Write};
use std::sync::atomic::{AtomicBool, AtomicIsize, AtomicU64, Ordering};
use std::sync::mpsc::{self, Sender};
use std::sync::OnceLock;
use std::thread;
use std::time::Duration;

use windows_sys::Win32::Foundation::{LPARAM, LRESULT, WPARAM};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::Input::KeyboardAndMouse::*;
use windows_sys::Win32::UI::WindowsAndMessaging::*;

static CAPTURING: AtomicBool = AtomicBool::new(false);
static SWALLOW: AtomicBool = AtomicBool::new(false);
static HOOK: AtomicIsize = AtomicIsize::new(0);
static EVENTS: OnceLock<Sender<Event>> = OnceLock::new();

/// 256 virtual keys, four 64-bit words.  Bit set = currently held.
static DOWN: [AtomicU64; 4] = [
    AtomicU64::new(0),
    AtomicU64::new(0),
    AtomicU64::new(0),
    AtomicU64::new(0),
];

enum Event {
    Ready,
    Started,
    Preview(String),
    Done(String),
    Cancel,
    Stopped,
    Error(String),
    Exit,
}

impl Event {
    fn json(&self) -> String {
        match self {
            Event::Ready => "{\"event\":\"ready\"}".to_string(),
            Event::Started => "{\"event\":\"started\"}".to_string(),
            Event::Preview(keys) => format!("{{\"event\":\"preview\",\"keys\":\"{}\"}}", escape(keys)),
            Event::Done(keys) => format!("{{\"event\":\"done\",\"keys\":\"{}\"}}", escape(keys)),
            Event::Cancel => "{\"event\":\"cancel\"}".to_string(),
            Event::Stopped => "{\"event\":\"stopped\"}".to_string(),
            Event::Error(message) => {
                format!("{{\"event\":\"error\",\"message\":\"{}\"}}", escape(message))
            }
            Event::Exit => "{\"event\":\"exit\"}".to_string(),
        }
    }
}

fn escape(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    for ch in value.chars() {
        match ch {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            ch if (ch as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", ch as u32)),
            ch => out.push(ch),
        }
    }
    out
}

fn send(event: Event) {
    if let Some(tx) = EVENTS.get() {
        let _ = tx.send(event);
    }
}

fn set_down(vk: u16, down: bool) {
    let vk = vk as u32;
    if vk > 255 {
        return;
    }
    let index = (vk / 64) as usize;
    let bit = 1u64 << (vk % 64);
    if down {
        DOWN[index].fetch_or(bit, Ordering::SeqCst);
    } else {
        DOWN[index].fetch_and(!bit, Ordering::SeqCst);
    }
}

fn is_down(vk: u16) -> bool {
    let vk = vk as u32;
    if vk > 255 {
        return false;
    }
    DOWN[(vk / 64) as usize].load(Ordering::SeqCst) & (1u64 << (vk % 64)) != 0
}

fn any_down() -> bool {
    DOWN.iter().any(|word| word.load(Ordering::SeqCst) != 0)
}

fn ctrl_down() -> bool {
    is_down(VK_LCONTROL) || is_down(VK_RCONTROL) || is_down(VK_CONTROL)
}

fn alt_down() -> bool {
    is_down(VK_LMENU) || is_down(VK_RMENU) || is_down(VK_MENU)
}

fn shift_down() -> bool {
    is_down(VK_LSHIFT) || is_down(VK_RSHIFT) || is_down(VK_SHIFT)
}

fn win_down() -> bool {
    is_down(VK_LWIN) || is_down(VK_RWIN)
}

fn any_mods() -> bool {
    ctrl_down() || alt_down() || shift_down() || win_down()
}

fn is_modifier(vk: u16) -> bool {
    matches!(
        vk,
        VK_LCONTROL
            | VK_RCONTROL
            | VK_CONTROL
            | VK_LMENU
            | VK_RMENU
            | VK_MENU
            | VK_LSHIFT
            | VK_RSHIFT
            | VK_SHIFT
            | VK_LWIN
            | VK_RWIN
    )
}

fn mods_string() -> String {
    let mut parts = Vec::new();
    if win_down() {
        parts.push("Win");
    }
    if ctrl_down() {
        parts.push("Ctrl");
    }
    if alt_down() {
        parts.push("Alt");
    }
    if shift_down() {
        parts.push("Shift");
    }
    parts.join("+")
}

fn key_name(vk: u16) -> Option<String> {
    let named = match vk {
        VK_SPACE => "Space",
        VK_RETURN => "Enter",
        VK_TAB => "Tab",
        VK_BACK => "Backspace",
        VK_DELETE => "Delete",
        VK_INSERT => "Insert",
        VK_HOME => "Home",
        VK_END => "End",
        VK_PRIOR => "PageUp",
        VK_NEXT => "PageDown",
        VK_UP => "Up",
        VK_DOWN => "Down",
        VK_LEFT => "Left",
        VK_RIGHT => "Right",
        _ => "",
    };
    if !named.is_empty() {
        return Some(named.to_string());
    }
    match vk {
        0x30..=0x39 => Some(((b'0' + (vk as u8 - 0x30)) as char).to_string()),
        0x41..=0x5A => Some(((b'A' + (vk as u8 - 0x41)) as char).to_string()),
        VK_F1..=VK_F24 => Some(format!("F{}", vk - VK_F1 + 1)),
        _ => None,
    }
}

fn combo_for(vk: u16) -> Option<String> {
    let key = key_name(vk)?;
    let mods = mods_string();
    if mods.is_empty() {
        Some(key)
    } else {
        Some(format!("{mods}+{key}"))
    }
}

/// Capture can start while the user is already holding a modifier (for
/// example they clicked the field with the other hand).  Seed our key state
/// from the real async state so the first event builds the right combo.
fn seed_modifiers() {
    for vk in [
        VK_LCONTROL,
        VK_RCONTROL,
        VK_LMENU,
        VK_RMENU,
        VK_LSHIFT,
        VK_RSHIFT,
        VK_LWIN,
        VK_RWIN,
    ] {
        unsafe {
            if GetAsyncKeyState(vk as i32) as u16 & 0x8000 != 0 {
                set_down(vk, true);
            }
        }
    }
}

fn start_capture() {
    seed_modifiers();
    CAPTURING.store(true, Ordering::SeqCst);
    send(Event::Started);
}

fn stop_capture() {
    let was_capturing = CAPTURING.swap(false, Ordering::SeqCst);
    // Keep swallowing the remains of a chord that was already half-swallowed;
    // otherwise releasing Win after clicking away could still open Start.
    if was_capturing && any_down() {
        SWALLOW.store(true, Ordering::SeqCst);
    }
    send(Event::Stopped);
}

fn finish_done(combo: String) {
    CAPTURING.store(false, Ordering::SeqCst);
    SWALLOW.store(true, Ordering::SeqCst);
    send(Event::Done(combo));
}

fn finish_cancel() {
    CAPTURING.store(false, Ordering::SeqCst);
    SWALLOW.store(true, Ordering::SeqCst);
    send(Event::Cancel);
}

unsafe extern "system" fn keyboard_proc(code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    if code != HC_ACTION as i32 {
        return CallNextHookEx(std::ptr::null_mut(), code, wparam, lparam);
    }

    let kb = &*(lparam as *const KBDLLHOOKSTRUCT);
    let vk = kb.vkCode as u16;
    let message = wparam as u32;
    let down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
    let up = message == WM_KEYUP || message == WM_SYSKEYUP;

    if !down && !up {
        return CallNextHookEx(std::ptr::null_mut(), code, wparam, lparam);
    }

    set_down(vk, down);

    if CAPTURING.load(Ordering::SeqCst) {
        if down {
            if is_modifier(vk) {
                let preview = mods_string();
                if !preview.is_empty() {
                    send(Event::Preview(preview));
                }
            } else if vk == VK_ESCAPE && !any_mods() {
                finish_cancel();
            } else {
                match combo_for(vk) {
                    Some(combo) => finish_done(combo),
                    None => finish_cancel(),
                }
            }
        }
        // Swallow both edges: the shell never sees the Win key, so neither the
        // Start menu nor the window system menu can open.
        return 1;
    }

    if SWALLOW.load(Ordering::SeqCst) {
        if up && !any_down() {
            SWALLOW.store(false, Ordering::SeqCst);
        }
        return 1;
    }

    CallNextHookEx(std::ptr::null_mut(), code, wparam, lparam)
}

fn hook_thread() {
    unsafe {
        // Force creation of this thread's message queue before installing the
        // hook; low-level hooks are delivered through that queue.
        let mut msg: MSG = std::mem::zeroed();
        PeekMessageW(&mut msg, std::ptr::null_mut(), 0, 0, PM_NOREMOVE);

        let module = GetModuleHandleW(std::ptr::null());
        let hook = SetWindowsHookExW(WH_KEYBOARD_LL, Some(keyboard_proc), module, 0);
        if hook.is_null() {
            send(Event::Error(format!(
                "SetWindowsHookExW failed: {}",
                io::Error::last_os_error()
            )));
            thread::sleep(Duration::from_millis(200));
            std::process::exit(2);
        }
        HOOK.store(hook as isize, Ordering::SeqCst);
        send(Event::Ready);

        loop {
            let result = GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0);
            if result > 0 {
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
                continue;
            }
            if result == -1 {
                send(Event::Error(format!(
                    "GetMessageW failed: {}",
                    io::Error::last_os_error()
                )));
            }
            break;
        }

        if !hook.is_null() {
            UnhookWindowsHookEx(hook);
        }
    }
    std::process::exit(0);
}

fn main() {
    let (tx, rx) = mpsc::channel::<Event>();
    let _ = EVENTS.set(tx);

    let writer = thread::spawn(move || {
        let stdout = io::stdout();
        let mut out = stdout.lock();
        for event in rx {
            let exit = matches!(event, Event::Exit);
            if writeln!(out, "{}", event.json()).is_err() {
                break;
            }
            if out.flush().is_err() {
                break;
            }
            if exit {
                break;
            }
        }
    });

    thread::spawn(hook_thread);

    let stdin = io::stdin();
    let mut lines = stdin.lock().lines();
    while let Some(Ok(line)) = lines.next() {
        match line.trim() {
            "start" => start_capture(),
            "stop" => stop_capture(),
            "quit" => break,
            "" => {}
            other => send(Event::Error(format!("unknown command: {other}"))),
        }
    }

    CAPTURING.store(false, Ordering::SeqCst);
    SWALLOW.store(false, Ordering::SeqCst);

    // Dropping stdin (parent exit / crash) also lands here, so the helper can
    // never linger as a hidden global keyboard hook.
    send(Event::Exit);
    let _ = writer.join();
    std::process::exit(0);
}
