use tauri::AppHandle;

/// The launcher script signals this named event to bring the GUI back.
pub const WAKE_EVENT_NAME: &str = "Local\\iMiniTuner.Show";

#[cfg(windows)]
pub fn start(app: AppHandle) {
    use std::thread;
    use windows_sys::Win32::Foundation::{CloseHandle, HANDLE, WAIT_OBJECT_0};
    use windows_sys::Win32::System::Threading::{CreateEventW, WaitForSingleObject, INFINITE};

    let wide: Vec<u16> = WAKE_EVENT_NAME
        .encode_utf16()
        .chain(std::iter::once(0))
        .collect();

    thread::spawn(move || {
        let handle: HANDLE = unsafe { CreateEventW(std::ptr::null(), 0, 0, wide.as_ptr()) };
        if handle.is_null() {
            return;
        }
        loop {
            let result = unsafe { WaitForSingleObject(handle, INFINITE) };
            if result == WAIT_OBJECT_0 {
                crate::show_main(&app);
            } else {
                break;
            }
        }
        unsafe {
            CloseHandle(handle);
        }
    });
}

#[cfg(not(windows))]
pub fn start(_app: AppHandle) {}
