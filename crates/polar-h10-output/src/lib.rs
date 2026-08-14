//! Native output fan-out. Sensor traffic never takes a detour through the web UI.

mod config;
mod lsl;
mod osc;

use std::{collections::HashMap, sync::Mutex};

pub use config::{
    BreathingConfig, CustomFormulaConfig, FormulaHealth, FormulaSource, MetricSpec, OutputConfig,
    OutputHealth, custom_output_stream_name, normalize_stream_base, output_stream_name,
};
use lsl::LslPublisher;
use osc::{OSC_TARGET, OscPublisher};
use polar_h10_core::{
    AccSample, ExperimentalBreathingClassifier, ExperimentalBreathingSample,
    ExperimentalBreathingSettings,
};
use polar_h10_math::{CompiledFormula, FormulaFrame, MAX_TOTAL_STATE_SAMPLES};
pub use polar_h10_math::{FormulaError, FormulaRuntimeState, FormulaValidation, validate_formula};
use serde::Serialize;

#[derive(Clone, Copy, Debug)]
pub struct MetricValue<'a> {
    pub id: &'a str,
    pub value: f32,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FormulaSeries {
    pub formula_id: String,
    pub values: Vec<f32>,
    pub state: FormulaRuntimeState,
}

#[derive(Clone, Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FormulaPublishBatch {
    pub series: Vec<FormulaSeries>,
    pub faults: Vec<FormulaError>,
}

impl FormulaPublishBatch {
    fn append(&mut self, mut other: Self) {
        self.series.append(&mut other.series);
        self.faults.append(&mut other.faults);
    }
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AccelerometerOutputBatch {
    pub breathing_samples: Vec<ExperimentalBreathingSample>,
    pub formulas: FormulaPublishBatch,
}

pub struct OutputRouter {
    inner: Mutex<RouterInner>,
}

struct RouterInner {
    config: OutputConfig,
    osc: Option<OscPublisher>,
    lsl: LslPublisher,
    breathing: ExperimentalBreathingClassifier,
    formulas: HashMap<String, FormulaRuntime>,
}

struct FormulaRuntime {
    config: CustomFormulaConfig,
    compiled: CompiledFormula,
    state: FormulaRuntimeState,
    message: Option<String>,
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
                formulas: HashMap::new(),
            }),
        }
    }

    /// Validates and atomically applies a complete output configuration.
    pub async fn configure(&self, config: OutputConfig) -> Result<OutputHealth, String> {
        let config = config.normalized()?;
        let mut compiled = HashMap::new();
        let mut total_state_samples = 0usize;
        for formula in config
            .custom_formulas
            .iter()
            .filter(|formula| formula.enabled)
        {
            let runtime =
                CompiledFormula::compile(formula.clone()).map_err(|error| error.to_string())?;
            total_state_samples = total_state_samples
                .checked_add(runtime.state_samples())
                .ok_or("Custom formula state budget overflow")?;
            if total_state_samples > MAX_TOTAL_STATE_SAMPLES {
                return Err("Custom formulas exceed the aggregate DSP state budget.".into());
            }
            compiled.insert(formula.id.clone(), runtime);
        }

        let osc = if config.osc_enabled {
            Some(OscPublisher::connect(OSC_TARGET).await?)
        } else {
            None
        };

        let mut inner = self.inner.lock().map_err(|_| "Output router lock failed")?;
        let mut previous = std::mem::take(&mut inner.formulas);
        let mut formulas = HashMap::new();
        for formula in config
            .custom_formulas
            .iter()
            .filter(|formula| formula.enabled)
        {
            let candidate = compiled
                .remove(&formula.id)
                .ok_or("Compiled custom formula is unavailable")?;
            let runtime = if let Some(mut existing) =
                previous.remove(&formula.id).filter(|existing| {
                    existing.config.source == formula.source
                        && existing.config.expression == formula.expression
                }) {
                existing.config = formula.clone();
                existing
            } else {
                FormulaRuntime {
                    config: formula.clone(),
                    compiled: candidate,
                    state: FormulaRuntimeState::Ready,
                    message: None,
                }
            };
            formulas.insert(formula.id.clone(), runtime);
        }

        inner.config = config;
        let breathing_settings = ExperimentalBreathingSettings {
            axes: inner.config.breathing_config.enabled_axes(),
            smoothing_window_seconds: inner.config.breathing_config.smoothing_window_seconds,
            sensitivity: inner.config.breathing_config.sensitivity,
            normalize: inner.config.breathing_config.normalize,
            invert: inner.config.breathing_config.invert,
        };
        inner.breathing.set_settings(breathing_settings);
        inner.formulas = formulas;
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

    pub fn formula_health(&self) -> Vec<FormulaHealth> {
        self.inner
            .lock()
            .map(|inner| inner.formula_health())
            .unwrap_or_default()
    }

    /// Clears every formula's history when a sensor session starts or ends.
    pub fn reset_formulas(&self) {
        let Ok(mut inner) = self.inner.lock() else {
            return;
        };
        for runtime in inner.formulas.values_mut() {
            if runtime.compiled.reset().is_ok() {
                runtime.state = FormulaRuntimeState::Ready;
                runtime.message = None;
            }
        }
    }

    pub fn publish_ecg(&self, sensor_timestamp_ns: u64, samples: &[i32]) -> FormulaPublishBatch {
        let Ok(mut inner) = self.inner.lock() else {
            return FormulaPublishBatch::default();
        };
        if inner.config.includes("raw_ecg") {
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
        let frames = samples.iter().copied().map(FormulaFrame::ecg).collect();
        inner.process_custom(FormulaSource::Ecg, frames, sensor_timestamp_ns)
    }

    pub fn publish_accelerometer(
        &self,
        sensor_timestamp_ns: u64,
        samples: &[AccSample],
    ) -> AccelerometerOutputBatch {
        let Ok(mut inner) = self.inner.lock() else {
            return AccelerometerOutputBatch {
                breathing_samples: Vec::new(),
                formulas: FormulaPublishBatch::default(),
            };
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
        let publish_breathing_magnitude = inner.config.includes("acc_breathing_magnitude");
        let publish_breathing_phase = inner.config.includes("acc_breathing_phase");
        let breathing_samples = if publish_breathing_magnitude || publish_breathing_phase {
            let breathing_samples: Vec<_> = samples
                .iter()
                .copied()
                .map(|sample| inner.breathing.process(sample))
                .collect();
            if publish_breathing_magnitude {
                inner.lsl.push_scalar_series(
                    "acc_breathing_magnitude",
                    breathing_samples.iter().map(|sample| sample.waveform),
                );
            }
            if publish_breathing_phase {
                inner.lsl.push_scalar_series(
                    "acc_breathing_phase",
                    breathing_samples
                        .iter()
                        .map(|sample| f32::from(sample.phase)),
                );
            }
            if let Some(osc) = &inner.osc {
                if publish_breathing_magnitude {
                    osc.send_series(
                        &inner.config.stream_name,
                        "acc_breathing_magnitude",
                        sensor_timestamp_ns,
                        breathing_samples.iter().map(|sample| sample.waveform),
                    );
                }
                if publish_breathing_phase {
                    osc.send_series(
                        &inner.config.stream_name,
                        "acc_breathing_phase",
                        sensor_timestamp_ns,
                        breathing_samples
                            .iter()
                            .map(|sample| f32::from(sample.phase)),
                    );
                }
            }
            breathing_samples
        } else {
            Vec::new()
        };

        let frames = samples
            .iter()
            .copied()
            .map(FormulaFrame::accelerometer)
            .collect();
        let formulas =
            inner.process_custom(FormulaSource::Accelerometer, frames, sensor_timestamp_ns);
        AccelerometerOutputBatch {
            breathing_samples,
            formulas,
        }
    }

    pub fn publish_metrics(
        &self,
        heart_rate_bpm: u16,
        rr_intervals_ms: &[f32],
        values: &[MetricValue<'_>],
    ) -> FormulaPublishBatch {
        let Ok(mut inner) = self.inner.lock() else {
            return FormulaPublishBatch::default();
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
        let mut result = inner.process_custom(
            FormulaSource::HeartRate,
            vec![FormulaFrame::heart_rate(heart_rate_bpm)],
            0,
        );
        result.append(
            inner.process_custom(
                FormulaSource::RrInterval,
                rr_intervals_ms
                    .iter()
                    .copied()
                    .map(FormulaFrame::rr_interval)
                    .collect(),
                0,
            ),
        );
        result
    }
}

impl RouterInner {
    fn process_custom(
        &mut self,
        source: FormulaSource,
        frames: Vec<FormulaFrame>,
        sensor_timestamp_ns: u64,
    ) -> FormulaPublishBatch {
        if frames.is_empty() {
            return FormulaPublishBatch::default();
        }
        let ids: Vec<_> = self
            .config
            .custom_formulas
            .iter()
            .filter(|formula| formula.enabled && formula.source == source)
            .map(|formula| formula.id.clone())
            .collect();
        let mut batch = FormulaPublishBatch::default();
        let mut publications = Vec::new();

        for id in ids {
            let Some(runtime) = self.formulas.get_mut(&id) else {
                continue;
            };
            let mut values = Vec::with_capacity(frames.len());
            let mut final_state = runtime.state;
            for frame in frames.iter().copied() {
                let evaluation = runtime.compiled.process(frame);
                final_state = evaluation.state;
                if let Some(value) = evaluation.value {
                    values.push(value);
                }
                if let Some(fault) = evaluation.fault {
                    runtime.message = Some(fault.message.clone());
                    batch.faults.push(fault);
                }
            }
            runtime.state = final_state;
            let config = runtime.config.clone();
            publications.push((config, values.clone()));
            batch.series.push(FormulaSeries {
                formula_id: id,
                values,
                state: final_state,
            });
        }

        for (config, values) in publications {
            if values.is_empty() {
                continue;
            }
            self.lsl
                .push_scalar_series(&config.id, values.iter().copied());
            if let Some(osc) = &self.osc {
                let name = custom_output_stream_name(&self.config.stream_name, &config);
                osc.send_named_series(&name, sensor_timestamp_ns, values);
            }
        }
        batch
    }

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
        for formula in self
            .config
            .custom_formulas
            .iter()
            .filter(|formula| formula.enabled)
        {
            self.lsl
                .add_custom_outlet(&self.config.stream_name, formula);
        }
    }

    fn formula_health(&self) -> Vec<FormulaHealth> {
        self.config
            .custom_formulas
            .iter()
            .filter_map(|formula| self.formulas.get(&formula.id))
            .map(|runtime| FormulaHealth {
                formula_id: runtime.config.id.clone(),
                state: runtime.state,
                message: runtime.message.clone(),
            })
            .collect()
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
            formulas: self.formula_health(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn evaluates_custom_formulas_in_the_native_output_path() {
        let router = OutputRouter::new();
        router
            .configure(OutputConfig {
                custom_formulas: vec![CustomFormulaConfig {
                    id: "123e4567-e89b-42d3-a456-426614174000".into(),
                    name: "ECG_doubled".into(),
                    source: FormulaSource::Ecg,
                    expression: "ecg * 2".into(),
                    unit: "µV".into(),
                    enabled: true,
                }],
                ..OutputConfig::default()
            })
            .await
            .unwrap();

        let batch = router.publish_ecg(42, &[1, -3, 5]);
        assert!(batch.faults.is_empty());
        assert_eq!(batch.series.len(), 1);
        assert_eq!(batch.series[0].values, [2.0, -6.0, 10.0]);
        assert_eq!(batch.series[0].state, FormulaRuntimeState::Ready);
    }
}
