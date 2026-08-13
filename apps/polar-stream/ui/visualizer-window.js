(() => {
  "use strict";

  const definitions = {
    raw_ecg: { label: "Raw ECG", unit: "µV", rate: 130, color: "#d85151", symmetric: true },
    raw_acc: {
      label: "Raw accelerometer · X/Y/Z", unit: "mg", rate: 200,
      channels: [
        { buffer: "acc_x", label: "X", color: "#3b78aa", symmetric: true },
        { buffer: "acc_y", label: "Y", color: "#168259", symmetric: true },
        { buffer: "acc_z", label: "Z", color: "#a66d19", symmetric: true },
      ],
    },
    heart_rate: { label: "Heart rate", unit: "bpm", rate: 1, color: "#d85151" },
    rr_interval: { label: "RR interval", unit: "ms", rate: 2, color: "#6c62a8" },
    acc_magnitude: { label: "3D acceleration magnitude", unit: "g", rate: 200, color: "#3b78aa" },
    acc_breathing_waveform: { label: "Breathing magnitude · curve", unit: "0–1", rate: 200, color: "#3b78aa" },
    acc_breathing_circle: { label: "Breathing phase · circle", unit: "", rate: 60, color: "#3b78aa", kind: "breathing-circle" },
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
      const number = Number(value);
      if (!Number.isFinite(number)) return;
      this.values[this.cursor] = number;
      this.cursor = (this.cursor + 1) % this.capacity;
      this.length = Math.min(this.length + 1, this.capacity);
    }

    pushMany(values = []) { for (const value of values) this.push(value); }

    tail(count) {
      const size = Math.min(this.length, count);
      const result = new Float32Array(size);
      const start = (this.cursor - size + this.capacity) % this.capacity;
      const first = Math.min(size, this.capacity - start);
      result.set(this.values.subarray(start, start + first));
      if (first < size) result.set(this.values.subarray(0, size - first), first);
      return result;
    }

    latest() { return this.length ? this.values[(this.cursor - 1 + this.capacity) % this.capacity] : null; }
  }

  const bufferIds = new Set(Object.entries(definitions).flatMap(([id, definition]) => (
    definition.channels?.map((channel) => channel.buffer) || [id]
  )));
  const buffers = Object.fromEntries([...bufferIds].map((id) => [id, new RingBuffer()]));
  const parameters = new URLSearchParams(location.search);
  const detachedId = `detached-${parameters.get("token") || Math.random().toString(36).slice(2, 10)}`;
  const initialViewId = parameters.get("view") || "detached-primary";
  const initialSource = definitions[parameters.get("source")] ? parameters.get("source") : "raw_ecg";
  const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel("polar-stream-visualizers") : null;
  const deck = document.getElementById("detached-deck");
  const emptyDeck = document.getElementById("detached-empty");
  const connectionState = document.getElementById("detached-state");
  const views = new Map();
  let choices = Object.entries(definitions).map(([id, definition]) => ({ id, label: definition.label }));
  let template = null;
  let sequence = 0;
  let draggedViewId = null;
  let breathingPhase = 0;
  let connected = false;

  function viewDom(card) {
    return {
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
  }

  function createOptions() {
    return choices.map((choice) => {
      const option = document.createElement("option");
      option.value = choice.id;
      option.textContent = choice.label;
      return option;
    });
  }

  function registerView(card, id, source) {
    if (!template) template = card.cloneNode(true);
    const dom = viewDom(card);
    const view = {
      id, source, card, dom,
      circleLevel: 0.5,
      circleVelocity: 0,
      circleFrameAt: performance.now(),
      observer: null,
    };
    card.dataset.visualizerId = id;
    card.draggable = true;
    views.set(id, view);
    populateOptions(view);
    dom.source.addEventListener("change", () => {
      view.source = dom.source.value;
      updateLabels(view);
      updateDocumentTitle();
    });
    card.querySelector('[data-visualizer-action="close"]').addEventListener("click", () => removeView(id));
    card.querySelector('[data-visualizer-action="dock"]').addEventListener("click", () => dockView(view));
    const handle = card.querySelector(".visualizer-drag-handle");
    handle.addEventListener("pointerdown", () => {
      card.dataset.dragReady = "true";
      window.addEventListener("pointerup", () => { delete card.dataset.dragReady; }, { once: true });
    });
    card.addEventListener("dragstart", (event) => startDrag(event, view));
    card.addEventListener("dragend", () => finishDrag(view));
    view.observer = new ResizeObserver(() => resizeCanvas(view));
    view.observer.observe(dom.shell);
    emptyDeck.hidden = true;
    requestAnimationFrame(() => resizeCanvas(view));
    return view;
  }

  function populateOptions(view) {
    view.dom.source.replaceChildren(...createOptions());
    if (!choices.some((choice) => choice.id === view.source)) view.source = choices[0]?.id || "";
    view.dom.source.value = view.source;
    view.dom.source.disabled = !choices.length;
    updateLabels(view);
  }

  function updateLabels(view) {
    const definition = definitions[view.source];
    view.dom.unit.textContent = definition?.unit || "";
    view.dom.current.classList.toggle("stacked-value", Boolean(definition?.channels));
    view.dom.shell.classList.toggle("stacked-axes", Boolean(definition?.channels));
    view.dom.shell.classList.toggle("breathing-circle", definition?.kind === "breathing-circle");
    view.dom.windowLabel.textContent = definition?.kind === "breathing-circle" ? "Phase driven" : "5 second window";
    view.dom.scaleLabel.textContent = definition?.kind === "breathing-circle"
      ? "Inertial easing"
      : view.source === "acc_breathing_waveform" && definition?.unit === "0–1"
        ? "Normalized 0–1"
        : "Auto scale";
    const series = definition?.channels || (definition ? [{ label: definition.label, color: definition.color }] : []);
    view.dom.legend.replaceChildren(...series.map((item) => {
      const legend = document.createElement("span");
      legend.className = "legend-item";
      const line = document.createElement("i");
      line.className = "legend-line";
      line.style.background = item.color;
      const label = document.createElement("strong");
      label.textContent = item.label;
      legend.append(line, label);
      return legend;
    }));
    resizeCanvas(view);
  }

  function addView(source = "", { arrange = true } = {}) {
    if (!choices.length) return null;
    const represented = new Set([...views.values()].map((view) => view.source));
    const nextSource = choices.some((choice) => choice.id === source)
      ? source
      : choices.find((choice) => !represented.has(choice.id))?.id || choices[0].id;
    const id = `detached-view-${++sequence}`;
    const card = template.cloneNode(true);
    card.querySelectorAll("[id]").forEach((node) => node.removeAttribute("id"));
    const select = card.querySelector("select");
    select.id = `source-${id}`;
    card.querySelector("label").htmlFor = select.id;
    deck.insertBefore(card, emptyDeck);
    const view = registerView(card, id, nextSource);
    if (arrange) requestAnimationFrame(arrangeViews);
    updateDocumentTitle();
    return view;
  }

  function removeView(id) {
    const view = views.get(id);
    if (!view) return;
    view.observer?.disconnect();
    views.delete(id);
    view.card.remove();
    emptyDeck.hidden = views.size > 0;
    updateDocumentTitle();
    if (!views.size) window.setTimeout(() => window.close(), 40);
  }

  function arrangeViews() {
    const items = [...views.values()];
    if (!items.length) return;
    const columns = items.length === 1 ? 1 : items.length <= 4 ? 2 : Math.ceil(Math.sqrt(items.length));
    const rows = Math.ceil(items.length / columns);
    const width = Math.max(210, (deck.clientWidth - 10 * (columns - 1) - 6) / columns);
    const height = Math.max(225, (deck.clientHeight - 10 * (rows - 1) - 6) / rows);
    for (const view of items) {
      view.card.style.width = `${Math.min(deck.clientWidth - 6, width)}px`;
      view.card.style.height = `${height}px`;
    }
  }

  function resizeCanvas(view) {
    const bounds = view.dom.shell.getBoundingClientRect();
    const ratio = Math.min(devicePixelRatio || 1, 2);
    const width = Math.max(1, Math.round(bounds.width * ratio));
    const height = Math.max(1, Math.round(bounds.height * ratio));
    if (view.dom.canvas.width !== width) view.dom.canvas.width = width;
    if (view.dom.canvas.height !== height) view.dom.canvas.height = height;
  }

  function startDrag(event, view) {
    if (view.card.dataset.dragReady !== "true") {
      event.preventDefault();
      return;
    }
    draggedViewId = view.id;
    view.card.classList.add("dragging");
    const payload = { source: view.source, origin: detachedId, viewId: view.id };
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("application/x-polar-visualizer", JSON.stringify(payload));
    event.dataTransfer.setData("text/plain", JSON.stringify(payload));
  }

  function finishDrag(view) {
    view.card.classList.remove("dragging");
    delete view.card.dataset.dragReady;
    draggedViewId = null;
  }

  function installDropMerging() {
    deck.addEventListener("dragover", (event) => {
      const transferTypes = [...event.dataTransfer.types];
      const compatible = transferTypes.includes("application/x-polar-visualizer") || transferTypes.includes("text/plain");
      if (!draggedViewId && !compatible) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
      const cards = [...deck.querySelectorAll(".visualizer-card:not(.dragging)")];
      cards.forEach((card) => card.classList.remove("drop-before"));
      const before = cards.find((card) => {
        const box = card.getBoundingClientRect();
        return event.clientY < box.top + box.height / 2
          || (event.clientY <= box.bottom && event.clientX < box.left + box.width / 2);
      });
      if (draggedViewId) {
        const dragged = views.get(draggedViewId)?.card;
        if (dragged) deck.insertBefore(dragged, before || emptyDeck);
      } else {
        before?.classList.add("drop-before");
      }
    });
    deck.addEventListener("drop", (event) => {
      event.preventDefault();
      deck.querySelectorAll(".drop-before").forEach((card) => card.classList.remove("drop-before"));
      if (draggedViewId) return;
      try {
        const raw = event.dataTransfer.getData("application/x-polar-visualizer") || event.dataTransfer.getData("text/plain");
        const payload = JSON.parse(raw);
        if (!payload?.source) return;
        addView(payload.source);
        channel?.postMessage({ type: "adopted", origin: payload.origin, viewId: payload.viewId });
      } catch { /* unrelated drop */ }
    });
  }

  function dockView(view) {
    channel?.postMessage({ type: "dock-view", source: view.source, origin: detachedId, viewId: view.id });
  }

  function updateDocumentTitle() {
    const labels = [...views.values()].map((view) => definitions[view.source]?.label).filter(Boolean);
    document.title = `${labels[0] || "Visualization"}${labels.length > 1 ? ` +${labels.length - 1}` : ""} · Polar Stream`;
  }

  function ingest(event) {
    if (!event) return;
    if (event.kind === "ecg") {
      buffers.raw_ecg.pushMany(event.microvolts);
    } else if (event.kind === "accelerometer") {
      const samples = event.samples || [];
      for (const sample of samples) {
        const x = Number(sample.xMg ?? sample.x_mg ?? 0);
        const y = Number(sample.yMg ?? sample.y_mg ?? 0);
        const z = Number(sample.zMg ?? sample.z_mg ?? 0);
        buffers.acc_x.push(x);
        buffers.acc_y.push(y);
        buffers.acc_z.push(z);
        buffers.acc_magnitude.push(Math.hypot(x, y, z) / 1000);
      }
      const breathing = event.visualizerBreathing || event.breathingSamples || event.breathing_samples || [];
      for (const sample of breathing) buffers.acc_breathing_waveform.push(sample.waveform ?? sample);
      breathingPhase = Number(event.visualizerBreathingPhase ?? breathing.at(-1)?.phase ?? breathingPhase);
    } else if (event.kind === "metrics") {
      buffers.heart_rate.push(event.heartRateBpm ?? event.heart_rate_bpm);
      buffers.rr_interval.pushMany(event.rrIntervalsMs ?? event.rr_intervals_ms);
      buffers.rmssd.push(event.rmssdMs ?? event.rmssd_ms);
    } else if (event.kind === "connection") {
      connected = Boolean(event.connected);
      renderConnectionState();
    }
  }

  function renderConnectionState() {
    connectionState.classList.toggle("live", connected);
    connectionState.lastChild.textContent = connected ? "Live from main workspace" : "Waiting for main workspace";
  }

  function handleChannel(messageEvent) {
    const message = messageEvent.data || {};
    if (message.type === "data") {
      ingest(message.event);
    } else if (message.type === "snapshot" && message.target === detachedId) {
      for (const [id, values] of Object.entries(message.buffers || {})) buffers[id]?.pushMany(values);
      breathingPhase = Number(message.breathingPhase) || 0;
      connected = Boolean(message.connected);
      renderConnectionState();
    } else if (message.type === "config") {
      choices = (message.choices || []).filter((choice) => definitions[choice.id]);
      definitions.acc_breathing_waveform.unit = message.breathingNormalized === false ? "g projection" : "0–1";
      for (const view of views.values()) populateOptions(view);
    } else if (message.type === "adopted" && message.origin === detachedId) {
      removeView(message.viewId);
    }
  }

  function formatValue(value, digits = 1) {
    const number = Number(value);
    if (!Number.isFinite(number)) return "—";
    return number.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits });
  }

  function shortAxis(value) {
    const absolute = Math.abs(value);
    if (absolute >= 1000) return `${(value / 1000).toFixed(1)}k`;
    if (absolute < 10) return value.toFixed(1);
    return Math.round(value).toString();
  }

  function drawView(view) {
    const definition = definitions[view.source];
    const canvas = view.dom.canvas;
    const context = canvas.getContext("2d", { alpha: true });
    if (!definition) {
      context.clearRect(0, 0, canvas.width, canvas.height);
      view.dom.empty.hidden = false;
      return;
    }
    if (definition.kind === "breathing-circle") return drawCircle(context, canvas, view);
    if (definition.channels) return drawStacked(context, canvas, definition, view);
    const buffer = buffers[view.source];
    const values = buffer.tail(Math.max(10, Math.ceil(definition.rate * 5)));
    view.dom.empty.hidden = values.length > 1;
    view.dom.current.textContent = formatValue(buffer.latest(), definition.unit === "g" ? 3 : definition.unit === "bpm" ? 0 : 1);
    context.clearRect(0, 0, canvas.width, canvas.height);
    if (values.length < 2) return;
    let min = Math.min(...values);
    let max = Math.max(...values);
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
    drawLine(context, canvas, values, min, max, definition.color);
  }

  function drawLine(context, canvas, values, min, max, color, box = null) {
    const width = canvas.width;
    const height = canvas.height;
    const area = box || { left: width * 0.035, top: height * 0.08, width: width * 0.93, height: height * 0.84 };
    const range = max - min || 1;
    context.beginPath();
    for (let index = 0; index < values.length; index += 1) {
      const x = area.left + (index / (values.length - 1)) * area.width;
      const y = area.top + (1 - (values[index] - min) / range) * area.height;
      if (!index) context.moveTo(x, y); else context.lineTo(x, y);
    }
    context.strokeStyle = color;
    context.lineWidth = Math.max(1.4, (devicePixelRatio || 1) * 0.9);
    context.lineJoin = "round";
    context.lineCap = "round";
    context.stroke();
  }

  function drawStacked(context, canvas, definition, view) {
    const count = Math.max(10, Math.ceil(definition.rate * 5));
    const series = definition.channels.map((channel) => ({ ...channel, buffer: buffers[channel.buffer], values: buffers[channel.buffer].tail(count) }));
    const hasData = series.some((item) => item.values.length > 1);
    view.dom.empty.hidden = hasData;
    view.dom.current.textContent = series.map((item) => `${item.label} ${formatValue(item.buffer.latest(), 0)}`).join(" · ");
    view.dom.yMax.textContent = "—";
    view.dom.yMin.textContent = "—";
    context.clearRect(0, 0, canvas.width, canvas.height);
    if (!hasData) return;
    const ratio = Math.min(devicePixelRatio || 1, 2);
    const left = Math.max(canvas.width * 0.08, 52 * ratio);
    const right = canvas.width * 0.035;
    const padY = canvas.height * 0.035;
    const gap = 10 * ratio;
    const laneHeight = (canvas.height - padY * 2 - gap * (series.length - 1)) / series.length;
    series.forEach((item, lane) => {
      const top = padY + lane * (laneHeight + gap);
      context.fillStyle = item.color;
      context.font = `700 ${Math.round(10 * ratio)}px ui-monospace, monospace`;
      context.textBaseline = "middle";
      context.fillText(item.label, 10 * ratio, top + laneHeight / 2);
      if (item.values.length < 2) return;
      let min = Math.min(...item.values);
      let max = Math.max(...item.values);
      const extent = Math.max(Math.abs(min), Math.abs(max), 1) * 1.08;
      min = -extent;
      max = extent;
      drawLine(context, canvas, item.values, min, max, item.color, { left, top, width: canvas.width - left - right, height: laneHeight });
    });
  }

  function drawCircle(context, canvas, view) {
    const hasData = buffers.acc_breathing_waveform.length > 1;
    const label = breathingPhase > 0 ? "Inhaling" : breathingPhase < 0 ? "Exhaling" : "Pausing";
    view.dom.empty.hidden = hasData;
    view.dom.current.textContent = hasData ? label : "—";
    view.dom.yMax.textContent = "";
    view.dom.yMin.textContent = "";
    const now = performance.now();
    const elapsed = Math.min(0.05, Math.max(0, (now - view.circleFrameAt) / 1000));
    view.circleFrameAt = now;
    const velocity = breathingPhase > 0 ? 0.95 : breathingPhase < 0 ? -0.95 : 0;
    view.circleVelocity += (velocity - view.circleVelocity) * (1 - Math.exp(-(breathingPhase ? 4.2 : 1.7) * elapsed));
    view.circleLevel += view.circleVelocity * elapsed * (view.circleVelocity >= 0 ? 1 - view.circleLevel : view.circleLevel) * 1.65;
    view.circleLevel = Math.min(0.985, Math.max(0.015, view.circleLevel));
    context.clearRect(0, 0, canvas.width, canvas.height);
    if (!hasData) return;
    const ratio = Math.min(devicePixelRatio || 1, 2);
    const x = canvas.width / 2;
    const y = canvas.height / 2;
    const minRadius = Math.min(canvas.width, canvas.height) * 0.13;
    const maxRadius = Math.min(canvas.width, canvas.height) * 0.36;
    const radius = minRadius + view.circleLevel * (maxRadius - minRadius);
    context.beginPath();
    context.arc(x, y, maxRadius, 0, Math.PI * 2);
    context.strokeStyle = "rgba(59, 120, 170, 0.12)";
    context.setLineDash([4 * ratio, 7 * ratio]);
    context.stroke();
    context.setLineDash([]);
    context.beginPath();
    context.arc(x, y, radius, 0, Math.PI * 2);
    context.fillStyle = "rgba(230, 240, 248, 0.82)";
    context.fill();
    context.strokeStyle = "#3b78aa";
    context.lineWidth = 1.6 * ratio;
    context.stroke();
    context.fillStyle = "#2b618d";
    context.font = `700 ${Math.round(12 * ratio)}px Aptos, sans-serif`;
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillText(label, x, y);
    context.textAlign = "left";
  }

  function drawFrame() {
    for (const view of views.values()) drawView(view);
    requestAnimationFrame(drawFrame);
  }

  const primaryCard = deck.querySelector(".visualizer-card");
  registerView(primaryCard, initialViewId, initialSource);
  installDropMerging();
  document.getElementById("detached-add").addEventListener("click", () => addView());
  emptyDeck.addEventListener("click", () => addView());
  document.getElementById("dock-all").addEventListener("click", () => {
    for (const view of [...views.values()]) dockView(view);
  });
  channel?.addEventListener("message", handleChannel);
  channel?.postMessage({ type: "request-snapshot", sender: detachedId });
  if (!channel) connectionState.lastChild.textContent = "Live sharing is unavailable in this WebView";
  requestAnimationFrame(() => {
    arrangeViews();
    drawFrame();
  });
})();
