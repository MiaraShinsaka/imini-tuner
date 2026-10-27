#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod autostart;
mod helper;
mod hotkey_capture;
mod runtime;
mod settings;
mod toast;
mod wake;

use std::fs::OpenOptions;
use std::io::Write;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::Duration;

use serde::Serialize;
use tauri::menu::{CheckMenuItem, Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Emitter, Manager, RunEvent, State, WindowEvent};
use tauri_plugin_global_shortcut::{GlobalShortcutExt, Shortcut, ShortcutState};

use settings::Settings;

const TEMP_LIMIT: i32 = 88;
const MIN_FAN_PERCENT: u8 = 35;
const ABSOLUTE_MIN_FAN_PERCENT: u8 = 20;
const AUTO_FAN: u8 = 101;

/// The tested board is IMINI Series-ARB32H-HPT.  A different board is allowed
/// to use the app, but the fan page must warn that it is unverified.
fn machine_verified(status: &helper::Status) -> bool {
    let board = status.board_product.to_lowercase();
    let model = status.system_model.to_lowercase();
    board.contains("arb32h-hpt") || model.contains("pro 830")
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct FanState {
    duty: u8,
    auto: bool,
    low_override: bool,
    temp_protection: bool,
    connected: bool,
    supported: bool,
    machine_verified: bool,
    temperature: i32,
    error: String,
}

impl Default for FanState {
    fn default() -> Self {
        Self {
            duty: AUTO_FAN,
            auto: true,
            low_override: false,
            temp_protection: true,
            connected: false,
            supported: false,
            machine_verified: false,
            temperature: 0,
            error: String::new(),
        }
    }
}

struct AppState {
    settings: Mutex<Settings>,
    /// Serialises firmware calls so a background poll cannot overlap a switch.
    operation: Mutex<()>,
    /// Last mode reported by the firmware, used by the hotkey to avoid a
    /// redundant status round-trip.
    last_mode: Mutex<i32>,
    fan: Mutex<FanState>,
    /// Bumped whenever the UI is shown, so a pending idle-destroy is cancelled.
    ui_generation: AtomicU64,
    quitting: AtomicBool,
}

fn log(message: &str) {
    let base = std::env::var_os("LOCALAPPDATA")
        .map(std::path::PathBuf::from)
        .unwrap_or_else(std::env::temp_dir);
    let dir = base.join("iminiControlCenter");
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        if let Ok(metadata) = std::fs::symlink_metadata(&dir) {
            if metadata.file_attributes() & 0x400 != 0 {
                return;
            }
        }
        if let Ok(metadata) = std::fs::symlink_metadata(dir.join("app.log")) {
            if metadata.file_attributes() & 0x400 != 0 {
                return;
            }
        }
    }
    let _ = std::fs::create_dir_all(&dir);
    let mut options = OpenOptions::new();
    options.create(true).append(true);
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        options.custom_flags(0x00200000); // FILE_FLAG_OPEN_REPARSE_POINT
    }
    if let Ok(mut file) = options.open(dir.join("app.log")) {
        let _ = writeln!(file, "{} {}", timestamp(), message);
    }
}

fn timestamp() -> String {
    // A compact local timestamp is enough for the diagnostic log.
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default();
    format!("t={}", now.as_secs())
}

pub fn show_main(app: &AppHandle) {
    let Some(state) = app.try_state::<AppState>() else {
        return;
    };
    state.ui_generation.fetch_add(1, Ordering::SeqCst);
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.show();
        let _ = window.unminimize();
        let _ = window.set_focus();
        return;
    }
    if let Err(error) = create_main_window(app) {
        log(&format!("recreate main window failed: {error}"));
        return;
    }
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.show();
        let _ = window.set_focus();
    }
}

fn create_main_window(app: &AppHandle) -> Result<(), String> {
    let config = app
        .config()
        .app
        .windows
        .iter()
        .find(|window| window.label == "main")
        .cloned()
        .ok_or_else(|| "主窗口配置缺失".to_string())?;
    tauri::WebviewWindowBuilder::from_config(app, &config)
        .and_then(|builder| builder.build())
        .map(|_| ())
        .map_err(|e| e.to_string())
}

/// Once the window has been hidden for 30 seconds, drop the WebView2 process.
/// The Rust service, tray menu, hotkey and fan monitor keep running; opening
/// the window again recreates it on demand.
fn schedule_ui_destroy(app: AppHandle) {
    let state = app.state::<AppState>();
    let generation = state.ui_generation.fetch_add(1, Ordering::SeqCst) + 1;
    std::thread::spawn(move || {
        std::thread::sleep(Duration::from_secs(30));
        let state = app.state::<AppState>();
        if state.quitting.load(Ordering::SeqCst)
            || state.ui_generation.load(Ordering::SeqCst) != generation
        {
            return;
        }
        if let Some(window) = app.get_webview_window("main") {
            if window.is_visible().unwrap_or(false) || window.is_minimized().unwrap_or(false) {
                return;
            }
            let _ = window.destroy();
            log("webview destroyed after 30s hidden");
        }
    });
}

fn settings_of(app: &AppHandle) -> Settings {
    app.state::<AppState>().settings.lock().unwrap().clone()
}

fn remember_mode(app: &AppHandle, mode: i32) {
    *app.state::<AppState>().last_mode.lock().unwrap() = mode;
}

fn last_mode(app: &AppHandle) -> i32 {
    *app.state::<AppState>().last_mode.lock().unwrap()
}

fn shutdown(app: &AppHandle) {
    let state = app.state::<AppState>();
    state.quitting.store(true, Ordering::SeqCst);
    hotkey_capture::shutdown();
    let fan = state.fan.lock().unwrap().clone();
    if !fan.auto {
        let _ = helper::set_fan(AUTO_FAN);
    }
    app.exit(0);
}

fn set_fan_inner(app: &AppHandle, duty: u8) -> Result<FanState, String> {
    if duty > AUTO_FAN {
        return Err("无效的风扇转速".into());
    }
    if duty != AUTO_FAN && duty < ABSOLUTE_MIN_FAN_PERCENT {
        return Err(format!(
            "为避免风扇停转损坏硬件，转速不能低于 {ABSOLUTE_MIN_FAN_PERCENT}%"
        ));
    }
    let state = app.state::<AppState>();
    let _guard = state.operation.lock().unwrap();
    let current = state.fan.lock().unwrap().clone();
    if duty < MIN_FAN_PERCENT && !current.low_override {
        return Err("风扇转速低于 35% 前，需要先开启“忽视转速调节最低限制”".into());
    }
    if duty < MIN_FAN_PERCENT && current.temp_protection {
        let status = helper::status()?;
        if status.temperature >= TEMP_LIMIT {
            return Err(format!(
                "当前 CPU 温度为 {}°C，已接近保护阈值，不能设置过低转速",
                status.temperature
            ));
        }
    }
    let status = helper::set_fan(duty)?;
    if !status.connected {
        return Err(status.error);
    }
    let mut fan = state.fan.lock().unwrap();
    fan.duty = duty;
    fan.auto = duty == AUTO_FAN;
    fan.connected = true;
    fan.supported = true;
    fan.temperature = status.temperature;
    fan.error.clear();
    let result = fan.clone();
    drop(fan);
    let _ = app.emit("fan-changed", result.clone());
    Ok(result)
}

fn set_fan_safety_inner(
    app: &AppHandle,
    low_override: bool,
    temp_protection: bool,
) -> Result<FanState, String> {
    let state = app.state::<AppState>();
    let _guard = state.operation.lock().unwrap();
    let mut fan = state.fan.lock().unwrap().clone();
    fan.low_override = low_override;
    fan.temp_protection = temp_protection;
    if !low_override && fan.duty < MIN_FAN_PERCENT {
        fan.duty = MIN_FAN_PERCENT;
        fan.auto = false;
        helper::set_fan(MIN_FAN_PERCENT)?;
    }
    *state.fan.lock().unwrap() = fan.clone();
    let _ = app.emit("fan-changed", fan.clone());
    Ok(fan)
}

fn start_fan_monitor(app: AppHandle) {
    std::thread::spawn(move || loop {
        std::thread::sleep(Duration::from_millis(2500));
        let state = app.state::<AppState>();
        if state.quitting.load(Ordering::SeqCst) {
            break;
        }
        let fan = state.fan.lock().unwrap().clone();
        if !fan.temp_protection || fan.auto {
            continue;
        }
        let status = {
            let _guard = state.operation.lock().unwrap();
            helper::status()
        };
        let Ok(status) = status else {
            continue;
        };
        if status.connected && status.temperature >= TEMP_LIMIT {
            let _guard = state.operation.lock().unwrap();
            if helper::set_fan(AUTO_FAN).is_ok() {
                let mut current = state.fan.lock().unwrap();
                current.duty = AUTO_FAN;
                current.auto = true;
                let next = current.clone();
                drop(current);
                let _ = app.emit("fan-changed", next);
                let _ = app.emit(
                    "fan-protection",
                    format!("温度达到 {}°C，已恢复自动风扇", status.temperature),
                );
                toast::show_message(&app, &format!("温度达到 {}°C，已恢复自动风扇", status.temperature));
            }
        } else if status.connected
            && fan.duty < MIN_FAN_PERCENT
            && status.fan_rpm > 0
            && status.fan_rpm < 500
        {
            let _guard = state.operation.lock().unwrap();
            if helper::set_fan(AUTO_FAN).is_ok() {
                let mut current = state.fan.lock().unwrap();
                current.duty = AUTO_FAN;
                current.auto = true;
                let next = current.clone();
                drop(current);
                let _ = app.emit("fan-changed", next);
                let _ = app.emit("fan-protection", "检测到风扇转速异常，已恢复自动风扇".to_string());
                toast::show_message(&app, "检测到风扇转速异常，已恢复自动风扇");
            }
        }
    });
}


fn build_tray_menu(
    app: &AppHandle,
    settings: &Settings,
    mode: i32,
    autostart_enabled: bool,
) -> tauri::Result<Menu<tauri::Wry>> {
    let show = MenuItem::with_id(app, "show", "显示主界面", true, None::<&str>)?;
    let sep1 = PredefinedMenuItem::separator(app)?;
    let silent = CheckMenuItem::with_id(app, "mode:2", "静音模式", true, mode == 2, None::<&str>)?;
    let balanced =
        CheckMenuItem::with_id(app, "mode:0", "均衡模式", true, mode == 0, None::<&str>)?;
    let performance =
        CheckMenuItem::with_id(app, "mode:1", "狂暴模式", true, mode == 1, None::<&str>)?;
    let fan_auto = MenuItem::with_id(app, "fan:auto", "风扇恢复自动", true, None::<&str>)?;
    let sep2 = PredefinedMenuItem::separator(app)?;
    let settings_item = MenuItem::with_id(app, "settings", "设置...", true, None::<&str>)?;
    let autostart = CheckMenuItem::with_id(
        app,
        "autostart",
        "开机自启",
        autostart_allowed(),
        autostart_enabled,
        None::<&str>,
    )?;
    let sep3 = PredefinedMenuItem::separator(app)?;
    let quit = MenuItem::with_id(app, "quit", "退出", true, None::<&str>)?;

    let _ = settings.show_tray;
    Menu::with_items(
        app,
        &[
            &show,
            &sep1,
            &silent,
            &balanced,
            &performance,
            &fan_auto,
            &sep2,
            &settings_item,
            &autostart,
            &sep3,
            &quit,
        ],
    )
}

fn tray_mode(app: &AppHandle, mode: i32) {
    let state = app.state::<AppState>();
    let _guard = state.operation.lock().unwrap();
    match helper::set_mode(mode as u8) {
        Ok(status) => {
            remember_mode(app, status.mode);
            let _ = app.emit("status-changed", status.clone());
            let _ = refresh_tray(app, Some(status.mode));
            toast::show_mode(app, status.mode);
        }
        Err(error) => {
            let _ = app.emit("operation-error", error);
        }
    }
}

fn create_tray(app: &AppHandle, settings: &Settings) -> tauri::Result<()> {
    let mode = helper::status().map(|s| s.mode).unwrap_or(-1);
    if mode >= 0 {
        remember_mode(app, mode);
    }
    let menu = build_tray_menu(app, settings, mode, autostart::is_enabled())?;
    let icon = app
        .default_window_icon()
        .cloned()
        .expect("application icon is configured");

    TrayIconBuilder::with_id("main")
        .icon(icon)
        .tooltip("iMini Tuner")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id.as_ref() {
            "show" => show_main(app),
            "settings" => {
                show_main(app);
                let _ = app.emit("open-settings", ());
            }
            "mode:2" => tray_mode(app, 2),
            "mode:0" => tray_mode(app, 0),
            "mode:1" => tray_mode(app, 1),
            "fan:auto" => {
                if let Err(error) = set_fan_inner(app, AUTO_FAN) {
                    let _ = app.emit("operation-error", error);
                }
            }
            "autostart" => {
                let enabled = !autostart::is_enabled();
                match autostart::set_enabled(enabled) {
                    Ok(()) => {
                        let _ = app.emit("autostart-changed", enabled);
                        let _ = refresh_tray(app, None);
                    }
                    Err(error) => {
                        let _ = app.emit("operation-error", error);
                    }
                }
            }
            "quit" => shutdown(app),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                show_main(tray.app_handle());
            }
        })
        .build(app)?;

    if let Some(tray) = app.tray_by_id("main") {
        tray.set_visible(settings.show_tray)?;
    }
    Ok(())
}

fn refresh_tray(app: &AppHandle, mode: Option<i32>) -> tauri::Result<()> {
    if cfg!(feature = "lite") {
        return Ok(());
    }
    let settings = settings_of(app);
    let mode = mode.unwrap_or_else(|| {
        helper::status()
            .map(|s| s.mode)
            .unwrap_or_else(|_| last_mode(app))
    });
    if mode >= 0 {
        remember_mode(app, mode);
    }
    let menu = build_tray_menu(app, &settings, mode, autostart::is_enabled())?;
    if let Some(tray) = app.tray_by_id("main") {
        tray.set_menu(Some(menu))?;
        tray.set_visible(settings.show_tray)?;
    } else {
        create_tray(app, &settings)?;
    }
    Ok(())
}

fn parse_shortcut(label: &str, value: &str) -> Result<Shortcut, String> {
    value
        .parse::<Shortcut>()
        .map_err(|_| format!("无法识别{label}的快捷键，请重新录制"))
}

fn apply_hotkey(app: &AppHandle, settings: &Settings) -> Result<(), String> {
    let shortcuts = app.global_shortcut();
    let _ = shortcuts.unregister_all();
    if cfg!(feature = "lite") || !settings.hotkey_enabled {
        return Ok(());
    }
    let mut wanted = Vec::new();
    if settings.hotkey_mode == "custom" {
        wanted.push(("静音模式", parse_shortcut("静音模式", &settings.hotkey_silent)?));
        wanted.push(("均衡模式", parse_shortcut("均衡模式", &settings.hotkey_balanced)?));
        wanted.push(("狂暴模式", parse_shortcut("狂暴模式", &settings.hotkey_performance)?));
    } else {
        wanted.push(("轮换模式", parse_shortcut("轮换模式", &settings.hotkey)?));
    }
    for (label, shortcut) in wanted {
        if let Err(error) = shortcuts.register(shortcut) {
            let _ = shortcuts.unregister_all();
            return Err(format!("注册{label}快捷键失败（可能被其他程序占用）: {error}"));
        }
    }
    Ok(())
}

fn handle_hotkey(app: &AppHandle, shortcut: &Shortcut) {
    let settings = settings_of(app);
    if settings.hotkey_mode == "custom" {
        let entries = [
            (2, settings.hotkey_silent.as_str()),
            (0, settings.hotkey_balanced.as_str()),
            (1, settings.hotkey_performance.as_str()),
        ];
        for (mode, text) in entries {
            if let Ok(parsed) = text.parse::<Shortcut>() {
                if &parsed == shortcut {
                    tray_mode(app, mode);
                    return;
                }
            }
        }
        return;
    }
    match cycle_mode(app) {
        Ok(status) => {
            remember_mode(app, status.mode);
            let _ = app.emit("status-changed", status.clone());
            toast::show_mode(app, status.mode);
            let _ = refresh_tray(app, Some(status.mode));
        }
        Err(error) => {
            let _ = app.emit("operation-error", error);
        }
    }
}

fn cycle_mode(app: &AppHandle) -> Result<helper::Status, String> {
    let state = app.state::<AppState>();
    let _guard = state.operation.lock().unwrap();
    let _ = app.emit("mode-busy", true);
    let current = last_mode(app);
    let mode = if current >= 0 {
        current
    } else {
        let status = helper::status()?;
        if !status.connected {
            return Err(status.error);
        }
        remember_mode(app, status.mode);
        status.mode
    };
    let next = match mode {
        2 => 0,
        0 => 1,
        1 => 2,
        _ => 2,
    };
    // Paint the intent immediately; the firmware round-trip can take a moment.
    let _ = app.emit("mode-pending", next);
    helper::set_mode(next)
}

#[tauri::command]
fn get_status(app: AppHandle) -> Result<helper::Status, String> {
    let state = app.state::<AppState>();
    let _guard = state.operation.lock().unwrap();
    let mut status = helper::status()?;
    status.machine_verified = machine_verified(&status);
    if status.connected {
        remember_mode(&app, status.mode);
    }
    Ok(status)
}

#[tauri::command]
fn set_mode(app: AppHandle, mode: u8) -> Result<helper::Status, String> {
    let state = app.state::<AppState>();
    let _guard = state.operation.lock().unwrap();
    let mut status = helper::set_mode(mode)?;
    status.machine_verified = machine_verified(&status);
    remember_mode(&app, status.mode);
    let _ = refresh_tray(&app, Some(status.mode));
    let _ = app.emit("status-changed", status.clone());
    toast::show_mode(&app, status.mode);
    Ok(status)
}

#[tauri::command]
fn get_fan_state(app: AppHandle, state: State<'_, AppState>) -> FanState {
    let mut fan = state.fan.lock().unwrap().clone();
    match helper::status() {
        Ok(status) => {
            fan.connected = status.connected;
            fan.supported = status.connected;
            fan.machine_verified = machine_verified(&status);
            fan.temperature = status.temperature;
            fan.error = status.error;
        }
        Err(error) => {
            fan.connected = false;
            fan.supported = false;
            fan.error = error;
        }
    }
    let _ = app.emit("fan-changed", fan.clone());
    fan
}

#[tauri::command]
fn set_fan(app: AppHandle, duty: u8) -> Result<FanState, String> {
    set_fan_inner(&app, duty)
}

#[tauri::command]
fn set_fan_safety(
    app: AppHandle,
    low_override: bool,
    temp_protection: bool,
) -> Result<FanState, String> {
    set_fan_safety_inner(&app, low_override, temp_protection)
}

#[tauri::command]
fn get_settings(state: State<'_, AppState>) -> Settings {
    state.settings.lock().unwrap().clone()
}

#[tauri::command]
fn save_settings(
    app: AppHandle,
    state: State<'_, AppState>,
    next: Settings,
) -> Result<Settings, String> {
    next.validate()?;
    let previous = state.settings.lock().unwrap().clone();
    {
        let mut guard = state.settings.lock().unwrap();
        *guard = next.clone();
    }
    if let Err(error) = apply_hotkey(&app, &next) {
        *state.settings.lock().unwrap() = previous.clone();
        let _ = apply_hotkey(&app, &previous);
        return Err(error);
    }
    if let Err(error) = next.save() {
        *state.settings.lock().unwrap() = previous.clone();
        let _ = apply_hotkey(&app, &previous);
        return Err(error);
    }
    let _ = refresh_tray(&app, None);
    let _ = app.emit("settings-changed", next.clone());
    Ok(next)
}

#[tauri::command]
fn set_autostart(app: AppHandle, enabled: bool) -> Result<bool, String> {
    if cfg!(feature = "lite") {
        return Err("便携版不支持开机自启".into());
    }
    autostart::set_enabled(enabled)?;
    let _ = refresh_tray(&app, None);
    let _ = app.emit("autostart-changed", enabled);
    Ok(autostart::is_enabled())
}

#[tauri::command]
fn get_autostart() -> bool {
    autostart::is_enabled()
}

fn autostart_allowed() -> bool {
    if cfg!(feature = "lite") || cfg!(feature = "portable") {
        return false;
    }
    let Ok(exe) = std::env::current_exe() else {
        return false;
    };
    let Ok(program_files) = runtime::program_files() else {
        return false;
    };
    exe.starts_with(program_files)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct AppInfo {
    version: String,
    lite: bool,
    portable: bool,
    hotkey: String,
    autostart_allowed: bool,
}

#[tauri::command]
fn get_app_info(app: AppHandle) -> AppInfo {
    let settings = settings_of(&app);
    AppInfo {
        version: app.package_info().version.to_string(),
        lite: cfg!(feature = "lite"),
        portable: cfg!(feature = "portable"),
        hotkey: settings.hotkey,
        autostart_allowed: autostart_allowed(),
    }
}

#[tauri::command]
fn quit_app(app: AppHandle) {
    shutdown(&app);
}

#[tauri::command]
fn prepare_hotkey_capture(app: AppHandle) -> Result<(), String> {
    hotkey_capture::prepare(&app)
}

#[tauri::command]
fn start_hotkey_capture(app: AppHandle) -> Result<(), String> {
    hotkey_capture::start(app)
}

#[tauri::command]
fn stop_hotkey_capture() {
    hotkey_capture::stop();
}

pub fn run() {
    let args: Vec<String> = std::env::args().collect();
    let start_minimized = args.iter().any(|a| a == "--minimized");
    let force_show = args.iter().any(|a| a == "--show");
    let quit = args.iter().any(|a| a == "--quit");
    let initial_settings = Settings::load();

    let builder = tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, argv, _cwd| {
            if argv.iter().any(|a| a == "--quit") {
                shutdown(app);
                return;
            }
            if argv.iter().any(|a| a == "--minimized")
                && !argv.iter().any(|a| a == "--show")
            {
                return;
            }
            show_main(app);
        }))
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(|app, shortcut, event| {
                    if event.state() == ShortcutState::Pressed {
                        handle_hotkey(app, shortcut);
                    }
                })
                .build(),
        )
        .manage(AppState {
            settings: Mutex::new(initial_settings.clone()),
            operation: Mutex::new(()),
            last_mode: Mutex::new(-1),
            fan: Mutex::new(FanState::default()),
            ui_generation: AtomicU64::new(0),
            quitting: AtomicBool::new(false),
        })
        .invoke_handler(tauri::generate_handler![
            get_status,
            set_mode,
            get_fan_state,
            set_fan,
            set_fan_safety,
            get_settings,
            save_settings,
            set_autostart,
            get_autostart,
            get_app_info,
            prepare_hotkey_capture,
            start_hotkey_capture,
            stop_hotkey_capture,
            quit_app
        ])
        .setup(move |app| {
            let handle = app.handle().clone();
            if quit {
                handle.exit(0);
                return Ok(());
            }
            if !cfg!(feature = "lite") {
                if let Err(error) = create_tray(&handle, &initial_settings) {
                    log(&format!("create_tray failed: {error}"));
                }
                if let Err(error) = apply_hotkey(&handle, &initial_settings) {
                    log(&format!("hotkey registration failed: {error}"));
                    let _ = handle.emit("operation-error", error);
                }
                // Prewarm the capture helper so opening Settings (and the very
                // first key press) does not wait for a process launch.
                if let Err(error) = hotkey_capture::prepare(&handle) {
                    log(&format!("hotkey helper prewarm failed: {error}"));
                }
            }
            let _ = helper::set_fan(AUTO_FAN);
            wake::start(handle.clone());
            start_fan_monitor(handle.clone());
            if force_show || !start_minimized || cfg!(feature = "lite") {
                show_main(&handle);
            }
            Ok(())
        })
        .on_window_event(|window, event| {
            if let WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let app = window.app_handle();
                let close_to_tray = if cfg!(feature = "lite") {
                    false
                } else {
                    settings_of(app).close_to_tray
                };
                if close_to_tray {
                    let _ = window.hide();
                    schedule_ui_destroy(app.clone());
                } else {
                    shutdown(app);
                }
            }
        });

    let app = match builder.build(tauri::generate_context!()) {
        Ok(app) => app,
        Err(error) => {
            log(&format!("tauri build failed: {error}"));
            eprintln!("error while running iMini Tuner: {error}");
            return;
        }
    };

    app.run(|app_handle, event| match event {
        RunEvent::ExitRequested { api, code, .. } => {
            let quitting = app_handle
                .state::<AppState>()
                .quitting
                .load(Ordering::SeqCst);
            if !quitting && code.is_none() {
                // The WebView can be destroyed while the tray service lives on.
                api.prevent_exit();
            }
        }
        // Make sure the capture helper never outlives the app.
        RunEvent::Exit => hotkey_capture::shutdown(),
        _ => {}
    });
}

