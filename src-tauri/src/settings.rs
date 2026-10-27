use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

const DEFAULT_HOTKEY: &str = "Ctrl+Alt+M";
const DEFAULT_SILENT: &str = "Ctrl+Alt+1";
const DEFAULT_BALANCED: &str = "Ctrl+Alt+2";
const DEFAULT_PERFORMANCE: &str = "Ctrl+Alt+3";

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Settings {
    #[serde(default = "default_true")]
    pub close_to_tray: bool,
    #[serde(default = "default_true")]
    pub show_tray: bool,
    #[serde(default = "default_true")]
    pub hotkey_enabled: bool,
    /// "cycle" = one key cycles all modes; "custom" = one key per mode.
    #[serde(default = "default_hotkey_mode")]
    pub hotkey_mode: String,
    #[serde(default = "default_hotkey")]
    pub hotkey: String,
    #[serde(default = "default_silent")]
    pub hotkey_silent: String,
    #[serde(default = "default_balanced")]
    pub hotkey_balanced: String,
    #[serde(default = "default_performance")]
    pub hotkey_performance: String,
}

fn default_true() -> bool {
    true
}

fn default_hotkey_mode() -> String {
    "cycle".to_string()
}

fn default_hotkey() -> String {
    DEFAULT_HOTKEY.to_string()
}

fn default_silent() -> String {
    DEFAULT_SILENT.to_string()
}

fn default_balanced() -> String {
    DEFAULT_BALANCED.to_string()
}

fn default_performance() -> String {
    DEFAULT_PERFORMANCE.to_string()
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            close_to_tray: true,
            show_tray: true,
            hotkey_enabled: true,
            hotkey_mode: default_hotkey_mode(),
            hotkey: DEFAULT_HOTKEY.to_string(),
            hotkey_silent: DEFAULT_SILENT.to_string(),
            hotkey_balanced: DEFAULT_BALANCED.to_string(),
            hotkey_performance: DEFAULT_PERFORMANCE.to_string(),
        }
    }
}

fn validate_hotkey(value: &str, label: &str) -> Result<(), String> {
    let hotkey = value.trim();
    if hotkey.is_empty() {
        return Err(format!("请先设置{label}的快捷键"));
    }
    let lower = hotkey.to_lowercase();
    if lower.contains("alt+f4") {
        return Err(format!("{label}不能使用 Alt+F4，系统会先关闭窗口"));
    }
    let parts = hotkey.split('+').map(str::trim).filter(|p| !p.is_empty());
    let mut has_modifier = false;
    let mut last = "";
    for part in parts {
        let p = part.to_ascii_lowercase();
        if matches!(p.as_str(), "ctrl" | "control" | "alt" | "shift" | "super" | "win" | "meta" | "cmd") {
            has_modifier = true;
        }
        last = part;
    }
    if !has_modifier {
        return Err(format!("{label}必须至少包含一个修饰键（Ctrl/Alt/Shift/Win）"));
    }
    let last_lower = last.to_ascii_lowercase();
    if matches!(last_lower.as_str(), "ctrl" | "control" | "alt" | "shift" | "super" | "win" | "meta" | "cmd") {
        return Err(format!("{label}还需要一个普通按键"));
    }
    Ok(())
}

impl Settings {
    pub fn validate(&self) -> Result<(), String> {
        if !self.hotkey_enabled {
            return Ok(());
        }
        if self.hotkey_mode == "custom" {
            validate_hotkey(&self.hotkey_silent, "静音模式")?;
            validate_hotkey(&self.hotkey_balanced, "均衡模式")?;
            validate_hotkey(&self.hotkey_performance, "狂暴模式")?;
            let keys = [
                self.hotkey_silent.trim().to_ascii_lowercase(),
                self.hotkey_balanced.trim().to_ascii_lowercase(),
                self.hotkey_performance.trim().to_ascii_lowercase(),
            ];
            if keys[0] == keys[1] || keys[0] == keys[2] || keys[1] == keys[2] {
                return Err("三个模式的快捷键不能重复".into());
            }
        } else {
            validate_hotkey(&self.hotkey, "轮换模式")?;
        }
        Ok(())
    }

    /// Writable settings path.  The portable build stores settings next to
    /// the executable; installed builds use LOCALAPPDATA.
    pub fn path() -> PathBuf {
        if cfg!(feature = "portable") {
            if let Ok(exe) = std::env::current_exe() {
                if let Some(dir) = exe.parent() {
                    return dir.join("imini-settings.json");
                }
            }
        }
        let base = std::env::var_os("LOCALAPPDATA")
            .map(PathBuf::from)
            .unwrap_or_else(std::env::temp_dir);
        base.join("iminiControlCenter").join("settings.json")
    }

    pub fn load() -> Self {
        let path = Self::path();
        let text = match fs::read_to_string(&path) {
            Ok(text) => text,
            Err(_) => return Self::default(),
        };
        serde_json::from_str::<Settings>(&text).unwrap_or_default()
    }

    pub fn save(&self) -> Result<(), String> {
        let path = Self::path();
        if let Some(dir) = path.parent() {
            fs::create_dir_all(dir).map_err(|e| format!("无法创建设置目录: {e}"))?;
        }
        #[cfg(windows)]
        {
            use std::os::windows::fs::MetadataExt;
            if let Ok(metadata) = fs::symlink_metadata(&path) {
                if metadata.file_attributes() & 0x400 != 0 {
                    return Err("设置文件是重解析点，已拒绝写入".into());
                }
            }
        }
        let json = serde_json::to_string_pretty(self).map_err(|e| e.to_string())?;
        let mut options = fs::OpenOptions::new();
        options.write(true).create(true).truncate(true);
        #[cfg(windows)]
        {
            use std::os::windows::fs::OpenOptionsExt;
            options.custom_flags(0x00200000); // FILE_FLAG_OPEN_REPARSE_POINT
        }
        let mut file = options
            .open(&path)
            .map_err(|e| format!("无法保存设置: {e}"))?;
        use std::io::Write;
        file.write_all(json.as_bytes())
            .map_err(|e| format!("无法保存设置: {e}"))
    }
}
