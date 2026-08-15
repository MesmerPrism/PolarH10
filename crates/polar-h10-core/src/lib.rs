//! Platform-neutral Polar H10 packet decoding and rolling metrics.

use std::collections::VecDeque;

use serde::Serialize;
use thiserror::Error;

pub const ECG_MEASUREMENT: u8 = 0x00;
pub const ACC_MEASUREMENT: u8 = 0x02;
const PMD_HEADER_SIZE: usize = 10;

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AccSample {
    pub x_mg: i16,
    pub y_mg: i16,
    pub z_mg: i16,
}

impl AccSample {
    pub fn magnitude_g(self) -> f32 {
        let x = f32::from(self.x_mg) / 1_000.0;
        let y = f32::from(self.y_mg) / 1_000.0;
        let z = f32::from(self.z_mg) / 1_000.0;
        (x * x + y * y + z * z).sqrt()
    }
}

/// User-facing settings for the deliberately experimental ACC respiration
/// projection. The classifier is kept separate from the device's validated
/// ECG path so applications must opt into it explicitly.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ExperimentalBreathingSettings {
    pub axes: [bool; 3],
    pub smoothing_window_seconds: f32,
    pub sensitivity: f32,
    pub normalize: bool,
    pub invert: bool,
}

impl Default for ExperimentalBreathingSettings {
    fn default() -> Self {
        Self {
            axes: [true, false, true],
            smoothing_window_seconds: 0.75,
            sensitivity: 0.6,
            normalize: true,
            invert: false,
        }
    }
}

impl ExperimentalBreathingSettings {
    fn clamped(mut self) -> Self {
        if self.axes.into_iter().filter(|enabled| *enabled).count() < 2 {
            self.axes = Self::default().axes;
        }
        self.smoothing_window_seconds = self.smoothing_window_seconds.clamp(0.2, 3.0);
        self.sensitivity = self.sensitivity.clamp(0.0, 1.0);
        self
    }
}

/// One derived sample. `phase` is `1` for inhale, `-1` for exhale, and `0`
/// for a pause or an interval where the projection is not yet ready.
#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ExperimentalBreathingSample {
    pub waveform: f32,
    pub normalized_01: f32,
    pub phase: i8,
}

/// Lightweight rolling PCA projection for exploratory breathing estimates.
/// It finds the dominant direction across the chosen ACC axes, smooths that
/// projection, adapts its recent bounds, and classifies the local direction.
pub struct ExperimentalBreathingClassifier {
    settings: ExperimentalBreathingSettings,
    history: VecDeque<[f32; 3]>,
    projection_history: VecDeque<f32>,
    smoothing_history: VecDeque<f32>,
    smoothing_sum: f32,
    mean: [f32; 3],
    axis: [f32; 3],
    projection_min: f32,
    projection_max: f32,
    previous_normalized: Option<f32>,
    samples_since_model_update: usize,
    samples_since_bounds_update: usize,
}

impl Default for ExperimentalBreathingClassifier {
    fn default() -> Self {
        Self::new(ExperimentalBreathingSettings::default())
    }
}

impl ExperimentalBreathingClassifier {
    const SAMPLE_RATE_HZ: usize = 200;
    const MODEL_WINDOW_SAMPLES: usize = Self::SAMPLE_RATE_HZ * 8;
    const BOUNDS_WINDOW_SAMPLES: usize = Self::SAMPLE_RATE_HZ * 20;
    const MIN_MODEL_SAMPLES: usize = Self::SAMPLE_RATE_HZ;

    pub fn new(settings: ExperimentalBreathingSettings) -> Self {
        let settings = settings.clamped();
        let axis = first_enabled_axis(settings.axes);
        Self {
            settings,
            history: VecDeque::with_capacity(Self::MODEL_WINDOW_SAMPLES),
            projection_history: VecDeque::with_capacity(Self::BOUNDS_WINDOW_SAMPLES),
            smoothing_history: VecDeque::with_capacity(Self::SAMPLE_RATE_HZ * 3),
            smoothing_sum: 0.0,
            mean: [0.0; 3],
            axis,
            projection_min: 0.0,
            projection_max: 0.0,
            previous_normalized: None,
            samples_since_model_update: 0,
            samples_since_bounds_update: 0,
        }
    }

    pub fn settings(&self) -> ExperimentalBreathingSettings {
        self.settings
    }

    pub fn set_settings(&mut self, settings: ExperimentalBreathingSettings) {
        let settings = settings.clamped();
        if self.settings == settings {
            return;
        }
        *self = Self::new(settings);
    }

    pub fn process(&mut self, sample: AccSample) -> ExperimentalBreathingSample {
        let vector = [
            f32::from(sample.x_mg) / 1_000.0,
            f32::from(sample.y_mg) / 1_000.0,
            f32::from(sample.z_mg) / 1_000.0,
        ];
        self.history.push_back(vector);
        if self.history.len() > Self::MODEL_WINDOW_SAMPLES {
            self.history.pop_front();
        }

        self.samples_since_model_update += 1;
        if self.samples_since_model_update >= 20 || self.history.len() == Self::MIN_MODEL_SAMPLES {
            self.update_projection_model();
            self.samples_since_model_update = 0;
        }

        let mut projection = dot(subtract(vector, self.mean), self.axis);
        if self.settings.invert {
            projection = -projection;
        }
        let smoothed = self.smooth(projection);
        self.projection_history.push_back(smoothed);
        if self.projection_history.len() > Self::BOUNDS_WINDOW_SAMPLES {
            self.projection_history.pop_front();
        }
        self.samples_since_bounds_update += 1;
        if self.samples_since_bounds_update >= 20 || self.projection_history.len() < 40 {
            self.update_projection_bounds();
            self.samples_since_bounds_update = 0;
        }

        let range = self.projection_max - self.projection_min;
        let ready = self.history.len() >= Self::MIN_MODEL_SAMPLES && range >= 0.000_5;
        let normalized = if ready {
            ((smoothed - self.projection_min) / range).clamp(0.0, 1.0)
        } else {
            0.5
        };
        let phase = if let Some(previous) = self.previous_normalized.filter(|_| ready) {
            let delta = normalized - previous;
            let threshold = 0.000_15 + (1.0 - self.settings.sensitivity) * 0.002_35;
            if delta > threshold {
                1
            } else if delta < -threshold {
                -1
            } else {
                0
            }
        } else {
            0
        };
        self.previous_normalized = Some(normalized);

        ExperimentalBreathingSample {
            waveform: if self.settings.normalize {
                normalized
            } else {
                smoothed
            },
            normalized_01: normalized,
            phase,
        }
    }

    fn smooth(&mut self, value: f32) -> f32 {
        let window = (self.settings.smoothing_window_seconds * Self::SAMPLE_RATE_HZ as f32)
            .round()
            .clamp(1.0, (Self::SAMPLE_RATE_HZ * 3) as f32) as usize;
        self.smoothing_history.push_back(value);
        self.smoothing_sum += value;
        while self.smoothing_history.len() > window {
            self.smoothing_sum -= self.smoothing_history.pop_front().unwrap_or_default();
        }
        self.smoothing_sum / self.smoothing_history.len().max(1) as f32
    }

    fn update_projection_bounds(&mut self) {
        if self.projection_history.is_empty() {
            return;
        }
        self.projection_min = self
            .projection_history
            .iter()
            .copied()
            .fold(f32::INFINITY, f32::min);
        self.projection_max = self
            .projection_history
            .iter()
            .copied()
            .fold(f32::NEG_INFINITY, f32::max);
    }

    fn update_projection_model(&mut self) {
        if self.history.is_empty() {
            return;
        }
        let mut mean = [0.0_f32; 3];
        for sample in &self.history {
            for index in 0..3 {
                if self.settings.axes[index] {
                    mean[index] += sample[index];
                }
            }
        }
        for value in &mut mean {
            *value /= self.history.len() as f32;
        }

        let mut covariance = [[0.0_f32; 3]; 3];
        for sample in &self.history {
            let centered = subtract(*sample, mean);
            for row in 0..3 {
                if !self.settings.axes[row] {
                    continue;
                }
                for column in 0..3 {
                    if self.settings.axes[column] {
                        covariance[row][column] += centered[row] * centered[column];
                    }
                }
            }
        }

        let previous_axis = self.axis;
        let mut axis = previous_axis;
        for _ in 0..8 {
            let next = [
                dot(covariance[0], axis),
                dot(covariance[1], axis),
                dot(covariance[2], axis),
            ];
            let length = dot(next, next).sqrt();
            if length < 1e-9 {
                axis = first_enabled_axis(self.settings.axes);
                break;
            }
            axis = next.map(|value| value / length);
        }
        if dot(axis, previous_axis) < 0.0 {
            axis = axis.map(|value| -value);
        }
        self.mean = mean;
        self.axis = axis;
    }
}

fn first_enabled_axis(axes: [bool; 3]) -> [f32; 3] {
    let index = axes.iter().position(|enabled| *enabled).unwrap_or(0);
    let mut axis = [0.0; 3];
    axis[index] = 1.0;
    axis
}

fn subtract(left: [f32; 3], right: [f32; 3]) -> [f32; 3] {
    [left[0] - right[0], left[1] - right[1], left[2] - right[2]]
}

fn dot(left: [f32; 3], right: [f32; 3]) -> f32 {
    left[0] * right[0] + left[1] * right[1] + left[2] * right[2]
}

#[derive(Clone, Debug, PartialEq)]
pub struct HeartRateFrame {
    pub beats_per_minute: u16,
    pub rr_intervals_ms: Vec<f32>,
}

#[derive(Clone, Debug, PartialEq)]
pub enum PmdFrame {
    Ecg {
        sensor_timestamp_ns: u64,
        microvolts: Vec<i32>,
    },
    Accelerometer {
        sensor_timestamp_ns: u64,
        samples: Vec<AccSample>,
    },
}

#[derive(Debug, Error, PartialEq)]
pub enum ProtocolError {
    #[error("PMD frame is shorter than its 10-byte header")]
    FrameTooShort,
    #[error("ECG payload is not a sequence of signed 24-bit samples")]
    InvalidEcgLength,
    #[error("accelerometer payload has an invalid length")]
    InvalidAccelerometerLength,
    #[error("unsupported PMD measurement or frame type")]
    UnsupportedFrame,
}

pub fn decode_heart_rate(bytes: &[u8]) -> HeartRateFrame {
    if bytes.len() < 2 {
        return HeartRateFrame {
            beats_per_minute: 0,
            rr_intervals_ms: Vec::new(),
        };
    }

    let flags = bytes[0];
    let is_u16 = flags & 0x01 != 0;
    let mut cursor = if is_u16 { 3 } else { 2 };
    let beats_per_minute = if is_u16 && bytes.len() >= 3 {
        u16::from_le_bytes([bytes[1], bytes[2]])
    } else {
        u16::from(bytes[1])
    };

    if flags & 0x08 != 0 {
        cursor += 2;
    }

    let mut rr_intervals_ms = Vec::new();
    if flags & 0x10 != 0 {
        while cursor + 1 < bytes.len() {
            let raw = u16::from_le_bytes([bytes[cursor], bytes[cursor + 1]]);
            rr_intervals_ms.push(f32::from(raw) * (1_000.0 / 1_024.0));
            cursor += 2;
        }
    }

    HeartRateFrame {
        beats_per_minute,
        rr_intervals_ms,
    }
}

pub fn decode_pmd(bytes: &[u8]) -> Result<PmdFrame, ProtocolError> {
    if bytes.len() < PMD_HEADER_SIZE {
        return Err(ProtocolError::FrameTooShort);
    }

    let timestamp = u64::from_le_bytes(bytes[1..9].try_into().expect("checked PMD header"));
    let frame_type = bytes[9];

    match bytes[0] {
        ECG_MEASUREMENT if frame_type == 0x00 => decode_ecg(timestamp, &bytes[10..]),
        ACC_MEASUREMENT => decode_accelerometer(timestamp, frame_type, &bytes[10..]),
        _ => Err(ProtocolError::UnsupportedFrame),
    }
}

fn decode_ecg(timestamp: u64, payload: &[u8]) -> Result<PmdFrame, ProtocolError> {
    if !payload.len().is_multiple_of(3) {
        return Err(ProtocolError::InvalidEcgLength);
    }

    let microvolts = payload
        .chunks_exact(3)
        .map(|sample| {
            let raw =
                i32::from(sample[0]) | (i32::from(sample[1]) << 8) | (i32::from(sample[2]) << 16);
            if raw & 0x0080_0000 != 0 {
                raw | !0x00ff_ffff
            } else {
                raw
            }
        })
        .collect();

    Ok(PmdFrame::Ecg {
        sensor_timestamp_ns: timestamp,
        microvolts,
    })
}

fn decode_accelerometer(
    timestamp: u64,
    frame_type: u8,
    payload: &[u8],
) -> Result<PmdFrame, ProtocolError> {
    let compressed = frame_type & 0x80 != 0;
    let frame_type_base = frame_type & 0x7f;
    let samples = if !compressed && frame_type_base == 0x01 {
        if !payload.len().is_multiple_of(6) {
            return Err(ProtocolError::InvalidAccelerometerLength);
        }
        payload
            .chunks_exact(6)
            .map(|sample| AccSample {
                x_mg: i16::from_le_bytes([sample[0], sample[1]]),
                y_mg: i16::from_le_bytes([sample[2], sample[3]]),
                z_mg: i16::from_le_bytes([sample[4], sample[5]]),
            })
            .collect()
    } else {
        decode_compressed_accelerometer(payload)?
    };

    Ok(PmdFrame::Accelerometer {
        sensor_timestamp_ns: timestamp,
        samples,
    })
}

fn decode_compressed_accelerometer(payload: &[u8]) -> Result<Vec<AccSample>, ProtocolError> {
    if payload.len() < 6 {
        return Err(ProtocolError::InvalidAccelerometerLength);
    }

    let mut x = i32::from(i16::from_le_bytes([payload[0], payload[1]]));
    let mut y = i32::from(i16::from_le_bytes([payload[2], payload[3]]));
    let mut z = i32::from(i16::from_le_bytes([payload[4], payload[5]]));
    let mut samples = vec![clamped_acc_sample(x, y, z)];
    let mut bit_offset = 0;
    let delta_data = &payload[6..];

    for _ in 0..((delta_data.len() * 8) / 48) {
        x += read_signed_bits(delta_data, &mut bit_offset, 16);
        y += read_signed_bits(delta_data, &mut bit_offset, 16);
        z += read_signed_bits(delta_data, &mut bit_offset, 16);
        samples.push(clamped_acc_sample(x, y, z));
    }
    Ok(samples)
}

fn clamped_acc_sample(x: i32, y: i32, z: i32) -> AccSample {
    AccSample {
        x_mg: x.clamp(i32::from(i16::MIN), i32::from(i16::MAX)) as i16,
        y_mg: y.clamp(i32::from(i16::MIN), i32::from(i16::MAX)) as i16,
        z_mg: z.clamp(i32::from(i16::MIN), i32::from(i16::MAX)) as i16,
    }
}

fn read_signed_bits(bytes: &[u8], bit_offset: &mut usize, width: usize) -> i32 {
    let mut value = 0_u32;
    for shift in 0..width {
        let absolute = *bit_offset + shift;
        let bit = (bytes[absolute / 8] >> (absolute % 8)) & 1;
        value |= u32::from(bit) << shift;
    }
    *bit_offset += width;
    if value & (1 << (width - 1)) != 0 {
        (value | (!0_u32 << width)) as i32
    } else {
        value as i32
    }
}

pub fn start_ecg_command() -> [u8; 10] {
    [0x02, ECG_MEASUREMENT, 0x00, 0x01, 130, 0, 0x01, 0x01, 14, 0]
}

pub fn start_accelerometer_command() -> [u8; 14] {
    [
        0x02,
        ACC_MEASUREMENT,
        0x02,
        0x01,
        8,
        0,
        0x00,
        0x01,
        200,
        0,
        0x01,
        0x01,
        16,
        0,
    ]
}

pub fn stop_command(measurement: u8) -> [u8; 2] {
    [0x03, measurement]
}

/// Time-domain metrics from one accepted RR window.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct RrMetrics {
    pub mean_nn_ms: f32,
    pub mean_heart_rate_bpm: f32,
    pub rmssd_ms: f32,
    pub ln_rmssd: f32,
    pub sdnn_ms: f32,
    pub pnn50_percent: f32,
    pub sd1_ms: f32,
    pub sample_count: usize,
    pub coverage_01: f32,
}

/// Rolling RR store used by applications that opt into real-time HRV output.
/// Acquisition code deliberately does not depend on these derived metrics.
#[derive(Default)]
pub struct RrTracker {
    intervals: VecDeque<f32>,
}

impl RrTracker {
    const MAX_HISTORY_SECONDS: f32 = 300.0;

    pub fn push(&mut self, value: f32) {
        if (250.0..=2_500.0).contains(&value) {
            self.intervals.push_back(value);
            let mut retained_ms: f32 = self.intervals.iter().sum();
            while retained_ms > Self::MAX_HISTORY_SECONDS * 1_000.0 && self.intervals.len() > 2 {
                retained_ms -= self.intervals.pop_front().unwrap_or_default();
            }
        }
    }

    /// Preserves the original 60-beat RMSSD behavior for callers that have not
    /// opted into a time-based window.
    pub fn rmssd(&self) -> Option<f32> {
        let intervals: Vec<_> = self
            .intervals
            .iter()
            .rev()
            .take(60)
            .rev()
            .copied()
            .collect();
        rmssd(&intervals)
    }

    pub fn metrics(&self, window_seconds: f32) -> Option<RrMetrics> {
        let window_seconds = window_seconds.clamp(5.0, Self::MAX_HISTORY_SECONDS);
        let intervals = self.window(window_seconds);
        if intervals.len() < 2 {
            return None;
        }

        let mean_nn_ms = intervals.iter().sum::<f32>() / intervals.len() as f32;
        let rmssd_ms = rmssd(&intervals)?;
        let deviation_sum = intervals
            .iter()
            .map(|value| {
                let difference = *value - mean_nn_ms;
                difference * difference
            })
            .sum::<f32>();
        let sdnn_ms = (deviation_sum / (intervals.len() - 1) as f32).sqrt();
        let pnn50_percent = 100.0
            * intervals
                .windows(2)
                .filter(|pair| (pair[1] - pair[0]).abs() > 50.0)
                .count() as f32
            / (intervals.len() - 1) as f32;
        let retained_ms = intervals.iter().sum::<f32>();

        Some(RrMetrics {
            mean_nn_ms,
            mean_heart_rate_bpm: 60_000.0 / mean_nn_ms,
            rmssd_ms,
            ln_rmssd: rmssd_ms.max(f32::MIN_POSITIVE).ln(),
            sdnn_ms,
            pnn50_percent,
            sd1_ms: rmssd_ms / std::f32::consts::SQRT_2,
            sample_count: intervals.len(),
            coverage_01: (retained_ms / (window_seconds * 1_000.0)).clamp(0.0, 1.0),
        })
    }

    /// Causal rolling adaptation of the Excite-O-Meter paper's post-session
    /// index. The original algorithm standardizes a complete session; this
    /// version standardizes the current RR and five-beat RMSSD against the
    /// retained real-time baseline so it can be streamed without future data.
    pub fn excitement_index(&self, baseline_seconds: f32) -> Option<f32> {
        let intervals = self.window(baseline_seconds.clamp(10.0, Self::MAX_HISTORY_SECONDS));
        if intervals.len() < 10 {
            return None;
        }

        let mut rmssd_history = Vec::with_capacity(intervals.len().saturating_sub(1));
        for end in 2..=intervals.len() {
            let start = end.saturating_sub(5);
            if let Some(value) = rmssd(&intervals[start..end]) {
                rmssd_history.push(value);
            }
        }
        let current_rmssd = *rmssd_history.last()?;
        let rr_percentile = normal_cdf(z_score(*intervals.last()?, &intervals)?);
        let rmssd_percentile = normal_cdf(z_score(current_rmssd, &rmssd_history)?);
        Some((1.0 - (rr_percentile + rmssd_percentile) / 2.0).clamp(0.0, 1.0))
    }

    fn window(&self, window_seconds: f32) -> Vec<f32> {
        let target_ms = window_seconds * 1_000.0;
        let mut retained_ms = 0.0;
        let mut values = Vec::new();
        for value in self.intervals.iter().rev().copied() {
            values.push(value);
            retained_ms += value;
            if retained_ms >= target_ms {
                break;
            }
        }
        values.reverse();
        values
    }
}

fn rmssd(intervals: &[f32]) -> Option<f32> {
    if intervals.len() < 2 {
        return None;
    }
    let squared_sum = intervals
        .windows(2)
        .map(|pair| {
            let difference = pair[1] - pair[0];
            difference * difference
        })
        .sum::<f32>();
    Some((squared_sum / (intervals.len() - 1) as f32).sqrt())
}

fn z_score(value: f32, values: &[f32]) -> Option<f32> {
    if values.len() < 2 {
        return None;
    }
    let mean = values.iter().sum::<f32>() / values.len() as f32;
    let variance = values
        .iter()
        .map(|candidate| {
            let difference = *candidate - mean;
            difference * difference
        })
        .sum::<f32>()
        / (values.len() - 1) as f32;
    let standard_deviation = variance.sqrt();
    (standard_deviation > f32::EPSILON).then_some((value - mean) / standard_deviation)
}

fn normal_cdf(value: f32) -> f32 {
    // Abramowitz and Stegun 7.1.26; adequate for a bounded operator preview.
    let absolute = value.abs();
    let t = 1.0 / (1.0 + 0.231_641_9 * absolute);
    let polynomial = t
        * (0.319_381_54
            + t * (-0.356_563_78 + t * (1.781_477_9 + t * (-1.821_256 + t * 1.330_274_5))));
    let density = (-0.5 * absolute * absolute).exp() / (2.0 * std::f32::consts::PI).sqrt();
    let upper = 1.0 - density * polynomial;
    if value >= 0.0 { upper } else { 1.0 - upper }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decodes_signed_24_bit_ecg() {
        let mut frame = vec![ECG_MEASUREMENT];
        frame.extend_from_slice(&42_u64.to_le_bytes());
        frame.push(0);
        frame.extend_from_slice(&[1, 0, 0, 0xff, 0xff, 0xff, 0, 0, 0x80]);

        assert_eq!(
            decode_pmd(&frame).unwrap(),
            PmdFrame::Ecg {
                sensor_timestamp_ns: 42,
                microvolts: vec![1, -1, -8_388_608],
            }
        );
    }

    #[test]
    fn decodes_uncompressed_accelerometer() {
        let mut frame = vec![ACC_MEASUREMENT];
        frame.extend_from_slice(&7_u64.to_le_bytes());
        frame.push(1);
        for value in [1_i16, -2, 3, 4, 5, -6] {
            frame.extend_from_slice(&value.to_le_bytes());
        }

        let PmdFrame::Accelerometer { samples, .. } = decode_pmd(&frame).unwrap() else {
            panic!("expected accelerometer frame")
        };
        assert_eq!(samples.len(), 2);
        assert_eq!(
            samples[0],
            AccSample {
                x_mg: 1,
                y_mg: -2,
                z_mg: 3
            }
        );
        assert_eq!(
            samples[1],
            AccSample {
                x_mg: 4,
                y_mg: 5,
                z_mg: -6
            }
        );
    }

    #[test]
    fn decodes_heart_rate_and_rr() {
        let sample = decode_heart_rate(&[0x10, 60, 0x00, 0x04]);
        assert_eq!(sample.beats_per_minute, 60);
        assert_eq!(sample.rr_intervals_ms, vec![1_000.0]);
    }

    #[test]
    fn commands_match_h10_settings() {
        assert_eq!(start_ecg_command()[..2], [0x02, ECG_MEASUREMENT]);
        assert_eq!(
            start_accelerometer_command()[..6],
            [0x02, ACC_MEASUREMENT, 0x02, 0x01, 8, 0]
        );
        assert_eq!(stop_command(ACC_MEASUREMENT), [0x03, ACC_MEASUREMENT]);
    }

    #[test]
    fn computes_rmssd_from_accepted_intervals() {
        let mut tracker = RrTracker::default();
        for value in [1_000.0, 1_020.0, 980.0] {
            tracker.push(value);
        }
        assert!((tracker.rmssd().unwrap() - 31.622_776).abs() < 0.001);
    }

    #[test]
    fn rejects_implausible_rr_values() {
        let mut tracker = RrTracker::default();
        for value in [100.0, 1_000.0, 3_000.0, 1_010.0] {
            tracker.push(value);
        }
        assert_eq!(tracker.intervals, VecDeque::from([1_000.0, 1_010.0]));
    }

    #[test]
    fn computes_the_complete_time_domain_metric_family() {
        let mut tracker = RrTracker::default();
        for value in [1_000.0, 1_020.0, 980.0, 1_060.0] {
            tracker.push(value);
        }
        let metrics = tracker.metrics(10.0).unwrap();
        assert_eq!(metrics.sample_count, 4);
        assert!((metrics.mean_nn_ms - 1_015.0).abs() < 0.001);
        assert!((metrics.mean_heart_rate_bpm - 59.1133).abs() < 0.001);
        assert!((metrics.rmssd_ms - 52.9150).abs() < 0.001);
        assert!((metrics.sd1_ms - metrics.rmssd_ms / std::f32::consts::SQRT_2).abs() < 0.001);
        assert!((metrics.pnn50_percent - 33.3333).abs() < 0.001);
        assert!(metrics.coverage_01 > 0.4 && metrics.coverage_01 < 0.41);
    }

    #[test]
    fn excitement_adaptation_is_bounded_after_baseline_warmup() {
        let mut tracker = RrTracker::default();
        for index in 0..30 {
            tracker.push(800.0 + (index as f32 * 0.7).sin() * 35.0);
        }
        let value = tracker.excitement_index(60.0).unwrap();
        assert!((0.0..=1.0).contains(&value));
    }

    #[test]
    fn experimental_breathing_classifier_emits_bounded_waveform_and_both_directions() {
        let mut classifier = ExperimentalBreathingClassifier::default();
        let mut phases = Vec::new();
        for index in 0..2_400 {
            let time = index as f32 / 200.0;
            let breath = (time * std::f32::consts::TAU / 4.0).sin();
            let sample = AccSample {
                x_mg: (breath * 35.0) as i16,
                y_mg: 8,
                z_mg: (1_000.0 + breath * 12.0) as i16,
            };
            let output = classifier.process(sample);
            assert!((0.0..=1.0).contains(&output.normalized_01));
            assert!((0.0..=1.0).contains(&output.waveform));
            if index > 600 {
                phases.push(output.phase);
            }
        }
        assert!(phases.contains(&1));
        assert!(phases.contains(&-1));
        assert!(phases.contains(&0));
    }

    #[test]
    fn experimental_breathing_classifier_accepts_raw_projection_output() {
        let settings = ExperimentalBreathingSettings {
            normalize: false,
            ..ExperimentalBreathingSettings::default()
        };
        let mut classifier = ExperimentalBreathingClassifier::new(settings);
        let output = classifier.process(AccSample {
            x_mg: 15,
            y_mg: 0,
            z_mg: 1_000,
        });
        assert!(output.waveform.is_finite());
    }
}
