use std::{
    collections::HashMap,
    ffi::{CString, c_char, c_double, c_float, c_int, c_void},
};

use libloading::Library;
use polar_h10_core::AccSample;

use crate::{MetricSpec, output_stream_name};

type StreamInfo = *mut c_void;
type Outlet = *mut c_void;
type CreateStreamInfo = unsafe extern "C" fn(
    *const c_char,
    *const c_char,
    c_int,
    c_double,
    c_int,
    *const c_char,
) -> StreamInfo;
type DestroyStreamInfo = unsafe extern "C" fn(StreamInfo);
type CreateOutlet = unsafe extern "C" fn(StreamInfo, c_int, c_int) -> Outlet;
type DestroyOutlet = unsafe extern "C" fn(Outlet);
type PushSample = unsafe extern "C" fn(Outlet, *const c_float, c_double, c_int) -> c_int;
type LocalClock = unsafe extern "C" fn() -> c_double;

struct LslApi {
    _library: Library,
    create_streaminfo: CreateStreamInfo,
    destroy_streaminfo: DestroyStreamInfo,
    create_outlet: CreateOutlet,
    destroy_outlet: DestroyOutlet,
    push_sample: PushSample,
    local_clock: LocalClock,
}

// liblsl documents outlets as usable across threads. Function pointers remain
// valid because their dynamic Library is owned by the same value.
unsafe impl Send for LslApi {}

impl LslApi {
    fn load() -> Result<Self, String> {
        let mut last_error = String::new();
        for candidate in ["liblsl.so", "liblsl.dylib", "lsl.dll"] {
            // SAFETY: The library is retained for the lifetime of every symbol.
            match unsafe { Library::new(candidate) } {
                Ok(library) => {
                    // SAFETY: Names and signatures are from liblsl's stable C API.
                    unsafe {
                        let create_streaminfo = *library
                            .get::<CreateStreamInfo>(b"lsl_create_streaminfo\0")
                            .map_err(|error| error.to_string())?;
                        let destroy_streaminfo = *library
                            .get::<DestroyStreamInfo>(b"lsl_destroy_streaminfo\0")
                            .map_err(|error| error.to_string())?;
                        let create_outlet = *library
                            .get::<CreateOutlet>(b"lsl_create_outlet\0")
                            .map_err(|error| error.to_string())?;
                        let destroy_outlet = *library
                            .get::<DestroyOutlet>(b"lsl_destroy_outlet\0")
                            .map_err(|error| error.to_string())?;
                        let push_sample = *library
                            .get::<PushSample>(b"lsl_push_sample_ftp\0")
                            .map_err(|error| error.to_string())?;
                        let local_clock = *library
                            .get::<LocalClock>(b"lsl_local_clock\0")
                            .map_err(|error| error.to_string())?;
                        return Ok(Self {
                            _library: library,
                            create_streaminfo,
                            destroy_streaminfo,
                            create_outlet,
                            destroy_outlet,
                            push_sample,
                            local_clock,
                        });
                    }
                }
                Err(error) => last_error = error.to_string(),
            }
        }
        Err(format!("liblsl not found ({last_error})"))
    }
}

struct LslOutlet {
    handle: Outlet,
    rate_hz: f64,
}

unsafe impl Send for LslOutlet {}

pub(crate) struct LslPublisher {
    api: Option<LslApi>,
    outlets: HashMap<String, LslOutlet>,
    status: String,
}

impl LslPublisher {
    pub(crate) fn new() -> Self {
        match LslApi::load() {
            Ok(api) => Self {
                api: Some(api),
                outlets: HashMap::new(),
                status: "Ready".into(),
            },
            Err(error) => Self {
                api: None,
                outlets: HashMap::new(),
                status: error,
            },
        }
    }

    pub(crate) fn status(&self) -> &str {
        &self.status
    }

    pub(crate) fn clear(&mut self) {
        if let Some(api) = &self.api {
            for (_, outlet) in self.outlets.drain() {
                // SAFETY: Handles were created by this API and are destroyed once.
                unsafe { (api.destroy_outlet)(outlet.handle) };
            }
        } else {
            self.outlets.clear();
        }
    }

    pub(crate) fn add_outlet(&mut self, base_name: &str, spec: MetricSpec) {
        let Some(api) = &self.api else { return };
        let Some(output_name) = output_stream_name(base_name, spec.id) else {
            return;
        };
        let Ok(name) = CString::new(output_name.as_str()) else {
            return;
        };
        let Ok(stream_type) = CString::new(spec.stream_type) else {
            return;
        };
        let Ok(source) = CString::new(format!("polar-h10-{output_name}")) else {
            return;
        };
        let Ok(_unit) = CString::new(spec.unit) else {
            return;
        };

        // cf_float32 == 1 in the public lsl_channel_format_t enum.
        // SAFETY: C strings live through these calls and info is checked below.
        let info = unsafe {
            (api.create_streaminfo)(
                name.as_ptr(),
                stream_type.as_ptr(),
                spec.channels,
                spec.rate_hz,
                1,
                source.as_ptr(),
            )
        };
        if info.is_null() {
            self.status = format!("Could not create {} stream", spec.label);
            return;
        }
        // SAFETY: info is live; create_outlet copies its metadata.
        let outlet = unsafe { (api.create_outlet)(info, 0, 360) };
        unsafe { (api.destroy_streaminfo)(info) };
        if outlet.is_null() {
            self.status = format!("Could not open {} outlet", spec.label);
            return;
        }
        self.outlets.insert(
            spec.id.into(),
            LslOutlet {
                handle: outlet,
                rate_hz: spec.rate_hz,
            },
        );
        self.status = format!("Publishing {} stream(s)", self.outlets.len());
    }

    pub(crate) fn push_scalar(&mut self, id: &str, value: f32) {
        self.push_values(id, &[value], None);
    }

    pub(crate) fn push_scalar_series<I>(&mut self, id: &str, values: I)
    where
        I: IntoIterator<Item = f32>,
    {
        let values: Vec<f32> = values.into_iter().collect();
        if values.is_empty() {
            return;
        }
        let rate = self.outlets.get(id).map_or(0.0, |outlet| outlet.rate_hz);
        let Some(api) = &self.api else { return };
        // SAFETY: Function pointer comes from the retained library.
        let now = unsafe { (api.local_clock)() };
        for (index, value) in values.iter().enumerate() {
            let backfill = if rate > 0.0 {
                (values.len() - index - 1) as f64 / rate
            } else {
                0.0
            };
            self.push_values(id, &[*value], Some(now - backfill));
        }
    }

    pub(crate) fn push_accelerometer(&mut self, samples: &[AccSample]) {
        let rate = self
            .outlets
            .get("raw_acc")
            .map_or(0.0, |outlet| outlet.rate_hz);
        let Some(api) = &self.api else { return };
        // SAFETY: Function pointer comes from the retained library.
        let now = unsafe { (api.local_clock)() };
        for (index, sample) in samples.iter().enumerate() {
            let backfill = if rate > 0.0 {
                (samples.len() - index - 1) as f64 / rate
            } else {
                0.0
            };
            self.push_values(
                "raw_acc",
                &[
                    f32::from(sample.x_mg),
                    f32::from(sample.y_mg),
                    f32::from(sample.z_mg),
                ],
                Some(now - backfill),
            );
        }
    }

    fn push_values(&mut self, id: &str, values: &[f32], timestamp: Option<f64>) {
        let (Some(api), Some(outlet)) = (&self.api, self.outlets.get(id)) else {
            return;
        };
        // SAFETY: values matches the channel count used to create this outlet.
        let result = unsafe {
            (api.push_sample)(outlet.handle, values.as_ptr(), timestamp.unwrap_or(0.0), 1)
        };
        if result != 0 {
            self.status = format!("LSL push failed ({result})");
        }
    }
}

impl Drop for LslPublisher {
    fn drop(&mut self) {
        self.clear();
    }
}
