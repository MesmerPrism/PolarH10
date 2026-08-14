use std::{
    env, fs,
    path::{Path, PathBuf},
    sync::Arc,
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

use capture_preview_fixture::{
    ACC_SAMPLE_RATE_HZ, CaptureBuffer, DEFAULT_DURATION, ECG_SAMPLE_RATE_HZ, MAX_DURATION,
    PreviewFixture, render_svg, validate_fixture,
};
use polar_h10_input::{DeviceSummary, InputEvent, InputManager};

const STREAM_START_TIMEOUT: Duration = Duration::from_secs(20);
const CAPTURE_GRACE: Duration = Duration::from_secs(15);

struct Options {
    device_filter: Option<String>,
    duration: Duration,
    fixture_path: PathBuf,
    svg_path: PathBuf,
    render_only: bool,
}

#[tokio::main]
async fn main() {
    if let Err(error) = run().await {
        eprintln!("error: {error}");
        std::process::exit(1);
    }
}

async fn run() -> Result<(), String> {
    let options = parse_options(env::args().skip(1))?;
    if options.render_only {
        let bytes = fs::read(&options.fixture_path).map_err(|error| {
            format!("Could not read {}: {error}", options.fixture_path.display())
        })?;
        let fixture: PreviewFixture = serde_json::from_slice(&bytes)
            .map_err(|error| format!("Preview fixture JSON is invalid: {error}"))?;
        validate_fixture(&fixture)?;
        write_text(&options.svg_path, &render_svg(&fixture)?)?;
        println!("Rendered {}", options.svg_path.display());
        return Ok(());
    }

    println!("Scanning for an awake Polar H10…");
    let manager = Arc::new(InputManager::new());
    let devices = manager.scan().await?;
    let device = select_device(&devices, options.device_filter.as_deref())?;
    println!(
        "Connecting to {} ({}) for a {} second ECG + ACC capture…",
        device.name,
        device.id,
        options.duration.as_secs()
    );
    let mut events = manager.connect(&device.id).await?;
    let captured = capture_stream(&mut events, options.duration).await;
    let disconnect_result = manager.disconnect().await;
    let buffer = captured?;
    disconnect_result?;

    let recorded_at_unix_ms = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|_| "System clock is before the Unix epoch.".to_string())?
        .as_millis()
        .min(u128::from(u64::MAX)) as u64;
    let fixture = buffer.finish(options.duration, recorded_at_unix_ms)?;
    let json = serde_json::to_vec(&fixture)
        .map_err(|error| format!("Could not serialize preview fixture: {error}"))?;
    let svg = render_svg(&fixture)?;
    write_bytes(&options.fixture_path, &json)?;
    write_text(&options.svg_path, &svg)?;
    println!(
        "Saved {} ECG samples and {} ACC samples to {}",
        fixture.ecg.microvolts.len(),
        fixture.accelerometer.samples.len(),
        options.fixture_path.display()
    );
    println!("Rendered {}", options.svg_path.display());
    Ok(())
}

async fn capture_stream(
    events: &mut tokio::sync::mpsc::Receiver<InputEvent>,
    duration: Duration,
) -> Result<CaptureBuffer, String> {
    println!("Waiting for both raw streams…");
    wait_for_both_streams(events).await?;
    println!(
        "Both streams are live. Recording now; keep reasonably still unless motion is intentional."
    );

    let started = Instant::now();
    let deadline = started + duration + CAPTURE_GRACE;
    let ecg_target = duration.as_secs() as usize * usize::from(ECG_SAMPLE_RATE_HZ);
    let acc_target = duration.as_secs() as usize * usize::from(ACC_SAMPLE_RATE_HZ);
    let mut buffer = CaptureBuffer::default();
    let mut last_progress_second = 0;

    while buffer.ecg_len() < ecg_target || buffer.accelerometer_len() < acc_target {
        let remaining = deadline.saturating_duration_since(Instant::now());
        let event = tokio::time::timeout(remaining, events.recv())
            .await
            .map_err(|_| {
                format!(
                    "Timed out before both streams reached their target counts (ECG {}/{ecg_target}, ACC {}/{acc_target}).",
                    buffer.ecg_len(),
                    buffer.accelerometer_len()
                )
            })?
            .ok_or_else(|| "Polar H10 disconnected before capture completed.".to_string())?;
        match event {
            InputEvent::Ecg { microvolts, .. } if buffer.ecg_len() < ecg_target => {
                buffer.push_ecg(&microvolts);
            }
            InputEvent::Accelerometer { samples, .. }
                if buffer.accelerometer_len() < acc_target =>
            {
                buffer.push_accelerometer(&samples);
            }
            InputEvent::HeartRate {
                beats_per_minute,
                rr_intervals_ms,
            } => buffer.push_metrics(started.elapsed(), beats_per_minute, rr_intervals_ms),
            InputEvent::Error(message) => eprintln!("warning: {message}"),
            InputEvent::Disconnected { .. } => {
                return Err("Polar H10 disconnected before capture completed.".into());
            }
            _ => {}
        }

        let complete_fraction = (buffer.ecg_len() as f64 / ecg_target as f64)
            .min(buffer.accelerometer_len() as f64 / acc_target as f64)
            .clamp(0.0, 1.0);
        let complete_second = (complete_fraction * duration.as_secs_f64()).floor() as u64;
        if complete_second >= last_progress_second + 10 {
            last_progress_second = complete_second;
            println!(
                "  {complete_second}/{} seconds captured",
                duration.as_secs()
            );
        }
    }
    Ok(buffer)
}

async fn wait_for_both_streams(
    events: &mut tokio::sync::mpsc::Receiver<InputEvent>,
) -> Result<(), String> {
    let deadline = Instant::now() + STREAM_START_TIMEOUT;
    let mut saw_ecg = false;
    let mut saw_acc = false;
    while !saw_ecg || !saw_acc {
        let event = tokio::time::timeout(
            deadline.saturating_duration_since(Instant::now()),
            events.recv(),
        )
        .await
        .map_err(|_| "Timed out waiting for both ECG and accelerometer streams.".to_string())?
        .ok_or_else(|| "Polar H10 disconnected while the streams were starting.".to_string())?;
        match event {
            InputEvent::Ecg { .. } => saw_ecg = true,
            InputEvent::Accelerometer { .. } => saw_acc = true,
            InputEvent::Error(message) => eprintln!("warning: {message}"),
            InputEvent::Disconnected { .. } => {
                return Err("Polar H10 disconnected while the streams were starting.".into());
            }
            _ => {}
        }
    }
    Ok(())
}

fn select_device(
    devices: &[DeviceSummary],
    requested: Option<&str>,
) -> Result<DeviceSummary, String> {
    if devices.is_empty() {
        return Err(
            "No Polar sensor was found. Wear the moistened strap, keep it near this computer, and retry."
                .into(),
        );
    }
    if let Some(requested) = requested {
        let requested_lower = requested.to_ascii_lowercase();
        let matches: Vec<_> = devices
            .iter()
            .filter(|device| {
                device.id == requested
                    || device.name.to_ascii_lowercase().contains(&requested_lower)
            })
            .collect();
        return match matches.as_slice() {
            [device] => Ok((*device).clone()),
            [] => Err(format_device_choices(
                devices,
                "No scanned Polar sensor matched --device.",
            )),
            _ => Err(format_device_choices(
                devices,
                "--device matched more than one sensor; use the exact device ID.",
            )),
        };
    }
    match devices {
        [device] => Ok(device.clone()),
        _ => Err(format_device_choices(
            devices,
            "More than one Polar sensor was found; rerun with --device <ID>.",
        )),
    }
}

fn format_device_choices(devices: &[DeviceSummary], message: &str) -> String {
    let choices = devices
        .iter()
        .map(|device| format!("  {}  {}", device.id, device.name))
        .collect::<Vec<_>>()
        .join("\n");
    format!("{message}\n{choices}")
}

fn parse_options(arguments: impl IntoIterator<Item = String>) -> Result<Options, String> {
    let repository = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../..");
    let mut options = Options {
        device_filter: None,
        duration: DEFAULT_DURATION,
        fixture_path: repository.join("apps/polar-stream/ui/data/preview-recording.json"),
        svg_path: repository.join("docs/assets/polar-stream-recorded-preview.svg"),
        render_only: false,
    };
    let mut arguments = arguments.into_iter();
    while let Some(argument) = arguments.next() {
        match argument.as_str() {
            "--device" => {
                options.device_filter = Some(next_value(&mut arguments, "--device")?);
            }
            "--duration" => {
                let value = next_value(&mut arguments, "--duration")?;
                let seconds = value
                    .parse::<u64>()
                    .map_err(|_| "--duration must be a whole number of seconds.".to_string())?;
                options.duration = Duration::from_secs(seconds);
                if options.duration.is_zero() || options.duration > MAX_DURATION {
                    return Err("--duration must be from 1 to 600 seconds.".into());
                }
            }
            "--out" => {
                options.fixture_path = PathBuf::from(next_value(&mut arguments, "--out")?);
            }
            "--svg-out" => {
                options.svg_path = PathBuf::from(next_value(&mut arguments, "--svg-out")?);
            }
            "--render-only" => options.render_only = true,
            "--help" | "-h" => return Err(usage().into()),
            _ => return Err(format!("Unknown argument: {argument}\n\n{}", usage())),
        }
    }
    Ok(options)
}

fn next_value(
    arguments: &mut impl Iterator<Item = String>,
    option: &str,
) -> Result<String, String> {
    arguments
        .next()
        .filter(|value| !value.is_empty())
        .ok_or_else(|| format!("{option} requires a value."))
}

fn usage() -> &'static str {
    "Capture one real Polar H10 loop for all previews.\n\n\
Usage:\n  cargo run -p capture-preview-fixture -- [options]\n\n\
Options:\n  --device <ID-or-name>  Select a sensor when more than one is visible\n  --duration <seconds>   Recording length (default: 60)\n  --out <path>           JSON fixture output path\n  --svg-out <path>       Derived waveform SVG output path\n  --render-only          Rebuild the SVG from an existing JSON fixture\n  -h, --help             Show this help"
}

fn write_bytes(path: &Path, contents: &[u8]) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)
            .map_err(|error| format!("Could not create {}: {error}", parent.display()))?;
    }
    fs::write(path, contents)
        .map_err(|error| format!("Could not write {}: {error}", path.display()))
}

fn write_text(path: &Path, contents: &str) -> Result<(), String> {
    write_bytes(path, contents.as_bytes())
}
