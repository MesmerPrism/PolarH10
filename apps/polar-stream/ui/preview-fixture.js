(function installPreviewFixture(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  if (root) root.PolarPreviewFixture = api;
})(typeof globalThis === "object" ? globalThis : this, () => {
  "use strict";

  const schemaVersion = 1;
  const sourceMarker = "real-polar-h10-recording";
  const ecgRateHz = 130;
  const accRateHz = 200;
  const maxDurationMs = 10 * 60 * 1000;

  function validateFixture(value) {
    if (!value || typeof value !== "object") fail("The preview fixture must be a JSON object.");
    if (value.schemaVersion !== schemaVersion) fail(`Unsupported preview fixture schema ${value.schemaVersion}.`);
    if (value.source !== sourceMarker) fail("The preview fixture is not marked as a real Polar H10 recording.");
    if (value.deviceModel !== "Polar H10") fail("The preview fixture has an unexpected device model.");
    const durationMs = requireInteger(value.durationMs, "durationMs", 1000, maxDurationMs);
    if (durationMs % 1000 !== 0) fail("durationMs must be a whole number of seconds.");
    if (value.ecg?.sampleRateHz !== ecgRateHz) fail(`ECG must be recorded at ${ecgRateHz} Hz.`);
    if (value.accelerometer?.sampleRateHz !== accRateHz || value.accelerometer?.unit !== "mg") {
      fail(`Accelerometer data must be recorded at ${accRateHz} Hz in mg.`);
    }

    const expectedEcg = durationMs / 1000 * ecgRateHz;
    const expectedAcc = durationMs / 1000 * accRateHz;
    if (!Array.isArray(value.ecg.microvolts) || value.ecg.microvolts.length !== expectedEcg) {
      fail(`ECG sample count must be exactly ${expectedEcg}.`);
    }
    if (!Array.isArray(value.accelerometer.samples) || value.accelerometer.samples.length !== expectedAcc) {
      fail(`Accelerometer sample count must be exactly ${expectedAcc}.`);
    }
    for (const sample of value.ecg.microvolts) {
      requireInteger(sample, "ECG sample", -8_388_608, 8_388_607);
    }
    for (const sample of value.accelerometer.samples) {
      if (!Array.isArray(sample) || sample.length !== 3) fail("Every accelerometer sample must contain X, Y, and Z.");
      for (const axis of sample) requireInteger(axis, "Accelerometer axis", -32_768, 32_767);
    }

    const metricEvents = Array.isArray(value.metricEvents) ? value.metricEvents : [];
    let previousOffset = -1;
    for (const event of metricEvents) {
      const offset = requireInteger(event?.offsetMs, "Metric event offset", 0, durationMs - 1);
      if (offset < previousOffset) fail("Metric events must be ordered by offsetMs.");
      previousOffset = offset;
      requireInteger(event.heartRateBpm, "Heart rate", 0, 300);
      if (!Array.isArray(event.rrIntervalsMs)) fail("Metric RR intervals must be an array.");
      for (const interval of event.rrIntervalsMs) requireFinite(interval, "RR interval", 0, 10_000);
      if (event.rmssdMs != null) requireFinite(event.rmssdMs, "RMSSD", 0, 10_000);
    }
    return value;
  }

  class LoopPlayer {
    constructor(fixture, emit, options = {}) {
      this.fixture = validateFixture(fixture);
      if (typeof emit !== "function") fail("Preview playback requires an event receiver.");
      this.emit = emit;
      this.now = options.now || (() => performance.now());
      this.schedule = options.schedule || ((callback) => setInterval(callback, 40));
      this.cancel = options.cancel || ((timer) => clearInterval(timer));
      this.onLoop = options.onLoop || (() => {});
      this.timer = null;
      this.startedAt = 0;
      this.loopIndex = 0;
      this.ecgCursor = 0;
      this.accCursor = 0;
      this.metricCursor = 0;
    }

    start() {
      this.stop();
      this.startedAt = this.now();
      this.resetCursors(0);
      this.tickAt(this.startedAt);
      this.timer = this.schedule(() => this.tickAt(this.now()));
    }

    stop() {
      if (this.timer != null) this.cancel(this.timer);
      this.timer = null;
    }

    tickAt(nowMs) {
      const elapsedMs = Math.max(0, Number(nowMs) - this.startedAt);
      const nextLoop = Math.floor(elapsedMs / this.fixture.durationMs);
      if (nextLoop !== this.loopIndex) {
        if (nextLoop === this.loopIndex + 1) {
          this.emitThrough(this.fixture.ecg.microvolts.length, this.fixture.accelerometer.samples.length, this.fixture.durationMs);
        }
        this.resetCursors(nextLoop);
        this.onLoop(nextLoop);
      }
      const loopTimeMs = elapsedMs - nextLoop * this.fixture.durationMs;
      const ecgTarget = Math.min(
        this.fixture.ecg.microvolts.length,
        Math.floor(loopTimeMs * this.fixture.ecg.sampleRateHz / 1000),
      );
      const accTarget = Math.min(
        this.fixture.accelerometer.samples.length,
        Math.floor(loopTimeMs * this.fixture.accelerometer.sampleRateHz / 1000),
      );
      this.emitThrough(ecgTarget, accTarget, loopTimeMs);
    }

    resetCursors(loopIndex) {
      this.loopIndex = loopIndex;
      this.ecgCursor = 0;
      this.accCursor = 0;
      this.metricCursor = 0;
    }

    emitThrough(ecgTarget, accTarget, loopTimeMs) {
      while (this.ecgCursor < ecgTarget) {
        const end = Math.min(ecgTarget, this.ecgCursor + 260);
        const microvolts = this.fixture.ecg.microvolts.slice(this.ecgCursor, end);
        this.emit({
          kind: "ecg",
          sensorTimestampNs: this.timestampNs(this.ecgCursor, this.fixture.ecg.sampleRateHz),
          microvolts,
          estimatedLatencyMs: Math.round(microvolts.length * 1000 / this.fixture.ecg.sampleRateHz),
          samplesPerPacket: microvolts.length,
        });
        this.ecgCursor = end;
      }
      while (this.accCursor < accTarget) {
        const end = Math.min(accTarget, this.accCursor + 400);
        const samples = this.fixture.accelerometer.samples
          .slice(this.accCursor, end)
          .map(([xMg, yMg, zMg]) => ({ xMg, yMg, zMg }));
        this.emit({
          kind: "accelerometer",
          sensorTimestampNs: this.timestampNs(this.accCursor, this.fixture.accelerometer.sampleRateHz),
          samples,
        });
        this.accCursor = end;
      }
      while (this.metricCursor < this.fixture.metricEvents.length) {
        const event = this.fixture.metricEvents[this.metricCursor];
        if (event.offsetMs > loopTimeMs) break;
        this.emit({
          kind: "metrics",
          heartRateBpm: event.heartRateBpm,
          rrIntervalsMs: event.rrIntervalsMs,
          rmssdMs: event.rmssdMs,
        });
        this.metricCursor += 1;
      }
    }

    timestampNs(sampleIndex, sampleRateHz) {
      const loopOffsetMs = this.loopIndex * this.fixture.durationMs;
      return Math.round((loopOffsetMs + sampleIndex * 1000 / sampleRateHz) * 1_000_000);
    }
  }

  function requireInteger(value, label, minimum, maximum) {
    if (!Number.isInteger(value) || value < minimum || value > maximum) {
      fail(`${label} must be an integer from ${minimum} to ${maximum}.`);
    }
    return value;
  }

  function requireFinite(value, label, minimum, maximum) {
    if (!Number.isFinite(value) || value < minimum || value > maximum) {
      fail(`${label} must be a finite number from ${minimum} to ${maximum}.`);
    }
    return value;
  }

  function fail(message) {
    throw new Error(message);
  }

  return Object.freeze({ validateFixture, LoopPlayer, schemaVersion, sourceMarker });
});
