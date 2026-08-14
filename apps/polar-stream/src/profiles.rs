use std::{
    fs,
    io::Write,
    path::{Path, PathBuf},
    sync::Mutex,
    time::{SystemTime, UNIX_EPOCH},
};

use polar_h10_output::OutputConfig;
use serde::{Deserialize, Serialize};

const SCHEMA_VERSION: u32 = 1;
const MAX_SETTINGS_BYTES: u64 = 512 * 1_024;
const MAX_PROFILES: usize = 50;
const MAX_VISUALIZERS: usize = 16;

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SavedSensor {
    pub id: String,
    pub name: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct VisualizerProfile {
    pub source: String,
    pub width: u32,
    pub height: u32,
    pub user_sized: bool,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkspaceLayout {
    pub pane_fractions: [f32; 2],
    pub visualizers: Vec<VisualizerProfile>,
}

impl Default for WorkspaceLayout {
    fn default() -> Self {
        Self {
            pane_fractions: [0.27, 0.30],
            visualizers: vec![VisualizerProfile {
                source: "raw_ecg".into(),
                width: 0,
                height: 0,
                user_sized: false,
            }],
        }
    }
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkspaceProfileV1 {
    #[serde(default = "schema_version")]
    pub schema_version: u32,
    pub preferred_sensor: Option<SavedSensor>,
    pub output_config: OutputConfig,
    #[serde(default)]
    pub workspace: WorkspaceLayout,
}

impl Default for WorkspaceProfileV1 {
    fn default() -> Self {
        Self {
            schema_version: SCHEMA_VERSION,
            preferred_sensor: None,
            output_config: OutputConfig::default(),
            workspace: WorkspaceLayout::default(),
        }
    }
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct NamedProfile {
    name: String,
    updated_at_ms: u64,
    profile: WorkspaceProfileV1,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct ProfileDocument {
    schema_version: u32,
    last_session: Option<WorkspaceProfileV1>,
    profiles: Vec<NamedProfile>,
}

impl Default for ProfileDocument {
    fn default() -> Self {
        Self {
            schema_version: SCHEMA_VERSION,
            last_session: None,
            profiles: Vec::new(),
        }
    }
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProfileSummary {
    pub name: String,
    pub updated_at_ms: u64,
}

pub struct ProfileStore {
    path: PathBuf,
    document: Mutex<ProfileDocument>,
}

impl ProfileStore {
    pub fn open(config_directory: PathBuf) -> Self {
        let path = config_directory.join("workspace-profiles-v1.json");
        let document = load_document(&path).unwrap_or_default();
        Self {
            path,
            document: Mutex::new(document),
        }
    }

    #[cfg(test)]
    fn at(path: PathBuf) -> Self {
        let document = load_document(&path).unwrap_or_default();
        Self {
            path,
            document: Mutex::new(document),
        }
    }

    pub fn last_session(&self) -> Option<WorkspaceProfileV1> {
        self.document
            .lock()
            .ok()
            .and_then(|document| document.last_session.clone())
    }

    pub fn summaries(&self) -> Vec<ProfileSummary> {
        self.document
            .lock()
            .map(|document| {
                document
                    .profiles
                    .iter()
                    .map(|profile| ProfileSummary {
                        name: profile.name.clone(),
                        updated_at_ms: profile.updated_at_ms,
                    })
                    .collect()
            })
            .unwrap_or_default()
    }

    pub fn save_last_session(&self, profile: WorkspaceProfileV1) -> Result<(), String> {
        let profile = validate_profile(profile)?;
        let mut document = self
            .document
            .lock()
            .map_err(|_| "Profile store lock failed")?;
        document.last_session = Some(profile);
        write_document(&self.path, &document)
    }

    pub fn save_named(
        &self,
        name: String,
        profile: WorkspaceProfileV1,
    ) -> Result<ProfileSummary, String> {
        let name = validate_profile_name(&name)?;
        let profile = validate_profile(profile)?;
        let updated_at_ms = now_ms();
        let mut document = self
            .document
            .lock()
            .map_err(|_| "Profile store lock failed")?;
        if let Some(existing) = document
            .profiles
            .iter_mut()
            .find(|profile| profile.name.eq_ignore_ascii_case(&name))
        {
            existing.name = name.clone();
            existing.updated_at_ms = updated_at_ms;
            existing.profile = profile;
        } else {
            if document.profiles.len() >= MAX_PROFILES {
                return Err(format!(
                    "At most {MAX_PROFILES} named profiles may be saved."
                ));
            }
            document.profiles.push(NamedProfile {
                name: name.clone(),
                updated_at_ms,
                profile,
            });
        }
        document
            .profiles
            .sort_by_key(|profile| std::cmp::Reverse(profile.updated_at_ms));
        write_document(&self.path, &document)?;
        Ok(ProfileSummary {
            name,
            updated_at_ms,
        })
    }

    pub fn load_named(&self, name: &str) -> Result<WorkspaceProfileV1, String> {
        let name = validate_profile_name(name)?;
        self.document
            .lock()
            .map_err(|_| "Profile store lock failed")?
            .profiles
            .iter()
            .find(|profile| profile.name.eq_ignore_ascii_case(&name))
            .map(|profile| profile.profile.clone())
            .ok_or_else(|| format!("Profile '{name}' was not found."))
    }

    pub fn delete_named(&self, name: &str) -> Result<(), String> {
        let name = validate_profile_name(name)?;
        let mut document = self
            .document
            .lock()
            .map_err(|_| "Profile store lock failed")?;
        let previous_len = document.profiles.len();
        document
            .profiles
            .retain(|profile| !profile.name.eq_ignore_ascii_case(&name));
        if previous_len == document.profiles.len() {
            return Err(format!("Profile '{name}' was not found."));
        }
        write_document(&self.path, &document)
    }
}

fn schema_version() -> u32 {
    SCHEMA_VERSION
}

fn validate_profile(mut profile: WorkspaceProfileV1) -> Result<WorkspaceProfileV1, String> {
    if profile.schema_version != SCHEMA_VERSION {
        return Err(format!(
            "Unsupported workspace profile version {}.",
            profile.schema_version
        ));
    }
    profile.output_config = profile.output_config.normalized()?;
    if let Some(sensor) = &profile.preferred_sensor
        && (sensor.id.is_empty()
            || sensor.id.len() > 256
            || sensor.name.is_empty()
            || sensor.name.len() > 256
            || sensor
                .id
                .chars()
                .chain(sensor.name.chars())
                .any(char::is_control))
    {
        return Err("Saved sensor details are invalid.".into());
    }
    if profile.workspace.visualizers.len() > MAX_VISUALIZERS {
        return Err(format!(
            "At most {MAX_VISUALIZERS} visualizers may be saved."
        ));
    }
    if profile
        .workspace
        .pane_fractions
        .iter()
        .any(|value| !value.is_finite() || !(0.05..=0.90).contains(value))
    {
        return Err("Workspace pane proportions are invalid.".into());
    }
    for visualizer in &profile.workspace.visualizers {
        if visualizer.source.is_empty()
            || visualizer.source.len() > 128
            || visualizer.source.chars().any(char::is_control)
            || visualizer.width > 8_192
            || visualizer.height > 8_192
        {
            return Err("Visualizer settings are invalid.".into());
        }
    }
    Ok(profile)
}

fn validate_profile_name(value: &str) -> Result<String, String> {
    let value = value.trim();
    if value.is_empty() || value.chars().count() > 64 || value.chars().any(char::is_control) {
        return Err("Profile name must contain 1 to 64 printable characters.".into());
    }
    if value.eq_ignore_ascii_case("Last session") {
        return Err("'Last session' is reserved for automatic recovery.".into());
    }
    Ok(value.to_string())
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis()
        .try_into()
        .unwrap_or(u64::MAX)
}

fn load_document(path: &Path) -> Result<ProfileDocument, String> {
    let metadata = match fs::metadata(path) {
        Ok(metadata) => metadata,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return Ok(ProfileDocument::default());
        }
        Err(error) => return Err(format!("Could not inspect profile settings: {error}")),
    };
    if metadata.len() > MAX_SETTINGS_BYTES {
        preserve_recovery(path);
        return Err("Profile settings file is too large.".into());
    }
    let bytes =
        fs::read(path).map_err(|error| format!("Could not read profile settings: {error}"))?;
    let mut document: ProfileDocument = serde_json::from_slice(&bytes).map_err(|error| {
        preserve_recovery(path);
        format!("Profile settings are corrupt: {error}")
    })?;
    if document.schema_version != SCHEMA_VERSION {
        preserve_recovery(path);
        return Err(format!(
            "Unsupported profile settings version {}.",
            document.schema_version
        ));
    }
    if document.profiles.len() > MAX_PROFILES {
        preserve_recovery(path);
        return Err("Profile settings contain too many named profiles.".into());
    }
    if let Some(last_session) = document.last_session.take() {
        document.last_session = Some(validate_profile(last_session).map_err(|error| {
            preserve_recovery(path);
            format!("Last-session settings are invalid: {error}")
        })?);
    }
    for named in &mut document.profiles {
        named.name = validate_profile_name(&named.name).map_err(|error| {
            preserve_recovery(path);
            format!("Named profile is invalid: {error}")
        })?;
        named.profile = validate_profile(named.profile.clone()).map_err(|error| {
            preserve_recovery(path);
            format!("Named profile '{}' is invalid: {error}", named.name)
        })?;
    }
    Ok(document)
}

fn preserve_recovery(path: &Path) {
    let recovery = path.with_file_name(format!("workspace-profiles-recovery-{}.json", now_ms()));
    let _ = fs::rename(path, recovery);
}

fn write_document(path: &Path, document: &ProfileDocument) -> Result<(), String> {
    let bytes = serde_json::to_vec_pretty(document)
        .map_err(|error| format!("Could not encode profile settings: {error}"))?;
    if bytes.len() as u64 > MAX_SETTINGS_BYTES {
        return Err("Profile settings exceed the storage limit.".into());
    }
    let parent = path
        .parent()
        .ok_or_else(|| "Profile settings directory is unavailable.".to_string())?;
    fs::create_dir_all(parent)
        .map_err(|error| format!("Could not create profile settings directory: {error}"))?;
    let temporary = path.with_extension("json.tmp");
    let previous = path.with_extension("json.previous");
    {
        let mut file = fs::File::create(&temporary)
            .map_err(|error| format!("Could not create temporary profile settings: {error}"))?;
        file.write_all(&bytes)
            .and_then(|()| file.sync_all())
            .map_err(|error| format!("Could not write profile settings: {error}"))?;
    }
    if path.exists() {
        let _ = fs::remove_file(&previous);
        fs::rename(path, &previous)
            .map_err(|error| format!("Could not stage existing profile settings: {error}"))?;
    }
    if let Err(error) = fs::rename(&temporary, path) {
        if previous.exists() {
            let _ = fs::rename(&previous, path);
        }
        return Err(format!("Could not install profile settings: {error}"));
    }
    let _ = fs::remove_file(previous);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temporary_path(name: &str) -> PathBuf {
        std::env::temp_dir().join(format!(
            "polar-stream-profile-test-{name}-{}-{}.json",
            std::process::id(),
            now_ms()
        ))
    }

    #[test]
    fn saves_and_restores_last_session_and_named_profiles() {
        let path = temporary_path("roundtrip");
        let store = ProfileStore::at(path.clone());
        let mut profile = WorkspaceProfileV1::default();
        profile.output_config.stream_name = "Participant 07".into();
        store.save_last_session(profile.clone()).unwrap();
        store.save_named("Baseline".into(), profile).unwrap();

        let reopened = ProfileStore::at(path.clone());
        assert_eq!(
            reopened.last_session().unwrap().output_config.stream_name,
            "Participant_07"
        );
        assert_eq!(reopened.summaries()[0].name, "Baseline");
        assert_eq!(
            reopened
                .load_named("baseline")
                .unwrap()
                .output_config
                .stream_name,
            "Participant_07"
        );
        reopened.delete_named("Baseline").unwrap();
        assert!(reopened.summaries().is_empty());
        let _ = fs::remove_file(path);
    }

    #[test]
    fn rejects_reserved_or_oversized_profiles() {
        let path = temporary_path("limits");
        let store = ProfileStore::at(path.clone());
        assert!(
            store
                .save_named("Last session".into(), WorkspaceProfileV1::default())
                .is_err()
        );
        let mut profile = WorkspaceProfileV1::default();
        profile.workspace.visualizers = (0..=MAX_VISUALIZERS)
            .map(|index| VisualizerProfile {
                source: format!("source-{index}"),
                width: 1,
                height: 1,
                user_sized: false,
            })
            .collect();
        assert!(store.save_last_session(profile).is_err());
        let _ = fs::remove_file(path);
    }

    #[test]
    fn preserves_corrupt_settings_as_a_recovery_file() {
        let path = temporary_path("corrupt");
        fs::write(&path, b"not-json").unwrap();
        let store = ProfileStore::at(path.clone());
        assert!(store.last_session().is_none());
        assert!(!path.exists());
        if let Some(parent) = path.parent() {
            for entry in fs::read_dir(parent).unwrap().flatten() {
                let name = entry.file_name();
                if name
                    .to_string_lossy()
                    .starts_with("workspace-profiles-recovery-")
                {
                    let _ = fs::remove_file(entry.path());
                }
            }
        }
    }
}
