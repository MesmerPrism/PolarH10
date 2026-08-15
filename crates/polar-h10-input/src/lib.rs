//! Bluetooth input boundary for Polar H10 sensors.
//!
//! This crate emits decoded input events. It has no dependency on Tauri, LSL,
//! OSC, charts, or any application state outside the Bluetooth connection.

use std::{
    collections::{HashMap, VecDeque},
    sync::Arc,
    time::{Duration, Instant},
};

use btleplug::{
    api::{Central, Manager as _, Peripheral as _, ScanFilter, WriteType},
    platform::{Manager, Peripheral},
};
use futures_util::StreamExt;
use polar_h10_core::{
    ACC_MEASUREMENT, AccSample, ECG_MEASUREMENT, PmdFrame, decode_heart_rate, decode_pmd,
    start_accelerometer_command, start_ecg_command, stop_command,
};
use serde::Serialize;
use tokio::sync::{Mutex, mpsc, watch};
use uuid::Uuid;

const HEART_RATE_SERVICE: Uuid = Uuid::from_u128(0x0000180d_0000_1000_8000_00805f9b34fb);
const HEART_RATE_MEASUREMENT: Uuid = Uuid::from_u128(0x00002a37_0000_1000_8000_00805f9b34fb);
const BATTERY_LEVEL: Uuid = Uuid::from_u128(0x00002a19_0000_1000_8000_00805f9b34fb);
const PMD_SERVICE: Uuid = Uuid::from_u128(0xfb005c80_02e7_f387_1cad_8acd2d8df0c8);
const PMD_CONTROL_POINT: Uuid = Uuid::from_u128(0xfb005c81_02e7_f387_1cad_8acd2d8df0c8);
const PMD_DATA: Uuid = Uuid::from_u128(0xfb005c82_02e7_f387_1cad_8acd2d8df0c8);
const ECG_SAMPLE_RATE_HZ: f64 = 130.0;
const LATENCY_WINDOW_SIZE: usize = 9;

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceSummary {
    pub id: String,
    pub name: String,
    pub rssi: Option<i16>,
}

#[derive(Clone, Debug)]
pub enum InputEvent {
    Status {
        phase: &'static str,
        message: String,
    },
    Connected {
        device_name: String,
        battery_percent: Option<u8>,
        att_mtu: u16,
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
    },
    HeartRate {
        beats_per_minute: u16,
        rr_intervals_ms: Vec<f32>,
    },
    Error(String),
    Disconnected {
        device_name: String,
        battery_percent: Option<u8>,
    },
}

struct ActiveConnection {
    id: String,
    peripheral: Peripheral,
    cancel: watch::Sender<bool>,
}

pub struct InputManager {
    devices: Mutex<HashMap<String, Peripheral>>,
    active: Mutex<Option<ActiveConnection>>,
}

/// Estimates the age of the oldest ECG sample when its notification reaches
/// the host. Polar batches ECG samples according to the active BLE transport;
/// the batch fill time is therefore a lower bound. A rolling median of actual
/// host arrival intervals accounts for persistent adapter or OS buffering
/// without allowing a single scheduling stall to dominate the display.
#[derive(Default)]
struct EcgLatencyEstimator {
    batch_durations_ms: VecDeque<f64>,
    host_intervals_ms: VecDeque<f64>,
    last_arrival: Option<Instant>,
}

impl EcgLatencyEstimator {
    fn observe(&mut self, sample_count: usize, arrived_at: Instant) -> u32 {
        let batch_duration_ms = sample_count as f64 * 1_000.0 / ECG_SAMPLE_RATE_HZ;
        push_bounded(&mut self.batch_durations_ms, batch_duration_ms);

        if let Some(previous) = self.last_arrival {
            push_bounded(
                &mut self.host_intervals_ms,
                arrived_at.duration_since(previous).as_secs_f64() * 1_000.0,
            );
        }
        self.last_arrival = Some(arrived_at);

        let batch_estimate = median(&self.batch_durations_ms).unwrap_or_default();
        let cadence_estimate = median(&self.host_intervals_ms).unwrap_or_default();
        batch_estimate
            .max(cadence_estimate)
            .round()
            .clamp(0.0, u32::MAX as f64) as u32
    }
}

fn push_bounded(values: &mut VecDeque<f64>, value: f64) {
    if values.len() == LATENCY_WINDOW_SIZE {
        values.pop_front();
    }
    values.push_back(value);
}

fn median(values: &VecDeque<f64>) -> Option<f64> {
    if values.is_empty() {
        return None;
    }
    let mut sorted: Vec<_> = values.iter().copied().collect();
    sorted.sort_by(f64::total_cmp);
    let middle = sorted.len() / 2;
    Some(if sorted.len().is_multiple_of(2) {
        (sorted[middle - 1] + sorted[middle]) / 2.0
    } else {
        sorted[middle]
    })
}

impl Default for InputManager {
    fn default() -> Self {
        Self::new()
    }
}

impl InputManager {
    pub fn new() -> Self {
        Self {
            devices: Mutex::new(HashMap::new()),
            active: Mutex::new(None),
        }
    }

    pub async fn scan(&self) -> Result<Vec<DeviceSummary>, String> {
        let manager = Manager::new()
            .await
            .map_err(|error| format!("Could not initialize Bluetooth: {error}"))?;
        let adapters = manager
            .adapters()
            .await
            .map_err(|error| format!("Could not enumerate Bluetooth adapters: {error}"))?;
        let adapter = adapters
            .into_iter()
            .next()
            .ok_or_else(|| "No Bluetooth Low Energy adapter was found.".to_string())?;

        // BlueZ combines service filters between applications, so filter locally.
        adapter
            .start_scan(ScanFilter::default())
            .await
            .map_err(|error| format!("Could not start Bluetooth scan: {error}"))?;
        tokio::time::sleep(Duration::from_secs(4)).await;
        let peripherals = adapter
            .peripherals()
            .await
            .map_err(|error| format!("Could not read scan results: {error}"))?;
        let _ = adapter.stop_scan().await;

        let mut found = Vec::new();
        let mut map = self.devices.lock().await;
        map.clear();
        for peripheral in peripherals {
            let Ok(Some(properties)) = peripheral.properties().await else {
                continue;
            };
            let name = properties
                .local_name
                .unwrap_or_else(|| "Unnamed Polar sensor".into());
            let is_polar = name.to_ascii_lowercase().contains("polar")
                || properties.services.contains(&HEART_RATE_SERVICE)
                || properties.services.contains(&PMD_SERVICE);
            if !is_polar {
                continue;
            }

            let id = peripheral.id().to_string();
            found.push(DeviceSummary {
                id: id.clone(),
                name,
                rssi: properties.rssi,
            });
            map.insert(id, peripheral);
        }
        found.sort_by(|left, right| {
            right
                .rssi
                .cmp(&left.rssi)
                .then_with(|| left.name.cmp(&right.name))
        });
        Ok(found)
    }

    /// Connect and return an event receiver owned by the caller. The caller
    /// decides independently whether events go to files, networks, or a UI.
    pub async fn connect(
        self: &Arc<Self>,
        device_id: &str,
    ) -> Result<mpsc::Receiver<InputEvent>, String> {
        self.disconnect().await?;
        let peripheral = self
            .devices
            .lock()
            .await
            .get(device_id)
            .cloned()
            .ok_or_else(|| {
                "That sensor is no longer in the scan results. Scan again.".to_string()
            })?;
        let (event_tx, event_rx) = mpsc::channel(128);
        send(
            &event_tx,
            InputEvent::Status {
                phase: "connecting",
                message: "Opening the low-energy connection…".into(),
            },
        )
        .await?;

        if !peripheral
            .is_connected()
            .await
            .map_err(|error| format!("Could not read connection state: {error}"))?
        {
            peripheral
                .connect()
                .await
                .map_err(|error| format!("Polar H10 connection failed: {error}"))?;
        }
        send(
            &event_tx,
            InputEvent::Status {
                phase: "discovering",
                message: "Connected. Discovering ECG and accelerometer services…".into(),
            },
        )
        .await?;
        peripheral
            .discover_services()
            .await
            .map_err(|error| format!("GATT service discovery failed: {error}"))?;

        let characteristics = peripheral.characteristics();
        let find_characteristic = |uuid| {
            characteristics
                .iter()
                .find(|characteristic| characteristic.uuid == uuid)
                .cloned()
        };
        let pmd_data = find_characteristic(PMD_DATA)
            .ok_or_else(|| "The sensor does not expose Polar PMD data.".to_string())?;
        let control = find_characteristic(PMD_CONTROL_POINT)
            .ok_or_else(|| "The sensor does not expose the PMD control point.".to_string())?;
        let heart_rate = find_characteristic(HEART_RATE_MEASUREMENT);
        let battery = find_characteristic(BATTERY_LEVEL);

        let mut notifications = peripheral
            .notifications()
            .await
            .map_err(|error| format!("Could not open BLE notifications: {error}"))?;
        peripheral
            .subscribe(&pmd_data)
            .await
            .map_err(|error| format!("Could not subscribe to PMD data: {error}"))?;
        peripheral
            .subscribe(&control)
            .await
            .map_err(|error| format!("Could not subscribe to PMD responses: {error}"))?;
        if let Some(characteristic) = &heart_rate {
            peripheral
                .subscribe(characteristic)
                .await
                .map_err(|error| format!("Could not subscribe to heart rate: {error}"))?;
        }

        peripheral
            .write(&control, &start_ecg_command(), WriteType::WithResponse)
            .await
            .map_err(|error| format!("Could not start ECG: {error}"))?;
        peripheral
            .write(
                &control,
                &start_accelerometer_command(),
                WriteType::WithResponse,
            )
            .await
            .map_err(|error| format!("Could not start accelerometer: {error}"))?;

        let battery_percent = if let Some(characteristic) = battery {
            peripheral
                .read(&characteristic)
                .await
                .ok()
                .and_then(|value| value.first().copied())
        } else {
            None
        };
        let device_name = peripheral
            .properties()
            .await
            .ok()
            .flatten()
            .and_then(|properties| properties.local_name)
            .unwrap_or_else(|| "Polar H10".into());
        let att_mtu = peripheral.mtu();

        let (cancel, mut cancelled) = watch::channel(false);
        self.active.lock().await.replace(ActiveConnection {
            id: device_id.to_string(),
            peripheral: peripheral.clone(),
            cancel,
        });
        send(
            &event_tx,
            InputEvent::Connected {
                device_name: device_name.clone(),
                battery_percent,
                att_mtu,
            },
        )
        .await?;

        let connection_id = device_id.to_string();
        let manager = self.clone();
        tokio::spawn(async move {
            let mut ecg_latency = EcgLatencyEstimator::default();
            loop {
                tokio::select! {
                    changed = cancelled.changed() => {
                        if changed.is_err() || *cancelled.borrow() { break; }
                    }
                    notification = notifications.next() => {
                        let Some(notification) = notification else { break };
                        let arrived_at = Instant::now();
                        let event = if notification.uuid == PMD_DATA {
                            match decode_pmd(&notification.value) {
                                Ok(PmdFrame::Ecg { sensor_timestamp_ns, microvolts }) => {
                                    let samples_per_packet = microvolts.len().min(u16::MAX.into()) as u16;
                                    let estimated_latency_ms = ecg_latency.observe(microvolts.len(), arrived_at);
                                    InputEvent::Ecg {
                                        sensor_timestamp_ns,
                                        microvolts,
                                        estimated_latency_ms,
                                        samples_per_packet,
                                    }
                                }
                                Ok(PmdFrame::Accelerometer { sensor_timestamp_ns, samples }) => {
                                    InputEvent::Accelerometer { sensor_timestamp_ns, samples }
                                }
                                Err(error) => InputEvent::Error(format!("Skipped malformed PMD frame: {error}")),
                            }
                        } else if notification.uuid == HEART_RATE_MEASUREMENT {
                            let frame = decode_heart_rate(&notification.value);
                            InputEvent::HeartRate {
                                beats_per_minute: frame.beats_per_minute,
                                rr_intervals_ms: frame.rr_intervals_ms,
                            }
                        } else {
                            continue;
                        };
                        if event_tx.send(event).await.is_err() { break; }
                    }
                }
            }

            let _ = peripheral
                .write(
                    &control,
                    &stop_command(ECG_MEASUREMENT),
                    WriteType::WithResponse,
                )
                .await;
            let _ = peripheral
                .write(
                    &control,
                    &stop_command(ACC_MEASUREMENT),
                    WriteType::WithResponse,
                )
                .await;
            let _ = peripheral.disconnect().await;
            let mut active = manager.active.lock().await;
            if active
                .as_ref()
                .is_some_and(|connection| connection.id == connection_id)
            {
                active.take();
            }
            let _ = event_tx
                .send(InputEvent::Disconnected {
                    device_name,
                    battery_percent,
                })
                .await;
        });

        Ok(event_rx)
    }

    pub async fn disconnect(&self) -> Result<(), String> {
        let active = self.active.lock().await.take();
        if let Some(active) = active {
            let _ = active.cancel.send(true);
            if active.peripheral.is_connected().await.unwrap_or_default() {
                active
                    .peripheral
                    .disconnect()
                    .await
                    .map_err(|error| format!("Could not disconnect sensor: {error}"))?;
            }
        }
        Ok(())
    }
}

async fn send(sender: &mpsc::Sender<InputEvent>, event: InputEvent) -> Result<(), String> {
    sender
        .send(event)
        .await
        .map_err(|_| "Input event receiver closed.".to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn latency_starts_with_the_observed_ecg_batch_age() {
        let mut estimator = EcgLatencyEstimator::default();
        let started = Instant::now();

        assert_eq!(estimator.observe(17, started), 131);
    }

    #[test]
    fn persistent_host_cadence_can_raise_the_transport_estimate() {
        let mut estimator = EcgLatencyEstimator::default();
        let started = Instant::now();
        estimator.observe(17, started);

        for index in 1..=5 {
            estimator.observe(17, started + Duration::from_millis(index * 180));
        }

        assert_eq!(
            estimator.observe(17, started + Duration::from_millis(6 * 180)),
            180
        );
    }

    #[test]
    fn one_host_scheduling_stall_does_not_dominate_the_median() {
        let mut estimator = EcgLatencyEstimator::default();
        let started = Instant::now();
        for millis in [0, 131, 262, 393, 893, 1_024, 1_155] {
            estimator.observe(17, started + Duration::from_millis(millis));
        }

        assert_eq!(
            estimator.observe(17, started + Duration::from_millis(1_286)),
            131
        );
    }
}
