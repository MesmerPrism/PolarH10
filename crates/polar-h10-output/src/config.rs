use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct OutputConfig {
    pub stream_name: String,
    pub lsl_enabled: bool,
    pub osc_enabled: bool,
    pub outputs: Vec<String>,
    #[serde(default)]
    pub breathing_config: BreathingConfig,
}

impl Default for OutputConfig {
    fn default() -> Self {
        Self {
            stream_name: "Polar-H10".into(),
            lsl_enabled: false,
            osc_enabled: false,
            outputs: vec!["raw_ecg".into(), "raw_acc".into()],
            breathing_config: BreathingConfig::default(),
        }
    }
}

impl OutputConfig {
    pub fn normalized(mut self) -> Result<Self, String> {
        self.stream_name = normalize_stream_base(&self.stream_name)?;
        self.outputs.sort();
        self.outputs.dedup();
        self.outputs.retain(|id| MetricSpec::for_id(id).is_some());
        self.breathing_config = self.breathing_config.normalized();
        Ok(self)
    }

    pub(crate) fn includes(&self, id: &str) -> bool {
        self.outputs.iter().any(|candidate| candidate == id)
    }
}

#[derive(Clone, Debug, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BreathingConfig {
    pub axes: Vec<String>,
    pub smoothing_window_seconds: f32,
    pub sensitivity: f32,
    pub normalize: bool,
    pub invert: bool,
}

impl Default for BreathingConfig {
    fn default() -> Self {
        Self {
            axes: vec!["x".into(), "z".into()],
            smoothing_window_seconds: 0.75,
            sensitivity: 0.6,
            normalize: true,
            invert: false,
        }
    }
}

impl BreathingConfig {
    fn normalized(mut self) -> Self {
        self.axes = ["x", "y", "z"]
            .into_iter()
            .filter(|axis| {
                self.axes
                    .iter()
                    .any(|candidate| candidate.eq_ignore_ascii_case(axis))
            })
            .map(str::to_string)
            .collect();
        if self.axes.len() < 2 {
            self.axes = Self::default().axes;
        }
        self.smoothing_window_seconds = self.smoothing_window_seconds.clamp(0.2, 3.0);
        self.sensitivity = self.sensitivity.clamp(0.0, 1.0);
        self
    }

    pub(crate) fn enabled_axes(&self) -> [bool; 3] {
        ["x", "y", "z"].map(|axis| self.axes.iter().any(|candidate| candidate == axis))
    }
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct OutputHealth {
    pub stream_name: String,
    pub lsl: String,
    pub osc: String,
}

#[derive(Clone, Copy)]
pub struct MetricSpec {
    pub(crate) id: &'static str,
    pub(crate) label: &'static str,
    pub(crate) stream_type: &'static str,
    pub(crate) unit: &'static str,
    pub(crate) channels: i32,
    pub(crate) rate_hz: f64,
    suffix: &'static str,
}

impl MetricSpec {
    pub fn for_id(id: &str) -> Option<Self> {
        Some(match id {
            "raw_ecg" => Self {
                id: "raw_ecg",
                label: "ECG",
                stream_type: "ECG",
                unit: "microvolts",
                channels: 1,
                rate_hz: 130.0,
                suffix: "rawECG",
            },
            "raw_acc" => Self {
                id: "raw_acc",
                label: "ACC",
                stream_type: "Accelerometer",
                unit: "milli-g",
                channels: 3,
                rate_hz: 200.0,
                suffix: "rawACC",
            },
            "heart_rate" => Self {
                id: "heart_rate",
                label: "Heart-rate",
                stream_type: "HeartRate",
                unit: "bpm",
                channels: 1,
                rate_hz: 0.0,
                suffix: "heartRate",
            },
            "rr_interval" => Self {
                id: "rr_interval",
                label: "RR-interval",
                stream_type: "RR",
                unit: "milliseconds",
                channels: 1,
                rate_hz: 0.0,
                suffix: "rrInterval",
            },
            "acc_magnitude" => Self {
                id: "acc_magnitude",
                label: "ACC-magnitude",
                stream_type: "AccelerometerMetric",
                unit: "g",
                channels: 1,
                rate_hz: 200.0,
                suffix: "accMagnitude",
            },
            "acc_breathing_magnitude" => Self {
                id: "acc_breathing_magnitude",
                label: "ACC-breathing-magnitude",
                stream_type: "ExperimentalRespirationMagnitude",
                unit: "normalized-or-g",
                channels: 1,
                rate_hz: 200.0,
                suffix: "accBreathingMagnitude",
            },
            "acc_breathing_phase" => Self {
                id: "acc_breathing_phase",
                label: "ACC-breathing-phase",
                stream_type: "ExperimentalRespirationPhase",
                unit: "state-minus1-0-1",
                channels: 1,
                rate_hz: 200.0,
                suffix: "accBreathingPhase",
            },
            "rmssd" => Self {
                id: "rmssd",
                label: "RMSSD",
                stream_type: "HRV",
                unit: "milliseconds",
                channels: 1,
                rate_hz: 0.0,
                suffix: "rmssd",
            },
            _ => return None,
        })
    }

    pub fn suffix(self) -> &'static str {
        self.suffix
    }
}

/// Produces a protocol-safe base shared by LSL stream names and OSC paths.
pub fn normalize_stream_base(value: &str) -> Result<String, String> {
    let mut normalized = String::with_capacity(value.len());
    let mut separator_pending = false;

    for character in value.trim().chars() {
        if character.is_ascii_alphanumeric() {
            if separator_pending && !normalized.is_empty() && !normalized.ends_with(['_', '-']) {
                normalized.push('_');
            }
            normalized.push(character);
            separator_pending = false;
        } else if character == '-' {
            normalized.push(character);
            separator_pending = false;
        } else {
            separator_pending = true;
        }
    }

    let normalized = normalized.trim_matches(['_', '-']).to_string();
    if normalized.is_empty() {
        return Err("Stream name must contain at least one letter or number.".into());
    }
    if normalized.chars().count() > 64 {
        return Err("Stream name must be 64 characters or fewer.".into());
    }
    Ok(normalized)
}

/// Returns the exact discoverable LSL name and OSC path component for a metric.
pub fn output_stream_name(base_name: &str, metric_id: &str) -> Option<String> {
    MetricSpec::for_id(metric_id).map(|spec| format!("{base_name}_{}", spec.suffix()))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_to_the_two_raw_streams() {
        assert_eq!(OutputConfig::default().outputs, ["raw_ecg", "raw_acc"]);
    }

    #[test]
    fn normalization_removes_unknowns_and_duplicates() {
        let config = OutputConfig {
            stream_name: "  Lab A ".into(),
            outputs: vec!["rmssd".into(), "unknown".into(), "rmssd".into()],
            ..OutputConfig::default()
        }
        .normalized()
        .unwrap();
        assert_eq!(config.stream_name, "Lab_A");
        assert_eq!(config.outputs, ["rmssd"]);
    }

    #[test]
    fn breathing_settings_require_two_known_axes_and_clamp_tuning() {
        let config = OutputConfig {
            breathing_config: BreathingConfig {
                axes: vec!["Y".into(), "unknown".into()],
                smoothing_window_seconds: 12.0,
                sensitivity: -2.0,
                normalize: false,
                invert: true,
            },
            ..OutputConfig::default()
        }
        .normalized()
        .unwrap();

        assert_eq!(config.breathing_config.axes, ["x", "z"]);
        assert_eq!(config.breathing_config.smoothing_window_seconds, 3.0);
        assert_eq!(config.breathing_config.sensitivity, 0.0);
        assert!(!config.breathing_config.normalize);
        assert!(config.breathing_config.invert);
    }

    #[test]
    fn replaces_characters_that_cannot_cross_native_protocols() {
        let config = OutputConfig {
            stream_name: "bad\0name".into(),
            ..OutputConfig::default()
        }
        .normalized()
        .unwrap();
        assert_eq!(config.stream_name, "bad_name");
    }

    #[test]
    fn generates_the_same_exact_discoverable_names_for_every_protocol() {
        let base = normalize_stream_base(" participant 07 ").unwrap();
        assert_eq!(base, "participant_07");
        assert_eq!(
            output_stream_name(&base, "raw_ecg").as_deref(),
            Some("participant_07_rawECG")
        );
        assert_eq!(
            output_stream_name(&base, "raw_acc").as_deref(),
            Some("participant_07_rawACC")
        );
        assert_eq!(
            output_stream_name(&base, "heart_rate").as_deref(),
            Some("participant_07_heartRate")
        );
    }

    #[test]
    fn breathing_magnitude_and_phase_are_independent_scalar_streams() {
        let magnitude = MetricSpec::for_id("acc_breathing_magnitude").unwrap();
        let phase = MetricSpec::for_id("acc_breathing_phase").unwrap();
        assert_eq!(magnitude.channels, 1);
        assert_eq!(phase.channels, 1);
        assert_eq!(magnitude.suffix(), "accBreathingMagnitude");
        assert_eq!(phase.suffix(), "accBreathingPhase");
        assert_eq!(
            output_stream_name("participant_07", "acc_breathing_magnitude").as_deref(),
            Some("participant_07_accBreathingMagnitude")
        );
        assert_eq!(
            output_stream_name("participant_07", "acc_breathing_phase").as_deref(),
            Some("participant_07_accBreathingPhase")
        );
    }
}
