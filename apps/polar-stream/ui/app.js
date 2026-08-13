(() => {
  "use strict";

  const nativeCore = window.__TAURI__?.core;
  const isNative = Boolean(nativeCore?.invoke && nativeCore?.Channel);
  const invoke = nativeCore?.invoke?.bind(nativeCore);
  const NativeChannel = nativeCore?.Channel;
  const preferences = window.PolarPreferences;

  const fallbackCatalog = [
    { id: "raw_ecg", streamSuffix: "rawECG", label: "Raw ECG", detail: "130 Hz · 1 channel", unit: "µV", raw: true, family: "ecg" },
    { id: "heart_rate", streamSuffix: "heartRate", label: "Heart rate", detail: "Device-derived", unit: "bpm", raw: false, family: "ecg" },
    { id: "rr_interval", streamSuffix: "rrInterval", label: "RR interval", detail: "Beat-to-beat interval", unit: "ms", raw: false, family: "ecg" },
    { id: "rmssd", streamSuffix: "rmssd", label: "RMSSD", detail: "Rolling 60-beat window", unit: "ms", raw: false, family: "ecg" },
    { id: "raw_acc", streamSuffix: "rawACC", label: "Raw accelerometer", detail: "200 Hz · X, Y, Z", unit: "mg", raw: true, family: "acc" },
    { id: "acc_magnitude", streamSuffix: "accMagnitude", label: "ACC magnitude", detail: "√(x² + y² + z²)", unit: "g", raw: false, family: "acc" },
    { id: "acc_breathing_magnitude", streamSuffix: "accBreathingMagnitude", label: "Breathing magnitude estimate", detail: "Continuous tunable ACC projection", unit: "normalized / g", raw: false, family: "acc", experimental: true },
    { id: "acc_breathing_phase", streamSuffix: "accBreathingPhase", label: "Breathing phase classifier", detail: "Three states · inhale, pause, exhale", unit: "state", raw: false, family: "acc", experimental: true },
  ];

  const defaultBreathingConfig = {
    axes: ["x", "z"],
    smoothingWindowSeconds: 0.75,
    sensitivity: 0.6,
    normalize: true,
    invert: false,
  };
  const breathingOutputIds = ["acc_breathing_magnitude", "acc_breathing_phase"];

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
    acc_magnitude: { label: "ACC magnitude", unit: "g", rate: 200, color: "#3b78aa" },
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
    sampleCount: 0,
    demoTimer: null,
    demoPhase: 0,
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
        outputs: ["raw_ecg", "raw_acc"], breathingConfig: defaultBreathingConfig,
      },
      platform: "browser preview",
      metricCatalog: fallbackCatalog,
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
    app.streamName = normalizeStreamBase(app.preferences.streamName)
      || normalizeStreamBase(bootstrap.config?.streamName)
      || "Polar-H10";
    elements["stream-name"].value = app.streamName;
    elements["lsl-toggle"].checked = Boolean(bootstrap.config?.lslEnabled);
    elements["osc-toggle"].checked = Boolean(bootstrap.config?.oscEnabled);
    elements["platform-label"].textContent = String(bootstrap.platform || "local").toUpperCase();
    if (!isNative) elements["scan-caption"].textContent = "Interactive browser preview";

    renderMetricOptions();
    renderOutputs();
    installInteractions();
    await configureOutputs({ quiet: true });
    resizeCanvas();
    window.requestAnimationFrame(drawFrame);
    if (app.preferences.lastDevice) void scanDevices({ automatic: true });
  }

  function installInteractions() {
    elements["scan-button"].addEventListener("click", () => scanDevices());
    elements["disconnect-button"].addEventListener("click", disconnectDevice);
    elements["lsl-toggle"].addEventListener("change", configureOutputs);
    elements["osc-toggle"].addEventListener("change", configureOutputs);

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
      if (elements["output-dialog"].returnValue !== "confirm") return;
      const selected = elements["metric-options"].querySelectorAll(".metric-checkbox:checked");
      app.outputs = new Set([...selected].map((input) => input.value));
      app.breathingConfig = readBreathingConfig();
      app.browserBreathing = null;
      renderOutputs();
      configureOutputs();
    });
    elements["output-dialog"].querySelector('button[value="confirm"]').addEventListener("click", (event) => {
      if (!validateBreathingAxes()) event.preventDefault();
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
    restoreVisualizerLayout();
    window.requestAnimationFrame(() => arrangeVisualizers(false));
    elements["add-visualizer"].addEventListener("click", () => addVisualizer());
    elements["visualizer-empty-deck"].addEventListener("click", () => addVisualizer());
    elements["reset-workspace-layout"].addEventListener("click", resetWorkspaceLayout);
    installWorkspaceSplitters();
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
        : await new Promise((resolve) => window.setTimeout(() => resolve([
            { id: "preview-h10-a", name: "Polar H10 8F3A2C1B", rssi: -48 },
            { id: "preview-h10-b", name: "Polar H10 4D9E7A20", rssi: -63 },
          ]), 850));
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
    setTopStatus(automatic ? "Reconnecting to last used Polar H10" : "Connecting to Polar H10", "working");
    elements["input-state"].textContent = "Connecting";
    elements["device-name"].textContent = device.name;
    elements["connection-detail"].textContent = "Opening the low-energy connection…";
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
        handleNativeEvent({
          kind: "connection", connected: true, streaming: true, deviceName: device.name,
          batteryPercent: 86, attMtu: 64, message: "Raw ECG and accelerometer are streaming",
        }, device);
        startDemoSignal();
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
    }
  }

  async function disconnectDevice() {
    const previousGeneration = app.connectionGeneration;
    app.connectionGeneration += 1;
    try {
      if (isNative) await invoke("disconnect_device");
      stopDemoSignal();
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
    broadcastVisualizerData(event);
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
      }
    } else {
      app.currentDeviceId = null;
    }
    renderDevices(app.devices);
    elements["connection-card"].classList.toggle("connected", app.connected);
    elements["disconnect-button"].hidden = !app.connected;
    elements["connection-meta"].hidden = !app.connected;
    elements["device-name"].textContent = app.connected ? event.deviceName : "No sensor connected";
    elements["connection-detail"].textContent = app.connected ? event.message : "Scan for a nearby chest strap.";
    elements["battery-value"].textContent = event.batteryPercent == null ? "—" : `${event.batteryPercent}%`;
    resetLatencyMetric();
    elements["input-state"].textContent = app.connected ? "Streaming" : "Idle";
    setTopStatus(app.connected ? "Sensor connected · streams live" : "Ready to connect", app.connected ? "connected" : "idle");
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
      label.append(mark, copy, checkbox);
      return label;
    });
    elements["metric-options"].replaceChildren(...options);
    setMetricFamily(app.metricFamily);
  }

  function syncDialogSelection() {
    elements["metric-options"].querySelectorAll(".metric-checkbox").forEach((input) => {
      input.checked = app.outputs.has(input.value);
    });
    syncBreathingControls();
    elements["metric-search"].value = "";
    filterMetricOptions();
    updateBreathingConfigVisibility();
    updateDialogCount();
  }

  function updateDialogCount() {
    const selected = [...elements["metric-options"].querySelectorAll(".metric-checkbox:checked")];
    const count = selected.length;
    const ecgCount = selected.filter((input) => input.closest(".metric-option")?.dataset.family === "ecg").length;
    const accCount = count - ecgCount;
    elements["dialog-selection-count"].textContent = `${count} selected`;
    elements["dialog-selection-detail"].textContent = `${ecgCount} ECG · ${accCount} ACC`;
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
    elements["output-chips"].replaceChildren(...chips);
    const count = app.outputs.size;
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
      .filter(([id, definition]) => app.outputs.has(definition.parent || id))
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
      choices: availableVisualChoices().map(({ id, definition }) => ({ id, label: definition.label, unit: definition.unit })),
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
      visualizerChannel?.postMessage({
        type: "snapshot",
        target: message.sender,
        buffers: Object.fromEntries(Object.entries(buffers).map(([id, buffer]) => [id, Array.from(buffer.tail(buffer.capacity))])),
        breathingPhase: app.breathingPhase,
        connected: app.connected,
      });
      broadcastVisualizerConfig();
    } else if (message.type === "dock-view" && message.source) {
      addVisualizer(message.source);
      visualizerChannel?.postMessage({ type: "adopted", origin: message.origin, viewId: message.viewId });
    } else if (message.type === "adopted" && message.origin === mainWindowId) {
      removeVisualizer(message.viewId);
    }
  }

  async function configureOutputs({ quiet = false } = {}) {
    const streamName = normalizeStreamBase(elements["stream-name"].value);
    if (!streamName) {
      elements["stream-name"].setAttribute("aria-invalid", "true");
      updateStreamNamePreview();
      return;
    }
    elements["stream-name"].removeAttribute("aria-invalid");
    app.streamName = streamName;
    const config = {
      streamName,
      lslEnabled: elements["lsl-toggle"].checked,
      oscEnabled: elements["osc-toggle"].checked,
      outputs: [...app.outputs],
      breathingConfig: app.breathingConfig,
    };
    if (!isNative) {
      elements["stream-name"].value = streamName;
      app.preferences = preferences.saveStreamName(streamName);
      renderOutputs();
      elements["lsl-detail"].textContent = config.lslEnabled ? "Preview · liblsl is checked in the native app" : "Local network · time synchronized";
      elements["osc-detail"].textContent = config.oscEnabled ? "Preview · UDP localhost:9000" : "UDP · localhost:9000";
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

  function startDemoSignal() {
    stopDemoSignal();
    let lastMetric = 0;
    app.demoTimer = window.setInterval(() => {
      const ecg = [];
      const acc = [];
      for (let index = 0; index < 17; index += 1) {
        const phase = app.demoPhase + index / 130;
        const beatPhase = (phase * 1.18) % 1;
        const qrs = 880 * Math.exp(-Math.pow((beatPhase - 0.12) / 0.028, 2));
        const q = -180 * Math.exp(-Math.pow((beatPhase - 0.09) / 0.018, 2));
        const t = 150 * Math.exp(-Math.pow((beatPhase - 0.42) / 0.09, 2));
        ecg.push(Math.round(qrs + q + t + Math.sin(phase * 7) * 12));
      }
      for (let index = 0; index < 26; index += 1) {
        const phase = app.demoPhase + index / 200;
        acc.push({
          xMg: Math.round(Math.sin(phase * 4.5) * 85),
          yMg: Math.round(Math.cos(phase * 3.1) * 52),
          zMg: Math.round(995 + Math.sin(phase * 6.3) * 25),
        });
      }
      app.demoPhase += 0.13;
      handleNativeEvent({ kind: "ecg", sensorTimestampNs: 0, microvolts: ecg, estimatedLatencyMs: 131, samplesPerPacket: 17 });
      handleNativeEvent({ kind: "accelerometer", sensorTimestampNs: 0, samples: acc });
      if (app.demoPhase - lastMetric >= 1) {
        lastMetric = app.demoPhase;
        handleNativeEvent({ kind: "metrics", heartRateBpm: 71, rrIntervalsMs: [845 + Math.sin(app.demoPhase) * 18], rmssdMs: 28.4 });
      }
    }, 130);
  }

  function stopDemoSignal() {
    if (app.demoTimer) window.clearInterval(app.demoTimer);
    app.demoTimer = null;
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
