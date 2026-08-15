(function installMetricLibrary(root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  if (root) root.PolarMetricLibrary = api;
})(typeof globalThis === "object" ? globalThis : this, () => {
  "use strict";

  const sources = Object.freeze({
    polarSdk: {
      label: "Polar BLE SDK · PMD technical documentation",
      url: "https://github.com/polarofficial/polar-ble-sdk/tree/4.0.0/technical_documentation",
    },
    polarHrv: {
      label: "Schaffarczyk et al. (2022) · Polar H10 HRV validity",
      url: "https://doi.org/10.3390/s22176536",
    },
    hrvOverview: {
      label: "Shaffer & Ginsberg (2017) · HRV metrics and norms",
      url: "https://doi.org/10.3389/fpubh.2017.00258",
    },
    hrvGuidelines: {
      label: "Quigley et al. (2024) · HR/HRV measurement guidelines",
      url: "https://doi.org/10.1111/psyp.14604",
    },
    hrvStandards: {
      label: "ESC/NASPE Task Force (1996) · HRV standards",
      url: "https://doi.org/10.1161/01.CIR.93.5.1043",
    },
    rmssdSd1: {
      label: "Ciccone et al. (2017) · RMSSD and SD1 equivalence",
      url: "https://doi.org/10.1002/mus.25573",
    },
    excitePaper: {
      label: "Quintero et al. (2021) · Excite-O-Meter",
      url: "https://doi.org/10.1109/ISMAR52148.2021.00052",
    },
    exciteProject: {
      label: "Excite-O-Meter project and downloads",
      url: "https://sites.google.com/view/exciteometer/eom",
    },
    accRespiration: {
      label: "Schipper et al. (2021) · chest-ACC respiration",
      url: "https://doi.org/10.1088/1361-6579/abf01f",
    },
    repoBreathing: {
      label: "PolarH10 · ACC breathing method and validation status",
      url: "https://mesmerprism.github.io/PolarH10/reference/breathing-formulas.html",
    },
  });

  const hrvCitations = [sources.hrvOverview, sources.hrvGuidelines, sources.hrvStandards];
  const knowledge = Object.freeze({
    raw_ecg: {
      summary: "Raw ECG is the H10's 130 Hz single-channel electrical waveform in microvolts. It is useful for waveform inspection and custom signal processing, but this research stream is not a diagnostic 12-lead ECG.",
      citations: [sources.polarSdk, sources.polarHrv],
    },
    heart_rate: {
      summary: "Heart rate is the device-reported number of cardiac cycles per minute. It is an inverse transform of beat interval and is highly context-sensitive, so it should not be treated as a unique readout of stress or emotion.",
      citations: [sources.hrvGuidelines, sources.polarHrv],
    },
    rr_interval: {
      summary: "RR interval is the elapsed time between successive detected heartbeats and is the input to the HRV metrics below. Artifact handling, posture, breathing, movement, and recording context all affect interpretation.",
      citations: [sources.polarHrv, sources.hrvGuidelines],
    },
    mean_nn: {
      summary: "Mean NN is the average accepted normal-to-normal beat interval inside the chosen window. It describes the central beat period; it does not itself quantify variability.",
      citations: hrvCitations,
      windowed: true,
    },
    mean_hr: {
      summary: "Mean heart rate converts the window's mean NN interval to beats per minute. Averaging the interval first is explicit and reproducible, but the value remains dependent on activity and recording conditions.",
      citations: hrvCitations,
      windowed: true,
    },
    rmssd: {
      summary: "RMSSD summarizes short-term beat-to-beat variation and is commonly interpreted as a vagally weighted HRV measure under controlled conditions. Window length, artifacts, respiration, and heart rate affect comparisons, so it is not a stand-alone stress or health score.",
      citations: hrvCitations,
      windowed: true,
    },
    ln_rmssd: {
      summary: "lnRMSSD is the natural-log transform of RMSSD, used to compress a right-skewed scale and support comparisons or statistical modeling. It retains RMSSD's physiological and measurement limitations.",
      citations: hrvCitations,
      windowed: true,
    },
    sdnn: {
      summary: "SDNN is the standard deviation of accepted NN intervals and reflects overall variability present in the selected recording window. Values from short, ultra-short, and 24-hour recordings are not interchangeable.",
      citations: hrvCitations,
      windowed: true,
    },
    pnn50: {
      summary: "pNN50 is the percentage of adjacent accepted NN pairs that differ by more than 50 ms. It is a conventional time-domain HRV measure, but it can be unstable in short windows and depends on clean beat detection.",
      citations: hrvCitations,
      windowed: true,
    },
    sd1: {
      summary: "SD1 is the short axis of the Poincaré representation and equals RMSSD divided by √2, so it carries the same short-term variability information on a different scale. Reporting both as independent evidence would be redundant.",
      citations: [sources.rmssdSd1, sources.hrvOverview, sources.hrvGuidelines],
      windowed: true,
    },
    excitement_index: {
      summary: "The Excite-O-Meter paper proposed a 0–1 cardiovascular-reactivity score by inverting standardized RR and five-beat RMSSD percentiles. Its four-person feasibility study did not significantly distinguish high- from low-arousal videos, and Polar Stream uses a causal rolling-baseline adaptation rather than the paper's post-session calculation.",
      citations: [sources.excitePaper, sources.exciteProject, sources.hrvGuidelines],
      windowed: true,
      externalPreview: sources.exciteProject,
    },
    raw_acc: {
      summary: "Raw accelerometer output is the H10's 200 Hz X/Y/Z chest motion in milli-g. It preserves the sensor channels for downstream movement analysis without claiming a physiological construct by itself.",
      citations: [sources.polarSdk],
    },
    acc_magnitude: {
      summary: "Three-dimensional acceleration magnitude is √(x²+y²+z²), expressed here in g. It removes direction and combines gravity with dynamic motion, so posture and movement both shape the trace.",
      citations: [sources.polarSdk],
    },
    acc_breathing_magnitude: {
      summary: "This experimental output projects chest acceleration into a smoothed breathing-like waveform. Chest accelerometry can capture respiratory motion, but this repository-specific H10 method has not yet been externally validated and should be checked against a reference sensor.",
      citations: [sources.accRespiration, sources.repoBreathing],
    },
    acc_breathing_phase: {
      summary: "This experimental classifier labels local movement as inhale, pause, or exhale from the ACC breathing projection. It is orientation- and motion-sensitive and has not been validated as a respiratory measurement for the Polar H10.",
      citations: [sources.accRespiration, sources.repoBreathing],
    },
  });

  function normalizeMetricSettings(value = {}) {
    const result = {};
    for (const id of Object.keys(knowledge)) {
      if (!knowledge[id].windowed) continue;
      const seconds = Number(value[id]?.windowSeconds ?? value[id]?.window_seconds ?? 60);
      result[id] = { windowSeconds: Math.min(300, Math.max(10, Number.isFinite(seconds) ? seconds : 60)) };
    }
    return result;
  }

  function preview(fixture, metricId, metricSettings = {}, breathingConfig = {}) {
    if (!fixture) return emptyPreview("Recorded preview data is still loading.");
    const info = knowledge[metricId] || {
      summary: "This output is available to downstream tools.",
      citations: [],
    };
    const settings = normalizeMetricSettings(metricSettings);
    const windowSeconds = settings[metricId]?.windowSeconds || 60;

    if (metricId === "raw_ecg") {
      return sampledPreview(
        [{ label: "ECG", color: "#d85151", values: fixture.ecg.microvolts, rate: fixture.ecg.sampleRateHz }],
        12,
      );
    }
    if (metricId === "raw_acc") {
      return sampledPreview([
        { label: "X", color: "#3b78aa", values: fixture.accelerometer.samples.map((sample) => sample[0]), rate: fixture.accelerometer.sampleRateHz },
        { label: "Y", color: "#168259", values: fixture.accelerometer.samples.map((sample) => sample[1]), rate: fixture.accelerometer.sampleRateHz },
        { label: "Z", color: "#a66d19", values: fixture.accelerometer.samples.map((sample) => sample[2]), rate: fixture.accelerometer.sampleRateHz },
      ], 12);
    }
    if (metricId === "acc_magnitude") {
      const values = fixture.accelerometer.samples.map(([x, y, z]) => Math.hypot(x, y, z) / 1000);
      return sampledPreview([{ label: "Magnitude", color: "#3b78aa", values, rate: fixture.accelerometer.sampleRateHz }], 12);
    }
    if (metricId === "acc_breathing_magnitude" || metricId === "acc_breathing_phase") {
      return breathingPreview(fixture, metricId, breathingConfig);
    }

    const rr = rrSamples(fixture);
    if (metricId === "heart_rate") {
      return eventPreview(fixture.metricEvents.map((event) => ({ time: event.offsetMs / 1000, value: event.heartRateBpm })), "Heart rate", "#d85151");
    }
    if (metricId === "rr_interval") return eventPreview(rr, "RR interval", "#6c62a8");
    if (metricId === "excitement_index") {
      return eventPreview(excitementSeries(rr, windowSeconds), "Excitement", "#b36a22", warmupNote(windowSeconds, fixture));
    }
    const series = rrMetricSeries(rr, metricId, windowSeconds);
    return eventPreview(series, info.label || metricId, "#168259", warmupNote(windowSeconds, fixture));
  }

  function rrSamples(fixture) {
    const values = [];
    for (const event of fixture.metricEvents || []) {
      const intervals = event.rrIntervalsMs || [];
      let beforeMs = intervals.slice(1).reduce((sum, value) => sum + value, 0);
      for (const interval of intervals) {
        values.push({ time: Math.max(0, (event.offsetMs - beforeMs) / 1000), value: Number(interval) });
        beforeMs -= interval;
      }
    }
    return values.sort((a, b) => a.time - b.time);
  }

  function rrMetricSeries(samples, id, windowSeconds) {
    const result = [];
    for (let index = 0; index < samples.length; index += 1) {
      const current = samples[index];
      const values = durationWindowValues(samples, index, windowSeconds);
      const metrics = timeDomainMetrics(values);
      const value = metrics?.[id];
      if (Number.isFinite(value)) result.push({ time: current.time, value });
    }
    return result;
  }

  function timeDomainMetrics(values) {
    if (values.length < 2) return null;
    const meanNn = mean(values);
    const differences = values.slice(1).map((value, index) => value - values[index]);
    const rmssd = Math.sqrt(mean(differences.map((value) => value * value)));
    const sdnn = Math.sqrt(values.reduce((sum, value) => sum + (value - meanNn) ** 2, 0) / (values.length - 1));
    return {
      mean_nn: meanNn,
      mean_hr: 60000 / meanNn,
      rmssd,
      ln_rmssd: Math.log(Math.max(Number.MIN_VALUE, rmssd)),
      sdnn,
      pnn50: 100 * differences.filter((value) => Math.abs(value) > 50).length / differences.length,
      sd1: rmssd / Math.SQRT2,
    };
  }

  function excitementSeries(samples, baselineSeconds) {
    const result = [];
    for (let index = 0; index < samples.length; index += 1) {
      const values = durationWindowValues(samples, index, baselineSeconds);
      if (values.length < 10) continue;
      const rmssdHistory = [];
      for (let end = 2; end <= values.length; end += 1) {
        const window = values.slice(Math.max(0, end - 5), end);
        const rmssd = timeDomainMetrics(window)?.rmssd;
        if (Number.isFinite(rmssd)) rmssdHistory.push(rmssd);
      }
      const rrZ = zScore(values.at(-1), values);
      const rmssdZ = zScore(rmssdHistory.at(-1), rmssdHistory);
      if (!Number.isFinite(rrZ) || !Number.isFinite(rmssdZ)) continue;
      result.push({ time: samples[index].time, value: Math.max(0, Math.min(1, 1 - (normalCdf(rrZ) + normalCdf(rmssdZ)) / 2)) });
    }
    return result;
  }

  function durationWindowValues(samples, endIndex, seconds) {
    const values = [];
    let retainedMs = 0;
    for (let index = endIndex; index >= 0; index -= 1) {
      values.push(samples[index].value);
      retainedMs += samples[index].value;
      if (retainedMs >= seconds * 1000) break;
    }
    return values.reverse();
  }

  function breathingPreview(fixture, metricId, value = {}) {
    const axes = ["x", "y", "z"].filter((axis) => (value.axes || ["x", "z"]).includes(axis));
    const enabled = ["x", "y", "z"].map((axis) => axes.includes(axis));
    const smoothingSeconds = Math.min(3, Math.max(0.2, Number(value.smoothingWindowSeconds) || 0.75));
    const sensitivity = Math.min(1, Math.max(0, Number(value.sensitivity) || 0.6));
    const normalize = value.normalize !== false;
    const invert = Boolean(value.invert);
    const baseline = [null, null, null];
    const smoothing = [];
    const lows = [];
    const highs = [];
    let lowHead = 0;
    let highHead = 0;
    const samples = [];
    let previous = null;
    const windowSize = Math.max(1, Math.round(smoothingSeconds * fixture.accelerometer.sampleRateHz));

    for (let index = 0; index < fixture.accelerometer.samples.length; index += 1) {
      const vector = fixture.accelerometer.samples[index].map((axis) => axis / 1000);
      let projection = 0;
      let count = 0;
      for (let axis = 0; axis < 3; axis += 1) {
        if (!enabled[axis]) continue;
        if (baseline[axis] == null) baseline[axis] = vector[axis];
        baseline[axis] += (vector[axis] - baseline[axis]) * 0.001;
        projection += vector[axis] - baseline[axis];
        count += 1;
      }
      projection /= Math.max(1, count);
      if (invert) projection *= -1;
      smoothing.push(projection);
      if (smoothing.length > windowSize) smoothing.shift();
      const smoothed = mean(smoothing);
      while (lows.length > lowHead && lows.at(-1).value >= smoothed) lows.pop();
      while (highs.length > highHead && highs.at(-1).value <= smoothed) highs.pop();
      lows.push({ index, value: smoothed });
      highs.push({ index, value: smoothed });
      while (lows[lowHead]?.index <= index - 4000) lowHead += 1;
      while (highs[highHead]?.index <= index - 4000) highHead += 1;
      const low = lows[lowHead].value;
      const high = highs[highHead].value;
      const ready = Math.min(index + 1, 4000) >= 200 && high - low >= 0.0005;
      const normalized = ready ? Math.min(1, Math.max(0, (smoothed - low) / (high - low))) : 0.5;
      const threshold = 0.00015 + (1 - sensitivity) * 0.00235;
      const delta = previous == null ? 0 : normalized - previous;
      const phase = !ready ? 0 : delta > threshold ? 1 : delta < -threshold ? -1 : 0;
      previous = normalized;
      samples.push(metricId === "acc_breathing_phase" ? phase : normalize ? normalized : smoothed);
    }

    return sampledPreview([{
      label: metricId === "acc_breathing_phase" ? "Phase" : "Breathing projection",
      color: "#3b78aa",
      values: samples,
      rate: fixture.accelerometer.sampleRateHz,
    }], 12);
  }

  function sampledPreview(series, visibleSeconds) {
    const normalized = series.map((entry) => {
      const count = Math.min(entry.values.length, Math.round(entry.rate * visibleSeconds));
      const start = Math.max(0, entry.values.length - count);
      return {
        label: entry.label,
        color: entry.color,
        values: entry.values.slice(start),
        times: entry.values.slice(start).map((_, index) => index / entry.rate),
      };
    });
    const current = normalized[0]?.values.at(-1);
    return { series: normalized, current, durationSeconds: visibleSeconds, note: "Recorded Polar H10 data" };
  }

  function eventPreview(samples, label, color, note = "Recorded Polar H10 data") {
    const clean = samples.filter((sample) => Number.isFinite(sample.value));
    return {
      series: [{ label, color, values: clean.map((sample) => sample.value), times: clean.map((sample) => sample.time) }],
      current: clean.at(-1)?.value,
      durationSeconds: clean.at(-1)?.time || 0,
      note,
    };
  }

  function emptyPreview(note) {
    return { series: [], current: null, durationSeconds: 0, note };
  }

  function warmupNote(windowSeconds, fixture) {
    return windowSeconds > fixture.durationMs / 1000
      ? `${windowSeconds}s window · preview shows the available ${fixture.durationMs / 1000}s warm-up`
      : `${windowSeconds}s rolling window · recorded Polar H10 data`;
  }

  function mean(values) {
    return values.reduce((sum, value) => sum + value, 0) / Math.max(1, values.length);
  }

  function zScore(value, values) {
    const average = mean(values);
    const deviation = Math.sqrt(values.reduce((sum, candidate) => sum + (candidate - average) ** 2, 0) / Math.max(1, values.length - 1));
    return deviation > Number.EPSILON ? (value - average) / deviation : NaN;
  }

  function normalCdf(value) {
    const absolute = Math.abs(value);
    const t = 1 / (1 + 0.2316419 * absolute);
    const polynomial = t * (0.31938154 + t * (-0.35656378 + t * (1.7814779 + t * (-1.821255978 + t * 1.330274429))));
    const upper = 1 - Math.exp(-0.5 * absolute * absolute) / Math.sqrt(2 * Math.PI) * polynomial;
    return value >= 0 ? upper : 1 - upper;
  }

  return Object.freeze({ knowledge, normalizeMetricSettings, preview, rrSamples, timeDomainMetrics });
});
