//! Thin application coordinator. Protocol decoding, Bluetooth input, and
//! network output are independent crates below `crates/`.

mod profiles;

use std::{path::PathBuf, sync::Arc};

use polar_h10_core::{AccSample, ExperimentalBreathingSample, RrTracker};
use polar_h10_input::{DeviceSummary, InputEvent, InputManager};
use polar_h10_output::{
    CustomFormulaConfig, FormulaError, FormulaPublishBatch, FormulaSource, FormulaValidation,
    MetricSpec, MetricValue, OutputConfig, OutputHealth, OutputRouter, validate_formula,
};
use profiles::{ProfileStore, ProfileSummary, WorkspaceProfileV1};
use serde::Serialize;
use tauri::{Manager, State, ipc::Channel};

struct AppState {
    input: Arc<InputManager>,
    output: Arc<OutputRouter>,
    profiles: ProfileStore,
}

impl AppState {
    fn new(config_directory: PathBuf) -> Self {
        Self {
            input: Arc::new(InputManager::new()),
            output: Arc::new(OutputRouter::new()),
            profiles: ProfileStore::open(config_directory),
        }
    }
}

#[derive(Clone, Debug, Serialize)]
#[serde(
    tag = "kind",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
enum AppEvent {
    Status {
        phase: String,
        message: String,
    },
    Connection {
        connected: bool,
        streaming: bool,
        device_name: String,
        battery_percent: Option<u8>,
        att_mtu: Option<u16>,
        message: String,
    },
    Ecg {
        sensor_timestamp_ns: u64,
        microvolts: Vec<i32>,
        estimated_latency_ms: u32,
        samples_per_packet: u16,
        formulas: FormulaPublishBatch,
    },
    Accelerometer {
        sensor_timestamp_ns: u64,
        samples: Vec<AccSample>,
        breathing_samples: Vec<ExperimentalBreathingSample>,
        formulas: FormulaPublishBatch,
    },
    Metrics {
        heart_rate_bpm: u16,
        rr_intervals_ms: Vec<f32>,
        rmssd_ms: Option<f32>,
        metrics: Vec<BuiltinMetricSample>,
        formulas: FormulaPublishBatch,
    },
    Error {
        message: String,
    },
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct BuiltinMetricSample {
    id: &'static str,
    value: f32,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Bootstrap {
    config: OutputConfig,
    platform: &'static str,
    metric_catalog: Vec<MetricDescriptor>,
    last_session: Option<WorkspaceProfileV1>,
    profiles: Vec<ProfileSummary>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct MetricDescriptor {
    id: &'static str,
    stream_suffix: &'static str,
    label: &'static str,
    detail: &'static str,
    unit: &'static str,
    raw: bool,
    family: &'static str,
    experimental: bool,
    formula: String,
    custom_expression: Option<String>,
    formula_source: FormulaSource,
}

impl MetricDescriptor {
    fn new(
        id: &'static str,
        label: &'static str,
        detail: &'static str,
        unit: &'static str,
        raw: bool,
        formula: impl Into<String>,
        custom_expression: Option<String>,
    ) -> Self {
        let stream_suffix = MetricSpec::for_id(id)
            .expect("application metric must have an output specification")
            .suffix();
        Self {
            id,
            stream_suffix,
            label,
            detail,
            unit,
            raw,
            family: if matches!(
                id,
                "raw_acc" | "acc_magnitude" | "acc_breathing_magnitude" | "acc_breathing_phase"
            ) {
                "acc"
            } else {
                "ecg"
            },
            experimental: matches!(
                id,
                "acc_breathing_magnitude" | "acc_breathing_phase" | "excitement_index"
            ),
            formula: formula.into(),
            custom_expression,
            formula_source: match id {
                "raw_acc" | "acc_magnitude" | "acc_breathing_magnitude" | "acc_breathing_phase" => {
                    FormulaSource::Accelerometer
                }
                "heart_rate" => FormulaSource::HeartRate,
                "rr_interval" | "mean_nn" | "mean_hr" | "rmssd" | "ln_rmssd" | "sdnn" | "pnn50"
                | "sd1" | "excitement_index" => FormulaSource::RrInterval,
                _ => FormulaSource::Ecg,
            },
        }
    }
}

#[tauri::command]
fn get_bootstrap(state: State<'_, Arc<AppState>>) -> Bootstrap {
    let last_session = state.profiles.last_session();
    let config = last_session
        .as_ref()
        .map(|profile| profile.output_config.clone())
        .unwrap_or_else(|| state.output.config());
    let axes = ["x", "y", "z"].map(|axis| {
        config
            .breathing_config
            .axes
            .iter()
            .any(|value| value == axis)
    });
    let magnitude_formula = format!(
        "breathing_magnitude(x, y, z, {}, {}, {}, {:.2}, {}, {})",
        axes[0],
        axes[1],
        axes[2],
        config.breathing_config.smoothing_window_seconds,
        config.breathing_config.normalize,
        config.breathing_config.invert
    );
    let phase_formula = format!(
        "breathing_phase(x, y, z, {}, {}, {}, {:.2}, {:.2}, {})",
        axes[0],
        axes[1],
        axes[2],
        config.breathing_config.smoothing_window_seconds,
        config.breathing_config.sensitivity,
        config.breathing_config.invert
    );
    let rr_formula = |function: &str, metric_id: &str| {
        format!(
            "{function}(rr, {:.0})",
            config.metric_window_seconds(metric_id)
        )
    };
    Bootstrap {
        platform: std::env::consts::OS,
        metric_catalog: vec![
            MetricDescriptor::new(
                "raw_ecg",
                "Raw ECG",
                "130 Hz · 1 channel",
                "µV",
                true,
                "ecg",
                Some("ecg".into()),
            ),
            MetricDescriptor::new(
                "raw_acc",
                "Raw accelerometer",
                "200 Hz · X, Y, Z",
                "mg",
                true,
                "channels(x, y, z)",
                Some(String::new()),
            ),
            MetricDescriptor::new(
                "heart_rate",
                "Heart rate",
                "Device-derived",
                "bpm",
                false,
                "hr",
                Some("hr".into()),
            ),
            MetricDescriptor::new(
                "rr_interval",
                "RR interval",
                "Beat-to-beat interval",
                "ms",
                false,
                "rr",
                Some("rr".into()),
            ),
            MetricDescriptor::new(
                "acc_magnitude",
                "3D acceleration magnitude",
                "Device motion · √(x² + y² + z²)",
                "g",
                false,
                "sqrt(x*x + y*y + z*z) / 1000",
                Some("sqrt(x*x + y*y + z*z) / 1000".into()),
            ),
            MetricDescriptor::new(
                "acc_breathing_magnitude",
                "Breathing magnitude estimate",
                "Continuous tunable ACC projection",
                "normalized / g",
                false,
                magnitude_formula.clone(),
                Some(magnitude_formula),
            ),
            MetricDescriptor::new(
                "acc_breathing_phase",
                "Breathing phase classifier",
                "Three states · inhale, pause, exhale",
                "state",
                false,
                phase_formula.clone(),
                Some(phase_formula),
            ),
            MetricDescriptor::new(
                "mean_nn",
                "Mean NN",
                "Average accepted beat interval",
                "ms",
                false,
                rr_formula("rr_mean", "mean_nn"),
                Some(rr_formula("rr_mean", "mean_nn")),
            ),
            MetricDescriptor::new(
                "mean_hr",
                "Mean heart rate",
                "Window mean derived from accepted NN",
                "bpm",
                false,
                rr_formula("rr_mean_hr", "mean_hr"),
                Some(rr_formula("rr_mean_hr", "mean_hr")),
            ),
            MetricDescriptor::new(
                "rmssd",
                "RMSSD",
                "Rolling RR window",
                "ms",
                false,
                rr_formula("rr_rmssd", "rmssd"),
                Some(rr_formula("rr_rmssd", "rmssd")),
            ),
            MetricDescriptor::new(
                "ln_rmssd",
                "lnRMSSD",
                "Natural log of rolling RMSSD",
                "ln(ms)",
                false,
                rr_formula("rr_ln_rmssd", "ln_rmssd"),
                Some(rr_formula("rr_ln_rmssd", "ln_rmssd")),
            ),
            MetricDescriptor::new(
                "sdnn",
                "SDNN",
                "Standard deviation of accepted NN",
                "ms",
                false,
                rr_formula("rr_sdnn", "sdnn"),
                Some(rr_formula("rr_sdnn", "sdnn")),
            ),
            MetricDescriptor::new(
                "pnn50",
                "pNN50",
                "Adjacent NN differences over 50 ms",
                "%",
                false,
                rr_formula("rr_pnn50", "pnn50"),
                Some(rr_formula("rr_pnn50", "pnn50")),
            ),
            MetricDescriptor::new(
                "sd1",
                "SD1",
                "Poincaré short-axis variability",
                "ms",
                false,
                rr_formula("rr_sd1", "sd1"),
                Some(rr_formula("rr_sd1", "sd1")),
            ),
            MetricDescriptor::new(
                "excitement_index",
                "Excite-O-Meter excitement level",
                "Experimental rolling adaptation",
                "0–1",
                false,
                rr_formula("excitement", "excitement_index"),
                Some(rr_formula("excitement", "excitement_index")),
            ),
        ],
        last_session,
        profiles: state.profiles.summaries(),
        config,
    }
}

#[tauri::command]
async fn scan_devices(state: State<'_, Arc<AppState>>) -> Result<Vec<DeviceSummary>, String> {
    state.input.scan().await
}

#[tauri::command]
async fn connect_device(
    state: State<'_, Arc<AppState>>,
    device_id: String,
    events: Channel<AppEvent>,
) -> Result<(), String> {
    let mut input_events = state.input.connect(&device_id).await?;
    let output = state.output.clone();
    output.reset_formulas();
    tauri::async_runtime::spawn(async move {
        let mut rr_tracker = RrTracker::default();
        while let Some(event) = input_events.recv().await {
            let frontend_event = match event {
                InputEvent::Status { phase, message } => AppEvent::Status {
                    phase: phase.into(),
                    message,
                },
                InputEvent::Connected {
                    device_name,
                    battery_percent,
                    att_mtu,
                } => AppEvent::Connection {
                    connected: true,
                    streaming: true,
                    device_name,
                    battery_percent,
                    att_mtu: Some(att_mtu),
                    message: "Raw ECG and accelerometer are streaming".into(),
                },
                InputEvent::Ecg {
                    sensor_timestamp_ns,
                    microvolts,
                    estimated_latency_ms,
                    samples_per_packet,
                } => {
                    let formulas = output.publish_ecg(sensor_timestamp_ns, &microvolts);
                    AppEvent::Ecg {
                        sensor_timestamp_ns,
                        microvolts,
                        estimated_latency_ms,
                        samples_per_packet,
                        formulas,
                    }
                }
                InputEvent::Accelerometer {
                    sensor_timestamp_ns,
                    samples,
                } => {
                    let published = output.publish_accelerometer(sensor_timestamp_ns, &samples);
                    AppEvent::Accelerometer {
                        sensor_timestamp_ns,
                        samples,
                        breathing_samples: published.breathing_samples,
                        formulas: published.formulas,
                    }
                }
                InputEvent::HeartRate {
                    beats_per_minute,
                    rr_intervals_ms,
                } => {
                    for rr in &rr_intervals_ms {
                        rr_tracker.push(*rr);
                    }
                    let config = output.config();
                    let rmssd_ms = rr_tracker.rmssd();
                    let mut metrics = vec![MetricValue {
                        id: "heart_rate",
                        value: f32::from(beats_per_minute),
                    }];
                    if let Some(rr) = rr_intervals_ms.last() {
                        metrics.push(MetricValue {
                            id: "rr_interval",
                            value: *rr,
                        });
                    }
                    for id in [
                        "mean_nn", "mean_hr", "rmssd", "ln_rmssd", "sdnn", "pnn50", "sd1",
                    ] {
                        let Some(values) = rr_tracker.metrics(config.metric_window_seconds(id))
                        else {
                            continue;
                        };
                        let value = match id {
                            "mean_nn" => values.mean_nn_ms,
                            "mean_hr" => values.mean_heart_rate_bpm,
                            "rmssd" => values.rmssd_ms,
                            "ln_rmssd" => values.ln_rmssd,
                            "sdnn" => values.sdnn_ms,
                            "pnn50" => values.pnn50_percent,
                            "sd1" => values.sd1_ms,
                            _ => unreachable!(),
                        };
                        metrics.push(MetricValue { id, value });
                    }
                    if let Some(value) = rr_tracker
                        .excitement_index(config.metric_window_seconds("excitement_index"))
                    {
                        metrics.push(MetricValue {
                            id: "excitement_index",
                            value,
                        });
                    }
                    let metric_samples = metrics
                        .iter()
                        .map(|metric| BuiltinMetricSample {
                            id: metric.id,
                            value: metric.value,
                        })
                        .collect();
                    let formulas =
                        output.publish_metrics(beats_per_minute, &rr_intervals_ms, &metrics);
                    AppEvent::Metrics {
                        heart_rate_bpm: beats_per_minute,
                        rr_intervals_ms,
                        rmssd_ms,
                        metrics: metric_samples,
                        formulas,
                    }
                }
                InputEvent::Error(message) => AppEvent::Error { message },
                InputEvent::Disconnected {
                    device_name,
                    battery_percent,
                } => AppEvent::Connection {
                    connected: false,
                    streaming: false,
                    device_name,
                    battery_percent,
                    att_mtu: None,
                    message: "Disconnected".into(),
                },
            };
            if events.send(frontend_event).is_err() {
                break;
            }
        }
    });
    Ok(())
}

#[tauri::command]
async fn disconnect_device(state: State<'_, Arc<AppState>>) -> Result<(), String> {
    state.input.disconnect().await?;
    state.output.reset_formulas();
    Ok(())
}

#[tauri::command]
async fn update_output_config(
    state: State<'_, Arc<AppState>>,
    config: OutputConfig,
) -> Result<OutputHealth, String> {
    state.output.configure(config).await
}

#[tauri::command]
fn validate_custom_formula(
    formula: CustomFormulaConfig,
) -> Result<FormulaValidation, FormulaError> {
    validate_formula(formula)
}

#[tauri::command]
fn save_last_session(
    state: State<'_, Arc<AppState>>,
    profile: WorkspaceProfileV1,
) -> Result<(), String> {
    state.profiles.save_last_session(profile)
}

#[tauri::command]
fn list_profiles(state: State<'_, Arc<AppState>>) -> Vec<ProfileSummary> {
    state.profiles.summaries()
}

#[tauri::command]
fn save_profile(
    state: State<'_, Arc<AppState>>,
    name: String,
    profile: WorkspaceProfileV1,
) -> Result<ProfileSummary, String> {
    state.profiles.save_named(name, profile)
}

#[tauri::command]
fn load_profile(
    state: State<'_, Arc<AppState>>,
    name: String,
) -> Result<WorkspaceProfileV1, String> {
    state.profiles.load_named(&name)
}

#[tauri::command]
fn delete_profile(state: State<'_, Arc<AppState>>, name: String) -> Result<(), String> {
    state.profiles.delete_named(&name)
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .setup(|app| {
            let config_directory = app.path().app_config_dir()?;
            app.manage(Arc::new(AppState::new(config_directory)));
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_bootstrap,
            scan_devices,
            connect_device,
            disconnect_device,
            update_output_config,
            validate_custom_formula,
            save_last_session,
            list_profiles,
            save_profile,
            load_profile,
            delete_profile,
        ])
        .run(tauri::generate_context!())
        .expect("failed to run Polar Stream");
}
