//! The command-line edge: one local intake for scripts, operators and humans. It owns
//! argument parsing, rendering and exit codes, and makes no product decision — every call
//! crosses the same hub, journal and completion path an MCP client's call does. Setup and
//! stewardship (enrollment, checks, companion management) also live here, never in the
//! model-facing catalog.

use std::io::{BufRead, Write};
use std::path::PathBuf;
use std::process::exit;
use std::sync::Arc;

use serde_json::{json, Value};

use tangent_connector::adapters::credentials;
use tangent_connector::adapters::mcp;
use tangent_connector::adapters::poller;
use tangent_connector::application::hub::ConnectorHub;
use tangent_connector::domain::identity::CallerId;
use tangent_connector::domain::intake::IntakeChannel;
use tangent_connector::{build_hub, data_directory};

const EXIT_OK: i32 = 0;
const EXIT_USAGE: i32 = 1;
const EXIT_PENDING: i32 = 2;
const EXIT_BLOCKED: i32 = 3;
const EXIT_FAILED: i32 = 4;

fn main() {
    let arguments: Vec<String> = std::env::args().skip(1).collect();
    let code = run(&arguments);
    exit(code);
}

fn run(arguments: &[String]) -> i32 {
    let Some(command) = arguments.first() else {
        usage();
        return EXIT_USAGE;
    };
    let rest = &arguments[1..];
    match command.as_str() {
        "serve" => serve(rest),
        "call" => call(rest),
        "catalog" => catalog(rest),
        "enroll" => enroll(rest),
        "companions" => companions(rest),
        "check" => check(rest),
        "forget" => forget(rest),
        "--help" | "-h" | "help" => {
            usage();
            EXIT_OK
        }
        other => {
            eprintln!("unknown command '{other}'");
            usage();
            EXIT_USAGE
        }
    }
}

fn usage() {
    eprintln!(
        "tangent-connector — the personal local MCP connector for Tangent\n\
         \n\
         serve                          MCP stdio server (the agent-facing intake)\n\
         call <tool> [json] [--view V]  invoke one participation tool through the same hub\n\
         call --stdin [--json]          read `<tool> <json>` lines from standard input\n\
         catalog [--json]               list the tool catalog\n\
         enroll --name N --server URL --credential-file P [--no-auto-check]\n\
         companions [--json]            list enrolled companions\n\
         check [--name N]               run one background digest check (no model)\n\
         forget --name N                remove a companion and its stored credential\n\
         \n\
         Environment: TANGENT_CONNECTOR_HOME (state directory),\n\
         TANGENT_CONNECTOR_PLAINTEXT_CREDENTIALS=1 (development credential fallback)."
    );
}

// ---------- the MCP intake ----------

fn serve(rest: &[String]) -> i32 {
    if !rest.is_empty() {
        eprintln!("serve takes no options");
        return EXIT_USAGE;
    }
    let data_dir = data_directory();
    let hub = match build_hub(CallerId("mcp".into()), data_dir) {
        Ok(hub) => hub,
        Err(error) => {
            eprintln!("cannot open connector state: {error}");
            return EXIT_FAILED;
        }
    };
    let auto: Vec<String> = {
        let store = hub.store().lock().expect("state lock");
        store.companions().iter().filter(|entry| entry.auto_check).map(|entry| entry.companion_id.clone()).collect()
    };
    let poll_seconds = {
        let store = hub.store().lock().expect("state lock");
        store.policy().poll_seconds
    };
    let _stoppers = poller::spawn_checkers(hub.clone(), auto, poll_seconds);
    let stdout = std::io::stdout();
    let mut out = stdout.lock();
    mcp::serve(hub, &mut out)
}

// ---------- the CLI intake ----------

struct CallOptions {
    view: Option<String>,
    json: bool,
    stdin: bool,
    positional: Vec<String>,
}

fn parse_call(rest: &[String]) -> Result<CallOptions, String> {
    let mut options = CallOptions { view: None, json: false, stdin: false, positional: Vec::new() };
    let mut remaining = rest.iter();
    while let Some(argument) = remaining.next() {
        match argument.as_str() {
            "--json" => options.json = true,
            "--stdin" => options.stdin = true,
            "--view" => {
                options.view = Some(remaining.next().ok_or("--view needs a value")?.clone());
            }
            other if other.starts_with("--view=") => options.view = Some(other["--view=".len()..].to_string()),
            other if other.starts_with("--") => return Err(format!("unknown option {other}")),
            other => options.positional.push(other.to_owned()),
        }
    }
    Ok(options)
}

fn call(rest: &[String]) -> i32 {
    let options = match parse_call(rest) {
        Ok(options) => options,
        Err(error) => {
            eprintln!("{error}");
            return EXIT_USAGE;
        }
    };
    let hub = match build_hub(CallerId("cli".into()), data_directory()) {
        Ok(hub) => hub,
        Err(error) => {
            eprintln!("cannot open connector state: {error}");
            return EXIT_FAILED;
        }
    };
    let stdout = std::io::stdout();
    let mut out = stdout.lock();
    if options.stdin {
        let stdin = std::io::stdin();
        let mut worst = EXIT_OK;
        for line in stdin.lock().lines() {
            let Ok(line) = line else { return EXIT_USAGE };
            let line = line.trim();
            if line.is_empty() {
                continue;
            }
            let (tool, arguments) = line.split_once(char::is_whitespace).unwrap_or((line, "{}"));
            let code = invoke_once(&hub, tool, arguments, &options, &mut out);
            if code != EXIT_OK {
                worst = code;
            }
        }
        return worst;
    }
    match options.positional.as_slice() {
        [tool] => invoke_once(&hub, tool, "{}", &options, &mut out),
        [tool, input] => invoke_once(&hub, tool, input, &options, &mut out),
        [] => {
            eprintln!("call needs a tool name");
            EXIT_USAGE
        }
        _ => {
            eprintln!("one tool and at most one JSON input per call");
            EXIT_USAGE
        }
    }
}

fn invoke_once(hub: &Arc<ConnectorHub>, tool: &str, input: &str, options: &CallOptions, out: &mut impl Write) -> i32 {
    let arguments: Value = match serde_json::from_str(input) {
        Ok(value) => value,
        Err(_) => {
            eprintln!("the input is not valid JSON");
            return EXIT_USAGE;
        }
    };
    let mut arguments = arguments;
    if let Some(view) = &options.view {
        arguments["view"] = json!(view);
    }
    let outcome = hub.invoke(IntakeChannel::Cli, tool, &arguments);
    if options.json {
        let _ = writeln!(out, "{}", serde_json::to_string_pretty(&outcome.structured).unwrap_or_default());
    } else {
        let _ = writeln!(out, "{}", outcome.text);
    }
    match outcome.status.as_str() {
        "ok" if !outcome.is_error => EXIT_OK,
        "ok" => EXIT_BLOCKED,
        "pending" => EXIT_PENDING,
        "blocked" => EXIT_BLOCKED,
        _ => EXIT_FAILED,
    }
}

fn catalog(rest: &[String]) -> i32 {
    let json = rest.iter().any(|argument| argument == "--json");
    let tools = mcp::catalog();
    let Some(tools) = tools.as_array() else { return EXIT_FAILED };
    if json {
        println!("{}", serde_json::to_string_pretty(&tools).unwrap_or_default());
    } else {
        for tool in tools {
            let name = tool.get("name").and_then(Value::as_str).unwrap_or_default();
            let description = tool.get("description").and_then(Value::as_str).unwrap_or_default();
            println!("{name}\n    {description}");
        }
    }
    EXIT_OK
}

// ---------- setup and stewardship ----------

fn enroll(rest: &[String]) -> i32 {
    let mut name = None;
    let mut server = None;
    let mut credential_file = None;
    let mut auto_check = true;
    let mut remaining = rest.iter();
    while let Some(argument) = remaining.next() {
        match argument.as_str() {
            "--name" => name = remaining.next().cloned(),
            "--server" => server = remaining.next().cloned(),
            "--credential-file" => credential_file = remaining.next().map(PathBuf::from),
            "--no-auto-check" => auto_check = false,
            other => {
                eprintln!("unknown enroll option {other}");
                return EXIT_USAGE;
            }
        }
    }
    let (Some(name), Some(server), Some(credential_file)) = (name, server, credential_file) else {
        eprintln!("enroll requires --name, --server and --credential-file");
        return EXIT_USAGE;
    };
    let credential = match credentials::read_credential_file(&credential_file) {
        Ok(credential) => credential,
        Err(error) => {
            eprintln!("{error}");
            return EXIT_USAGE;
        }
    };
    let hub = match build_hub(CallerId("cli".into()), data_directory()) {
        Ok(hub) => hub,
        Err(error) => {
            eprintln!("{error}");
            return EXIT_FAILED;
        }
    };
    match hub.enroll(&name, &server, &credential, auto_check) {
        Ok(entry) => {
            println!(
                "Enrolled {} as {} ({}) on {} — manual enrollment of an imported scoped credential.\n\
                 companionId: {} (credential kept in the {})",
                entry.name,
                entry.display_name.as_deref().unwrap_or(&entry.did),
                entry.did,
                entry.origin,
                entry.companion_id,
                if entry.credential_source == "platform" { "platform credential store" } else { "development plaintext fallback" }
            );
            println!("Delete the imported credential file if it is no longer needed.");
            EXIT_OK
        }
        Err(error) => {
            eprintln!("enrollment failed: {error}");
            EXIT_FAILED
        }
    }
}

fn companions(rest: &[String]) -> i32 {
    let json = rest.iter().any(|argument| argument == "--json");
    let hub = match build_hub(CallerId("cli".into()), data_directory()) {
        Ok(hub) => hub,
        Err(error) => {
            eprintln!("{error}");
            return EXIT_FAILED;
        }
    };
    let store = hub.store().lock().expect("state lock");
    let companions: Vec<Value> = store
        .companions()
        .iter()
        .map(|entry| {
            json!({
                "companionId": entry.companion_id,
                "name": entry.name,
                "did": entry.did,
                "displayName": entry.display_name,
                "handle": entry.handle,
                "server": entry.origin,
                "autoCheck": entry.auto_check,
            })
        })
        .collect();
    drop(store);
    if json {
        println!("{}", serde_json::to_string_pretty(&companions).unwrap_or_default());
    } else {
        if companions.is_empty() {
            println!("No companions are enrolled. Use enroll first.");
        }
        for entry in &companions {
            println!(
                "{}\n    {} · {} · auto-check {}",
                entry.get("name").and_then(Value::as_str).unwrap_or_default(),
                entry.get("did").and_then(Value::as_str).unwrap_or_default(),
                entry.get("server").and_then(Value::as_str).unwrap_or_default(),
                entry.get("autoCheck").and_then(Value::as_bool).unwrap_or_default(),
            );
        }
    }
    EXIT_OK
}

fn check(rest: &[String]) -> i32 {
    let mut name = None;
    let mut remaining = rest.iter();
    while let Some(argument) = remaining.next() {
        match argument.as_str() {
            "--name" => name = remaining.next().cloned(),
            other => {
                eprintln!("unknown check option {other}");
                return EXIT_USAGE;
            }
        }
    }
    let hub = match build_hub(CallerId("cli".into()), data_directory()) {
        Ok(hub) => hub,
        Err(error) => {
            eprintln!("{error}");
            return EXIT_FAILED;
        }
    };
    let targets: Vec<(String, String)> = {
        let store = hub.store().lock().expect("state lock");
        store
            .companions()
            .iter()
            .filter(|entry| name.as_deref().is_none_or(|value| entry.matches(value) || entry.companion_id == value))
            .map(|entry| (entry.companion_id.clone(), entry.name.clone()))
            .collect()
    };
    if targets.is_empty() {
        eprintln!("no matching enrolled companion");
        return EXIT_USAGE;
    }
    let mut worst = EXIT_OK;
    for (companion_id, name) in targets {
        match hub.background_check(&companion_id) {
            Ok(summary) => println!("{name}: {summary}"),
            Err(error) => {
                eprintln!("{name}: {error}");
                worst = EXIT_FAILED;
            }
        }
    }
    worst
}

fn forget(rest: &[String]) -> i32 {
    let mut name = None;
    let mut remaining = rest.iter();
    while let Some(argument) = remaining.next() {
        match argument.as_str() {
            "--name" => name = remaining.next().cloned(),
            other => {
                eprintln!("unknown forget option {other}");
                return EXIT_USAGE;
            }
        }
    }
    let Some(name) = name else {
        eprintln!("forget requires --name");
        return EXIT_USAGE;
    };
    let data_dir = data_directory();
    let hub = match build_hub(CallerId("cli".into()), data_dir.clone()) {
        Ok(hub) => hub,
        Err(error) => {
            eprintln!("{error}");
            return EXIT_FAILED;
        }
    };
    let mut store = hub.store().lock().expect("state lock");
    let Some(entry) = store.find_companion(&name) else {
        eprintln!("no companion matches '{name}'");
        return EXIT_USAGE;
    };
    credentials::delete(
        &data_dir,
        &if entry.credential_source == "plaintext-dev" { credentials::CredentialSource::PlaintextDev } else { credentials::CredentialSource::PlatformStore },
        &entry.name,
    );
    store.remove_companion(&entry.companion_id);
    let result = store.save();
    drop(store);
    match result {
        Ok(()) => {
            println!("Removed {} and its stored credential.", entry.name);
            EXIT_OK
        }
        Err(error) => {
            eprintln!("state could not be saved: {error}");
            EXIT_FAILED
        }
    }
}
