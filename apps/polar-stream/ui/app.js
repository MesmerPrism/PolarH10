(() => {
  "use strict";

  const nativeCore = window.__TAURI__?.core;
  const isNative = Boolean(nativeCore?.invoke && nativeCore?.Channel);
  const invoke = nativeCore?.invoke?.bind(nativeCore);
  const NativeChannel = nativeCore?.Channel;
  const preferences = window.PolarPreferences;
  const previewFixtureApi = window.PolarPreviewFixture;

  const fallbackCatalog = [
    { id: "raw_ecg", streamSuffix: "rawECG", label: "Raw ECG", detail: "130 Hz · 1 channel", unit: "µV", raw: true, family: "ecg", formula: "ecg", customExpression: "ecg", formulaSource: "ecg" },
    { id: "heart_rate", streamSuffix: "heartRate", label: "Heart rate", detail: "Device-derived", unit: "bpm", raw: false, family: "ecg", formula: "hr", customExpression: "hr", formulaSource: "heartRate" },
    { id: "rr_interval", streamSuffix: "rrInterval", label: "RR interval", detail: "Beat-to-beat interval", unit: "ms", raw: false, family: "ecg", formula: "rr", customExpression: "rr", formulaSource: "rrInterval" },
    { id: "rmssd", streamSuffix: "rmssd", label: "RMSSD", detail: "Rolling 60-beat window", unit: "ms", raw: false, family: "ecg", formula: "rmssd(rr, 60)", customExpression: "rmssd(rr, 60)", formulaSource: "rrInterval" },
    { id: "raw_acc", streamSuffix: "rawACC", label: "Raw accelerometer", detail: "200 Hz · X, Y, Z", unit: "mg", raw: true, family: "acc", formula: "channels(x, y, z)", customExpression: "", formulaSource: "accelerometer" },
    { id: "acc_magnitude", streamSuffix: "accMagnitude", label: "3D acceleration magnitude", detail: "Device motion · √(x² + y² + z²)", unit: "g", raw: false, family: "acc", formula: "sqrt(x*x + y*y + z*z) / 1000", customExpression: "sqrt(x*x + y*y + z*z) / 1000", formulaSource: "accelerometer" },
    { id: "acc_breathing_magnitude", streamSuffix: "accBreathingMagnitude", label: "Breathing magnitude estimate", detail: "Continuous tunable ACC projection", unit: "normalized / g", raw: false, family: "acc", experimental: true, formula: "breathing_magnitude(x, y, z, true, false, true, 0.75, true, false)", customExpression: "breathing_magnitude(x, y, z, true, false, true, 0.75, true, false)", formulaSource: "accelerometer" },
    { id: "acc_breathing_phase", streamSuffix: "accBreathingPhase", label: "Breathing phase classifier", detail: "Three states · inhale, pause, exhale", unit: "state", raw: false, family: "acc", experimental: true, formula: "breathing_phase(x, y, z, true, false, true, 0.75, 0.60, false)", customExpression: "breathing_phase(x, y, z, true, false, true, 0.75, 0.60, false)", formulaSource: "accelerometer" },
  ];

  const defaultBreathingConfig = {
    axes: ["x", "z"],
    smoothingWindowSeconds: 0.75,
    sensitivity: 0.6,
    normalize: true,
    invert: false,
  };
  const breathingOutputIds = ["acc_breathing_magnitude", "acc_breathing_phase"];
  const sourceDetails = {
    ecg: { label: "ECG · 130 Hz", variables: "ecg", rate: 130, family: "ecg", color: "#d85151" },
    accelerometer: { label: "Accelerometer · 200 Hz", variables: "x, y, z", rate: 200, family: "acc", color: "#3b78aa" },
    heartRate: { label: "Heart rate · event rate", variables: "hr", rate: 1, family: "ecg", color: "#a65757" },
    rrInterval: { label: "RR interval · beat rate", variables: "rr", rate: 2, family: "ecg", color: "#6c62a8" },
  };

  const visualDefinitions = {
    raw_ecg: { label: "Raw ECG", unit: "µV", rate: 130, color: "#d85151", symmetric: true },
    raw_acc: {
      label: "Raw accelerometer · X/Y/Z",
      unit: "mg",
      rate: 200,
      channels: [
        { buffer: "acc_x", label: "X", color: "#3b78aa", symmetric: true },
        { buffer: "acc_y", label: "Y", color: "#168259", symmetric: true },
        { buffer: "acc_z", label: "Z", color: "#a66d19", symmetric: true },
      ],
    },
    heart_rate: { label: "Heart rate", unit: "bpm", rate: 1, color: "#d85151" },
    rr_interval: { label: "RR interval", unit: "ms", rate: 2, color: "#6c62a8" },
    acc_magnitude: { label: "3D acceleration magnitude", unit: "g", rate: 200, color: "#3b78aa" },
    acc_breathing_waveform: { parent: "acc_breathing_magnitude", label: "Breathing magnitude · curve", unit: "", rate: 200, color: "#3b78aa" },
    acc_breathing_circle: { parent: "acc_breathing_phase", label: "Breathing phase · circle", unit: "", rate: 60, color: "#3b78aa", kind: "breathing-circle" },
    rmssd: { label: "RMSSD", unit: "ms", rate: 1, color: "#168259" },
  };

  class RingBuffer {
    constructor(capacity = 4096) {
      this.values = new Float32Array(capacity);
      this.capacity = capacity;
      this.length = 0;
      this.cursor = 0;
    }

    push(value) {
      if (!Number.isFinite(value)) return;
      this.values[this.cursor] = value;
      this.cursor = (this.cursor + 1) % this.capacity;
      this.length = Math.min(this.length + 1, this.capacity);
    }

    pushMany(values) {
      for (const value of values) this.push(Number(value));
    }

    tail(count) {
      const size = Math.min(this.length, count);
      const result = new Float32Array(size);
      const start = (this.cursor - size + this.capacity) % this.capacity;
      const first = Math.min(size, this.capacity - start);
      result.set(this.values.subarray(start, start + first));
      if (first < size) result.set(this.values.subarray(0, size - first), first);
      return result;
    }

    latest() {
      return this.length ? this.values[(this.cursor - 1 + this.capacity) % this.capacity] : null;
    }
  }

  class BrowserBreathingClassifier {
    constructor(settings) {
      this.settings = normalizeBreathingConfig(settings);
      this.baseline = [null, null, null];
      this.smoothing = [];
      this.bounds = [];
      this.previous = null;
    }

    process(sample) {
      const values = [
        Number(sample.xMg ?? sample.x_mg ?? 0) / 1000,
        Number(sample.yMg ?? sample.y_mg ?? 0) / 1000,
        Number(sample.zMg ?? sample.z_mg ?? 0) / 1000,
      ];
      const enabled = ["x", "y", "z"].map((axis) => this.settings.axes.includes(axis));
      let projection = 0;
      let count = 0;
      for (let index = 0; index < 3; index += 1) {
        if (!enabled[index]) continue;
        if (this.baseline[index] == null) this.baseline[index] = values[index];
        this.baseline[index] += (values[index] - this.baseline[index]) * 0.001;
        projection += values[index] - this.baseline[index];
        count += 1;
      }
      projection /= Math.max(1, count);
      if (this.settings.invert) projection *= -1;
      const windowSize = Math.max(1, Math.round(this.settings.smoothingWindowSeconds * 200));
      this.smoothing.push(projection);
      if (this.smoothing.length > windowSize) this.smoothing.shift();
      const smoothed = this.smoothing.reduce((sum, value) => sum + value, 0) / this.smoothing.length;
      this.bounds.push(smoothed);
      if (this.bounds.length > 4000) this.bounds.shift();
      let min = Math.min(...this.bounds);
      let max = Math.max(...this.bounds);
      const ready = this.bounds.length >= 200 && max - min >= 0.0005;
      if (!ready) { min = -1; max = 1; }
      const normalized = ready ? Math.min(1, Math.max(0, (smoothed - min) / (max - min))) : 0.5;
      const threshold = 0.00015 + (1 - this.settings.sensitivity) * 0.00235;
      const delta = this.previous == null ? 0 : normalized - this.previous;
      const phase = !ready ? 0 : delta > threshold ? 1 : delta < -threshold ? -1 : 0;
      this.previous = normalized;
      return { waveform: this.settings.normalize ? normalized : smoothed, normalized01: normalized, phase };
    }
  }

  const bufferIds = new Set(Object.entries(visualDefinitions).flatMap(([id, definition]) => (
    definition.channels?.map((channel) => channel.buffer) || [id]
  )));
  const buffers = Object.fromEntries([...bufferIds].map((id) => [id, new RingBuffer()]));
  const visualizerChannel = typeof BroadcastChannel === "function"
    ? new BroadcastChannel("polar-stream-visualizers")
    : null;
  const mainWindowId = `main-${Math.random().toString(36).slice(2, 10)}`;
  let visualizerCardTemplate = null;
  const elements = {};
  const ids = [
    "app-state-dot", "app-state-text", "platform-label", "input-state", "connection-card",
    "device-name", "connection-detail", "disconnect-button", "connection-meta", "battery-value",
    "latency-metric", "latency-value",
    "scan-button", "scan-caption", "device-list", "activity-list", "output-state", "raw-ecg-value",
    "raw-acc-x", "raw-acc-y", "raw-acc-z", "ecg-spark", "stream-name", "lsl-toggle", "osc-toggle",
    "lsl-detail", "osc-detail", "included-count", "output-chips", "open-output-dialog", "visual-source",
    "visual-current", "visual-unit", "render-rate", "chart-shell", "signal-canvas",
    "chart-empty", "y-max", "y-min", "footer-status", "sample-counter", "output-dialog",
    "add-visualizer", "reset-workspace-layout", "visualizer-deck", "visualizer-primary", "visualizer-empty-deck",
    "metric-options", "dialog-selection-count", "dialog-selection-detail", "toast-region", "stream-name-preview",
    "show-ecg-metrics", "show-acc-metrics", "metric-family-context", "metric-family-title",
    "metric-family-description", "metric-search", "metric-result-count", "metric-empty-search",
    "breathing-config", "breathing-axis-x", "breathing-axis-y", "breathing-axis-z",
    "breathing-axis-error", "breathing-smoothing", "breathing-smoothing-value",
    "breathing-sensitivity", "breathing-sensitivity-value", "breathing-normalize", "breathing-invert",
    "profile-select", "save-profile-button", "delete-profile-button", "formula-builder", "formula-boxes",
    "formula-empty", "add-custom-formula",
  ];
  for (const id of ids) elements[id] = document.getElementById(id);

  const app = {
    connected: false,
    connecting: false,
    scanning: false,
    configuring: false,
    catalog: fallbackCatalog,
    outputs: new Set(["raw_ecg", "raw_acc"]),
    streamName: "Polar-H10",
    selectedVisual: "raw_ecg",
    visualizers: new Map(),
    visualizerSequence: 0,
    draggedVisualizerId: null,
    metricFamily: "ecg",
    breathingConfig: { ...defaultBreathingConfig, axes: [...defaultBreathingConfig.axes] },
    browserBreathing: null,
    breathingPhase: 0,
    customFormulas: [],
    formulaDrafts: null,
    formulaValidation: new Map(),
    formulaValidationTimers: new Map(),
    formulaFaultsShown: new Set(),
    profileSummaries: [],
    pendingWorkspace: null,
    sessionSaveTimer: null,
    sampleCount: 0,
    previewRecording: null,
    previewRecordingError: null,
    previewPlayer: null,
    outputSequence: 0,
    connectionGeneration: 0,
    currentDeviceId: null,
    attMtu: null,
    pendingDevice: null,
    devices: [],
    preferences: preferences.load(),
    activity: [{ time: "NOW", message: "Bluetooth interface ready" }],
  };

  function normalizeStreamBase(value) {
    if (typeof value !== "string") return null;
    let normalized = "";
    let separatorPending = false;
    for (const character of String(value).trim()) {
      if (/[A-Za-z0-9]/.test(character)) {
        if (separatorPending && normalized && !normalized.endsWith("_") && !normalized.endsWith("-")) {
          normalized += "_";
        }
        normalized += character;
        separatorPending = false;
      } else if (character === "-") {
        normalized += character;
        separatorPending = false;
      } else {
        separatorPending = true;
      }
    }
    normalized = normalized.replace(/^[_-]+|[_-]+$/g, "");
    return normalized && normalized.length <= 64 ? normalized : null;
  }

  function streamOutputName(metric, value = app.streamName) {
    const base = normalizeStreamBase(value);
    const suffix = metric.streamSuffix
      || fallbackCatalog.find((candidate) => candidate.id === metric.id)?.streamSuffix
      || metric.id;
    return base ? `${base}_${suffix}` : `—_${suffix}`;
  }

  function normalizeBreathingConfig(value = {}) {
    const requestedAxes = Array.isArray(value?.axes)
      ? value.axes.map((axis) => String(axis).toLowerCase()).filter((axis) => ["x", "y", "z"].includes(axis))
      : [...defaultBreathingConfig.axes];
    const axes = [...new Set(requestedAxes)];
    const smoothing = Number(value?.smoothingWindowSeconds);
    const sensitivity = Number(value?.sensitivity);
    return {
      axes: axes.length >= 2 ? axes : [...defaultBreathingConfig.axes],
      smoothingWindowSeconds: Number.isFinite(smoothing) ? Math.min(3, Math.max(0.2, smoothing)) : defaultBreathingConfig.smoothingWindowSeconds,
      sensitivity: Number.isFinite(sensitivity) ? Math.min(1, Math.max(0, sensitivity)) : defaultBreathingConfig.sensitivity,
      normalize: value?.normalize == null ? defaultBreathingConfig.normalize : Boolean(value.normalize),
      invert: Boolean(value?.invert),
    };
  }

  function newFormulaId() {
    if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
    const bytes = new Uint8Array(16);
    if (globalThis.crypto?.getRandomValues) globalThis.crypto.getRandomValues(bytes);
    else for (let index = 0; index < bytes.length; index += 1) bytes[index] = Math.floor(Math.random() * 256);
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    return [...bytes].map((value, index) => `${[4, 6, 8, 10].includes(index) ? "-" : ""}${value.toString(16).padStart(2, "0")}`).join("");
  }

  function normalizeFormulaDraft(value = {}) {
    const source = sourceDetails[value.source] ? value.source : "ecg";
    return {
      id: String(value.id || newFormulaId()),
      name: String(value.name || "Processed_signal"),
      source,
      expression: String(value.expression || ""),
      unit: String(value.unit || (source === "ecg" ? "µV" : source === "accelerometer" ? "mg" : source === "heartRate" ? "bpm" : "ms")),
      enabled: value.enabled !== false,
    };
  }

  function customVisualId(id) {
    return `custom:${id}`;
  }

  function customStreamName(formula, value = app.streamName) {
    const base = normalizeStreamBase(value);
    let suffix = "";
    let separatorPending = false;
    for (const character of formula.name.trim()) {
      if (/[A-Za-z0-9_-]/.test(character)) {
        if (separatorPending && suffix && !suffix.endsWith("_") && !suffix.endsWith("-")) suffix += "_";
        suffix += character;
        separatorPending = false;
      } else {
        separatorPending = true;
      }
    }
    suffix = suffix.replace(/^[_-]+|[_-]+$/g, "") || "invalid";
    return `${base || "—"}_${suffix}`;
  }

  function syncCustomVisualDefinitions() {
    for (const id of Object.keys(visualDefinitions)) {
      if (id.startsWith("custom:")) delete visualDefinitions[id];
    }
    for (const formula of app.customFormulas.filter((item) => item.enabled)) {
      const id = customVisualId(formula.id);
      const source = sourceDetails[formula.source] || sourceDetails.ecg;
      visualDefinitions[id] = {
        label: formula.name || "Custom formula",
        unit: formula.unit,
        rate: source.rate,
        color: source.color,
        symmetric: formula.source === "ecg",
        formulaId: formula.id,
      };
      if (!buffers[id]) buffers[id] = new RingBuffer();
    }
  }

  function setTopStatus(message, state = "idle") {
    elements["app-state-text"].textContent = message;
    elements["app-state-dot"].className = `state-dot${state === "idle" ? "" : ` ${state}`}`;
    elements["footer-status"].textContent = message;
  }

  function addActivity(message) {
    const time = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    app.activity.unshift({ time, message });
    app.activity.length = Math.min(app.activity.length, 3);
    elements["activity-list"].replaceChildren(...app.activity.map((item) => {
      const row = document.createElement("li");
      const stamp = document.createElement("time");
      const text = document.createElement("span");
      stamp.textContent = item.time;
      text.textContent = item.message;
      row.append(stamp, text);
      return row;
    }));
  }

  function toast(message, error = false) {
    const node = document.createElement("div");
    node.className = `toast${error ? " error" : ""}`;
    node.textContent = message;
    elements["toast-region"].append(node);
    window.setTimeout(() => node.remove(), 4200);
  }

  async function initialize() {
    let bootstrap = {
      config: {
        streamName: "Polar-H10", lslEnabled: false, oscEnabled: false,
        outputs: ["raw_ecg", "raw_acc"], breathingConfig: defaultBreathingConfig, customFormulas: [],
      },
      platform: "browser preview",
      metricCatalog: fallbackCatalog,
      lastSession: null,
      profiles: [],
    };
    if (isNative) {
      try {
        bootstrap = await invoke("get_bootstrap");
      } catch (error) {
        toast(String(error), true);
      }
    }

    app.catalog = bootstrap.metricCatalog || fallbackCatalog;
    app.outputs = new Set(bootstrap.config?.outputs || ["raw_ecg", "raw_acc"]);
    app.breathingConfig = normalizeBreathingConfig(bootstrap.config?.breathingConfig);
    app.customFormulas = (bootstrap.config?.customFormulas || []).map(normalizeFormulaDraft);
    app.profileSummaries = bootstrap.profiles || [];
    app.pendingWorkspace = bootstrap.lastSession?.workspace || null;
    if (bootstrap.lastSession?.preferredSensor) {
      app.preferences = preferences.saveLastDevice(bootstrap.lastSession.preferredSensor);
    }
    app.streamName = normalizeStreamBase(bootstrap.lastSession?.outputConfig?.streamName)
      || normalizeStreamBase(app.preferences.streamName)
      || normalizeStreamBase(bootstrap.config?.streamName)
      || "Polar-H10";
    elements["stream-name"].value = app.streamName;
    elements["lsl-toggle"].checked = Boolean(bootstrap.config?.lslEnabled);
    elements["osc-toggle"].checked = Boolean(bootstrap.config?.oscEnabled);
    elements["platform-label"].textContent = String(bootstrap.platform || "local").toUpperCase();
    if (!isNative) {
      prepareMockDataControl();
      try {
        await loadPreviewRecording();
        const seconds = app.previewRecording.durationMs / 1000;
        elements["platform-label"].textContent = "RECORDED PREVIEW";
        addActivity(`Real Polar H10 preview fixture ready · ${seconds} second loop`);
      } catch (error) {
        app.previewRecordingError = String(error);
        addActivity(app.previewRecordingError);
      }
      updateMockDataControl();
    }

    renderMetricOptions();
    renderFormulaBoxes();
    renderOutputs();
    renderProfileOptions();
    installInteractions();
    await configureOutputs({ quiet: true });
    resizeCanvas();
    window.requestAnimationFrame(drawFrame);
    if (isNative && app.preferences.lastDevice) void scanDevices({ automatic: true });
  }

  function installInteractions() {
    elements["scan-button"].addEventListener("click", () => {
      if (isNative) void scanDevices();
      else void activateMockData();
    });
    elements["disconnect-button"].addEventListener("click", disconnectDevice);
    elements["lsl-toggle"].addEventListener("change", configureOutputs);
    elements["osc-toggle"].addEventListener("change", configureOutputs);
    elements["add-custom-formula"].addEventListener("click", () => addCustomFormula({ source: app.metricFamily === "acc" ? "accelerometer" : "ecg" }));
    elements["save-profile-button"].addEventListener("click", saveNamedProfile);
    elements["profile-select"].addEventListener("change", loadSelectedProfile);
    elements["delete-profile-button"].addEventListener("click", deleteSelectedProfile);

    let nameTimer;
    elements["stream-name"].addEventListener("input", () => {
      app.streamName = elements["stream-name"].value;
      renderOutputs();
      window.clearTimeout(nameTimer);
      nameTimer = window.setTimeout(configureOutputs, 320);
    });

    elements["open-output-dialog"].addEventListener("click", () => {
      syncDialogSelection();
      elements["output-dialog"].showModal();
      window.setTimeout(() => elements["metric-search"].focus(), 0);
    });
    elements["output-dialog"].addEventListener("close", () => {
      if (elements["output-dialog"].returnValue !== "confirm") {
        app.formulaDrafts = null;
        return;
      }
      const selected = elements["metric-options"].querySelectorAll(".metric-checkbox:checked");
      app.outputs = new Set([...selected].map((input) => input.value));
      app.breathingConfig = readBreathingConfig();
      app.customFormulas = readFormulaCards();
      app.formulaDrafts = null;
      app.browserBreathing = null;
      syncCustomVisualDefinitions();
      renderOutputs();
      configureOutputs();
    });
    elements["output-dialog"].querySelector('button[value="confirm"]').addEventListener("click", async (event) => {
      event.preventDefault();
      if (!validateBreathingAxes()) return;
      if (!await validateAllFormulaCards()) return;
      elements["output-dialog"].close("confirm");
    });
    elements["show-ecg-metrics"].addEventListener("click", () => setMetricFamily("ecg"));
    elements["show-acc-metrics"].addEventListener("click", () => setMetricFamily("acc"));
    elements["metric-search"].addEventListener("input", filterMetricOptions);
    for (const axis of ["x", "y", "z"]) {
      elements[`breathing-axis-${axis}`].addEventListener("change", () => {
        validateBreathingAxes();
        updateDialogCount();
      });
    }
    elements["breathing-smoothing"].addEventListener("input", updateBreathingControlLabels);
    elements["breathing-sensitivity"].addEventListener("input", updateBreathingControlLabels);
    registerVisualizer(elements["visualizer-primary"], "primary", app.selectedVisual);
    if (app.pendingWorkspace) applyWorkspaceLayout(app.pendingWorkspace);
    else restoreVisualizerLayout();
    window.requestAnimationFrame(() => arrangeVisualizers(false));
    elements["add-visualizer"].addEventListener("click", () => addVisualizer());
    elements["visualizer-empty-deck"].addEventListener("click", () => addVisualizer());
    elements["reset-workspace-layout"].addEventListener("click", resetWorkspaceLayout);
    installWorkspaceSplitters();
    if (app.pendingWorkspace) applyWorkspaceFractions(app.pendingWorkspace.paneFractions || defaultWorkspaceFractions());
    installVisualizerDeckInteractions();
    visualizerChannel?.addEventListener("message", handleVisualizerChannelMessage);
  }

  async function scanDevices({ automatic = false } = {}) {
    if (app.scanning) return;
    app.scanning = true;
    elements["scan-button"].disabled = true;
    elements["scan-button"].classList.add("scanning");
    elements["scan-button"].querySelector("span").textContent = "Scanning…";
    elements["input-state"].textContent = "Scanning";
    setTopStatus("Scanning for Polar sensors", "working");
    addActivity(automatic ? "Looking for last used sensor" : "BLE scan started");

    try {
      const devices = isNative
        ? await invoke("scan_devices")
        : await previewDevices();
      app.devices = devices;
      renderDevices(devices);
      const count = devices.length;

      if (automatic && app.preferences.lastDevice) {
        const exact = devices.find((device) => device.id === app.preferences.lastDevice.id);
        const nameMatches = devices.filter((device) => device.name === app.preferences.lastDevice.name);
        const preferredDevice = exact || (nameMatches.length === 1 ? nameMatches[0] : null);
        if (preferredDevice) {
          addActivity(`Last used sensor found · ${preferredDevice.name}`);
          await connectDevice(preferredDevice, { automatic: true });
          return;
        }
        elements["input-state"].textContent = count ? `${count} found` : "None found";
        setTopStatus("Last used sensor unavailable · choose below");
        addActivity("Last used sensor was not found");
        return;
      }

      elements["input-state"].textContent = count ? `${count} found` : "None found";
      setTopStatus(app.connected ? "Sensor connected · choose another to switch" : count ? "Choose a sensor to connect" : "No Polar sensor found", app.connected ? "connected" : "idle");
      addActivity(count ? `${count} compatible sensor${count === 1 ? "" : "s"} found` : "Scan finished with no sensors");
    } catch (error) {
      const message = String(error);
      setTopStatus("Bluetooth scan failed", "error");
      elements["input-state"].textContent = "Error";
      addActivity(message);
      toast(message, true);
    } finally {
      app.scanning = false;
      elements["scan-button"].disabled = false;
      elements["scan-button"].classList.remove("scanning");
      elements["scan-button"].querySelector("span").textContent = "Scan again";
    }
  }

  function prepareMockDataControl() {
    elements["scan-button"].classList.add("mock-data-button");
    elements["scan-button"].querySelector("svg").hidden = true;
    elements["scan-button"].closest(".scan-row").classList.add("mock-data-row");
    elements["scan-caption"].hidden = true;
    elements["device-list"].hidden = true;
  }

  function updateMockDataControl() {
    if (isNative) return;
    elements["scan-button"].querySelector("span").textContent = app.connecting
      ? "Loading Mock Data…"
      : app.connected
        ? "Mock Data active"
        : "Mock Data";
    elements["scan-button"].disabled = app.connecting || app.connected;
  }

  async function activateMockData() {
    if (app.connecting || app.connected) return;
    try {
      const devices = await previewDevices();
      app.devices = devices;
      await connectDevice(devices[0]);
    } catch (error) {
      const message = String(error);
      app.previewRecordingError = message;
      setTopStatus("Mock Data unavailable", "error");
      elements["input-state"].textContent = "Error";
      addActivity(message);
      toast(message, true);
      updateMockDataControl();
    }
  }

  function renderDevices(devices) {
    if (!devices.length) {
      const empty = document.createElement("div");
      empty.className = "empty-state";
      const orbit = document.createElement("span");
      orbit.className = "empty-orbit";
      const message = document.createElement("p");
      message.textContent = "No Polar sensors found.";
      const hint = document.createElement("small");
      hint.textContent = "Check Bluetooth, wear the strap, then scan again.";
      empty.append(orbit, message, hint);
      elements["device-list"].replaceChildren(empty);
      return;
    }

    const rows = devices.map((device) => {
      const isCurrent = app.connected && app.currentDeviceId === device.id;
      const isPending = app.pendingDevice?.id === device.id;
      const isPreferred = app.preferences.lastDevice?.id === device.id;
      const button = document.createElement("button");
      button.className = `device-row${isCurrent ? " current" : ""}${isPreferred ? " preferred" : ""}`;
      button.type = "button";
      button.disabled = app.connecting || isCurrent;
      button.addEventListener("click", () => connectDevice(device));

      const icon = document.createElement("span");
      icon.className = "device-icon";
      icon.textContent = "H10";
      const copy = document.createElement("span");
      copy.className = "device-copy";
      const nameLine = document.createElement("span");
      nameLine.className = "device-name-line";
      const name = document.createElement("strong");
      name.textContent = device.name;
      nameLine.append(name);
      if (isPreferred) {
        const badge = document.createElement("span");
        badge.className = "preference-badge";
        badge.textContent = "LAST USED";
        nameLine.append(badge);
      }
      const id = document.createElement("small");
      id.textContent = device.id;
      copy.append(nameLine, id);
      const rssi = document.createElement("span");
      rssi.className = "rssi";
      rssi.textContent = isCurrent
        ? "Connected"
        : isPending
          ? "Connecting…"
          : device.rssi == null
            ? "Connect →"
            : `${device.rssi} dBm  →`;
      button.append(icon, copy, rssi);
      return button;
    });
    elements["device-list"].replaceChildren(...rows);
  }

  async function connectDevice(device, { automatic = false } = {}) {
    const generation = ++app.connectionGeneration;
    app.connecting = true;
    app.pendingDevice = device;
    renderDevices(app.devices);
    updateMockDataControl();
    setTopStatus(
      isNative
        ? automatic ? "Reconnecting to last used Polar H10" : "Connecting to Polar H10"
        : "Loading Mock Data",
      "working",
    );
    elements["input-state"].textContent = isNative ? "Connecting" : "Loading";
    elements["device-name"].textContent = device.name;
    elements["connection-detail"].textContent = isNative
      ? "Opening the low-energy connection…"
      : "Preparing the recorded preview loop…";
    addActivity(`${automatic ? "Reconnecting" : "Connecting"} to ${device.name}`);

    try {
      if (isNative) {
        const channel = new NativeChannel();
        channel.onmessage = (event) => {
          if (generation === app.connectionGeneration) handleNativeEvent(event, device);
        };
        await invoke("connect_device", { deviceId: device.id, events: channel });
      } else {
        await new Promise((resolve) => window.setTimeout(resolve, 450));
        if (!app.previewRecording) throw new Error(app.previewRecordingError || "The real preview recording is unavailable.");
        handleNativeEvent({
          kind: "connection", connected: true, streaming: true, deviceName: device.name,
          batteryPercent: null, attMtu: null,
          message: `Looping a real ${app.previewRecording.durationMs / 1000}-second ECG and accelerometer recording`,
        }, device);
        startPreviewRecording();
      }
    } catch (error) {
      if (generation !== app.connectionGeneration) return;
      app.connecting = false;
      app.pendingDevice = null;
      setTopStatus("Connection failed", "error");
      elements["input-state"].textContent = "Error";
      elements["connection-detail"].textContent = String(error);
      addActivity(String(error));
      toast(String(error), true);
      renderDevices(app.devices);
      updateMockDataControl();
    }
  }

  async function disconnectDevice() {
    const previousGeneration = app.connectionGeneration;
    app.connectionGeneration += 1;
    try {
      if (isNative) await invoke("disconnect_device");
      stopPreviewRecording();
      handleNativeEvent({
        kind: "connection", connected: false, streaming: false,
        deviceName: elements["device-name"].textContent, batteryPercent: null, message: "Disconnected",
      });
    } catch (error) {
      app.connectionGeneration = previousGeneration;
      toast(String(error), true);
    }
  }

  function handleNativeEvent(event, device = null) {
    switch (event.kind) {
      case "status":
        setTopStatus(event.message, "working");
        elements["connection-detail"].textContent = event.message;
        addActivity(event.message);
        break;
      case "connection":
        updateConnection(event, device);
        break;
      case "ecg":
        ingestEcg(event.microvolts || [], event);
        break;
      case "accelerometer":
        ingestAccelerometer(event.samples || [], event.breathingSamples || event.breathing_samples || []);
        break;
      case "metrics":
        ingestMetrics(event);
        break;
      case "error":
        addActivity(event.message);
        toast(event.message, true);
        break;
      default:
        break;
    }
    ingestFormulaBatch(event.formulas);
    broadcastVisualizerData(event);
  }

  function ingestFormulaBatch(batch) {
    if (!batch) return;
    for (const series of batch.series || []) {
      const id = customVisualId(series.formulaId ?? series.formula_id);
      if (!buffers[id]) buffers[id] = new RingBuffer();
      buffers[id].pushMany(series.values || []);
      const card = elements["formula-boxes"].querySelector(`[data-formula-id="${series.formulaId ?? series.formula_id}"]`);
      if (card && card.dataset.valid === "true") {
        const state = card.querySelector(".formula-status strong");
        if (state) state.textContent = series.state === "warmingUp" ? "Warming up" : series.state === "faulted" ? "Faulted" : "Live";
        card.classList.toggle("faulted", series.state === "faulted");
      }
    }
    for (const fault of batch.faults || []) {
      const id = fault.formulaId ?? fault.formula_id ?? "formula";
      const key = `${id}:${fault.code || fault.message}`;
      if (app.formulaFaultsShown.has(key)) continue;
      app.formulaFaultsShown.add(key);
      const formula = app.customFormulas.find((candidate) => candidate.id === id);
      toast(`${formula?.name || "Custom formula"} stopped: ${fault.message || "non-finite output"}`, true);
    }
  }

  function updateConnection(event, device = null) {
    app.connected = Boolean(event.connected);
    app.connecting = false;
    app.pendingDevice = null;
    app.attMtu = app.connected ? Number(event.attMtu ?? event.att_mtu) || null : null;
    if (app.connected) {
      const connectedDevice = device || app.devices.find((candidate) => candidate.name === event.deviceName);
      app.currentDeviceId = connectedDevice?.id || null;
      if (connectedDevice) {
        app.preferences = preferences.saveLastDevice(connectedDevice);
        scheduleLastSessionSave();
      }
    } else {
      app.currentDeviceId = null;
    }
    renderDevices(app.devices);
    updateMockDataControl();
    elements["connection-card"].classList.toggle("connected", app.connected);
    elements["disconnect-button"].hidden = !app.connected;
    elements["connection-meta"].hidden = !app.connected;
    elements["device-name"].textContent = app.connected ? event.deviceName : "No sensor connected";
    elements["connection-detail"].textContent = app.connected
      ? event.message
      : isNative ? "Scan for a nearby chest strap." : "Choose Mock Data to preview the interface.";
    elements["battery-value"].textContent = event.batteryPercent == null ? "—" : `${event.batteryPercent}%`;
    resetLatencyMetric();
    elements["input-state"].textContent = app.connected ? "Streaming" : "Idle";
    setTopStatus(
      app.connected ? isNative ? "Sensor connected · streams live" : "Mock Data active" : "Ready to connect",
      app.connected ? "connected" : "idle",
    );
    addActivity(app.connected ? `${event.deviceName} connected` : "Sensor disconnected");
  }

  function ingestEcg(values, event = {}) {
    buffers.raw_ecg.pushMany(values);
    app.sampleCount += values.length;
    const latest = buffers.raw_ecg.latest();
    elements["raw-ecg-value"].textContent = formatValue(latest, 0);
    updateSparkline();
    updateSampleCounter();
    updateLatencyMetric(event.estimatedLatencyMs ?? event.estimated_latency_ms, event.samplesPerPacket ?? event.samples_per_packet ?? values.length);
  }

  function resetLatencyMetric() {
    elements["latency-metric"].className = "latency-metric";
    elements["latency-value"].textContent = app.connected ? "Measuring…" : "—";
    elements["latency-metric"].title = app.connected
      ? `Waiting for ECG packets${app.attMtu ? ` · negotiated ATT MTU ${app.attMtu} bytes` : ""}.`
      : "Measured after ECG packets begin arriving.";
  }

  function updateLatencyMetric(value, samplesPerPacket) {
    if (!app.connected) return;
    const latencyMs = Number(value);
    if (!Number.isFinite(latencyMs)) return;
    const rounded = Math.max(0, Math.round(latencyMs / 10) * 10);
    const level = rounded <= 150 ? "low" : rounded <= 300 ? "moderate" : "high";
    elements["latency-metric"].className = `latency-metric ${level}`;
    elements["latency-value"].textContent = `≈${rounded} ms`;
    const packetDetail = Number.isFinite(Number(samplesPerPacket))
      ? `${Number(samplesPerPacket)} ECG samples per packet at 130 Hz`
      : "ECG packet size and host arrival cadence";
    const mtuDetail = app.attMtu ? ` · negotiated ATT MTU ${app.attMtu} bytes` : "";
    elements["latency-metric"].title = `Host estimate from ${packetDetail}${mtuDetail}. Additional fixed radio or OS delay may remain.`;
  }

  function ingestAccelerometer(samples, breathingSamples = []) {
    if (!samples.length) return;
    for (const sample of samples) {
      const x = Number(sample.xMg ?? sample.x_mg ?? 0);
      const y = Number(sample.yMg ?? sample.y_mg ?? 0);
      const z = Number(sample.zMg ?? sample.z_mg ?? 0);
      buffers.acc_x.push(x);
      buffers.acc_y.push(y);
      buffers.acc_z.push(z);
      buffers.acc_magnitude.push(Math.hypot(x, y, z) / 1000);
    }
    if (!breathingSamples.length && !isNative && breathingOutputIds.some((id) => app.outputs.has(id))) {
      if (!app.browserBreathing) app.browserBreathing = new BrowserBreathingClassifier(app.breathingConfig);
      breathingSamples = samples.map((sample) => app.browserBreathing.process(sample));
    }
    for (const sample of breathingSamples) {
      buffers.acc_breathing_waveform.push(Number(sample.waveform));
      const phase = Number(sample.phase);
      if (Number.isFinite(phase)) app.breathingPhase = Math.sign(phase);
    }
    const last = samples[samples.length - 1];
    elements["raw-acc-x"].textContent = formatValue(last.xMg ?? last.x_mg, 0);
    elements["raw-acc-y"].textContent = formatValue(last.yMg ?? last.y_mg, 0);
    elements["raw-acc-z"].textContent = formatValue(last.zMg ?? last.z_mg, 0);
    app.sampleCount += samples.length;
    updateSampleCounter();
  }

  function ingestMetrics(event) {
    buffers.heart_rate.push(event.heartRateBpm ?? event.heart_rate_bpm);
    buffers.rr_interval.pushMany(event.rrIntervalsMs ?? event.rr_intervals_ms ?? []);
    const rmssd = event.rmssdMs ?? event.rmssd_ms;
    if (rmssd != null) buffers.rmssd.push(rmssd);
  }

  function updateSampleCounter() {
    elements["sample-counter"].textContent = `${app.sampleCount.toLocaleString()} samples`;
  }

  function renderMetricOptions() {
    const options = app.catalog.map((metric) => {
      const label = document.createElement("label");
      const family = metric.family || (metric.id.includes("acc") ? "acc" : "ecg");
      label.className = `metric-option ${family}`;
      label.dataset.family = family;
      label.dataset.search = `${metric.label} ${metric.detail} ${metric.unit} ${metric.streamSuffix}`.toLowerCase();
      const mark = document.createElement("span");
      mark.className = "metric-option-mark";
      mark.textContent = metric.raw ? "RAW" : family.toUpperCase();
      const copy = document.createElement("span");
      copy.className = "metric-option-copy";
      const nameLine = document.createElement("span");
      nameLine.className = "metric-name-line";
      const name = document.createElement("strong");
      name.textContent = metric.label;
      nameLine.append(name);
      if (metric.experimental) {
        const badge = document.createElement("span");
        badge.className = "experimental-badge";
        badge.textContent = "Experimental";
        nameLine.append(badge);
      }
      const detail = document.createElement("small");
      detail.textContent = `${metric.detail} · ${metric.unit} · _${metric.streamSuffix}`;
      copy.append(nameLine, detail);
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.className = "metric-checkbox";
      checkbox.value = metric.id;
      checkbox.addEventListener("change", () => {
        updateDialogCount();
        updateBreathingConfigVisibility();
      });
      const formulaRow = document.createElement("span");
      formulaRow.className = "metric-formula";
      const formulaCode = document.createElement("code");
      formulaCode.textContent = metric.formula || metric.customExpression || "—";
      formulaCode.title = metric.formula || "";
      const useFormula = document.createElement("button");
      useFormula.type = "button";
      useFormula.textContent = metric.customExpression === "" ? "Start custom" : "Use as custom";
      useFormula.addEventListener("click", (event) => {
        event.preventDefault();
        event.stopPropagation();
        addCustomFormula({
          name: metric.id === "raw_acc" ? "Processed_ACC" : `${metric.label}_custom`,
          source: metric.formulaSource || (family === "acc" ? "accelerometer" : "ecg"),
          expression: metric.customExpression ?? metric.formula ?? "",
          unit: metric.unit,
        });
      });
      formulaRow.append(formulaCode, useFormula);
      label.append(mark, copy, checkbox, formulaRow);
      return label;
    });
    elements["metric-options"].replaceChildren(...options);
    setMetricFamily(app.metricFamily);
  }

  function addCustomFormula(template = {}) {
    const current = elements["formula-boxes"].children.length ? readFormulaCards() : (app.formulaDrafts || app.customFormulas);
    if (current.length >= 32) {
      toast("At most 32 custom formulas may be configured.", true);
      return;
    }
    const source = template.source || (app.metricFamily === "acc" ? "accelerometer" : "ecg");
    const baseName = template.name || (source === "accelerometer" ? "Processed_ACC" : "Processed_ECG");
    const used = new Set(current.map((formula) => formula.name.toLowerCase()));
    let name = baseName;
    let copy = 2;
    while (used.has(name.toLowerCase())) name = `${baseName}_${copy++}`;
    app.formulaDrafts = [...current, normalizeFormulaDraft({ ...template, source, name })];
    renderFormulaBoxes();
    const card = elements["formula-boxes"].lastElementChild;
    card?.scrollIntoView({ behavior: "smooth", block: "nearest" });
    card?.querySelector('[data-field="name"]')?.focus();
    updateDialogCount();
  }

  function renderFormulaBoxes() {
    const formulas = app.formulaDrafts || app.customFormulas;
    const cards = formulas.map((formula) => createFormulaCard(normalizeFormulaDraft(formula)));
    elements["formula-boxes"].replaceChildren(...cards);
    elements["formula-empty"].hidden = cards.length > 0;
    for (const card of cards) void validateFormulaCard(card);
  }

  function createFormulaCard(formula) {
    const card = document.createElement("article");
    card.className = "formula-card";
    card.dataset.formulaId = formula.id;
    card.dataset.valid = String(!formula.enabled);

    const header = document.createElement("header");
    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.checked = formula.enabled;
    enabled.className = "formula-enabled";
    enabled.dataset.field = "enabled";
    enabled.setAttribute("aria-label", `Enable ${formula.name}`);
    const title = document.createElement("span");
    title.className = "formula-card-title";
    const titleName = document.createElement("strong");
    titleName.textContent = formula.name;
    const stream = document.createElement("small");
    stream.textContent = customStreamName(formula, elements["stream-name"].value);
    title.append(titleName, stream);
    const actions = document.createElement("span");
    actions.className = "formula-actions";
    const duplicate = document.createElement("button");
    duplicate.type = "button";
    duplicate.title = "Duplicate formula";
    duplicate.setAttribute("aria-label", `Duplicate ${formula.name}`);
    duplicate.textContent = "⧉";
    duplicate.addEventListener("click", () => {
      const current = readFormulaCards();
      const value = current.find((candidate) => candidate.id === formula.id);
      if (value) addCustomFormula({ ...value, id: newFormulaId(), name: `${value.name}_copy` });
    });
    const remove = document.createElement("button");
    remove.type = "button";
    remove.title = "Delete formula";
    remove.setAttribute("aria-label", `Delete ${formula.name}`);
    remove.textContent = "×";
    remove.addEventListener("click", () => {
      app.formulaDrafts = readFormulaCards().filter((candidate) => candidate.id !== formula.id);
      app.formulaValidation.delete(formula.id);
      renderFormulaBoxes();
      updateDialogCount();
    });
    actions.append(duplicate, remove);
    header.append(enabled, title, actions);

    const fields = document.createElement("div");
    fields.className = "formula-fields";
    const nameField = createFormulaField("Stream suffix", "input", "name", formula.name);
    nameField.control.maxLength = 48;
    const sourceField = createFormulaField("Source", "select", "source", formula.source);
    for (const [value, detail] of Object.entries(sourceDetails)) {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = detail.label;
      sourceField.control.append(option);
    }
    sourceField.control.value = formula.source;
    const unitField = createFormulaField("Unit", "input", "unit", formula.unit);
    unitField.control.maxLength = 24;
    const expressionField = createFormulaField("Expression", "textarea", "expression", formula.expression);
    expressionField.label.classList.add("expression");
    expressionField.control.maxLength = 2048;
    expressionField.control.spellcheck = false;
    fields.append(nameField.label, sourceField.label, unitField.label, expressionField.label);

    const status = document.createElement("div");
    status.className = "formula-status";
    const statusMessage = document.createElement("span");
    statusMessage.textContent = `Variables: ${sourceDetails[formula.source].variables}`;
    const statusState = document.createElement("strong");
    statusState.textContent = formula.enabled ? "Checking…" : "Draft disabled";
    status.append(statusMessage, statusState);
    card.append(header, fields, status);

    const schedule = () => {
      const value = readFormulaCard(card);
      titleName.textContent = value.name || "Unnamed formula";
      stream.textContent = customStreamName(value, elements["stream-name"].value);
      enabled.setAttribute("aria-label", `Enable ${value.name || "formula"}`);
      window.clearTimeout(app.formulaValidationTimers.get(value.id));
      app.formulaValidationTimers.set(value.id, window.setTimeout(() => void validateFormulaCard(card), 220));
      updateDialogCount();
    };
    for (const input of card.querySelectorAll("input, select, textarea")) input.addEventListener("input", schedule);
    enabled.addEventListener("change", schedule);
    sourceField.control.addEventListener("change", () => {
      statusMessage.textContent = `Variables: ${sourceDetails[sourceField.control.value].variables}`;
    });
    return card;
  }

  function createFormulaField(caption, kind, field, value) {
    const label = document.createElement("label");
    label.className = "formula-field";
    label.append(document.createTextNode(caption));
    const control = document.createElement(kind);
    control.dataset.field = field;
    if (kind !== "select") control.value = value;
    label.append(control);
    return { label, control };
  }

  function readFormulaCard(card) {
    const field = (name) => card.querySelector(`[data-field="${name}"]`);
    return {
      id: card.dataset.formulaId,
      name: field("name").value,
      source: field("source").value,
      expression: field("expression").value,
      unit: field("unit").value,
      enabled: field("enabled").checked,
    };
  }

  function readFormulaCards() {
    return [...elements["formula-boxes"].querySelectorAll(".formula-card")].map(readFormulaCard);
  }

  function formulaErrorMessage(error) {
    if (error && typeof error === "object") return error.message || error.error || JSON.stringify(error);
    const text = String(error || "Formula validation failed.");
    try {
      const parsed = JSON.parse(text);
      return parsed.message || text;
    } catch {
      return text;
    }
  }

  async function validateFormulaCard(card) {
    if (!card?.isConnected) return false;
    const validationSequence = String(Number(card.dataset.validationSequence || 0) + 1);
    card.dataset.validationSequence = validationSequence;
    const formula = readFormulaCard(card);
    const message = card.querySelector(".formula-status span");
    const state = card.querySelector(".formula-status strong");
    if (!formula.enabled) {
      card.dataset.valid = "true";
      card.classList.remove("invalid");
      message.textContent = `Variables: ${sourceDetails[formula.source].variables}`;
      state.textContent = "Draft disabled";
      return true;
    }
    state.textContent = "Checking…";
    try {
      let validation;
      if (isNative) {
        validation = await invoke("validate_custom_formula", { formula });
      } else {
        if (!formula.name.trim() || !formula.unit.trim() || !formula.expression.trim()) throw new Error("Name, unit, and expression are required.");
        if (formula.expression.length > 2048) throw new Error("Expression must be at most 2048 bytes.");
        validation = { normalized: formula, allowedVariables: sourceDetails[formula.source].variables.split(", "), stateSamples: 0 };
      }
      if (!card.isConnected || card.dataset.validationSequence !== validationSequence) return false;
      card.dataset.valid = "true";
      card.classList.remove("invalid");
      app.formulaValidation.set(formula.id, validation);
      const variables = validation.allowedVariables || sourceDetails[formula.source].variables.split(", ");
      const stateCost = Number(validation.stateSamples || 0);
      message.textContent = `Variables: ${variables.join(", ")}${stateCost ? ` · state: ${stateCost.toLocaleString()} samples` : ""}`;
      state.textContent = "Valid";
      return true;
    } catch (error) {
      if (!card.isConnected || card.dataset.validationSequence !== validationSequence) return false;
      card.dataset.valid = "false";
      card.classList.add("invalid");
      app.formulaValidation.delete(formula.id);
      message.textContent = formulaErrorMessage(error);
      state.textContent = "Fix formula";
      return false;
    }
  }

  async function validateAllFormulaCards() {
    const cards = [...elements["formula-boxes"].querySelectorAll(".formula-card")];
    const results = await Promise.all(cards.map(validateFormulaCard));
    const enabled = cards.map(readFormulaCard).filter((formula) => formula.enabled);
    const names = new Set();
    const builtInNames = new Set(app.catalog.map((metric) => String(metric.streamSuffix).toLowerCase()));
    let unique = true;
    let conflicts = false;
    for (const formula of enabled) {
      const normalized = app.formulaValidation.get(formula.id)?.normalized;
      const key = String(normalized?.name || formula.name).trim().toLowerCase();
      if (names.has(key)) unique = false;
      if (builtInNames.has(key)) conflicts = true;
      names.add(key);
    }
    if (!unique) toast("Enabled custom formula names must be unique.", true);
    if (conflicts) toast("A custom formula name conflicts with a built-in stream suffix.", true);
    const valid = results.every(Boolean) && unique && !conflicts;
    if (!valid) cards.find((card) => card.dataset.valid !== "true")?.scrollIntoView({ behavior: "smooth", block: "center" });
    return valid;
  }

  function syncDialogSelection() {
    elements["metric-options"].querySelectorAll(".metric-checkbox").forEach((input) => {
      input.checked = app.outputs.has(input.value);
    });
    syncBreathingControls();
    app.formulaDrafts = app.customFormulas.map((formula) => ({ ...formula }));
    renderFormulaBoxes();
    elements["metric-search"].value = "";
    filterMetricOptions();
    updateBreathingConfigVisibility();
    updateDialogCount();
  }

  function updateDialogCount() {
    const selected = [...elements["metric-options"].querySelectorAll(".metric-checkbox:checked")];
    const custom = elements["formula-boxes"].querySelectorAll('.formula-card [data-field="enabled"]:checked').length;
    const count = selected.length + custom;
    const ecgCount = selected.filter((input) => input.closest(".metric-option")?.dataset.family === "ecg").length;
    const accCount = count - ecgCount;
    elements["dialog-selection-count"].textContent = `${count} selected`;
    elements["dialog-selection-detail"].textContent = `${ecgCount} ECG · ${accCount} ACC · ${custom} custom`;
  }

  function setMetricFamily(family) {
    app.metricFamily = family === "acc" ? "acc" : "ecg";
    const isAcc = app.metricFamily === "acc";
    elements["show-ecg-metrics"].classList.toggle("active", !isAcc);
    elements["show-ecg-metrics"].setAttribute("aria-pressed", String(!isAcc));
    elements["show-acc-metrics"].classList.toggle("active", isAcc);
    elements["show-acc-metrics"].setAttribute("aria-pressed", String(isAcc));
    elements["metric-family-context"].className = `family-context ${isAcc ? "acc" : "ecg"}`;
    elements["metric-family-title"].textContent = isAcc
      ? "ACC breathing remains exploratory"
      : "ECG is the primary measurement family";
    elements["metric-family-description"].textContent = isAcc
      ? "Keep this set small and verify breathing outputs against a reference sensor."
      : "Start here for the H10's established electrical and beat-to-beat signals.";
    filterMetricOptions();
    updateBreathingConfigVisibility();
  }

  function filterMetricOptions() {
    const query = elements["metric-search"].value.trim().toLowerCase();
    let visible = 0;
    for (const option of elements["metric-options"].querySelectorAll(".metric-option")) {
      const matches = option.dataset.family === app.metricFamily && (!query || option.dataset.search.includes(query));
      option.hidden = !matches;
      if (matches) visible += 1;
    }
    elements["metric-result-count"].textContent = `${visible} ${app.metricFamily.toUpperCase()} metric${visible === 1 ? "" : "s"}`;
    elements["metric-empty-search"].hidden = visible !== 0;
    updateBreathingConfigVisibility();
  }

  function syncBreathingControls() {
    const config = normalizeBreathingConfig(app.breathingConfig);
    for (const axis of ["x", "y", "z"]) {
      elements[`breathing-axis-${axis}`].checked = config.axes.includes(axis);
    }
    elements["breathing-smoothing"].value = String(config.smoothingWindowSeconds);
    elements["breathing-sensitivity"].value = String(config.sensitivity);
    elements["breathing-normalize"].checked = config.normalize;
    elements["breathing-invert"].checked = config.invert;
    updateBreathingControlLabels();
    validateBreathingAxes();
  }

  function readBreathingConfig() {
    const axes = ["x", "y", "z"].filter((axis) => elements[`breathing-axis-${axis}`].checked);
    return normalizeBreathingConfig({
      axes,
      smoothingWindowSeconds: elements["breathing-smoothing"].value,
      sensitivity: elements["breathing-sensitivity"].value,
      normalize: elements["breathing-normalize"].checked,
      invert: elements["breathing-invert"].checked,
    });
  }

  function validateBreathingAxes() {
    const breathingSelected = breathingOutputIds.some((id) => (
      elements["metric-options"].querySelector(`input[value="${id}"]`)?.checked
    ));
    const axisCount = ["x", "y", "z"].filter((axis) => elements[`breathing-axis-${axis}`].checked).length;
    const invalid = breathingSelected && axisCount < 2;
    elements["breathing-axis-error"].closest("fieldset").classList.toggle("invalid", invalid);
    elements["output-dialog"].querySelector('button[value="confirm"]').disabled = invalid;
    return !invalid;
  }

  function updateBreathingControlLabels() {
    elements["breathing-smoothing-value"].textContent = `${Number(elements["breathing-smoothing"].value).toFixed(2)} s`;
    elements["breathing-sensitivity-value"].textContent = `${Math.round(Number(elements["breathing-sensitivity"].value) * 100)}%`;
  }

  function updateBreathingConfigVisibility() {
    const selected = breathingOutputIds.some((id) => (
      elements["metric-options"].querySelector(`input[value="${id}"]`)?.checked
    ));
    const query = elements["metric-search"].value.trim().toLowerCase();
    const classifierMatches = !query || "breathing magnitude estimate continuous curve phase classifier inhale pause exhale tunable acc projection".includes(query);
    const visible = app.metricFamily === "acc" && selected && classifierMatches;
    elements["breathing-config"].hidden = !visible;
    elements["breathing-config"].closest(".metric-scroll").classList.toggle("with-config", visible);
    validateBreathingAxes();
  }

  function renderOutputs() {
    syncCustomVisualDefinitions();
    const byId = new Map(app.catalog.map((metric) => [metric.id, metric]));
    const chips = [...app.outputs].map((id) => {
      const metric = byId.get(id);
      if (!metric) return null;
      const chip = document.createElement("span");
      const family = metric.family || (metric.id.includes("acc") ? "acc" : "ecg");
      chip.className = `output-chip ${family}`;
      const dot = document.createElement("i");
      const label = document.createElement("span");
      label.textContent = streamOutputName(metric, elements["stream-name"].value);
      chip.title = metric.label;
      const remove = document.createElement("button");
      remove.type = "button";
      remove.setAttribute("aria-label", `Remove ${metric.label}`);
      remove.textContent = "×";
      remove.addEventListener("click", () => {
        app.outputs.delete(id);
        renderOutputs();
        configureOutputs();
      });
      chip.append(dot, label, remove);
      return chip;
    }).filter(Boolean);
    for (const formula of app.customFormulas.filter((item) => item.enabled)) {
      const chip = document.createElement("span");
      chip.className = `output-chip ${sourceDetails[formula.source]?.family || "ecg"}`;
      const dot = document.createElement("i");
      const label = document.createElement("span");
      label.textContent = customStreamName(formula, elements["stream-name"].value);
      chip.title = `${formula.name} · ${formula.expression}`;
      const remove = document.createElement("button");
      remove.type = "button";
      remove.setAttribute("aria-label", `Disable ${formula.name}`);
      remove.textContent = "×";
      remove.addEventListener("click", () => {
        formula.enabled = false;
        renderOutputs();
        configureOutputs();
      });
      chip.append(dot, label, remove);
      chips.push(chip);
    }
    elements["output-chips"].replaceChildren(...chips);
    const count = app.outputs.size + app.customFormulas.filter((formula) => formula.enabled).length;
    elements["included-count"].textContent = `${count} active`;
    elements["output-state"].textContent = `${count} signal${count === 1 ? "" : "s"}`;
    updateStreamNamePreview();
    rebuildVisualOptions();
  }

  function updateStreamNamePreview() {
    const byId = new Map(app.catalog.map((metric) => [metric.id, metric]));
    const names = [...app.outputs]
      .map((id) => byId.get(id))
      .filter(Boolean)
      .map((metric) => streamOutputName(metric, elements["stream-name"].value));
    names.push(...app.customFormulas.filter((formula) => formula.enabled).map((formula) => customStreamName(formula, elements["stream-name"].value)));
    if (!normalizeStreamBase(elements["stream-name"].value)) {
      elements["stream-name-preview"].textContent = "Use at least one letter or number; spaces become underscores.";
      return;
    }
    const extra = names.length > 2 ? ` · +${names.length - 2} more` : "";
    elements["stream-name-preview"].textContent = names.length
      ? `Publishes ${names.slice(0, 2).join(" · ")}${extra}`
      : "No outputs are currently selected.";
  }

  function rebuildVisualOptions() {
    visualDefinitions.acc_breathing_waveform.unit = app.breathingConfig.normalize ? "0–1" : "g projection";
    const choices = availableVisualChoices();
    if (!app.visualizers.size) {
      elements["visual-source"].replaceChildren(...createVisualOptions(choices));
      if (!choices.some((choice) => choice.id === app.selectedVisual)) app.selectedVisual = choices[0]?.id || "";
      elements["visual-source"].value = app.selectedVisual;
      elements["visual-source"].disabled = !choices.length;
    }
    for (const view of app.visualizers.values()) populateVisualizerOptions(view, choices);
    broadcastVisualizerConfig();
  }

  function availableVisualChoices() {
    return Object.entries(visualDefinitions)
      .filter(([id, definition]) => definition.formulaId
        ? app.customFormulas.some((formula) => formula.id === definition.formulaId && formula.enabled)
        : app.outputs.has(definition.parent || id))
      .map(([id, definition]) => ({ id, definition }));
  }

  function createVisualOptions(choices) {
    return choices.map(({ id, definition }) => {
      const option = document.createElement("option");
      option.value = id;
      option.textContent = definition.label;
      return option;
    });
  }

  function populateVisualizerOptions(view, choices = availableVisualChoices()) {
    view.dom.source.replaceChildren(...createVisualOptions(choices));
    if (!choices.some((choice) => choice.id === view.source)) view.source = choices[0]?.id || "";
    view.dom.source.value = view.source;
    view.dom.source.disabled = !choices.length;
    if (view.id === "primary") app.selectedVisual = view.source;
    updateVisualLabels(view);
  }

  function updateVisualLabels(view) {
    if (!view) return;
    const definition = visualDefinitions[view.source];
    view.dom.unit.textContent = definition?.unit || "";
    view.dom.current.classList.toggle("stacked-value", Boolean(definition?.channels));
    view.dom.shell.classList.toggle("stacked-axes", Boolean(definition?.channels));
    view.dom.shell.classList.toggle("breathing-circle", definition?.kind === "breathing-circle");
    view.dom.windowLabel.textContent = definition?.kind === "breathing-circle" ? "Phase driven" : "5 second window";
    view.dom.scaleLabel.textContent = definition?.kind === "breathing-circle"
      ? "Inertial easing"
      : view.source === "acc_breathing_waveform" && app.breathingConfig.normalize
        ? "Normalized 0–1"
        : "Auto scale";
    view.dom.canvas.setAttribute(
      "aria-label",
      definition?.kind === "breathing-circle"
        ? "Animated ACC-derived breathing phase circle"
        : definition?.channels
        ? "Live raw accelerometer X, Y, and Z signals in three stacked plots"
        : `Live ${definition?.label || "selected Polar H10"} signal`,
    );

    const channels = definition?.channels || (definition ? [{ label: definition.label, color: definition.color }] : []);
    view.dom.legend.replaceChildren(...channels.map((channel) => {
      const item = document.createElement("span");
      item.className = "legend-item";
      const line = document.createElement("i");
      line.className = "legend-line";
      line.style.background = channel.color || "#87958d";
      const label = document.createElement("strong");
      label.textContent = channel.label;
      item.append(line, label);
      return item;
    }));
    resizeCanvas(view);
  }

  function registerVisualizer(card, id, source) {
    if (!visualizerCardTemplate) visualizerCardTemplate = card.cloneNode(true);
    const dom = {
      source: card.querySelector("select"),
      current: card.querySelector(".visual-value strong"),
      unit: card.querySelector(".visual-value span"),
      shell: card.querySelector(".chart-shell"),
      canvas: card.querySelector("canvas"),
      empty: card.querySelector(".chart-empty"),
      yMax: card.querySelector(".chart-y-labels span:first-child"),
      yMin: card.querySelector(".chart-y-labels span:last-child"),
      legend: card.querySelector(".visual-legend"),
      windowLabel: card.querySelector(".chart-window-label"),
      scaleLabel: card.querySelector(".chart-scale-label"),
    };
    const view = {
      id,
      source,
      card,
      dom,
      circleLevel: 0.5,
      circleVelocity: 0,
      circleFrameAt: performance.now(),
      resizeObserver: null,
    };
    card.dataset.visualizerId = id;
    card.draggable = true;
    app.visualizers.set(id, view);
    populateVisualizerOptions(view);
    dom.source.addEventListener("change", () => {
      view.source = dom.source.value;
      if (view.id === "primary") app.selectedVisual = view.source;
      updateVisualLabels(view);
      saveVisualizerLayout();
    });
    card.querySelector('[data-visualizer-action="close"]').addEventListener("click", () => removeVisualizer(id));
    card.querySelector('[data-visualizer-action="popout"]').addEventListener("click", () => detachVisualizer(view));
    const dragHandle = card.querySelector(".visualizer-drag-handle");
    dragHandle.addEventListener("pointerdown", () => {
      card.dataset.dragReady = "true";
      window.addEventListener("pointerup", () => { delete card.dataset.dragReady; }, { once: true });
    });
    card.addEventListener("dragstart", (event) => startVisualizerDrag(event, view));
    card.addEventListener("dragend", (event) => endVisualizerDrag(event, view));
    card.addEventListener("pointerdown", (event) => watchVisualizerResize(event, view));
    view.resizeObserver = new ResizeObserver(() => resizeCanvas(view));
    view.resizeObserver.observe(dom.shell);
    elements["visualizer-empty-deck"].hidden = true;
    window.requestAnimationFrame(() => resizeCanvas(view));
    return view;
  }

  function addVisualizer(source = "", options = {}) {
    const choices = availableVisualChoices();
    if (!choices.length) {
      toast("Add an output before opening a visualization.", true);
      return null;
    }
    const represented = new Set([...app.visualizers.values()].map((view) => view.source));
    const selectedSource = choices.some((choice) => choice.id === source)
      ? source
      : choices.find((choice) => !represented.has(choice.id))?.id || choices[0].id;
    const id = `view-${++app.visualizerSequence}`;
    const card = visualizerCardTemplate.cloneNode(true);
    card.removeAttribute("id");
    card.querySelectorAll("[id]").forEach((node) => node.removeAttribute("id"));
    const select = card.querySelector("select");
    select.id = `visual-source-${id}`;
    card.querySelector("label").htmlFor = select.id;
    elements["visualizer-deck"].insertBefore(card, elements["visualizer-empty-deck"]);
    const view = registerVisualizer(card, id, selectedSource);
    if (options.width) card.style.width = `${options.width}px`;
    if (options.height) card.style.height = `${options.height}px`;
    if (options.userSized) card.dataset.userSized = "true";
    if (options.arrange !== false) window.requestAnimationFrame(() => arrangeVisualizers(false));
    saveVisualizerLayout();
    return view;
  }

  function removeVisualizer(id, { save = true } = {}) {
    const view = app.visualizers.get(id);
    if (!view) return;
    view.resizeObserver?.disconnect();
    app.visualizers.delete(id);
    view.card.remove();
    elements["visualizer-empty-deck"].hidden = app.visualizers.size > 0;
    if (save) saveVisualizerLayout();
  }

  function arrangeVisualizers(force = true) {
    const views = [...elements["visualizer-deck"].querySelectorAll(".visualizer-card")]
      .map((card) => app.visualizers.get(card.dataset.visualizerId))
      .filter(Boolean);
    if (!views.length) return;
    const deck = elements["visualizer-deck"];
    const columns = views.length === 1 ? 1 : views.length <= 4 ? 2 : Math.ceil(Math.sqrt(views.length));
    const rows = Math.ceil(views.length / columns);
    const gap = 10;
    const width = Math.max(240, (deck.clientWidth - gap * (columns - 1) - 6) / columns);
    const height = Math.max(225, (deck.clientHeight - gap * (rows - 1) - 6) / rows);
    for (const view of views) {
      if (!force && view.card.dataset.userSized) continue;
      view.card.style.width = `${Math.min(deck.clientWidth - 6, width)}px`;
      view.card.style.height = `${height}px`;
      if (force) delete view.card.dataset.userSized;
    }
    saveVisualizerLayout();
  }

  function watchVisualizerResize(event, view) {
    if (event.button !== 0 || event.target.closest("button, select")) return;
    const start = view.card.getBoundingClientRect();
    if (event.clientX < start.right - 22 && event.clientY < start.bottom - 22) return;
    const finish = () => {
      const end = view.card.getBoundingClientRect();
      if (Math.abs(end.width - start.width) > 2 || Math.abs(end.height - start.height) > 2) {
        view.card.dataset.userSized = "true";
        saveVisualizerLayout();
      }
    };
    window.addEventListener("pointerup", finish, { once: true });
  }

  function saveVisualizerLayout() {
    const layout = [...elements["visualizer-deck"].querySelectorAll(".visualizer-card")]
      .map((card) => app.visualizers.get(card.dataset.visualizerId))
      .filter(Boolean)
      .map((view) => ({
      source: view.source,
      width: Math.round(view.card.getBoundingClientRect().width),
      height: Math.round(view.card.getBoundingClientRect().height),
      userSized: Boolean(view.card.dataset.userSized),
      }));
    try { localStorage.setItem("polarStream.visualizerLayout.v1", JSON.stringify(layout)); } catch { /* storage is optional */ }
    scheduleLastSessionSave();
  }

  function captureWorkspaceLayout() {
    const available = workspaceAvailableWidth();
    const input = document.querySelector(".input-panel")?.getBoundingClientRect().width || available * defaultWorkspaceFractions()[0];
    const output = document.querySelector(".output-panel")?.getBoundingClientRect().width || available * defaultWorkspaceFractions()[1];
    const fractions = window.matchMedia("(max-width: 900px)").matches
      ? savedWorkspaceFractions()
      : [input / available, output / available];
    return {
      paneFractions: fractions.map((value, index) => Math.min(0.9, Math.max(0.05, Number(value) || defaultWorkspaceFractions()[index]))),
      visualizers: [...elements["visualizer-deck"].querySelectorAll(".visualizer-card")]
        .map((card) => app.visualizers.get(card.dataset.visualizerId))
        .filter(Boolean)
        .map((view) => ({
          source: view.source,
          width: Math.max(0, Math.round(view.card.getBoundingClientRect().width)),
          height: Math.max(0, Math.round(view.card.getBoundingClientRect().height)),
          userSized: Boolean(view.card.dataset.userSized),
        })),
    };
  }

  function applyWorkspaceLayout(workspace) {
    const visualizers = Array.isArray(workspace?.visualizers) ? workspace.visualizers : [];
    for (const id of [...app.visualizers.keys()]) removeVisualizer(id, { save: false });
    for (const item of visualizers) {
      addVisualizer(item.source, {
        arrange: false,
        userSized: Boolean(item.userSized),
        width: item.userSized ? Number(item.width) || null : null,
        height: item.userSized ? Number(item.height) || null : null,
      });
    }
    if (!visualizers.length) elements["visualizer-empty-deck"].hidden = false;
    if (Array.isArray(workspace?.paneFractions)) applyWorkspaceFractions(workspace.paneFractions);
  }

  function restoreVisualizerLayout() {
    let layout;
    try { layout = JSON.parse(localStorage.getItem("polarStream.visualizerLayout.v1") || "null"); } catch { return; }
    if (!Array.isArray(layout) || !layout.length) return;
    const choices = new Set(availableVisualChoices().map((choice) => choice.id));
    const saved = layout.filter((item) => choices.has(item?.source));
    if (!saved.length) return;
    const primary = app.visualizers.get("primary");
    primary.source = saved[0].source;
    if (saved[0].userSized) {
      primary.card.style.width = `${saved[0].width}px`;
      primary.card.style.height = `${saved[0].height}px`;
      primary.card.dataset.userSized = "true";
    }
    populateVisualizerOptions(primary);
    for (const item of saved.slice(1)) {
      addVisualizer(item.source, {
        arrange: false,
        userSized: item.userSized,
        width: item.userSized ? item.width : null,
        height: item.userSized ? item.height : null,
      });
    }
  }

  function resetWorkspaceLayout() {
    try {
      localStorage.removeItem("polarStream.workspaceSplit.v1");
      localStorage.removeItem("polarStream.visualizerLayout.v1");
    } catch { /* storage is optional */ }
    applyWorkspaceFractions(defaultWorkspaceFractions());
    arrangeVisualizers(true);
    scheduleLastSessionSave();
    toast("Workspace layout reset.");
  }

  function defaultWorkspaceFractions() {
    return [0.27, 0.30];
  }

  function workspaceAvailableWidth() {
    return Math.max(1, elements["visualizer-deck"].closest(".workspace").clientWidth - 14);
  }

  function applyWorkspaceFractions(fractions) {
    if (window.matchMedia("(max-width: 900px)").matches) return;
    const available = workspaceAvailableWidth();
    const input = Math.min(available - 460, Math.max(190, Number(fractions[0]) * available));
    const output = Math.min(available - input - 250, Math.max(210, Number(fractions[1]) * available));
    setWorkspaceWidths(input, output);
  }

  function setWorkspaceWidths(input, output, { save = false } = {}) {
    const available = workspaceAvailableWidth();
    const safeInput = Math.max(190, Math.min(input, available - 460));
    const safeOutput = Math.max(210, Math.min(output, available - safeInput - 250));
    document.documentElement.style.setProperty("--input-pane", `${Math.round(safeInput)}px`);
    document.documentElement.style.setProperty("--output-pane", `${Math.round(safeOutput)}px`);
    const splitters = document.querySelectorAll(".workspace-splitter");
    splitters[0]?.setAttribute("aria-valuenow", String(Math.round((safeInput / available) * 100)));
    splitters[1]?.setAttribute("aria-valuenow", String(Math.round((safeOutput / available) * 100)));
    if (save) {
      try {
        localStorage.setItem("polarStream.workspaceSplit.v1", JSON.stringify([safeInput / available, safeOutput / available]));
      } catch { /* storage is optional */ }
      scheduleLastSessionSave();
    }
  }

  function savedWorkspaceFractions() {
    try {
      const parsed = JSON.parse(localStorage.getItem("polarStream.workspaceSplit.v1") || "null");
      if (Array.isArray(parsed) && parsed.length === 2 && parsed.every(Number.isFinite)) return parsed;
    } catch { /* use defaults */ }
    return defaultWorkspaceFractions();
  }

  function installWorkspaceSplitters() {
    const splitters = [...document.querySelectorAll(".workspace-splitter")];
    const beginResize = (event, index) => {
      if (event.button !== 0 || window.matchMedia("(max-width: 900px)").matches) return;
      event.preventDefault();
      const splitter = splitters[index];
      const inputPanel = elements["visualizer-deck"].closest(".workspace").querySelector(".input-panel");
      const outputPanel = elements["visualizer-deck"].closest(".workspace").querySelector(".output-panel");
      const visualPanel = elements["visualizer-deck"].closest(".visual-panel");
      const startX = event.clientX;
      const inputWidth = inputPanel.getBoundingClientRect().width;
      const outputWidth = outputPanel.getBoundingClientRect().width;
      const visualWidth = visualPanel.getBoundingClientRect().width;
      document.body.classList.add("resizing-workspace");
      splitter.classList.add("active");
      splitter.setPointerCapture?.(event.pointerId);
      const move = (moveEvent) => {
        const delta = moveEvent.clientX - startX;
        if (index === 0) {
          const pair = inputWidth + outputWidth;
          const nextInput = Math.max(190, Math.min(pair - 210, inputWidth + delta));
          setWorkspaceWidths(nextInput, pair - nextInput);
        } else {
          const pair = outputWidth + visualWidth;
          const nextOutput = Math.max(210, Math.min(pair - 250, outputWidth + delta));
          setWorkspaceWidths(inputWidth, nextOutput);
        }
      };
      const finish = () => {
        document.body.classList.remove("resizing-workspace");
        splitter.classList.remove("active");
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", finish);
        const currentInput = inputPanel.getBoundingClientRect().width;
        const currentOutput = outputPanel.getBoundingClientRect().width;
        setWorkspaceWidths(currentInput, currentOutput, { save: true });
      };
      window.addEventListener("pointermove", move);
      window.addEventListener("pointerup", finish);
    };
    splitters.forEach((splitter, index) => {
      splitter.addEventListener("pointerdown", (event) => beginResize(event, index));
      splitter.addEventListener("dblclick", () => {
        applyWorkspaceFractions(defaultWorkspaceFractions());
        setWorkspaceWidths(
          document.querySelector(".input-panel").getBoundingClientRect().width,
          document.querySelector(".output-panel").getBoundingClientRect().width,
          { save: true },
        );
      });
      splitter.addEventListener("keydown", (event) => {
        if (!["ArrowLeft", "ArrowRight"].includes(event.key)) return;
        event.preventDefault();
        const delta = event.key === "ArrowRight" ? 16 : -16;
        const input = document.querySelector(".input-panel").getBoundingClientRect().width;
        const output = document.querySelector(".output-panel").getBoundingClientRect().width;
        if (index === 0) setWorkspaceWidths(input + delta, output - delta, { save: true });
        else setWorkspaceWidths(input, output + delta, { save: true });
      });
    });
    applyWorkspaceFractions(savedWorkspaceFractions());
    let resizeTimer;
    window.addEventListener("resize", () => {
      window.clearTimeout(resizeTimer);
      resizeTimer = window.setTimeout(() => applyWorkspaceFractions(savedWorkspaceFractions()), 80);
    });
  }

  function installVisualizerDeckInteractions() {
    const deck = elements["visualizer-deck"];
    deck.addEventListener("dragover", (event) => {
      const transferTypes = [...event.dataTransfer.types];
      const hasTransfer = transferTypes.includes("application/x-polar-visualizer") || transferTypes.includes("text/plain");
      if (!app.draggedVisualizerId && !hasTransfer) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
      const candidates = [...deck.querySelectorAll(".visualizer-card:not(.dragging)")];
      for (const card of candidates) card.classList.remove("drop-before");
      const before = candidates.find((card) => {
        const box = card.getBoundingClientRect();
        return event.clientY < box.top + box.height / 2
          || (event.clientY <= box.bottom && event.clientX < box.left + box.width / 2);
      });
      if (app.draggedVisualizerId) {
        const dragged = app.visualizers.get(app.draggedVisualizerId)?.card;
        if (dragged) deck.insertBefore(dragged, before || elements["visualizer-empty-deck"]);
      } else {
        before?.classList.add("drop-before");
      }
    });
    deck.addEventListener("drop", (event) => {
      event.preventDefault();
      deck.querySelectorAll(".drop-before").forEach((card) => card.classList.remove("drop-before"));
      if (app.draggedVisualizerId) {
        const localView = app.visualizers.get(app.draggedVisualizerId);
        if (localView) localView.droppedInside = true;
        saveVisualizerLayout();
        return;
      }
      try {
        const raw = event.dataTransfer.getData("application/x-polar-visualizer") || event.dataTransfer.getData("text/plain");
        const payload = JSON.parse(raw);
        if (!payload?.source) return;
        addVisualizer(payload.source);
        visualizerChannel?.postMessage({ type: "adopted", origin: payload.origin, viewId: payload.viewId });
      } catch { /* ignore unrelated drags */ }
    });
  }

  function startVisualizerDrag(event, view) {
    if (view.card.dataset.dragReady !== "true") {
      event.preventDefault();
      return;
    }
    app.draggedVisualizerId = view.id;
    view.droppedInside = false;
    view.card.classList.add("dragging");
    const payload = { source: view.source, origin: mainWindowId, viewId: view.id };
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("application/x-polar-visualizer", JSON.stringify(payload));
    event.dataTransfer.setData("text/plain", JSON.stringify(payload));
  }

  function endVisualizerDrag(event, view) {
    view.card.classList.remove("dragging");
    delete view.card.dataset.dragReady;
    app.draggedVisualizerId = null;
    const outside = event.clientX <= 0 || event.clientY <= 0 || event.clientX >= window.innerWidth || event.clientY >= window.innerHeight;
    if (!view.droppedInside && event.dataTransfer.dropEffect === "none" && outside) void detachVisualizer(view);
    view.droppedInside = false;
    saveVisualizerLayout();
  }

  async function detachVisualizer(view) {
    const token = `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
    const query = new URLSearchParams({ source: view.source, origin: mainWindowId, view: view.id, token });
    const relativeUrl = `visualizer.html?${query}`;
    try {
      const TauriWindow = window.__TAURI__?.webviewWindow?.WebviewWindow;
      if (isNative && TauriWindow) {
        const detached = new TauriWindow(`visualizer-${token}`, {
          url: relativeUrl,
          title: `${visualDefinitions[view.source]?.label || "Visualization"} · Polar Stream`,
          width: Math.max(520, Math.round(view.card.getBoundingClientRect().width)),
          height: Math.max(420, Math.round(view.card.getBoundingClientRect().height)),
          minWidth: 360,
          minHeight: 280,
          resizable: true,
        });
        await new Promise((resolve, reject) => {
          detached.once("tauri://created", resolve);
          detached.once("tauri://error", (error) => reject(error?.payload || error));
        });
      } else {
        const popup = window.open(relativeUrl, `polar-visualizer-${token}`, `popup,width=${Math.max(520, Math.round(view.card.offsetWidth))},height=${Math.max(420, Math.round(view.card.offsetHeight))}`);
        if (!popup) throw new Error("The browser blocked the visualization window.");
      }
      removeVisualizer(view.id);
    } catch (error) {
      toast(`Could not detach visualization: ${String(error)}`, true);
    }
  }

  function broadcastVisualizerConfig() {
    visualizerChannel?.postMessage({
      type: "config",
      choices: availableVisualChoices().map(({ id, definition }) => ({
        id,
        label: definition.label,
        unit: definition.unit,
        rate: definition.rate,
        color: definition.color,
        symmetric: Boolean(definition.symmetric),
        formulaId: definition.formulaId || null,
      })),
      breathingNormalized: app.breathingConfig.normalize,
    });
  }

  function broadcastVisualizerData(event) {
    if (["ecg", "accelerometer", "metrics", "connection"].includes(event.kind)) {
      let payload = event;
      if (event.kind === "accelerometer" && breathingOutputIds.some((id) => app.outputs.has(id))) {
        const sampleCount = event.samples?.length || 0;
        payload = {
          ...event,
          visualizerBreathing: Array.from(buffers.acc_breathing_waveform.tail(sampleCount)),
          visualizerBreathingPhase: app.breathingPhase,
        };
      }
      visualizerChannel?.postMessage({ type: "data", event: payload });
    }
  }

  function handleVisualizerChannelMessage(messageEvent) {
    const message = messageEvent.data || {};
    if (message.type === "request-snapshot") {
      broadcastVisualizerConfig();
      visualizerChannel?.postMessage({
        type: "snapshot",
        target: message.sender,
        buffers: Object.fromEntries(Object.entries(buffers).map(([id, buffer]) => [id, Array.from(buffer.tail(buffer.capacity))])),
        breathingPhase: app.breathingPhase,
        connected: app.connected,
      });
    } else if (message.type === "dock-view" && message.source) {
      addVisualizer(message.source);
      visualizerChannel?.postMessage({ type: "adopted", origin: message.origin, viewId: message.viewId });
    } else if (message.type === "adopted" && message.origin === mainWindowId) {
      removeVisualizer(message.viewId);
    }
  }

  function currentOutputConfig() {
    return {
      streamName: normalizeStreamBase(elements["stream-name"].value) || app.streamName || "Polar-H10",
      lslEnabled: elements["lsl-toggle"].checked,
      oscEnabled: elements["osc-toggle"].checked,
      outputs: [...app.outputs],
      breathingConfig: app.breathingConfig,
      customFormulas: app.customFormulas.map((formula) => ({ ...formula })),
    };
  }

  function captureWorkspaceProfile() {
    const sensor = app.preferences.lastDevice;
    return {
      schemaVersion: 1,
      preferredSensor: sensor ? { id: sensor.id, name: sensor.name } : null,
      outputConfig: currentOutputConfig(),
      workspace: captureWorkspaceLayout(),
    };
  }

  function scheduleLastSessionSave(delay = 550) {
    if (!isNative || !app.visualizers) return;
    window.clearTimeout(app.sessionSaveTimer);
    app.sessionSaveTimer = window.setTimeout(() => void persistLastSession(), delay);
  }

  async function persistLastSession() {
    if (!isNative) return;
    try {
      await invoke("save_last_session", { profile: captureWorkspaceProfile() });
    } catch (error) {
      console.warn("Could not save Last session", error);
    }
  }

  function renderProfileOptions(selected = "") {
    const placeholder = document.createElement("option");
    placeholder.value = "";
    placeholder.textContent = "Profiles…";
    const options = app.profileSummaries.map((profile) => {
      const option = document.createElement("option");
      option.value = profile.name;
      option.textContent = profile.name;
      option.title = `Updated ${new Date(profile.updatedAtMs).toLocaleString()}`;
      return option;
    });
    elements["profile-select"].replaceChildren(placeholder, ...options);
    elements["profile-select"].value = app.profileSummaries.some((profile) => profile.name === selected) ? selected : "";
    elements["delete-profile-button"].disabled = !elements["profile-select"].value;
  }

  async function refreshProfileOptions(selected = "") {
    if (!isNative) return;
    app.profileSummaries = await invoke("list_profiles");
    renderProfileOptions(selected);
  }

  async function saveNamedProfile() {
    if (!isNative) {
      toast("Named profiles are stored by the native app. Open the desktop build to save one.");
      return;
    }
    const currentName = elements["profile-select"].value;
    const requested = window.prompt("Profile name", currentName || "My workspace");
    if (requested == null) return;
    try {
      const summary = await invoke("save_profile", { name: requested, profile: captureWorkspaceProfile() });
      await refreshProfileOptions(summary.name);
      await persistLastSession();
      toast(`Saved profile “${summary.name}”.`);
    } catch (error) {
      toast(String(error), true);
    }
  }

  async function loadSelectedProfile() {
    const name = elements["profile-select"].value;
    elements["delete-profile-button"].disabled = !name;
    if (!name || !isNative) return;
    try {
      const profile = await invoke("load_profile", { name });
      await applyWorkspaceProfile(profile);
      renderProfileOptions(name);
      toast(`Loaded profile “${name}”.`);
    } catch (error) {
      renderProfileOptions();
      toast(String(error), true);
    }
  }

  async function deleteSelectedProfile() {
    const name = elements["profile-select"].value;
    if (!name || !isNative || !window.confirm(`Delete profile “${name}”?`)) return;
    try {
      await invoke("delete_profile", { name });
      await refreshProfileOptions();
      toast(`Deleted profile “${name}”.`);
    } catch (error) {
      toast(String(error), true);
    }
  }

  async function applyWorkspaceProfile(profile) {
    const config = profile.outputConfig || {};
    app.outputs = new Set(config.outputs || []);
    app.breathingConfig = normalizeBreathingConfig(config.breathingConfig);
    app.customFormulas = (config.customFormulas || []).map(normalizeFormulaDraft);
    app.streamName = normalizeStreamBase(config.streamName) || "Polar-H10";
    elements["stream-name"].value = app.streamName;
    elements["lsl-toggle"].checked = Boolean(config.lslEnabled);
    elements["osc-toggle"].checked = Boolean(config.oscEnabled);
    if (profile.preferredSensor) app.preferences = preferences.saveLastDevice(profile.preferredSensor);
    else app.preferences = preferences.saveLastDevice(null);
    app.browserBreathing = null;
    app.formulaFaultsShown.clear();
    renderOutputs();
    renderFormulaBoxes();
    await configureOutputs({ quiet: false, persist: false });
    applyWorkspaceLayout(profile.workspace || {});
    await persistLastSession();
    if (profile.preferredSensor && window.confirm(`Outputs and workspace are applied. Reconnect to saved sensor “${profile.preferredSensor.name}” now?`)) {
      if (app.connected) await disconnectDevice();
      await scanDevices({ automatic: true });
    }
  }

  async function configureOutputs({ quiet = false, persist = true } = {}) {
    const streamName = normalizeStreamBase(elements["stream-name"].value);
    if (!streamName) {
      elements["stream-name"].setAttribute("aria-invalid", "true");
      updateStreamNamePreview();
      return;
    }
    elements["stream-name"].removeAttribute("aria-invalid");
    app.streamName = streamName;
    const config = { ...currentOutputConfig(), streamName };
    if (!isNative) {
      elements["stream-name"].value = streamName;
      app.preferences = preferences.saveStreamName(streamName);
      renderOutputs();
      elements["lsl-detail"].textContent = config.lslEnabled ? "Preview · liblsl is checked in the native app" : "Local network · time synchronized";
      elements["osc-detail"].textContent = config.oscEnabled ? "Preview · UDP localhost:9000" : "UDP · localhost:9000";
      if (persist) scheduleLastSessionSave();
      return;
    }

    const sequence = ++app.outputSequence;
    try {
      const health = await invoke("update_output_config", { config });
      if (sequence !== app.outputSequence) return;
      app.streamName = health.streamName || streamName;
      elements["stream-name"].value = app.streamName;
      app.preferences = preferences.saveStreamName(app.streamName);
      renderOutputs();
      updateDestinationHealth(health);
      if (persist) scheduleLastSessionSave();
    } catch (error) {
      if (!quiet) toast(String(error), true);
    }
  }

  function updateDestinationHealth(health) {
    const lslText = elements["lsl-toggle"].checked ? health.lsl : "Local network · time synchronized";
    const oscText = elements["osc-toggle"].checked ? health.osc : "UDP · localhost:9000";
    elements["lsl-detail"].textContent = lslText;
    elements["osc-detail"].textContent = oscText;
    elements["lsl-detail"].classList.toggle("warning", elements["lsl-toggle"].checked && /not found|failed|could not|unavailable/i.test(lslText));
    elements["osc-detail"].classList.toggle("warning", elements["osc-toggle"].checked && /failed|could not|unavailable/i.test(oscText));
    for (const formulaHealth of health.formulas || []) {
      const id = formulaHealth.formulaId ?? formulaHealth.formula_id;
      const card = elements["formula-boxes"].querySelector(`[data-formula-id="${id}"]`);
      if (!card) continue;
      card.classList.toggle("faulted", formulaHealth.state === "faulted");
      const status = card.querySelector(".formula-status strong");
      if (status && formulaHealth.state === "faulted") status.textContent = "Faulted";
    }
  }

  function resizeCanvas(view = null) {
    if (!view) {
      for (const candidate of app.visualizers.values()) resizeCanvas(candidate);
      return;
    }
    const canvas = view.dom.canvas;
    const bounds = view.dom.shell.getBoundingClientRect();
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    const width = Math.max(1, Math.round(bounds.width * ratio));
    const height = Math.max(1, Math.round(bounds.height * ratio));
    if (canvas.width !== width) canvas.width = width;
    if (canvas.height !== height) canvas.height = height;
  }

  let previousFrame = performance.now();
  let frameAccumulator = 0;
  let frameSamples = 0;
  function drawFrame(now) {
    const elapsed = now - previousFrame;
    previousFrame = now;
    frameAccumulator += elapsed;
    frameSamples += 1;
    if (frameAccumulator >= 800) {
      elements["render-rate"].textContent = `${Math.round((frameSamples * 1000) / frameAccumulator)} fps`;
      frameAccumulator = 0;
      frameSamples = 0;
    }

    for (const view of app.visualizers.values()) drawSignal(view);
    window.requestAnimationFrame(drawFrame);
  }

  function drawSignal(view) {
    const canvas = view.dom.canvas;
    const context = canvas.getContext("2d", { alpha: true });
    const definition = visualDefinitions[view.source];
    if (!definition) {
      context.clearRect(0, 0, canvas.width, canvas.height);
      view.dom.empty.hidden = false;
      view.dom.current.textContent = "—";
      return;
    }

    if (definition.kind === "breathing-circle") {
      drawBreathingCircle(context, canvas, view);
      return;
    }

    if (definition.channels) {
      drawStackedSignal(context, canvas, definition, view);
      return;
    }

    const buffer = buffers[view.source];
    if (!buffer) return;

    const visibleCount = Math.max(10, Math.ceil(definition.rate * 5));
    const values = buffer.tail(visibleCount);
    view.dom.empty.hidden = values.length > 1;
    view.dom.current.textContent = formatValue(buffer.latest(), definition.unit === "g" ? 3 : definition.unit === "bpm" ? 0 : 1);
    if (values.length < 2) {
      context.clearRect(0, 0, canvas.width, canvas.height);
      return;
    }

    let min = Infinity;
    let max = -Infinity;
    for (const value of values) {
      if (value < min) min = value;
      if (value > max) max = value;
    }
    if (definition.symmetric) {
      const extent = Math.max(Math.abs(min), Math.abs(max), 1) * 1.08;
      min = -extent;
      max = extent;
    } else {
      const padding = Math.max((max - min) * 0.12, Math.abs(max) * 0.02, 0.5);
      min -= padding;
      max += padding;
    }

    view.dom.yMax.textContent = shortAxis(max);
    view.dom.yMin.textContent = shortAxis(min);
    const width = canvas.width;
    const height = canvas.height;
    const padX = Math.round(width * 0.035);
    const padY = Math.round(height * 0.08);
    const drawWidth = width - padX * 2;
    const drawHeight = height - padY * 2;
    const range = max - min || 1;
    context.clearRect(0, 0, width, height);
    context.beginPath();
    for (let index = 0; index < values.length; index += 1) {
      const x = padX + (index / (values.length - 1)) * drawWidth;
      const y = padY + (1 - (values[index] - min) / range) * drawHeight;
      if (index === 0) context.moveTo(x, y); else context.lineTo(x, y);
    }
    context.strokeStyle = definition.color;
    context.lineWidth = Math.max(1.4, (window.devicePixelRatio || 1) * 0.9);
    context.lineJoin = "round";
    context.lineCap = "round";
    context.stroke();
  }

  function drawBreathingCircle(context, canvas, view) {
    const buffer = buffers.acc_breathing_waveform;
    const hasData = buffer.length > 1;
    view.dom.empty.hidden = hasData;
    view.dom.yMax.textContent = "";
    view.dom.yMin.textContent = "";
    const phaseLabel = app.breathingPhase > 0 ? "Inhaling" : app.breathingPhase < 0 ? "Exhaling" : "Pausing";
    view.dom.current.textContent = hasData ? phaseLabel : "—";

    const now = performance.now();
    const deltaSeconds = Math.min(0.05, Math.max(0, (now - view.circleFrameAt) / 1000));
    view.circleFrameAt = now;
    const targetVelocity = app.breathingPhase > 0 ? 0.95 : app.breathingPhase < 0 ? -0.95 : 0;
    const response = app.breathingPhase === 0 ? 1.7 : 4.2;
    view.circleVelocity += (targetVelocity - view.circleVelocity) * (1 - Math.exp(-response * deltaSeconds));
    if (view.circleVelocity >= 0) {
      view.circleLevel += view.circleVelocity * deltaSeconds * (1 - view.circleLevel) * 1.65;
    } else {
      view.circleLevel += view.circleVelocity * deltaSeconds * view.circleLevel * 1.65;
    }
    view.circleLevel = Math.min(0.985, Math.max(0.015, view.circleLevel));

    const width = canvas.width;
    const height = canvas.height;
    context.clearRect(0, 0, width, height);
    if (!hasData) return;
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    const centerX = width / 2;
    const centerY = height / 2;
    const minRadius = Math.min(width, height) * 0.13;
    const maxRadius = Math.min(width, height) * 0.36;
    const radius = minRadius + view.circleLevel * (maxRadius - minRadius);

    context.beginPath();
    context.arc(centerX, centerY, maxRadius, 0, Math.PI * 2);
    context.strokeStyle = "rgba(59, 120, 170, 0.11)";
    context.lineWidth = Math.max(1, ratio);
    context.setLineDash([4 * ratio, 7 * ratio]);
    context.stroke();
    context.setLineDash([]);

    const glow = context.createRadialGradient(centerX, centerY, radius * 0.25, centerX, centerY, radius * 1.2);
    glow.addColorStop(0, "rgba(59, 120, 170, 0.20)");
    glow.addColorStop(0.72, "rgba(59, 120, 170, 0.10)");
    glow.addColorStop(1, "rgba(59, 120, 170, 0)");
    context.beginPath();
    context.arc(centerX, centerY, radius * 1.2, 0, Math.PI * 2);
    context.fillStyle = glow;
    context.fill();

    context.beginPath();
    context.arc(centerX, centerY, radius, 0, Math.PI * 2);
    context.fillStyle = "rgba(230, 240, 248, 0.78)";
    context.fill();
    context.strokeStyle = "#3b78aa";
    context.lineWidth = Math.max(2, 1.6 * ratio);
    context.stroke();

    context.fillStyle = "#2b618d";
    context.font = `700 ${Math.round(12 * ratio)}px Aptos, Noto Sans, sans-serif`;
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillText(phaseLabel, centerX, centerY - 3 * ratio);
    context.fillStyle = "#728390";
    context.font = `${Math.round(8 * ratio)}px ui-monospace, SFMono-Regular, Menlo, monospace`;
    context.fillText("ACC · EXPERIMENTAL", centerX, centerY + 14 * ratio);
    context.textAlign = "left";
  }

  function drawStackedSignal(context, canvas, definition, view) {
    const visibleCount = Math.max(10, Math.ceil(definition.rate * 5));
    const series = definition.channels.map((channel) => ({
      ...channel,
      buffer: buffers[channel.buffer],
      values: buffers[channel.buffer].tail(visibleCount),
    }));
    const hasData = series.some(({ values }) => values.length > 1);
    view.dom.empty.hidden = hasData;
    view.dom.current.textContent = series
      .map(({ label, buffer }) => `${label} ${formatValue(buffer.latest(), 0)}`)
      .join("  ·  ");
    view.dom.yMax.textContent = "—";
    view.dom.yMin.textContent = "—";

    const width = canvas.width;
    const height = canvas.height;
    context.clearRect(0, 0, width, height);
    if (!hasData) return;

    const pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
    const padLeft = Math.max(Math.round(width * 0.08), Math.round(52 * pixelRatio));
    const padRight = Math.round(width * 0.035);
    const padY = Math.round(height * 0.035);
    const laneGap = Math.round(10 * pixelRatio);
    const laneHeight = (height - padY * 2 - laneGap * (series.length - 1)) / series.length;
    const drawWidth = width - padLeft - padRight;

    context.font = `${Math.round(9 * pixelRatio)}px ui-monospace, SFMono-Regular, Menlo, monospace`;
    context.textBaseline = "middle";
    context.lineJoin = "round";
    context.lineCap = "round";

    series.forEach(({ label, color, symmetric, values }, seriesIndex) => {
      const laneTop = padY + seriesIndex * (laneHeight + laneGap);
      const laneBottom = laneTop + laneHeight;
      if (seriesIndex > 0) {
        const separatorY = laneTop - laneGap / 2;
        context.beginPath();
        context.moveTo(padLeft, separatorY);
        context.lineTo(width - padRight, separatorY);
        context.strokeStyle = "rgba(81, 103, 91, 0.18)";
        context.lineWidth = Math.max(1, pixelRatio * 0.6);
        context.stroke();
      }

      context.fillStyle = color;
      context.font = `700 ${Math.round(10 * pixelRatio)}px ui-monospace, SFMono-Regular, Menlo, monospace`;
      context.fillText(label, Math.round(10 * pixelRatio), laneTop + laneHeight / 2);
      if (values.length < 2) return;

      let min = Infinity;
      let max = -Infinity;
      for (const value of values) {
        if (value < min) min = value;
        if (value > max) max = value;
      }
      if (symmetric) {
        const extent = Math.max(Math.abs(min), Math.abs(max), 1) * 1.08;
        min = -extent;
        max = extent;
      } else {
        const padding = Math.max((max - min) * 0.12, Math.abs(max) * 0.02, 0.5);
        min -= padding;
        max += padding;
      }

      context.fillStyle = "#85928a";
      context.font = `${Math.round(8 * pixelRatio)}px ui-monospace, SFMono-Regular, Menlo, monospace`;
      context.textAlign = "right";
      context.fillText(shortAxis(max), padLeft - Math.round(6 * pixelRatio), laneTop + Math.round(7 * pixelRatio));
      context.fillText("0", padLeft - Math.round(6 * pixelRatio), laneTop + laneHeight / 2);
      context.fillText(shortAxis(min), padLeft - Math.round(6 * pixelRatio), laneBottom - Math.round(7 * pixelRatio));
      context.textAlign = "left";

      const range = max - min || 1;
      context.beginPath();
      for (let index = 0; index < values.length; index += 1) {
        const x = padLeft + (index / (values.length - 1)) * drawWidth;
        const y = laneTop + (1 - (values[index] - min) / range) * laneHeight;
        if (index === 0) context.moveTo(x, y); else context.lineTo(x, y);
      }
      context.strokeStyle = color;
      context.lineWidth = Math.max(1.4, pixelRatio * 0.9);
      context.stroke();
    });
  }

  function updateSparkline() {
    const values = buffers.raw_ecg.tail(56);
    if (values.length < 2) return;
    let min = Infinity;
    let max = -Infinity;
    for (const value of values) { min = Math.min(min, value); max = Math.max(max, value); }
    const range = max - min || 1;
    const path = [];
    for (let index = 0; index < values.length; index += 1) {
      const x = (index / (values.length - 1)) * 280;
      const y = 4 + (1 - (values[index] - min) / range) * 36;
      path.push(`${index ? "L" : "M"}${x.toFixed(1)} ${y.toFixed(1)}`);
    }
    elements["ecg-spark"].setAttribute("d", path.join(" "));
  }

  async function loadPreviewRecording() {
    if (!previewFixtureApi) throw new Error("Preview fixture support did not load.");
    const response = await fetch("data/preview-recording.json", { cache: "no-store" });
    if (!response.ok) {
      throw new Error("Real preview recording is missing. Run `cargo run -p capture-preview-fixture` with an awake Polar H10.");
    }
    const fixture = previewFixtureApi.validateFixture(await response.json());
    app.previewRecording = fixture;
    app.previewRecordingError = null;
    return fixture;
  }

  async function previewDevices() {
    await new Promise((resolve) => window.setTimeout(resolve, 300));
    if (!app.previewRecording) {
      try {
        await loadPreviewRecording();
      } catch (error) {
        app.previewRecordingError = String(error);
        throw error;
      }
    }
    return [{
      id: "recorded-preview-loop",
      name: "Mock Data",
      rssi: null,
    }];
  }

  function startPreviewRecording() {
    stopPreviewRecording();
    app.previewPlayer = new previewFixtureApi.LoopPlayer(
      app.previewRecording,
      (event) => handleNativeEvent(event),
      { onLoop: () => { app.browserBreathing = null; } },
    );
    app.previewPlayer.start();
  }

  function stopPreviewRecording() {
    app.previewPlayer?.stop();
    app.previewPlayer = null;
  }

  function formatValue(value, digits = 1) {
    const number = Number(value);
    if (!Number.isFinite(number)) return "—";
    return number.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits });
  }

  function shortAxis(value) {
    const absolute = Math.abs(value);
    if (absolute >= 1_000) return `${(value / 1_000).toFixed(1)}k`;
    if (absolute < 10) return value.toFixed(1);
    return Math.round(value).toString();
  }

  initialize();
})();
