use std::{fmt::Write as _, time::Duration};

use polar_h10_core::{AccSample, RrTracker};
use serde::{Deserialize, Serialize};

pub const SCHEMA_VERSION: u8 = 1;
pub const ECG_SAMPLE_RATE_HZ: u16 = 130;
pub const ACC_SAMPLE_RATE_HZ: u16 = 200;
pub const DEFAULT_DURATION: Duration = Duration::from_secs(60);
pub const MAX_DURATION: Duration = Duration::from_secs(10 * 60);

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PreviewFixture {
    pub schema_version: u8,
    pub source: String,
    pub recorded_at_unix_ms: u64,
    pub duration_ms: u64,
    pub device_model: String,
    pub ecg: EcgRecording,
    pub accelerometer: AccRecording,
    pub metric_events: Vec<MetricEvent>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct EcgRecording {
    pub sample_rate_hz: u16,
    pub microvolts: Vec<i32>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AccRecording {
    pub sample_rate_hz: u16,
    pub unit: String,
    pub samples: Vec<[i16; 3]>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MetricEvent {
    pub offset_ms: u64,
    pub heart_rate_bpm: u16,
    pub rr_intervals_ms: Vec<f32>,
    pub rmssd_ms: Option<f32>,
}

#[derive(Default)]
pub struct CaptureBuffer {
    ecg: Vec<i32>,
    accelerometer: Vec<[i16; 3]>,
    metric_events: Vec<MetricEvent>,
    rr_tracker: RrTracker,
}

impl CaptureBuffer {
    pub fn push_ecg(&mut self, microvolts: &[i32]) {
        self.ecg.extend_from_slice(microvolts);
    }

    pub fn push_accelerometer(&mut self, samples: &[AccSample]) {
        self.accelerometer.extend(
            samples
                .iter()
                .map(|sample| [sample.x_mg, sample.y_mg, sample.z_mg]),
        );
    }

    pub fn push_metrics(
        &mut self,
        offset: Duration,
        heart_rate_bpm: u16,
        rr_intervals_ms: Vec<f32>,
    ) {
        for interval in &rr_intervals_ms {
            self.rr_tracker.push(*interval);
        }
        self.metric_events.push(MetricEvent {
            offset_ms: offset.as_millis().min(u128::from(u64::MAX)) as u64,
            heart_rate_bpm,
            rr_intervals_ms,
            rmssd_ms: self.rr_tracker.rmssd(),
        });
    }

    pub fn ecg_len(&self) -> usize {
        self.ecg.len()
    }

    pub fn accelerometer_len(&self) -> usize {
        self.accelerometer.len()
    }

    pub fn finish(
        mut self,
        duration: Duration,
        recorded_at_unix_ms: u64,
    ) -> Result<PreviewFixture, String> {
        validate_duration(duration)?;
        let ecg_target = target_samples(duration, ECG_SAMPLE_RATE_HZ)?;
        let acc_target = target_samples(duration, ACC_SAMPLE_RATE_HZ)?;
        if self.ecg.len() < ecg_target || self.accelerometer.len() < acc_target {
            return Err(format!(
                "Capture ended before both fixed-rate streams were complete (ECG {}/{ecg_target}, ACC {}/{acc_target}).",
                self.ecg.len(),
                self.accelerometer.len()
            ));
        }
        self.ecg.truncate(ecg_target);
        self.accelerometer.truncate(acc_target);
        let duration_ms = duration.as_millis().min(u128::from(u64::MAX)) as u64;
        self.metric_events
            .retain(|event| event.offset_ms < duration_ms);

        Ok(PreviewFixture {
            schema_version: SCHEMA_VERSION,
            source: "real-polar-h10-recording".into(),
            recorded_at_unix_ms,
            duration_ms,
            device_model: "Polar H10".into(),
            ecg: EcgRecording {
                sample_rate_hz: ECG_SAMPLE_RATE_HZ,
                microvolts: self.ecg,
            },
            accelerometer: AccRecording {
                sample_rate_hz: ACC_SAMPLE_RATE_HZ,
                unit: "mg".into(),
                samples: self.accelerometer,
            },
            metric_events: self.metric_events,
        })
    }
}

pub fn validate_fixture(fixture: &PreviewFixture) -> Result<(), String> {
    if fixture.schema_version != SCHEMA_VERSION {
        return Err(format!(
            "Unsupported preview fixture schema {} (expected {SCHEMA_VERSION}).",
            fixture.schema_version
        ));
    }
    if fixture.source != "real-polar-h10-recording" {
        return Err("Preview fixture is not marked as a real Polar H10 recording.".into());
    }
    if fixture.device_model != "Polar H10" {
        return Err("Preview fixture must identify the source model as Polar H10.".into());
    }
    if fixture.ecg.sample_rate_hz != ECG_SAMPLE_RATE_HZ
        || fixture.accelerometer.sample_rate_hz != ACC_SAMPLE_RATE_HZ
        || fixture.accelerometer.unit != "mg"
    {
        return Err("Preview fixture sample rates or accelerometer unit are invalid.".into());
    }
    let duration = Duration::from_millis(fixture.duration_ms);
    validate_duration(duration)?;
    let expected_ecg = target_samples(duration, ECG_SAMPLE_RATE_HZ)?;
    let expected_acc = target_samples(duration, ACC_SAMPLE_RATE_HZ)?;
    if fixture.ecg.microvolts.len() != expected_ecg
        || fixture.accelerometer.samples.len() != expected_acc
    {
        return Err(format!(
            "Preview fixture sample counts do not match its duration (ECG {}/{expected_ecg}, ACC {}/{expected_acc}).",
            fixture.ecg.microvolts.len(),
            fixture.accelerometer.samples.len()
        ));
    }
    if fixture
        .metric_events
        .iter()
        .any(|event| event.offset_ms >= fixture.duration_ms)
    {
        return Err("Preview fixture contains a metric event outside its loop duration.".into());
    }
    Ok(())
}

pub fn render_svg(fixture: &PreviewFixture) -> Result<String, String> {
    validate_fixture(fixture)?;
    let width = 1_200_f64;
    let left = 72_f64;
    let right = 28_f64;
    let plot_width = width - left - right;
    let lanes = [
        ("ECG", "µV", "#d85151", 58_f64, 190_f64),
        ("ACC X", "mg", "#3b78aa", 220_f64, 292_f64),
        ("ACC Y", "mg", "#168259", 310_f64, 382_f64),
        ("ACC Z", "mg", "#a66d19", 400_f64, 472_f64),
    ];
    let ecg_path = svg_path(
        &fixture.ecg.microvolts,
        left,
        plot_width,
        lanes[0].3,
        lanes[0].4,
    );
    let acc_axes: [Vec<i32>; 3] = std::array::from_fn(|axis| {
        fixture
            .accelerometer
            .samples
            .iter()
            .map(|sample| i32::from(sample[axis]))
            .collect()
    });
    let acc_paths: Vec<_> = acc_axes
        .iter()
        .enumerate()
        .map(|(index, values)| {
            svg_path(
                values,
                left,
                plot_width,
                lanes[index + 1].3,
                lanes[index + 1].4,
            )
        })
        .collect();

    let mut svg = format!(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"520\" viewBox=\"0 0 1200 520\" role=\"img\" aria-labelledby=\"title desc\">\n  <title id=\"title\">Recorded Polar H10 preview waveforms</title>\n  <desc id=\"desc\">ECG and three-axis accelerometer traces drawn from the shared {} second real recording fixture.</desc>\n  <rect width=\"1200\" height=\"520\" rx=\"18\" fill=\"#f7f8f4\"/>\n  <text x=\"32\" y=\"34\" fill=\"#26352d\" font-family=\"system-ui, sans-serif\" font-size=\"17\" font-weight=\"700\">Polar H10 · recorded preview loop</text>\n  <text x=\"1168\" y=\"34\" text-anchor=\"end\" fill=\"#728390\" font-family=\"ui-monospace, monospace\" font-size=\"12\">{} s · REAL ECG + ACC</text>\n",
        fixture.duration_ms / 1_000,
        fixture.duration_ms / 1_000
    );
    for (index, (label, unit, color, top, bottom)) in lanes.iter().enumerate() {
        let center = (top + bottom) / 2.0;
        writeln!(
            svg,
            "  <line x1=\"{left}\" y1=\"{center:.1}\" x2=\"{}\" y2=\"{center:.1}\" stroke=\"#dce2dc\" stroke-width=\"1\"/>",
            left + plot_width
        )
        .expect("writing to a String cannot fail");
        writeln!(
            svg,
            "  <text x=\"24\" y=\"{center:.1}\" fill=\"{color}\" font-family=\"ui-monospace, monospace\" font-size=\"12\" font-weight=\"700\">{label}</text>"
        )
        .expect("writing to a String cannot fail");
        writeln!(
            svg,
            "  <text x=\"24\" y=\"{:.1}\" fill=\"#87938c\" font-family=\"ui-monospace, monospace\" font-size=\"9\">{unit}</text>",
            center + 15.0
        )
        .expect("writing to a String cannot fail");
        let path = if index == 0 {
            &ecg_path
        } else {
            &acc_paths[index - 1]
        };
        writeln!(
            svg,
            "  <path d=\"{path}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.35\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>"
        )
        .expect("writing to a String cannot fail");
    }
    svg.push_str("</svg>\n");
    Ok(svg)
}

fn validate_duration(duration: Duration) -> Result<(), String> {
    if duration.is_zero() || duration > MAX_DURATION || duration.subsec_millis() != 0 {
        return Err("Preview duration must be a whole number of seconds from 1 to 600.".into());
    }
    Ok(())
}

fn target_samples(duration: Duration, sample_rate_hz: u16) -> Result<usize, String> {
    let count = duration
        .as_secs()
        .checked_mul(u64::from(sample_rate_hz))
        .ok_or_else(|| "Preview sample count overflowed.".to_string())?;
    usize::try_from(count).map_err(|_| "Preview sample count is too large.".to_string())
}

fn svg_path(values: &[i32], left: f64, width: f64, top: f64, bottom: f64) -> String {
    const MAX_POINTS: usize = 2_200;
    let stride = values.len().div_ceil(MAX_POINTS).max(1);
    let sampled: Vec<_> = values.iter().step_by(stride).copied().collect();
    let (mut min, mut max) = sampled
        .iter()
        .fold((i32::MAX, i32::MIN), |(min, max), value| {
            (min.min(*value), max.max(*value))
        });
    if min == max {
        min -= 1;
        max += 1;
    }
    let range = f64::from(max - min);
    let draw_height = bottom - top;
    let denominator = sampled.len().saturating_sub(1).max(1) as f64;
    let mut path = String::with_capacity(sampled.len() * 18);
    for (index, value) in sampled.iter().enumerate() {
        let x = left + index as f64 / denominator * width;
        let y = bottom - f64::from(*value - min) / range * draw_height;
        write!(path, "{}{x:.2},{y:.2}", if index == 0 { "M" } else { " L" })
            .expect("writing to a String cannot fail");
    }
    path
}

#[cfg(test)]
mod tests {
    use super::*;

    fn complete_buffer(duration: Duration) -> CaptureBuffer {
        let mut buffer = CaptureBuffer::default();
        buffer.push_ecg(&vec![
            42;
            target_samples(duration, ECG_SAMPLE_RATE_HZ).unwrap()
        ]);
        buffer.push_accelerometer(&vec![
            AccSample {
                x_mg: 1,
                y_mg: 2,
                z_mg: 1_003,
            };
            target_samples(duration, ACC_SAMPLE_RATE_HZ).unwrap()
        ]);
        buffer.push_metrics(Duration::from_millis(500), 72, vec![833.0, 840.0]);
        buffer
    }

    #[test]
    fn fixture_uses_exact_sample_counts_and_real_source_marker() {
        let duration = Duration::from_secs(2);
        let fixture = complete_buffer(duration).finish(duration, 123).unwrap();

        assert_eq!(fixture.source, "real-polar-h10-recording");
        assert_eq!(fixture.ecg.microvolts.len(), 260);
        assert_eq!(fixture.accelerometer.samples.len(), 400);
        assert!(fixture.metric_events[0].rmssd_ms.is_some());
        validate_fixture(&fixture).unwrap();
    }

    #[test]
    fn incomplete_stream_is_rejected_instead_of_padded_with_generated_data() {
        let duration = Duration::from_secs(1);
        let mut buffer = CaptureBuffer::default();
        buffer.push_ecg(&[1; 130]);

        let error = buffer.finish(duration, 123).unwrap_err();

        assert!(error.contains("ACC 0/200"));
    }

    #[test]
    fn svg_is_derived_from_fixture_samples() {
        let duration = Duration::from_secs(1);
        let fixture = complete_buffer(duration).finish(duration, 123).unwrap();
        let svg = render_svg(&fixture).unwrap();

        assert!(svg.contains("recorded preview loop"));
        assert!(svg.contains("REAL ECG + ACC"));
        assert!(svg.contains("<path d=\"M"));
    }
}
