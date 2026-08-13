//! Native output fan-out. Sensor traffic never takes a detour through the web UI.

mod config;
mod lsl;
mod osc;

use std::sync::Mutex;

pub use config::{
    BreathingConfig, MetricSpec, OutputConfig, OutputHealth, normalize_stream_base,
    output_stream_name,
};
use lsl::LslPublisher;
use osc::{OSC_TARGET, OscPublisher};
use polar_h10_core::{
    AccSample, ExperimentalBreathingClassifier, ExperimentalBreathingSample,
    ExperimentalBreathingSettings,
};

#[derive(Clone, Copy, Debug)]
pub struct MetricValue<'a> {
    pub id: &'a str,
    pub value: f32,
}

pub struct OutputRouter {
    inner: Mutex<RouterInner>,
}

struct RouterInner {
    config: OutputConfig,
    osc: Option<OscPublisher>,
    lsl: LslPublisher,
    breathing: ExperimentalBreathingClassifier,
}

impl Default for OutputRouter {
    fn default() -> Self {
        Self::new()
    }
}

impl OutputRouter {
    pub fn new() -> Self {
        Self {
            inner: Mutex::new(RouterInner {
                config: OutputConfig::default(),
                osc: None,
                lsl: LslPublisher::new(),
                breathing: ExperimentalBreathingClassifier::default(),
            }),
        }
    }

    pub async fn configure(&self, config: OutputConfig) -> Result<OutputHealth, String> {
        let config = config.normalized()?;
        let osc = if config.osc_enabled {
            Some(OscPublisher::connect(OSC_TARGET).await?)
        } else {
            None
        };

        let mut inner = self.inner.lock().map_err(|_| "Output router lock failed")?;
        inner.config = config;
        let breathing_settings = ExperimentalBreathingSettings {
            axes: inner.config.breathing_config.enabled_axes(),
            smoothing_window_seconds: inner.config.breathing_config.smoothing_window_seconds,
            sensitivity: inner.config.breathing_config.sensitivity,
            normalize: inner.config.breathing_config.normalize,
            invert: inner.config.breathing_config.invert,
        };
        inner.breathing.set_settings(breathing_settings);
        inner.osc = osc;
        inner.rebuild_lsl();
        Ok(inner.health())
    }

    pub fn config(&self) -> OutputConfig {
        self.inner
            .lock()
            .map(|inner| inner.config.clone())
            .unwrap_or_default()
    }

    pub fn publish_ecg(&self, sensor_timestamp_ns: u64, samples: &[i32]) {
        let Ok(mut inner) = self.inner.lock() else {
            return;
        };
        if !inner.config.includes("raw_ecg") {
            return;
        }
        inner
            .lsl
            .push_scalar_series("raw_ecg", samples.iter().map(|value| *value as f32));
        if let Some(osc) = &inner.osc {
            osc.send_series(
                &inner.config.stream_name,
                "raw_ecg",
                sensor_timestamp_ns,
                samples.iter().map(|value| *value as f32),
            );
        }
    }

    pub fn publish_accelerometer(
        &self,
        sensor_timestamp_ns: u64,
        samples: &[AccSample],
    ) -> Vec<ExperimentalBreathingSample> {
        let Ok(mut inner) = self.inner.lock() else {
            return Vec::new();
        };
        if inner.config.includes("raw_acc") {
            inner.lsl.push_accelerometer(samples);
            if let Some(osc) = &inner.osc {
                osc.send_accelerometer(&inner.config.stream_name, sensor_timestamp_ns, samples);
            }
        }
        if inner.config.includes("acc_magnitude") {
            inner.lsl.push_scalar_series(
                "acc_magnitude",
                samples.iter().map(|sample| sample.magnitude_g()),
            );
            if let Some(osc) = &inner.osc {
                osc.send_series(
                    &inner.config.stream_name,
                    "acc_magnitude",
                    sensor_timestamp_ns,
                    samples.iter().map(|sample| sample.magnitude_g()),
                );
            }
        }
        if inner.config.includes("acc_breathing") {
            let breathing_samples: Vec<_> = samples
                .iter()
                .copied()
                .map(|sample| inner.breathing.process(sample))
                .collect();
            inner.lsl.push_breathing(&breathing_samples);
            if let Some(osc) = &inner.osc {
                osc.send_series(
                    &inner.config.stream_name,
                    "acc_breathing",
                    sensor_timestamp_ns,
                    breathing_samples
                        .iter()
                        .flat_map(|sample| [sample.waveform, f32::from(sample.phase)]),
                );
            }
            breathing_samples
        } else {
            Vec::new()
        }
    }

    pub fn publish_metrics(&self, values: &[MetricValue<'_>]) {
        let Ok(mut inner) = self.inner.lock() else {
            return;
        };
        for metric in values {
            if !inner.config.includes(metric.id) {
                continue;
            }
            inner.lsl.push_scalar(metric.id, metric.value);
            if let Some(osc) = &inner.osc {
                osc.send_series(
                    &inner.config.stream_name,
                    metric.id,
                    0,
                    std::iter::once(metric.value),
                );
            }
        }
    }
}

impl RouterInner {
    fn rebuild_lsl(&mut self) {
        self.lsl.clear();
        if !self.config.lsl_enabled {
            return;
        }
        for id in &self.config.outputs {
            if let Some(spec) = MetricSpec::for_id(id) {
                self.lsl.add_outlet(&self.config.stream_name, spec);
            }
        }
    }

    fn health(&self) -> OutputHealth {
        OutputHealth {
            stream_name: self.config.stream_name.clone(),
            lsl: if !self.config.lsl_enabled {
                "Off".into()
            } else {
                self.lsl.status().into()
            },
            osc: if !self.config.osc_enabled {
                "Off".into()
            } else if self.osc.is_some() {
                format!("Sending to {OSC_TARGET}")
            } else {
                "Unavailable".into()
            },
        }
    }
}
