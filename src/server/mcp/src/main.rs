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

use tangent_connector::adapters::lockfile;
use tangent_connector::adapters::mcp;
use tangent_connector::adapters::operator;
use tangent_connector::adapters::poller;
use tangent_connector::application::hub::ConnectorHub;
use tangent_connector::domain::events::DomainEvent;
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
        "operator" => operator::operator(rest),
        "call" => call(rest),
        "catalog" => catalog(rest),
        "enroll" => enroll(rest),
        "enroll-unbound" => enroll_unbound(rest),
        "identities" => identities(rest),
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
         serve [--force]                MCP stdio server (the agent-facing intake);
                                        also hosts the loopback operator page in-process
                                        (its URL goes to stderr, never stdout)\n\
         operator [--port N] [--no-open] [--force]\n\
                                        local operator web page + tray (identities,
                                        atproto sign-in via the /bind route, enrollments,\n\
                                        status). Fixed default port 5219 (stable URL);\n\
                                        TANGENT_CONNECTOR_PORT or --port overrides\n\
         call <tool> [json] [--view V]  invoke one participation tool through the same hub\n\
         call --stdin [--json]          read `<tool> <json>` lines from standard input\n\
         catalog [--json]               list the tool catalog\n\
         enroll --name N --server URL --token-file P [--identity I] [--no-auto-check]\n\
                                        manual import of an existing session token\n\
         enroll-unbound --identity I --server URL\n\
                                        unbound enrollment (the local-posture disarm\n\
                                        tier; no DID proof — deliberate CLI-only path)
         identities [--json]            list local identities\n\
         companions [--json]            list enrollments (companions)\n\
         check [--name N]               run one background digest check (no model)\n\
         forget --name N                remove an enrollment and its stored session\n\
         \n\
         Environment: TANGENT_CONNECTOR_HOME (state directory);\n\
         TANGENT_CONNECTOR_PORT (operator page port; default 5219, 0 = ephemeral)."
    );
}

// ---------- the MCP intake ----------

fn serve(rest: &[String]) -> i32 {
    let force = rest.iter().any(|argument| argument == "--force");
    if !force && !rest.is_empty() {
        eprintln!("serve takes no options besides --force");
        return EXIT_USAGE;
    }
    let data_dir = data_directory();
    // Long-running verbs are mutually exclusive per data directory: whole-file state
    // saves from two processes would clobber each other.
    let _lock = match lockfile::DataDirLock::acquire(&data_dir, force) {
        Ok(lock) => lock,
        Err(error) => {
            eprintln!("{error}");
            return EXIT_FAILED;
        }
    };
    // The SAME loopback operator server the operator verb hosts, in-process — on the
    // same fixed default port (stable URL; TANGENT_CONNECTOR_PORT or --port overrides).
    // Its URL goes to stderr and the diagnostics journal — NEVER stdout, which is
    // protocol-owned JSON-RPC and nothing else.
    let port = match tangent_connector::adapters::operator::resolve_operator_port(None) {
        Ok(port) => port,
        Err(error) => {
            eprintln!("{error}");
            return EXIT_FAILED;
        }
    };
    let listener = match tangent_connector::adapters::operator::bind_listener(&data_dir, port) {
        Ok(listener) => listener,
        Err(error) => {
            eprintln!("{error}");
            return EXIT_FAILED;
        }
    };
    let bound_port = listener.local_addr().map(|address| address.port()).unwrap_or_default();
    let url = format!("http://127.0.0.1:{bound_port}/");
    eprintln!("Tangent connector operator page: {url}");
    eprintln!("The connector records it in its state so any Connect can pop this page.");
    // The hub is constructed when the initialize request names the connecting client;
    // clientInfo.name becomes the caller (attribution + feed labeling, never a domain
    // input). One process still serves exactly one client. The operator server joins
    // at that moment, sharing the one hub (one state store, one lock).
    let build_data_dir = data_dir.clone();
    let build = move |client_name: &str| -> Result<Arc<ConnectorHub>, String> {
        let hub = build_hub(CallerId(format!("mcp:{client_name}")), build_data_dir.clone())?;
        // The URL enters the diagnostics journal (the operator's recovery path once
        // stderr has scrolled away) and durable state (so a Connect in any process —
        // the CLI one-shots included — can pop this page); no model-visible surface.
        hub.events().publish(DomainEvent::OperatorPageReady { url: url.clone() });
        hub.announce_operator_page(&url);
        let operator_hub = hub.clone();
        std::thread::Builder::new()
            .name("tangent-operator".into())
            .spawn(move || operator::serve(listener, operator_hub))
            .expect("operator server thread");
        let (auto, poll_seconds) = {
            let store = hub.store().lock().expect("state lock");
            let auto = store.companions().iter().filter(|entry| entry.auto_check).map(|entry| entry.companion_id.clone()).collect();
            let poll_seconds = store.policy().poll_seconds;
            (auto, poll_seconds)
        };
        poller::spawn_checkers(hub.clone(), auto, poll_seconds);
        Ok(hub)
    };
    let stdout = std::io::stdout();
    let mut out = stdout.lock();
    let code = mcp::serve(build, &mut out);
    // Clean shutdown (stdin ended): the operator page died with this process, so the
    // recorded URL must not point a later Connect at a dead port. The reachability
    // probe would catch it too, but honest state beats a probe.
    if let Ok(mut store) = tangent_connector::adapters::store::StateStore::open(&data_dir) {
        store.clear_operator_page_url();
        let _ = store.save();
    }
    // stdin ended: the lock releases on drop as the process winds down.
    code
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

/// Reads an operator-supplied session token file: raw `ts_…` token or `{"token": "..."}`
/// JSON. The file is input, never storage — the session is kept in connector state.
fn read_token_file(path: &std::path::Path) -> Result<String, String> {
    let raw = tangent_connector::adapters::store::read_bounded(path, 16 * 1024)?;
    let trimmed = raw.trim();
    if trimmed.starts_with('{') {
        let parsed: Value = serde_json::from_str(trimmed)
            .map_err(|_| "token file is not valid enrollment JSON".to_string())?;
        return parsed.get("token").and_then(|value| value.as_str()).map(str::to_string)
            .filter(|token| !token.is_empty())
            .ok_or_else(|| "token JSON has no token field".to_string());
    }
    if trimmed.len() < 8 {
        return Err("token file does not contain a usable session token".to_string());
    }
    Ok(trimmed.to_string())
}

fn enroll(rest: &[String]) -> i32 {
    let mut name = None;
    let mut server = None;
    let mut token_file = None;
    let mut identity = None;
    let mut auto_check = true;
    let mut remaining = rest.iter();
    while let Some(argument) = remaining.next() {
        match argument.as_str() {
            "--name" => name = remaining.next().cloned(),
            "--server" => server = remaining.next().cloned(),
            "--token-file" => token_file = remaining.next().map(PathBuf::from),
            "--identity" => identity = remaining.next().cloned(),
            "--no-auto-check" => auto_check = false,
            other => {
                eprintln!("unknown enroll option {other}");
                return EXIT_USAGE;
            }
        }
    }
    let (Some(name), Some(server), Some(token_file)) = (name, server, token_file) else {
        eprintln!("enroll requires --name, --server and --token-file");
        return EXIT_USAGE;
    };
    let token = match read_token_file(&token_file) {
        Ok(token) => token,
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
    match hub.enroll_as(&name, identity.as_deref(), &server, &token, auto_check) {
        Ok(entry) => {
            let identity_label = identity.as_deref().unwrap_or(&entry.name).to_string();
            println!(
                "Enrolled {} as identity {} ({}) on {} — manual enrollment of an imported session.\n\
                 companionId: {} (session kept in connector state)",
                entry.name,
                identity_label,
                entry.did.as_deref().unwrap_or(&entry.participant_ref),
                entry.origin,
                entry.companion_id
            );
            println!("Delete the imported token file if it is no longer needed.");
            EXIT_OK
        }
        Err(error) => {
            eprintln!("enrollment failed: {error}");
            EXIT_FAILED
        }
    }
}

/// Unbound enrollment from the CLI — the disarm tier (R2): the page no longer enrolls,
/// so this verb and the hub method are the deliberate remaining paths for the
/// local-posture setting of the same handshake.
fn enroll_unbound(rest: &[String]) -> i32 {
    let mut identity = None;
    let mut server = None;
    let mut remaining = rest.iter();
    while let Some(argument) = remaining.next() {
        match argument.as_str() {
            "--identity" => identity = remaining.next().cloned(),
            "--server" => server = remaining.next().cloned(),
            other => {
                eprintln!("unknown enroll-unbound option {other}");
                return EXIT_USAGE;
            }
        }
    }
    let (Some(identity), Some(server)) = (identity, server) else {
        eprintln!("enroll-unbound requires --identity and --server");
        return EXIT_USAGE;
    };
    let hub = match build_hub(CallerId("cli".into()), data_directory()) {
        Ok(hub) => hub,
        Err(error) => {
            eprintln!("cannot open connector state: {error}");
            return EXIT_FAILED;
        }
    };
    let local_id = {
        let store = hub.store().lock().expect("state lock");
        store.identity_by_moniker(&identity).map(|found| found.local_id)
    };
    let Some(local_id) = local_id else {
        eprintln!("no local identity matches '{identity}'; create one first (operator page or identities command)");
        return EXIT_USAGE;
    };
    match hub.enroll_unbound(&local_id, &server) {
        Ok(entry) => {
            println!(
                "Enrolled identity {identity} unbound at {} — participant {}, session kept in connector state.\n\
                 companionId: {} (no DID proof; the server's local-posture tier)",
                entry.origin,
                entry.participant_ref,
                entry.companion_id
            );
            EXIT_OK
        }
        Err(error) => {
            eprintln!("unbound enrollment failed: {error}");
            EXIT_FAILED
        }
    }
}

fn identities(rest: &[String]) -> i32 {
    let json = rest.iter().any(|argument| argument == "--json");
    let hub = match build_hub(CallerId("cli".into()), data_directory()) {
        Ok(hub) => hub,
        Err(error) => {
            eprintln!("cannot open connector state: {error}");
            return EXIT_FAILED;
        }
    };
    let entries: Vec<(Value, usize)> = {
        let store = hub.store().lock().expect("state lock");
        store
            .identities()
            .iter()
            .map(|identity| {
                let enrollment_count = store.companions_of(&identity.local_id).len();
                (
                    json!({
                        "localId": identity.local_id,
                        "handle": identity.handle,
                        "displayName": identity.display_name,
                        "boundDid": identity.bound_did,
                    }),
                    enrollment_count,
                )
            })
            .collect()
    };
    if json {
        let plain: Vec<Value> = entries.into_iter().map(|(value, _)| value).collect();
        println!("{}", serde_json::to_string_pretty(&plain).unwrap_or_default());
    } else {
        if entries.is_empty() {
            println!("No identities exist yet. Create one in the operator page (tangent-connector operator).");
        }
        for (identity, count) in &entries {
            println!(
                "{}\n    {} · {} enrollment(s)",
                identity.get("handle").and_then(Value::as_str).unwrap_or_default(),
                identity.get("localId").and_then(Value::as_str).unwrap_or_default(),
                count
            );
        }
    }
    EXIT_OK
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
                "identityId": entry.local_id,
                "name": entry.name,
                "participantRef": entry.participant_ref,
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
            println!("No companions are enrolled. Use enroll or the operator page first.");
        }
        for entry in &companions {
            println!(
                "{}\n    identity {} · {} · {} · auto-check {}",
                entry.get("name").and_then(Value::as_str).unwrap_or_default(),
                entry.get("identityId").and_then(Value::as_str).unwrap_or_default(),
                entry.get("participantRef").and_then(Value::as_str).unwrap_or_default(),
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
    store.remove_companion(&entry.companion_id);
    let result = store.save();
    drop(store);
    match result {
        Ok(()) => {
            println!("Removed {} and its stored session.", entry.name);
            EXIT_OK
        }
        Err(error) => {
            eprintln!("state could not be saved: {error}");
            EXIT_FAILED
        }
    }
}
