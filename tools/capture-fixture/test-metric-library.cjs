"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const library = require("../../apps/polar-stream/ui/metric-library.js");
const formulaPreview = require("../../apps/polar-stream/ui/formula-preview.js");

const metricIds = [
  "raw_ecg", "heart_rate", "rr_interval", "mean_nn", "mean_hr", "rmssd",
  "ln_rmssd", "sdnn", "pnn50", "sd1", "excitement_index", "raw_acc",
  "acc_magnitude", "acc_breathing_magnitude", "acc_breathing_phase",
];

function fixture() {
  const fixturePath = path.join(__dirname, "../../apps/polar-stream/ui/data/preview-recording.json");
  return JSON.parse(fs.readFileSync(fixturePath, "utf8"));
}

test("documents every built-in metric with concise scientific context and citations", () => {
  assert.deepEqual(Object.keys(library.knowledge).sort(), [...metricIds].sort());
  for (const id of metricIds) {
    const entry = library.knowledge[id];
    const sentences = entry.summary.match(/[.!?](?:\s|$)/g)?.length || 0;
    assert.ok(sentences >= 1 && sentences <= 3, `${id} should use one to three sentences`);
    assert.ok(entry.citations.length > 0, `${id} should cite relevant literature or technical documentation`);
    for (const citation of entry.citations) assert.match(citation.url, /^https:\/\//);
  }
  assert.match(library.knowledge.excitement_index.externalPreview.url, /exciteometer/);
});

test("renders a recorded-data outcome preview for every built-in metric", () => {
  const value = fixture();
  for (const id of metricIds) {
    const preview = library.preview(value, id, {}, {});
    assert.ok(preview.series.length > 0, `${id} should have a preview series`);
    assert.ok(preview.series.every((series) => series.values.length > 0), `${id} preview should contain values`);
    assert.ok(Number.isFinite(preview.current), `${id} preview should expose its latest value`);
  }
});

test("time-window and breathing settings visibly change their previews", () => {
  const value = fixture();
  const shortRmssd = library.preview(value, "rmssd", { rmssd: { windowSeconds: 10 } }, {});
  const longRmssd = library.preview(value, "rmssd", { rmssd: { windowSeconds: 60 } }, {});
  assert.notEqual(shortRmssd.current.toFixed(5), longRmssd.current.toFixed(5));

  const shortBreathing = library.preview(value, "acc_breathing_magnitude", {}, { smoothingWindowSeconds: 0.2 });
  const longBreathing = library.preview(value, "acc_breathing_magnitude", {}, { smoothingWindowSeconds: 3 });
  const difference = shortBreathing.series[0].values.reduce((sum, sample, index) => (
    sum + Math.abs(sample - longBreathing.series[0].values[index])
  ), 0);
  assert.ok(difference > 0.1);
});

test("derived ECG previews remain finite and excitement stays bounded", () => {
  const value = fixture();
  for (const id of ["mean_nn", "mean_hr", "rmssd", "ln_rmssd", "sdnn", "pnn50", "sd1"]) {
    const preview = library.preview(value, id, {}, {});
    assert.ok(preview.series[0].values.every(Number.isFinite), `${id} should remain finite`);
  }
  const excitement = library.preview(value, "excitement_index", {}, {});
  assert.ok(excitement.series[0].values.every((sample) => sample >= 0 && sample <= 1));
});

test("previews guided formulas on every source without evaluating arbitrary JavaScript", () => {
  const value = fixture();
  const formulas = [
    { source: "ecg", expression: "moving_mean(ecg, 0.2)" },
    { source: "accelerometer", expression: "sqrt(x*x + y*y + z*z) / 1000" },
    { source: "heartRate", expression: "moving_mean_n(hr, 10)" },
    { source: "rrInterval", expression: "rr_rmssd(rr, 60)" },
    { source: "rrInterval", expression: "rr_pnn50(rr, 60)" },
    { source: "rrInterval", expression: "excitement(rr, 60)" },
  ];
  for (const formula of formulas) {
    const preview = formulaPreview.preview(value, formula);
    assert.ok(preview.input.length > 0);
    assert.ok(preview.output.length > 0);
    assert.ok(Number.isFinite(preview.current));
  }
  assert.throws(() => formulaPreview.parse("globalThis.alert(1)"), /Unexpected|Unsupported|not supported|available/);
});

test("duration-based RR formula templates match their built-in previews", () => {
  const value = fixture();
  const formulas = {
    mean_nn: "rr_mean(rr, 60)",
    mean_hr: "rr_mean_hr(rr, 60)",
    rmssd: "rr_rmssd(rr, 60)",
    ln_rmssd: "rr_ln_rmssd(rr, 60)",
    sdnn: "rr_sdnn(rr, 60)",
    pnn50: "rr_pnn50(rr, 60)",
    sd1: "rr_sd1(rr, 60)",
    excitement_index: "excitement(rr, 60)",
  };
  for (const [id, expression] of Object.entries(formulas)) {
    const builtIn = library.preview(value, id, {}, {}).current;
    const custom = formulaPreview.preview(value, { source: "rrInterval", expression }).current;
    assert.ok(Math.abs(builtIn - custom) < 1e-9, `${id} formula should match its built-in preview`);
  }
});

test("output selection is focused and add-one-at-a-time rather than checkbox based", () => {
  const uiRoot = path.join(__dirname, "../../apps/polar-stream/ui");
  const html = fs.readFileSync(path.join(uiRoot, "index.html"), "utf8");
  const app = fs.readFileSync(path.join(uiRoot, "app.js"), "utf8");
  const styles = fs.readFileSync(path.join(uiRoot, "styles.css"), "utf8");
  const detached = fs.readFileSync(path.join(uiRoot, "visualizer-window.js"), "utf8");

  assert.match(html, /id="metric-preview-canvas"/);
  assert.match(html, /id="metric-add-button"/);
  assert.doesNotMatch(app, /metric-checkbox/);
  assert.match(styles, /width:\s*min\(1280px/);
  assert.match(styles, /@media \(max-width: 900px\)[\s\S]*\.metric-library-layout \{ grid-template-columns: 1fr;/);
  assert.match(app, /Recorded before \/ after/);
  assert.match(app, /formulaPreview\.preview/);
  for (const id of ["mean_nn", "mean_hr", "ln_rmssd", "sdnn", "pnn50", "sd1", "excitement_index"]) {
    assert.match(detached, new RegExp(`\\b${id}\\b`), `detached visualizer is missing ${id}`);
  }
  assert.match(detached, /event\.metrics \|\| \[\]/);
});

test("custom formulas expose an insert keyboard and recorded before/after preview", () => {
  const value = fixture();
  const rrKeys = formulaPreview.keypad("rrInterval").functions.map((entry) => entry.label);
  for (const label of ["mean NN", "mean HR", "RMSSD", "lnRMSSD", "SDNN", "pNN50", "SD1", "excitement"]) {
    assert.ok(rrKeys.includes(label), `RR keyboard should include ${label}`);
  }

  const ecg = formulaPreview.preview(value, {
    source: "ecg",
    expression: "moving_mean(ecg, 0.2)",
  });
  assert.ok(ecg.input.length > 0 && ecg.output.length > 0);
  assert.ok(Number.isFinite(ecg.current));

  for (const expression of ["rr_rmssd(rr, 10)", "rr_pnn50(rr, 10)", "excitement(rr, 10)"]) {
    const result = formulaPreview.preview(value, { source: "rrInterval", expression });
    assert.ok(result.output.length > 0, `${expression} should produce a recorded preview`);
    assert.ok(Number.isFinite(result.current));
  }
});
