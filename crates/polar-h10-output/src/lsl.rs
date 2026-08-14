use std::{
    collections::HashMap,
    ffi::{CString, c_char, c_double, c_float, c_int, c_void},
};

use libloading::Library;
use polar_h10_core::AccSample;

use crate::{CustomFormulaConfig, custom_output_stream_name};
use crate::{MetricSpec, output_stream_name};

type StreamInfo = *mut c_void;
type Outlet = *mut c_void;
type XmlElement = *mut c_void;
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
type GetDesc = unsafe extern "C" fn(StreamInfo) -> XmlElement;
type AppendChild = unsafe extern "C" fn(XmlElement, *const c_char) -> XmlElement;
type AppendChildValue =
    unsafe extern "C" fn(XmlElement, *const c_char, *const c_char) -> XmlElement;

struct LslApi {
    _library: Library,
    create_streaminfo: CreateStreamInfo,
    destroy_streaminfo: DestroyStreamInfo,
    create_outlet: CreateOutlet,
    destroy_outlet: DestroyOutlet,
    push_sample: PushSample,
    local_clock: LocalClock,
    get_desc: GetDesc,
    append_child: AppendChild,
    append_child_value: AppendChildValue,
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
                        let get_desc = *library
                            .get::<GetDesc>(b"lsl_get_desc\0")
                            .map_err(|error| error.to_string())?;
                        let append_child = *library
                            .get::<AppendChild>(b"lsl_append_child\0")
                            .map_err(|error| error.to_string())?;
                        let append_child_value = *library
                            .get::<AppendChildValue>(b"lsl_append_child_value\0")
                            .map_err(|error| error.to_string())?;
                        return Ok(Self {
                            _library: library,
                            create_streaminfo,
                            destroy_streaminfo,
                            create_outlet,
                            destroy_outlet,
                            push_sample,
                            local_clock,
                            get_desc,
                            append_child,
                            append_child_value,
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
        let Some(output_name) = output_stream_name(base_name, spec.id) else {
            return;
        };
        self.add_named_outlet(
            spec.id,
            &output_name,
            spec.stream_type,
            spec.label,
            spec.unit,
            spec.channels,
            spec.rate_hz,
            None,
        );
    }

    pub(crate) fn add_custom_outlet(&mut self, base_name: &str, formula: &CustomFormulaConfig) {
        let output_name = custom_output_stream_name(base_name, formula);
        let source = format!("{:?}", formula.source);
        self.add_named_outlet(
            &formula.id,
            &output_name,
            formula.source.stream_type(),
            &formula.name,
            &formula.unit,
            1,
            formula.source.rate_hz(),
            Some((&formula.expression, &source, &formula.id)),
        );
    }

    #[allow(clippy::too_many_arguments)]
    fn add_named_outlet(
        &mut self,
        key: &str,
        output_name: &str,
        stream_type_value: &str,
        label_value: &str,
        unit_value: &str,
        channels: i32,
        rate_hz: f64,
        formula_metadata: Option<(&str, &str, &str)>,
    ) {
        let Some(api) = &self.api else { return };
        let Ok(name) = CString::new(output_name) else {
            return;
        };
        let Ok(stream_type) = CString::new(stream_type_value) else {
            return;
        };
        let source_id = formula_metadata
            .map(|(_, _, formula_id)| format!("polar-h10-formula-{formula_id}"))
            .unwrap_or_else(|| format!("polar-h10-{output_name}"));
        let Ok(source) = CString::new(source_id) else {
            return;
        };

        // cf_float32 == 1 in the public lsl_channel_format_t enum.
        // SAFETY: C strings live through these calls and info is checked below.
        let info = unsafe {
            (api.create_streaminfo)(
                name.as_ptr(),
                stream_type.as_ptr(),
                channels,
                rate_hz,
                1,
                source.as_ptr(),
            )
        };
        if info.is_null() {
            self.status = format!("Could not create {label_value} stream");
            return;
        }
        self.append_metadata(
            info,
            label_value,
            unit_value,
            stream_type_value,
            channels,
            formula_metadata,
        );
        // SAFETY: info is live; create_outlet copies its metadata.
        let outlet = unsafe { (api.create_outlet)(info, 0, 360) };
        unsafe { (api.destroy_streaminfo)(info) };
        if outlet.is_null() {
            self.status = format!("Could not open {label_value} outlet");
            return;
        }
        self.outlets.insert(
            key.into(),
            LslOutlet {
                handle: outlet,
                rate_hz,
            },
        );
        self.status = format!("Publishing {} stream(s)", self.outlets.len());
    }

    fn append_metadata(
        &self,
        info: StreamInfo,
        label: &str,
        unit: &str,
        stream_type: &str,
        channels: i32,
        formula_metadata: Option<(&str, &str, &str)>,
    ) {
        let Some(api) = &self.api else { return };
        let Ok(channels_name) = CString::new("channels") else {
            return;
        };
        let Ok(channel_name) = CString::new("channel") else {
            return;
        };
        let Ok(label_name) = CString::new("label") else {
            return;
        };
        let Ok(unit_name) = CString::new("unit") else {
            return;
        };
        let Ok(type_name) = CString::new("type") else {
            return;
        };
        let (Ok(label), Ok(unit), Ok(stream_type)) = (
            CString::new(label),
            CString::new(unit),
            CString::new(stream_type),
        ) else {
            return;
        };
        // SAFETY: `info` is live until outlet creation; all C strings live for
        // these calls, and liblsl owns the appended metadata nodes.
        unsafe {
            let desc = (api.get_desc)(info);
            if desc.is_null() {
                return;
            }
            let channels_element = (api.append_child)(desc, channels_name.as_ptr());
            for index in 0..channels {
                let channel = (api.append_child)(channels_element, channel_name.as_ptr());
                let channel_label = if channels == 3 {
                    match index {
                        0 => CString::new("X"),
                        1 => CString::new("Y"),
                        _ => CString::new("Z"),
                    }
                } else {
                    CString::new(label.as_bytes())
                };
                let Ok(channel_label) = channel_label else {
                    return;
                };
                (api.append_child_value)(channel, label_name.as_ptr(), channel_label.as_ptr());
                (api.append_child_value)(channel, unit_name.as_ptr(), unit.as_ptr());
                (api.append_child_value)(channel, type_name.as_ptr(), stream_type.as_ptr());
            }

            if let Some((expression, source, formula_id)) = formula_metadata {
                let Ok(processing_name) = CString::new("processing") else {
                    return;
                };
                let Ok(formula_name) = CString::new("formula") else {
                    return;
                };
                let Ok(source_name) = CString::new("source") else {
                    return;
                };
                let Ok(id_name) = CString::new("formula_id") else {
                    return;
                };
                let (Ok(expression), Ok(source), Ok(formula_id)) = (
                    CString::new(expression),
                    CString::new(source),
                    CString::new(formula_id),
                ) else {
                    return;
                };
                let processing = (api.append_child)(desc, processing_name.as_ptr());
                (api.append_child_value)(processing, formula_name.as_ptr(), expression.as_ptr());
                (api.append_child_value)(processing, source_name.as_ptr(), source.as_ptr());
                (api.append_child_value)(processing, id_name.as_ptr(), formula_id.as_ptr());
            }
        }
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
