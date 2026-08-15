"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const { LoopPlayer, validateFixture } = require("../../apps/polar-stream/ui/preview-fixture.js");

function fixture(durationMs = 2000) {
  return {
    schemaVersion: 1,
    source: "real-polar-h10-recording",
    recordedAtUnixMs: 123,
    durationMs,
    deviceModel: "Polar H10",
    ecg: { sampleRateHz: 130, microvolts: Array(durationMs / 1000 * 130).fill(42) },
    accelerometer: {
      sampleRateHz: 200,
      unit: "mg",
      samples: Array.from({ length: durationMs / 1000 * 200 }, () => [1, 2, 1003]),
    },
    metricEvents: [{ offsetMs: 500, heartRateBpm: 72, rrIntervalsMs: [833], rmssdMs: 22 }],
  };
}

test("validates the real-recording marker and exact sensor sample counts", () => {
  const value = fixture();
  assert.equal(validateFixture(value), value);
  value.source = "simulation";
  assert.throws(() => validateFixture(value), /not marked as a real Polar H10 recording/);
});

test("canonical personal fixture is complete and contains no device identifier", () => {
  const fixturePath = path.join(__dirname, "../../apps/polar-stream/ui/data/preview-recording.json");
  const raw = fs.readFileSync(fixturePath, "utf8");
  const value = validateFixture(JSON.parse(raw));

  assert.equal(value.durationMs, 60_000);
  assert.equal(value.ecg.microvolts.length, 7_800);
  assert.equal(value.accelerometer.samples.length, 12_000);
  assert.ok(value.metricEvents.length > 0);
  assert.doesNotMatch(raw, /address|serial|deviceId/i);
});

test("plays sensor data and metrics through the normal event shapes", () => {
  const events = [];
  const player = new LoopPlayer(fixture(), (event) => events.push(event), {
    now: () => 100,
    schedule: () => 1,
    cancel: () => {},
  });
  player.start();
  player.tickAt(1_100);

  assert.equal(events.filter((event) => event.kind === "ecg").flatMap((event) => event.microvolts).length, 130);
  assert.equal(events.filter((event) => event.kind === "accelerometer").flatMap((event) => event.samples).length, 200);
  assert.equal(events.filter((event) => event.kind === "metrics").length, 1);
});

test("loops without inventing or dropping the end of the recording", () => {
  const events = [];
  let loops = 0;
  const player = new LoopPlayer(fixture(1000), (event) => events.push(event), {
    now: () => 0,
    schedule: () => 1,
    cancel: () => {},
    onLoop: () => { loops += 1; },
  });
  player.start();
  player.tickAt(1000);

  assert.equal(events.filter((event) => event.kind === "ecg").flatMap((event) => event.microvolts).length, 130);
  assert.equal(events.filter((event) => event.kind === "accelerometer").flatMap((event) => event.samples).length, 200);
  assert.equal(loops, 1);
});
