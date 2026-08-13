//! Thin application coordinator. Protocol decoding, Bluetooth input, and
//! network output are independent crates below `crates/`.

use std::sync::Arc;

use polar_h10_core::{AccSample, ExperimentalBreathingSample, RrTracker};
use polar_h10_input::{DeviceSummary, InputEvent, InputManager};
use polar_h10_output::{MetricSpec, MetricValue, OutputConfig, OutputHealth, OutputRouter};
use serde::Serialize;
use tauri::{State, ipc::Channel};

struct AppState {
    input: Arc<InputManager>,
    output: Arc<OutputRouter>,
}

impl AppState {
    fn new() -> Self {
        Self {
            input: Arc::new(InputManager::new()),
            output: Arc::new(OutputRouter::new()),
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
    },
    Accelerometer {
        sensor_timestamp_ns: u64,
        samples: Vec<AccSample>,
        breathing_samples: Vec<ExperimentalBreathingSample>,
    },
    Metrics {
        heart_rate_bpm: u16,
        rr_intervals_ms: Vec<f32>,
        rmssd_ms: Option<f32>,
    },
    Error {
        message: String,
    },
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Bootstrap {
    config: OutputConfig,
    platform: &'static str,
    metric_catalog: Vec<MetricDescriptor>,
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
}

impl MetricDescriptor {
    fn new(
        id: &'static str,
        label: &'static str,
        detail: &'static str,
        unit: &'static str,
        raw: bool,
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
            experimental: matches!(id, "acc_breathing_magnitude" | "acc_breathing_phase"),
        }
    }
}

#[tauri::command]
fn get_bootstrap(state: State<'_, Arc<AppState>>) -> Bootstrap {
    Bootstrap {
        config: state.output.config(),
        platform: std::env::consts::OS,
        metric_catalog: vec![
            MetricDescriptor::new("raw_ecg", "Raw ECG", "130 Hz · 1 channel", "µV", true),
            MetricDescriptor::new(
                "raw_acc",
                "Raw accelerometer",
                "200 Hz · X, Y, Z",
                "mg",
                true,
            ),
            MetricDescriptor::new("heart_rate", "Heart rate", "Device-derived", "bpm", false),
            MetricDescriptor::new(
                "rr_interval",
                "RR interval",
                "Beat-to-beat interval",
                "ms",
                false,
            ),
            MetricDescriptor::new(
                "acc_magnitude",
                "3D acceleration magnitude",
                "Device motion · √(x² + y² + z²)",
                "g",
                false,
            ),
            MetricDescriptor::new(
                "acc_breathing_magnitude",
                "Breathing magnitude estimate",
                "Continuous tunable ACC projection",
                "normalized / g",
                false,
            ),
            MetricDescriptor::new(
                "acc_breathing_phase",
                "Breathing phase classifier",
                "Three states · inhale, pause, exhale",
                "state",
                false,
            ),
            MetricDescriptor::new("rmssd", "RMSSD", "Rolling 60-beat window", "ms", false),
        ],
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
                    output.publish_ecg(sensor_timestamp_ns, &microvolts);
                    AppEvent::Ecg {
                        sensor_timestamp_ns,
                        microvolts,
                        estimated_latency_ms,
                        samples_per_packet,
                    }
                }
                InputEvent::Accelerometer {
                    sensor_timestamp_ns,
                    samples,
                } => {
                    let breathing_samples =
                        output.publish_accelerometer(sensor_timestamp_ns, &samples);
                    AppEvent::Accelerometer {
                        sensor_timestamp_ns,
                        samples,
                        breathing_samples,
                    }
                }
                InputEvent::HeartRate {
                    beats_per_minute,
                    rr_intervals_ms,
                } => {
                    for rr in &rr_intervals_ms {
                        rr_tracker.push(*rr);
                    }
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
                    if let Some(value) = rmssd_ms {
                        metrics.push(MetricValue { id: "rmssd", value });
                    }
                    output.publish_metrics(&metrics);
                    AppEvent::Metrics {
                        heart_rate_bpm: beats_per_minute,
                        rr_intervals_ms,
                        rmssd_ms,
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
    state.input.disconnect().await
}

#[tauri::command]
async fn update_output_config(
    state: State<'_, Arc<AppState>>,
    config: OutputConfig,
) -> Result<OutputHealth, String> {
    state.output.configure(config).await
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .manage(Arc::new(AppState::new()))
        .invoke_handler(tauri::generate_handler![
            get_bootstrap,
            scan_devices,
            connect_device,
            disconnect_device,
            update_output_config,
        ])
        .run(tauri::generate_context!())
        .expect("failed to run Polar Stream");
}
