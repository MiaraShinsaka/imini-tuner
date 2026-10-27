(() => {
  "use strict";

  // No page context menu: the tray icon owns the only right-click menu.
  document.addEventListener("contextmenu", (event) => event.preventDefault());
  document.addEventListener("dragstart", (event) => event.preventDefault());

  const tauri = window.__TAURI__ || {};
  const invoke = tauri.core && tauri.core.invoke;
  const listen = tauri.event && tauri.event.listen;
  const appWindow = tauri.window && tauri.window.getCurrentWindow
    ? tauri.window.getCurrentWindow()
    : null;

  const $ = (id) => document.getElementById(id);
  const cards = Array.from(document.querySelectorAll(".card"));
  const tabs = Array.from(document.querySelectorAll(".tab"));
  const modeNames = { 2: "静音模式", 0: "均衡模式", 1: "狂暴模式" };

  let status = {
    connected: false,
    error: "",
    mode: -1,
    temperature: 0,
    fanRpm: 0,
    systemManufacturer: "",
    systemModel: "",
    boardProduct: "",
    machineVerified: false,
    machineLoaded: false
  };
  let settings = {
    closeToTray: true,
    showTray: true,
    hotkeyEnabled: true,
    hotkeyMode: "cycle",
    hotkey: "Ctrl+Alt+M",
    hotkeySilent: "Ctrl+Alt+1",
    hotkeyBalanced: "Ctrl+Alt+2",
    hotkeyPerformance: "Ctrl+Alt+3",
    fanLowOverride: false,
    tempProtection: true
  };
  let info = { version: "2.2.0", lite: false, portable: false, autostartAllowed: false };
  let autostart = false;
  let refreshing = false;
  let switching = false;
  let lastNotifiedMode = -1;
  let hintTimer = null;
  let hintOverride = "";
  let hotkeyMode = "cycle";
  const hotkeyDrafts = {
    cycle: "Ctrl+Alt+M",
    silent: "Ctrl+Alt+1",
    balanced: "Ctrl+Alt+2",
    performance: "Ctrl+Alt+3"
  };
  let activeHotkeyField = null;
  let capturing = false;
  let activePage = "mode";
  let fanDisclaimerAccepted = false;
  let fanOperation = false;

  let fan = {
    connected: false,
    supported: false,
    duty: 101,
    auto: true,
    lowOverride: false,
    tempProtection: true,
    temperature: 0,
    machineVerified: false
  };
  let fanDraft = 55;

  function setHint(text, isError) {
    const hint = $("hint");
    hint.textContent = text;
    hint.style.color = isError ? "var(--danger)" : "";
    hintOverride = text;
    if (hintTimer) clearTimeout(hintTimer);
    hintTimer = setTimeout(() => {
      hintOverride = "";
      hint.style.color = "";
      renderHint();
    }, 5000);
  }

  function defaultHint() {
    if (info.lite) return "关闭窗口即退出程序";
    if (!settings.hotkeyEnabled) return "右键托盘图标查看更多";
    if (settings.hotkeyMode === "custom") return "热键 自定义三模式 · 右键托盘图标查看更多";
    return `热键 ${formatHotkey(settings.hotkey)} · 右键托盘图标查看更多`;
  }

  function renderHint() {
    $("hint").textContent = hintOverride || defaultHint();
  }

  function setBusy(active, text) {
    const busy = $("busy");
    if (!busy) return;
    busy.hidden = !active;
    if (active && text) $("busy-text").textContent = text;
  }

  const modifierTips = {
    Ctrl: "Ctrl：Control 键，常用的快捷键修饰键",
    Alt: "Alt：Alternate 键，常用于菜单和组合键",
    Shift: "Shift：Shift 键，用于大小写或组合快捷键",
    Win: "Win：Windows 键，用于系统级快捷键"
  };

  function displayPart(part) {
    return part.toLowerCase() === "super" ? "Win" : part;
  }

  function formatHotkey(value) {
    return String(value || "Ctrl+Alt+M")
      .split("+")
      .map((part) => part.trim())
      .filter(Boolean)
      .map(displayPart)
      .join(" + ");
  }

  function canonicalHotkey(value) {
    return String(value || "")
      .split("+")
      .map((part) => part.trim())
      .filter(Boolean)
      .map((part) => (part.toLowerCase() === "win" ? "Super" : part))
      .join("+");
  }

  function isModifier(part) {
    return ["ctrl", "control", "alt", "shift", "super", "win", "meta", "cmd"]
      .includes(String(part).toLowerCase());
  }

  function canonicalModifier(part) {
    const p = String(part).toLowerCase();
    if (p === "control") return "Ctrl";
    if (p === "win" || p === "meta" || p === "cmd" || p === "super") return "Win";
    return p.charAt(0).toUpperCase() + p.slice(1);
  }

  function renderHotkeyField(el, value) {
    if (!el) return;
    el.textContent = "";
    const parts = String(value || "").split("+").map((p) => p.trim()).filter(Boolean);
    if (!parts.length) {
      const placeholder = document.createElement("span");
      placeholder.className = "hk-placeholder";
      placeholder.textContent = "点击后按下组合键";
      el.appendChild(placeholder);
      return;
    }
    parts.forEach((part, index) => {
      if (index > 0) {
        const sep = document.createElement("span");
        sep.className = "hk-sep";
        sep.textContent = "+";
        el.appendChild(sep);
      }
      const token = document.createElement("span");
      if (isModifier(part)) {
        const canonical = canonicalModifier(part);
        token.className = "hk-mod";
        token.textContent = displayPart(canonical);
        token.dataset.tip = modifierTips[canonical] || `${canonical}：修饰键`;
        token.title = token.dataset.tip;
      } else {
        token.className = "hk-key";
        token.textContent = displayPart(part);
      }
      el.appendChild(token);
    });
  }

  function renderMachine() {
    const el = $("machine-model");
    if (!el) return;
    const manufacturer = String(status.systemManufacturer || "").trim();
    const model = String(status.systemModel || "").trim();
    const label = [manufacturer, model].filter(Boolean).join(" ");
    if (label) {
      el.textContent = label;
      el.title = status.boardProduct ? `主板：${status.boardProduct}` : label;
    } else if (status.machineLoaded) {
      el.textContent = "机型信息不可用";
      el.title = "";
    } else {
      el.textContent = "正在读取机型…";
      el.title = "";
    }
  }

  function renderStatus() {
    const connected = !!status.connected;
    const dot = $("status-dot");
    dot.className = "status-dot " + (connected ? "ok" : "error");
    $("status-text").textContent = connected
      ? "已连接 · 固件接口正常"
      : (status.error ? `未连接 · ${status.error}` : "未连接 · 固件接口");
    $("autostart").setAttribute("aria-checked", autostart ? "true" : "false");
    renderMachine();
    renderHint();
  }

  function renderMode() {
    const connected = !!status.connected;
    const mode = connected ? Number(status.mode) : -1;
    const name = modeNames[mode] || (connected ? "未知模式" : "未连接");

    const modeName = $("mode-name");
    modeName.textContent = name;
    modeName.dataset.mode = String(mode);

    const temp = Number(status.temperature);
    $("temp").textContent = connected && temp > 0 && temp < 130 ? String(temp) : "—";

    const fanRpm = Number(status.fanRpm);
    $("fan").textContent = connected && fanRpm > 0 && fanRpm < 10000 ? String(fanRpm) : "—";

    cards.forEach((card) => {
      const cardMode = Number(card.dataset.mode);
      card.classList.toggle("selected", connected && cardMode === mode);
      card.disabled = !connected || switching;
      card.setAttribute("aria-pressed", connected && cardMode === mode ? "true" : "false");
    });

    renderFanMetrics();
  }

  function renderFanMetrics() {
    const connected = !!status.connected;
    const temp = Number(status.temperature);
    const fanRpm = Number(status.fanRpm);
    $("fan-temp").textContent = connected && temp > 0 && temp < 130 ? String(temp) : "—";
    $("fan-rpm").textContent = connected && fanRpm > 0 && fanRpm < 10000 ? String(fanRpm) : "—";
  }

  function fanIsAvailable() {
    return !!(fan.connected && fan.supported);
  }

  function sliderMin() {
    return fan.lowOverride ? 20 : 35;
  }

  function clampFanValue(value) {
    const min = sliderMin();
    return Math.max(min, Math.min(100, Math.round(Number(value))));
  }

  function setSliderFill(value) {
    const slider = $("fan-slider");
    const min = sliderMin();
    const span = 100 - min || 1;
    const fill = ((value - min) / span) * 100;
    slider.style.setProperty("--fill", `${Math.max(0, Math.min(100, fill))}%`);
  }

  function renderFan() {
    const available = fanIsAvailable();
    const auto = fan.auto || !available;
    const value = auto ? 55 : clampFanValue(fan.duty);
    fanDraft = value;

    const state = $("fan-state");
    state.classList.remove("auto", "custom", "low");
    if (!available) {
      state.textContent = "不可用";
    } else if (auto) {
      state.textContent = "自动调节";
      state.classList.add("auto");
    } else if (!fan.lowOverride && fan.duty < 35) {
      state.textContent = `低速运行 · ${fan.duty}%`;
      state.classList.add("low");
    } else {
      state.textContent = `自定义转速 · ${fan.duty}%`;
      state.classList.add("custom");
    }

    const valueEl = $("fan-value");
    valueEl.classList.remove("auto", "low");
    if (!available) {
      valueEl.textContent = "—";
    } else if (auto) {
      valueEl.textContent = "自动";
      valueEl.classList.add("auto");
    } else {
      valueEl.textContent = `${fan.duty}%`;
      if (!fan.lowOverride && fan.duty < 35) valueEl.classList.add("low");
    }

    const slider = $("fan-slider");
    slider.min = String(sliderMin());
    slider.value = String(value);
    slider.disabled = !available || fanOperation;
    setSliderFill(value);

    const compat = $("compat-warning");
    compat.hidden = !(available && !fan.machineVerified);

    $("fan-min-label").textContent = `${sliderMin()}%`;
    $("fan-auto").disabled = !available || fanOperation || auto;

    const toggle = $("low-override");
    toggle.setAttribute("aria-checked", fan.lowOverride ? "true" : "false");
    toggle.disabled = !available || fanOperation;

    $("temp-protection-row").hidden = !fan.lowOverride;
    $("temp-protection").setAttribute("aria-checked", fan.tempProtection ? "true" : "false");
    $("temp-protection").disabled = !available || fanOperation;

    if (!available) {
      $("safety-note").textContent = "风扇控制不可用：需要管理员权限，且主板必须支持风扇调节。";
    } else if (!fan.lowOverride) {
      $("safety-note").textContent = "低温保护默认启用；自定义转速会在退出程序时恢复自动。";
    } else {
      $("safety-note").textContent = "已解除 35% 最低限制，20% 为硬件保护下限。温度达到 88°C 时会自动恢复固件自动风扇。";
    }
  }

  function render() {
    renderMode();
    renderStatus();
  }

  async function refreshStatus() {
    if (!invoke || refreshing || switching || fanOperation) return;
    refreshing = true;
    try {
      status = await invoke("get_status");
      status.machineLoaded = true;
    } catch (error) {
      status = {
        connected: false,
        error: String(error),
        mode: -1,
        temperature: 0,
        fanRpm: 0,
        machineLoaded: true
      };
    } finally {
      refreshing = false;
      render();
    }
  }

  async function refreshFanState() {
    if (!invoke) return;
    try {
      fan = await invoke("get_fan_state");
    } catch (error) {
      fan = {
        connected: false,
        supported: false,
        duty: 101,
        auto: true,
        lowOverride: false,
        tempProtection: true,
        temperature: 0,
        machineVerified: false
      };
      setHint(String(error), true);
    }
    renderFan();
  }

  async function switchMode(mode) {
    if (!invoke || switching) return;
    const previous = { ...status };
    const started = performance.now();
    switching = true;

    status = { ...status, mode };
    render();
    setBusy(true, `正在切换到“${modeNames[mode] || "指定模式"}”…`);

    try {
      status = await invoke("set_mode", { mode });
      lastNotifiedMode = status.mode;
      render();
      setHint(`已切换到“${modeNames[mode] || "指定模式"}”`);
    } catch (error) {
      status = previous;
      render();
      setHint(String(error), true);
    } finally {
      const remaining = Math.max(0, 380 - (performance.now() - started));
      setTimeout(() => {
        switching = false;
        setBusy(false);
        render();
      }, remaining);
    }
  }

  async function applyFan(duty) {
    if (!invoke || fanOperation || !fanIsAvailable()) return;
    const previous = { ...fan };
    const nextDuty = duty === 101 ? 101 : clampFanValue(duty);
    fanOperation = true;
    fan = { ...fan, duty: nextDuty, auto: nextDuty === 101 };
    renderFan();

    try {
      fan = await invoke("set_fan", { duty: nextDuty });
      renderFan();
      if (fan.auto) {
        setHint("风扇已恢复自动调节");
      } else {
        setHint(`风扇转速已设置为 ${fan.duty}%`);
      }
    } catch (error) {
      fan = previous;
      renderFan();
      setHint(String(error), true);
    } finally {
      fanOperation = false;
      renderFan();
    }
  }

  async function setFanSafety(lowOverride, tempProtection) {
    if (!invoke || fanOperation) return;
    fanOperation = true;
    renderFan();
    try {
      fan = await invoke("set_fan_safety", { lowOverride, tempProtection });
    } catch (error) {
      setHint(String(error), true);
    } finally {
      fanOperation = false;
      await refreshFanState();
    }
  }

  function showPage(name) {
    if (name !== "mode" && name !== "fan") return;
    activePage = name;
    tabs.forEach((tab) => tab.classList.toggle("active", tab.dataset.page === name));
    $("page-mode").hidden = name !== "mode";
    $("page-fan").hidden = name !== "fan";

    if (name === "fan") {
      if (!fanDisclaimerAccepted) {
        openFanGate();
      } else {
        hideFanGate();
        refreshFanState();
      }
    } else {
      // The disclaimer only guards the fan page; tabs stay usable until the
      // user explicitly accepts it.
      hideFanGate();
      refreshStatus();
    }
  }

  function openFanGate() {
    $("fan-gate").hidden = false;
    $("page-fan").inert = true;
    $("fan-gate-accept").focus();
  }

  function hideFanGate() {
    $("fan-gate").hidden = true;
    $("page-fan").inert = false;
  }

  function acceptFanGate() {
    fanDisclaimerAccepted = true;
    hideFanGate();
    refreshFanState().then(() => $("fan-slider").focus());
  }

  function openWarning() {
    $("warning-overlay").hidden = false;
    $("tabs").inert = true;
    $("workspace").inert = true;
    $("warning-confirm").focus();
  }

  function closeWarning() {
    $("warning-overlay").hidden = true;
    $("tabs").inert = false;
    $("workspace").inert = false;
    $("low-override").setAttribute("aria-checked", fan.lowOverride ? "true" : "false");
    $("low-override").focus();
  }

  async function confirmLowOverride() {
    closeWarning();
    await setFanSafety(true, true);
    if (fan.lowOverride) {
      setHint("已开启低速风扇；温度保护已同时启用");
    }
  }

  function handleLowOverride(enabled) {
    if (!enabled) {
      setFanSafety(false, fan.tempProtection);
      return;
    }
    if (fan.lowOverride) return;
    $("low-override").setAttribute("aria-checked", "false");
    openWarning();
  }

  function handleTempProtection(enabled) {
    if (!fan.lowOverride) return;
    setFanSafety(true, enabled);
  }

  async function toggleAutostart(enabled) {
    if (!invoke) return;
    try {
      autostart = await invoke("set_autostart", { enabled });
      renderStatus();
      setHint(autostart ? "已开启开机自启" : "已关闭开机自启");
    } catch (error) {
      $("autostart").setAttribute("aria-checked", autostart ? "true" : "false");
      setHint(String(error), true);
    }
  }

  function hotkeyFieldId(key) {
    return key === "cycle" ? "hotkey" : `hotkey-${key}`;
  }

  function renderHotkeys() {
    Object.keys(hotkeyDrafts).forEach((key) => {
      renderHotkeyField($(hotkeyFieldId(key)), hotkeyDrafts[key]);
    });
  }

  function setHotkeyNote(message, isError) {
    const id = hotkeyMode === "custom" ? "hotkey-custom-note" : "hotkey-note";
    const note = $(id);
    if (!note) return;
    note.textContent = message;
    note.classList.toggle("error", !!isError);
  }

  function syncHotkeyMode() {
    const toggle = $("hotkey-mode-toggle");
    if (toggle) toggle.setAttribute("aria-checked", hotkeyMode === "custom" ? "true" : "false");
    $("hotkey-cycle").hidden = hotkeyMode !== "cycle";
    $("hotkey-custom").hidden = hotkeyMode !== "custom";
    setHotkeyNote(
      hotkeyMode === "custom"
        ? "三个快捷键不能重复；至少包含一个修饰键。"
        : "点击输入框后按下组合键；至少包含一个修饰键。",
      false
    );
  }

  function setHotkeyMode(mode) {
    hotkeyMode = mode === "custom" ? "custom" : "cycle";
    syncHotkeyMode();
    const key = hotkeyMode === "custom" ? "silent" : "cycle";
    $(hotkeyFieldId(key)).focus();
  }

  function openSettings() {
    if (info.lite) return;
    hotkeyMode = settings.hotkeyMode === "custom" ? "custom" : "cycle";
    hotkeyDrafts.cycle = canonicalHotkey(settings.hotkey);
    hotkeyDrafts.silent = canonicalHotkey(settings.hotkeySilent);
    hotkeyDrafts.balanced = canonicalHotkey(settings.hotkeyBalanced);
    hotkeyDrafts.performance = canonicalHotkey(settings.hotkeyPerformance);
    renderHotkeys();
    syncHotkeyMode();
    $("close-to-tray").checked = !!settings.closeToTray;
    $("show-tray").checked = !!settings.showTray;
    $("settings-overlay").hidden = false;
    if (invoke) invoke("prepare_hotkey_capture").catch(() => {});
    const key = hotkeyMode === "custom" ? "silent" : "cycle";
    $(hotkeyFieldId(key)).focus();
  }

  function closeSettings() {
    capturing = false;
    activeHotkeyField = null;
    $("settings-overlay").hidden = true;
    if (invoke) invoke("stop_hotkey_capture").catch(() => {});
  }

  function validateHotkeyDraft(value, label) {
    const hotkey = canonicalHotkey(value);
    if (!hotkey) return `${label}还没有设置快捷键`;
    if (/alt\+f4/i.test(hotkey)) return `${label}不能使用 Alt+F4`;
    const parts = hotkey.split("+").filter(Boolean);
    const mods = parts.filter(isModifier);
    if (!mods.length) return `${label}必须至少包含一个修饰键（Ctrl/Alt/Shift/Win）`;
    if (isModifier(parts[parts.length - 1])) return `${label}还需要一个普通按键`;
    return "";
  }

  async function saveSettings() {
    if (!invoke) return;
    const error = hotkeyMode === "custom"
      ? (validateHotkeyDraft(hotkeyDrafts.silent, "静音模式") ||
         validateHotkeyDraft(hotkeyDrafts.balanced, "均衡模式") ||
         validateHotkeyDraft(hotkeyDrafts.performance, "狂暴模式") ||
         (new Set([
           canonicalHotkey(hotkeyDrafts.silent).toLowerCase(),
           canonicalHotkey(hotkeyDrafts.balanced).toLowerCase(),
           canonicalHotkey(hotkeyDrafts.performance).toLowerCase()
         ]).size !== 3 ? "三个模式的快捷键不能重复" : ""))
      : validateHotkeyDraft(hotkeyDrafts.cycle, "轮换模式");
    if (error) {
      setHotkeyNote(error, true);
      return;
    }
    const next = {
      ...settings,
      closeToTray: $("close-to-tray").checked,
      showTray: $("show-tray").checked,
      hotkeyEnabled: true,
      hotkeyMode,
      hotkey: canonicalHotkey(hotkeyDrafts.cycle),
      hotkeySilent: canonicalHotkey(hotkeyDrafts.silent),
      hotkeyBalanced: canonicalHotkey(hotkeyDrafts.balanced),
      hotkeyPerformance: canonicalHotkey(hotkeyDrafts.performance)
    };
    try {
      settings = await invoke("save_settings", { next });
      closeSettings();
      renderHint();
      setHint("设置已保存");
    } catch (errorText) {
      setHotkeyNote(String(errorText), true);
    }
  }

  function keyLabel(event) {
    const key = event.key;
    if (key === " ") return "Space";
    if (key === "ArrowUp") return "Up";
    if (key === "ArrowDown") return "Down";
    if (key === "ArrowLeft") return "Left";
    if (key === "ArrowRight") return "Right";
    if (key === "Escape") return "Escape";
    if (key === "Delete") return "Delete";
    if (key === "Backspace") return "Backspace";
    if (key.length === 1) return key.toUpperCase();
    return key;
  }

  function captureHotkey(event, fieldKey) {
    if (!capturing || activeHotkeyField !== fieldKey) return;
    if (event.key === "Tab") return;
    event.preventDefault();
    event.stopPropagation();

    if (event.key === "Escape") {
      closeSettings();
      return;
    }
    if (event.key === "Backspace" && !event.ctrlKey && !event.altKey && !event.shiftKey && !event.metaKey) {
      hotkeyDrafts[fieldKey] = "";
      renderHotkeyField($(hotkeyFieldId(fieldKey)), "");
      return;
    }

    const mods = [];
    if (event.ctrlKey) mods.push("Ctrl");
    if (event.altKey) mods.push("Alt");
    if (event.shiftKey) mods.push("Shift");
    if (event.metaKey) mods.push("Win");

    const key = keyLabel(event);
    if (["Control", "Alt", "Shift", "Meta"].includes(key)) {
      hotkeyDrafts[fieldKey] = mods.join("+");
      renderHotkeyField($(hotkeyFieldId(fieldKey)), hotkeyDrafts[fieldKey]);
      return;
    }

    if (!mods.length) {
      setHotkeyNote("快捷键必须至少包含一个修饰键（Ctrl/Alt/Shift/Win）。", true);
      return;
    }
    if (key === "F4" && event.altKey && !event.ctrlKey) {
      setHotkeyNote("Alt+F4 是系统关闭快捷键，不能用于切换模式。", true);
      return;
    }

    hotkeyDrafts[fieldKey] = mods.concat(key).join("+");
    renderHotkeyField($(hotkeyFieldId(fieldKey)), hotkeyDrafts[fieldKey]);
    setHotkeyNote("松开按键后点击“保存设置”生效。", false);
  }

  function applyTheme(dark) {
    document.documentElement.dataset.theme = dark ? "dark" : "light";
  }

  function initTheme() {
    const mq = window.matchMedia ? window.matchMedia("(prefers-color-scheme: dark)") : null;
    applyTheme(mq ? mq.matches : false);
    if (mq && mq.addEventListener) {
      mq.addEventListener("change", (event) => applyTheme(event.matches));
    }
    if (appWindow && appWindow.theme) {
      appWindow.theme().then((theme) => {
        if (theme) applyTheme(theme === "dark");
      }).catch(() => {});
    }
    if (appWindow && appWindow.onThemeChanged) {
      appWindow.onThemeChanged((event) => {
        if (event && event.payload) applyTheme(event.payload === "dark");
      });
    }
  }

  function wireWindowDrag() {
    if (!appWindow || !appWindow.startDragging) return;
    const bar = document.querySelector(".titlebar");
    if (!bar) return;
    bar.addEventListener("mousedown", (event) => {
      if (event.button !== 0) return;
      if (event.target.closest("button, .window-actions")) return;
      event.preventDefault();
      const dragging = appWindow.startDragging();
      if (dragging && dragging.catch) {
        dragging.catch((error) => setHint("拖动失败：" + error, true));
      }
    });
  }

  function wireEvents() {
    $("btn-min").addEventListener("click", () => {
      if (appWindow) appWindow.minimize();
    });
    $("btn-close").addEventListener("click", () => {
      if (appWindow) appWindow.close();
    });
    wireWindowDrag();
    $("btn-settings").addEventListener("click", openSettings);

    tabs.forEach((tab) => {
      tab.addEventListener("click", () => showPage(tab.dataset.page));
    });

    cards.forEach((card) => {
      card.addEventListener("click", () => switchMode(Number(card.dataset.mode)));
    });

    $("autostart").addEventListener("click", () => {
      toggleAutostart($("autostart").getAttribute("aria-checked") !== "true");
    });

    const slider = $("fan-slider");
    slider.addEventListener("input", () => {
      const value = clampFanValue(slider.value);
      fanDraft = value;
      setSliderFill(value);
      $("fan-value").textContent = `${value}%`;
      $("fan-value").classList.remove("auto", "low");
      if (!fan.lowOverride && value < 35) $("fan-value").classList.add("low");
    });
    slider.addEventListener("change", () => applyFan(clampFanValue(slider.value)));

    $("fan-auto").addEventListener("click", () => applyFan(101));
    $("low-override").addEventListener("click", () => handleLowOverride(!fan.lowOverride));
    $("temp-protection").addEventListener("click", () => handleTempProtection(!fan.tempProtection));
    $("fan-gate-accept").addEventListener("click", acceptFanGate);
    $("warning-cancel").addEventListener("click", closeWarning);
    $("warning-confirm").addEventListener("click", confirmLowOverride);

    $("settings-close").addEventListener("click", closeSettings);
    $("settings-cancel").addEventListener("click", closeSettings);
    $("settings-save").addEventListener("click", saveSettings);

    $("hotkey-mode-toggle").addEventListener("click", () => {
      setHotkeyMode(hotkeyMode === "custom" ? "cycle" : "custom");
    });

    $("hotkey-reset").addEventListener("click", () => {
      if (hotkeyMode === "custom") {
        hotkeyDrafts.silent = "Ctrl+Alt+1";
        hotkeyDrafts.balanced = "Ctrl+Alt+2";
        hotkeyDrafts.performance = "Ctrl+Alt+3";
      } else {
        hotkeyDrafts.cycle = "Ctrl+Alt+M";
      }
      renderHotkeys();
      setHotkeyNote("已恢复默认组合键，点击“保存设置”生效。", false);
    });

    Object.keys(hotkeyDrafts).forEach((key) => {
      const field = $(hotkeyFieldId(key));
      if (!field) return;
      field.addEventListener("focus", async () => {
        activeHotkeyField = key;
        field.classList.add("capturing");
        if (!invoke) {
          capturing = true;
          return;
        }
        setHotkeyNote("正在准备快捷键录制…", false);
        try {
          await invoke("start_hotkey_capture");
          if (document.activeElement !== field) {
            invoke("stop_hotkey_capture").catch(() => {});
            return;
          }
          capturing = true;
          setHotkeyNote("请按下组合键（Esc 取消）。", false);
        } catch (error) {
          capturing = false;
          field.classList.remove("capturing");
          setHotkeyNote(`无法开始录制：${String(error)}`, true);
        }
      });
      field.addEventListener("blur", () => {
        capturing = false;
        activeHotkeyField = null;
        field.classList.remove("capturing");
        if (invoke) invoke("stop_hotkey_capture").catch(() => {});
      });
      field.addEventListener("keydown", (event) => captureHotkey(event, key));
    });

    $("settings-overlay").addEventListener("mousedown", (event) => {
      if (event.target === $("settings-overlay")) closeSettings();
    });

    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !$("settings-overlay").hidden) {
        closeSettings();
      }
    });
  }

  async function init() {
    initTheme();
    wireEvents();
    render();

    if (!invoke) {
      status = {
        connected: false,
        error: "Tauri 接口未加载",
        mode: -1,
        temperature: 0,
        fanRpm: 0,
        machineLoaded: true
      };
      render();
      return;
    }

    try {
      info = await invoke("get_app_info");
      settings = await invoke("get_settings");
      autostart = await invoke("get_autostart");
    } catch (error) {
      setHint(String(error), true);
    }

    if (info.lite) {
      $("btn-settings").style.display = "none";
    }
    if (info.lite || !info.autostartAllowed) {
      $("autostart").style.display = "none";
    }

    fan.lowOverride = !!settings.fanLowOverride;
    fan.tempProtection = settings.tempProtection !== false;

    await refreshStatus();
    lastNotifiedMode = status.mode;
    setInterval(refreshStatus, 2500);
    document.addEventListener("visibilitychange", () => {
      if (!document.hidden) refreshStatus();
    });

    if (listen) {
      listen("hotkey-capture-done", (event) => {
        const combo = String(event.payload || "");
        if (!activeHotkeyField || !combo) return;
        hotkeyDrafts[activeHotkeyField] = canonicalHotkey(combo);
        renderHotkeyField($(hotkeyFieldId(activeHotkeyField)), hotkeyDrafts[activeHotkeyField]);
        setHotkeyNote(`已录入 ${formatHotkey(combo)}，点击“保存设置”生效。`, false);
        capturing = false;
        if (invoke) invoke("stop_hotkey_capture").catch(() => {});
      });

      listen("hotkey-capture-preview", (event) => {
        if (!activeHotkeyField) return;
        const preview = String(event.payload || "");
        if (preview) setHotkeyNote(`当前按键：${formatHotkey(preview)} + …`, false);
      });

      listen("hotkey-capture-cancel", () => {
        if (!activeHotkeyField) return;
        setHotkeyNote("已取消录入，请重新点击输入框。", false);
      });

      listen("hotkey-capture-error", (event) => {
        if (!activeHotkeyField) return;
        capturing = false;
        const field = $(hotkeyFieldId(activeHotkeyField));
        if (field) field.classList.remove("capturing");
        setHotkeyNote(`快捷键录制出错：${String(event.payload || "未知错误")}`, true);
      });

      listen("mode-busy", () => {
        if (!status.connected) return;
        switching = true;
        setBusy(true, "正在切换模式…");
        setHint("正在切换模式…");
      });

      listen("mode-pending", (event) => {
        const mode = Number(event.payload);
        if (!status.connected) return;
        switching = true;
        status = { ...status, mode };
        render();
        setBusy(true, `正在切换到“${modeNames[mode] || "指定模式"}”…`);
        setHint("正在切换模式…");
      });

      listen("status-changed", (event) => {
        const next = event.payload;
        if (!switching && next.connected && next.mode !== lastNotifiedMode) {
          setHint(`已切换到“${modeNames[next.mode] || "指定模式"}”`);
        }
        lastNotifiedMode = next.mode;
        next.machineLoaded = true;
        status = next;
        switching = false;
        setBusy(false);
        render();
      });

      listen("fan-changed", (event) => {
        fan = event.payload;
        renderFan();
      });

      listen("fan-protection", (event) => {
        setHint(String(event.payload), true);
        refreshFanState();
      });

      listen("settings-changed", (event) => {
        settings = event.payload;
        renderHint();
      });

      listen("autostart-changed", (event) => {
        autostart = !!event.payload;
        renderStatus();
      });

      listen("open-settings", openSettings);

      listen("operation-error", (event) => {
        switching = false;
        fanOperation = false;
        setBusy(false);
        render();
        setHint(String(event.payload), true);
      });
    }
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();

