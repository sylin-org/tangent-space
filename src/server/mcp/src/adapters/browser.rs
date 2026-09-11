//! Browser opening for the operator page: the platform's own handler, spawned detached
//! with null stdio so the connector never owns or waits on a browser process. The
//! ghostlight `browser_command()` shape (win-peer/bridge house style).

use std::process::{Command, Stdio};

/// The platform command that hands a URL to the user's default browser.
pub fn browser_command(url: &str) -> Command {
    let mut command = if cfg!(target_os = "windows") {
        // rundll32 with FileProtocolHandler resolves the default browser without a shell.
        let mut command = Command::new("rundll32.exe");
        command.arg("url.dll,FileProtocolHandler").arg(url);
        command
    } else if cfg!(target_os = "macos") {
        let mut command = Command::new("open");
        command.arg(url);
        command
    } else {
        let mut command = Command::new("xdg-open");
        command.arg(url);
        command
    };
    command.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null());
    command
}

/// Opens the URL detached; failures are silent (the operator page prints the URL too).
pub fn open(url: &str) {
    let _ = browser_command(url).spawn();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_command_is_the_platform_handler() {
        let url = "http://127.0.0.1:5220/?token=abc123";
        let command = browser_command(url);
        let program = command.get_program().to_string_lossy().to_string();
        let arguments: Vec<String> = command
            .get_args()
            .map(|argument| argument.to_string_lossy().to_string())
            .collect();
        if cfg!(target_os = "windows") {
            assert!(program.ends_with("rundll32.exe"), "program was {program}");
            assert_eq!(arguments, vec!["url.dll,FileProtocolHandler".to_string(), url.to_string()]);
        } else if cfg!(target_os = "macos") {
            assert_eq!(program, "open");
            assert_eq!(arguments, vec![url.to_string()]);
        } else {
            assert_eq!(program, "xdg-open");
            assert_eq!(arguments, vec![url.to_string()]);
        }
    }
}
