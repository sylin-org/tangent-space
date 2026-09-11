//! Credential custody. The default facility is the platform credential store (Windows
//! Credential Manager, macOS Keychain, Secret Service). A plaintext file under the data
//! directory is available only as an explicitly env-gated development fallback, labelled as
//! such. Secrets never enter model arguments, results, logs or fixtures.

use std::path::Path;

use crate::adapters::store::read_bounded;

pub const SERVICE: &str = "tangent-connector";
const PLAINTEXT_ENV: &str = "TANGENT_CONNECTOR_PLAINTEXT_CREDENTIALS";
const PLAINTEXT_DIR: &str = "credentials";
const CREDENTIAL_LIMIT: u64 = 16 * 1024;

/// Where a companion's credential lives.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CredentialSource {
    PlatformStore,
    PlaintextDev,
}

/// Reads an operator-supplied credential file: raw `ts_…` token or `{"token": "..."}` JSON.
pub fn read_credential_file(path: &Path) -> Result<String, String> {
    let raw = read_bounded(path, CREDENTIAL_LIMIT)?;
    let trimmed = raw.trim();
    if trimmed.starts_with('{') {
        let parsed: serde_json::Value = serde_json::from_str(trimmed)
            .map_err(|_| "credential file is not valid enrollment JSON".to_string())?;
        return parsed.get("token").and_then(|value| value.as_str()).map(str::to_string)
            .filter(|token| !token.is_empty())
            .ok_or_else(|| "credential JSON has no token field".to_string());
    }
    if trimmed.len() < 8 {
        return Err("credential file does not contain a usable token".to_string());
    }
    Ok(trimmed.to_string())
}

fn plaintext_allowed() -> bool {
    std::env::var(PLAINTEXT_ENV).map(|value| value == "1").unwrap_or(false)
}

fn plaintext_path(data_dir: &Path, name: &str) -> std::path::PathBuf {
    data_dir.join(PLAINTEXT_DIR).join(format!("{}.txt", safe_name(name)))
}

fn safe_name(name: &str) -> String {
    name.chars()
        .map(|character| if character.is_ascii_alphanumeric() || character == '-' || character == '_' { character } else { '_' })
        .collect()
}

/// Stores the secret for one companion. Returns the source it landed in. With the
/// development env flag set, plaintext is chosen outright so automation never touches a real
/// user's credential store.
pub fn store(data_dir: &Path, name: &str, secret: &str) -> Result<CredentialSource, String> {
    if !plaintext_allowed() {
        let entry = keyring::Entry::new(SERVICE, &safe_name(name)).map_err(|error| format!("credential store error: {error}"))?;
        match entry.set_password(secret) {
            Ok(()) => return Ok(CredentialSource::PlatformStore),
            Err(error) => {
                return Err(format!(
                    "the platform credential store rejected this secret ({error}); set {PLAINTEXT_ENV}=1 only for development"
                ));
            }
        }
    }
    let path = plaintext_path(data_dir, name);
    std::fs::create_dir_all(path.parent().unwrap()).map_err(|error| format!("cannot create credential directory: {error}"))?;
    std::fs::write(&path, secret).map_err(|error| format!("cannot write development credential: {error}"))?;
    Ok(CredentialSource::PlaintextDev)
}

/// Loads the secret for one companion from its recorded source.
pub fn load(data_dir: &Path, source: &CredentialSource, name: &str) -> Result<String, String> {
    match source {
        CredentialSource::PlatformStore => {
            let entry = keyring::Entry::new(SERVICE, &safe_name(name)).map_err(|error| format!("credential store error: {error}"))?;
            entry.get_password().map_err(|error| format!("stored credential is unavailable ({error}); enroll again").to_string())
        }
        CredentialSource::PlaintextDev => {
            let path = plaintext_path(data_dir, name);
            read_bounded(&path, CREDENTIAL_LIMIT).map(|value| value.trim().to_string())
        }
    }
}

/// Removes the stored secret for one companion.
pub fn delete(data_dir: &Path, source: &CredentialSource, name: &str) {
    match source {
        CredentialSource::PlatformStore => {
            if let Ok(entry) = keyring::Entry::new(SERVICE, &safe_name(name)) {
                let _ = entry.delete_credential();
            }
        }
        CredentialSource::PlaintextDev => {
            let _ = std::fs::remove_file(plaintext_path(data_dir, name));
        }
    }
}
