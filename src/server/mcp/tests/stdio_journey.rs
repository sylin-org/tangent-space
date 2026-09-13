//! A real end-to-end stdio journey: enroll through the CLI intake, then drive the compiled
//! binary's MCP edge over its actual stdio transport — initialize negotiation, tools/list,
//! tools/call, ping — against the scripted fake experience server. All data is synthetic.

mod common;

use std::io::{BufRead, BufReader, Read, Write};
use std::process::{Child, Command, Stdio};
use std::sync::mpsc::{channel, Receiver};
use std::thread;

use serde_json::{json, Value};

use common::{FakeServer, LUMEN_CREDENTIAL, STEWARD_CREDENTIAL};

struct Peer {
    child: Child,
    lines: Receiver<String>,
    stderr: Receiver<String>,
}

impl Peer {
    fn spawn(arguments: &[&str], home: &std::path::Path) -> Self {
        let mut child = Command::new(env!("CARGO_BIN_EXE_tangent-connector"))
            .args(arguments)
            .env("TANGENT_CONNECTOR_HOME", home)
            // Browser spawns are guarded in tests; URLs are still constructed.
            .env("TANGENT_CONNECTOR_NO_BROWSER", "1")
            // Tests run in parallel: the operator page takes an ephemeral port instead
            // of the fixed default 5219 (the fixed-port discipline has its own tests).
            .env("TANGENT_CONNECTOR_PORT", "0")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .expect("spawn connector");
        let stdout = child.stdout.take().expect("stdout");
        let (sender, receiver) = channel();
        thread::spawn(move || {
            let reader = BufReader::new(stdout);
            for line in reader.lines() {
                match line {
                    Ok(line) => {
                        if sender.send(line).is_err() {
                            return;
                        }
                    }
                    Err(_) => return,
                }
            }
        });
        let stderr = child.stderr.take().expect("stderr");
        let (error_sender, error_receiver) = channel();
        thread::spawn(move || {
            let reader = BufReader::new(stderr);
            for line in reader.lines() {
                match line {
                    Ok(line) => {
                        if error_sender.send(line).is_err() {
                            return;
                        }
                    }
                    Err(_) => return,
                }
            }
        });
        Self { child, lines: receiver, stderr: error_receiver }
    }

    fn send(&mut self, value: &Value) {
        let mut stdin = self.child.stdin.take().expect("stdin");
        let mut line = serde_json::to_string(value).expect("encode request");
        line.push('\n');
        stdin.write_all(line.as_bytes()).expect("write request");
        stdin.flush().expect("flush request");
        self.child.stdin = Some(stdin);
    }

    fn receive(&mut self) -> Value {
        self.lines.recv_timeout(std::time::Duration::from_secs(10)).expect("response line").parse().expect("valid JSON")
    }
}

impl Drop for Peer {
    fn drop(&mut self) {
        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

#[test]
fn the_stdio_edge_negotiates_and_serves_the_fourteen_tools() {
    let server = FakeServer::start();
    let home = std::env::temp_dir().join(format!("tangent-connector-stdio-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&home);
    std::fs::create_dir_all(&home).expect("temp dir");

    // The CLI intake performs setup: manual enrollment of the synthetic session token.
    let token_file = home.join("session-token.txt");
    std::fs::write(&token_file, LUMEN_CREDENTIAL).expect("token file");
    let enroll = Command::new(env!("CARGO_BIN_EXE_tangent-connector"))
        .args(["enroll", "--name", "lumen", "--server", server.origin(), "--token-file"])
        .arg(&token_file)
        .env("TANGENT_CONNECTOR_HOME", &home)
        .output()
        .expect("run enroll");
    assert!(
        enroll.status.success(),
        "enrollment failed: {}",
        String::from_utf8_lossy(&enroll.stderr)
    );
    assert!(String::from_utf8_lossy(&enroll.stdout).contains("manual enrollment"));

    // The MCP intake: the real stdio transport.
    let mut peer = Peer::spawn(&["serve"], &home);
    peer.send(&json!({
        "jsonrpc": "2.0", "id": 1, "method": "initialize",
        "params": { "protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": { "name": "journey-test", "version": "0" } }
    }));
    let initialize = peer.receive();
    assert_eq!(initialize["id"], json!(1));
    assert_eq!(initialize["result"]["protocolVersion"], json!("2025-06-18"));
    assert_eq!(initialize["result"]["serverInfo"]["name"], json!("tangent-connector"));

    // A future revision is counteroffered with the latest supported one.
    peer.send(&json!({
        "jsonrpc": "2.0", "id": 2, "method": "initialize",
        "params": { "protocolVersion": "2030-01-01", "capabilities": {}, "clientInfo": { "name": "journey-test", "version": "0" } }
    }));
    let counteroffer = peer.receive();
    assert_eq!(counteroffer["result"]["protocolVersion"], json!("2025-11-25"));

    peer.send(&json!({ "jsonrpc": "2.0", "method": "notifications/initialized" }));
    peer.send(&json!({ "jsonrpc": "2.0", "id": 3, "method": "tools/list" }));
    let tools = peer.receive();
    let names: Vec<&str> = tools["result"]["tools"]
        .as_array()
        .expect("tools array")
        .iter()
        .filter_map(|tool| tool["name"].as_str())
        .collect();
    assert_eq!(names.len(), 14);
    assert!(names.contains(&"SelectCompanion"));
    assert!(names.contains(&"GetOperation"));
    assert!(names.contains(&"OpenRegistration"));
    assert!(names.contains(&"Connect"));

    peer.send(&json!({ "jsonrpc": "2.0", "id": 4, "method": "ping" }));
    let ping = peer.receive();
    assert_eq!(ping["result"], json!({}));

    // A small participation flow through tools/call.
    peer.send(&json!({
        "jsonrpc": "2.0", "id": 5, "method": "tools/call",
        "params": { "name": "SelectCompanion", "arguments": { "moniker": "lumen" } }
    }));
    let selected = peer.receive();
    assert_eq!(selected["result"]["isError"], json!(false));
    let companion = selected["result"]["structuredContent"]["connector"]["companionId"].as_str().expect("companion id").to_string();
    let server_url = selected["result"]["structuredContent"]["connector"]["serverUrl"].as_str().expect("server url").to_string();

    peer.send(&json!({
        "jsonrpc": "2.0", "id": 6, "method": "tools/call",
        "params": { "name": "Arrive", "arguments": { "companionId": companion, "serverUrl": server_url } }
    }));
    let arrival = peer.receive();
    assert_eq!(arrival["result"]["isError"], json!(false));
    let text = arrival["result"]["content"][0]["text"].as_str().expect("text");
    assert!(text.contains("you are participating as Lumen"), "text was: {text}");
    let context = arrival["result"]["structuredContent"]["connector"]["contextId"].as_str().expect("context id").to_string();

    peer.send(&json!({
        "jsonrpc": "2.0", "id": 7, "method": "tools/call",
        "params": { "name": "GetUpdates", "arguments": { "contextId": context, "view": "expanded" } }
    }));
    let updates = peer.receive();
    let text = updates["result"]["content"][0]["text"].as_str().expect("text");
    assert!(text.contains("Leo asked you"), "text was: {text}");

    // Statelessly discoverable hosts receive the supported-version set.
    peer.send(&json!({
        "jsonrpc": "2.0", "id": 8, "method": "server/discover",
        "params": { "_meta": { "io.modelcontextprotocol~1protocolVersion": "2026-07-28" } }
    }));
    let discover = peer.receive();
    assert!(discover["result"]["supportedVersions"].as_array().expect("versions").len() >= 4);

    // Every model-facing request carried the bearer session, and no token leaked into
    // any response text.
    let requests = server.requests();
    assert!(
        requests
            .iter()
            .all(|request| request.path == "/api/server" || request.bearer.contains("Bearer")),
        "missing bearer outside the public server profile on {:?}",
        requests.iter().map(|request| request.path.clone()).collect::<Vec<_>>()
    );
    let _ = context;
}

#[test]
fn authorized_scope_emits_tool_list_changed_after_the_result() {
    let server = FakeServer::start();
    let unique = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos();
    let home = std::env::temp_dir().join(format!("tangent-connector-steward-{}-{unique}", std::process::id()));
    std::fs::create_dir_all(&home).unwrap();
    let token_file = home.join("session-token.txt");
    std::fs::write(&token_file, STEWARD_CREDENTIAL).unwrap();
    let enroll = Command::new(env!("CARGO_BIN_EXE_tangent-connector"))
        .args(["enroll", "--name", "steward", "--server", server.origin(), "--token-file"])
        .arg(&token_file).env("TANGENT_CONNECTOR_HOME", &home).output().unwrap();
    assert!(enroll.status.success(), "{}", String::from_utf8_lossy(&enroll.stderr));
    let mut peer = Peer::spawn(&["serve"], &home);
    peer.send(&json!({ "jsonrpc": "2.0", "id": 1, "method": "initialize",
        "params": { "protocolVersion": "2025-11-25", "capabilities": {}, "clientInfo": { "name": "steward-test" } } }));
    assert_eq!(peer.receive()["result"]["capabilities"]["tools"]["listChanged"], true);
    peer.send(&json!({ "jsonrpc": "2.0", "method": "notifications/initialized" }));
    peer.send(&json!({ "jsonrpc": "2.0", "id": 2, "method": "tools/list" }));
    assert_eq!(peer.receive()["result"]["tools"].as_array().unwrap().len(), 14);
    peer.send(&json!({ "jsonrpc": "2.0", "id": 3, "method": "tools/call",
        "params": { "name": "SelectCompanion", "arguments": { "moniker": "steward" } } }));
    let selected = peer.receive();
    let companion = selected["result"]["structuredContent"]["connector"]["companionId"].as_str().unwrap().to_string();
    peer.send(&json!({ "jsonrpc": "2.0", "id": 4, "method": "tools/call",
        "params": { "name": "Arrive", "arguments": { "companionId": companion, "serverUrl": server.origin() } } }));
    let arrival = peer.receive();
    let context = arrival["result"]["structuredContent"]["connector"]["contextId"].as_str().unwrap().to_string();
    peer.send(&json!({ "jsonrpc": "2.0", "id": 5, "method": "tools/call",
        "params": { "name": "ReadTopic", "arguments": { "contextId": context,
            "topicRef": format!("{}::home::lounge", server.origin()) } } }));
    assert_eq!(peer.receive()["id"], 5);
    let notification = peer.receive();
    assert_eq!(notification["method"], "notifications/tools/list_changed");
    assert!(notification.get("id").is_none());
    peer.send(&json!({ "jsonrpc": "2.0", "id": 6, "method": "tools/list" }));
    let tools = peer.receive();
    let names: Vec<_> = tools["result"]["tools"].as_array().unwrap().iter()
        .filter_map(|entry| entry["name"].as_str()).collect();
    assert_eq!(names.len(), 15);
    assert!(names.contains(&"ListModerationCases"));
    assert!(!names.contains(&"ReadModerationCase"));
    peer.send(&json!({ "jsonrpc": "2.0", "id": 7, "method": "tools/call",
        "params": { "name": "GetUpdates", "arguments": { "contextId": context } } }));
    assert_eq!(peer.receive()["id"], 7);
    assert_eq!(peer.receive()["method"], "notifications/tools/list_changed");
    peer.send(&json!({ "jsonrpc": "2.0", "id": 8, "method": "tools/list" }));
    assert_eq!(peer.receive()["result"]["tools"].as_array().unwrap().len(), 14);
}

#[test]
fn serve_mode_hosts_the_operator_page_with_a_clean_url_and_pure_stdout() {
    let home = std::env::temp_dir().join(format!("tangent-connector-serve-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&home);
    std::fs::create_dir_all(&home).expect("temp dir");

    let mut peer = Peer::spawn(&["serve"], &home);

    // The plain loopback URL goes to stderr — never stdout, and never a token (the
    // owner correction removed it; the URL is a clean process-lifetime address).
    let mut operator_url = None;
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
    while std::time::Instant::now() < deadline {
        let line = peer.stderr.recv_timeout(std::time::Duration::from_secs(10)).expect("stderr startup line");
        if let Some(url) = line.split("operator page: ").nth(1) {
            operator_url = Some(url.trim().to_string());
            break;
        }
    }
    let url = operator_url.expect("the operator page URL is on stderr");
    assert!(url.starts_with("http://127.0.0.1:") && url.ends_with('/'), "clean loopback URL, no query: {url}");
    assert!(!url.contains("token"), "the page carries no token: {url}");
    // The startup line says the connector records the address so any Connect can pop it.
    let follow_up = peer.stderr.recv_timeout(std::time::Duration::from_secs(10)).expect("second stderr line");
    assert!(follow_up.contains("records it in its state"), "line was: {follow_up}");
    let port: u16 = url.trim_start_matches("http://127.0.0.1:").split(['/', '?']).next().unwrap_or_default().parse().expect("port");

    // stdout stays empty before any JSON-RPC traffic: it is protocol-owned.
    assert!(peer.lines.try_recv().is_err(), "nothing but JSON-RPC ever appears on stdout");

    let mut exchanges: Vec<Value> = Vec::new();
    peer.send(&json!({
        "jsonrpc": "2.0", "id": 1, "method": "initialize",
        "params": { "protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": { "name": "serve-journey", "version": "0" } }
    }));
    exchanges.push(peer.receive());
    peer.send(&json!({ "jsonrpc": "2.0", "method": "notifications/initialized" }));
    peer.send(&json!({ "jsonrpc": "2.0", "id": 2, "method": "tools/list" }));
    let tools = peer.receive();
    exchanges.push(tools.clone());
    assert_eq!(tools["result"]["tools"].as_array().expect("tools").len(), 14);

    // OpenRegistration under the no-browser guard: ok, and the URL never renders.
    peer.send(&json!({
        "jsonrpc": "2.0", "id": 3, "method": "tools/call",
        "params": { "name": "OpenRegistration", "arguments": {} }
    }));
    let opened = peer.receive();
    exchanges.push(opened.clone());
    assert_eq!(opened["result"]["isError"], json!(false));
    let text = opened["result"]["content"][0]["text"].as_str().expect("text");
    assert!(text.contains("Opened the local operator page"), "text was: {text}");
    for exchange in &exchanges {
        let rendered = serde_json::to_string(exchange).unwrap_or_default();
        assert!(!rendered.contains(&url), "the page URL never reaches stdout: {rendered}");
    }

    // The in-process operator server is reachable on loopback, plainly.
    let get = |request: &str| {
        let mut stream = std::net::TcpStream::connect(("127.0.0.1", port)).expect("connect");
        stream.write_all(request.as_bytes()).expect("write");
        stream.flush().expect("flush");
        let mut raw = Vec::new();
        stream.read_to_end(&mut raw).expect("read");
        String::from_utf8_lossy(&raw).to_string()
    };
    let page = get("GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
    assert!(page.starts_with("HTTP/1.1 200"), "page was: {page}");
    assert!(page.contains("Atmosphere handle"), "the Atmosphere-handle column is served");
    assert!(
        !page.contains("enroll-bound") && !page.contains("Enroll unbound") && !page.contains("Enroll with bound"),
        "no enroll buttons remain on the page"
    );
    let api = get("GET /api/identities HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
    assert!(api.starts_with("HTTP/1.1 200") && api.contains("\"status\":\"ok\""), "api was: {api}");

    // The startup URL is recoverable from the diagnostics journal, and the connector
    // recorded it in state so any process's Connect can pop this page (P4).
    let journal = std::fs::read_to_string(home.join("connector.log")).unwrap_or_default();
    assert!(journal.contains("operator_page_ready") && journal.contains(&url), "journal was: {journal}");
    let state = std::fs::read_to_string(home.join("state.json")).unwrap_or_default();
    assert!(state.contains(&format!("\"operator_page_url\": \"{url}\"")), "state was: {state}");
}

/// The live-deadlock journey against the real binary (serve mode hosts the operator
/// page in-process, exactly like the live run): an MCP client Connects with the one
/// local identity (auto-resolution — the allowlist is gone), the handshake waits for
/// the operator and pops the page, the popped tab refreshes its identity list, a
/// polling client Connects again, and the operator mutates identities — every step must
/// answer promptly. Before the re-entrant-lock fix, the page's identity fetch froze the
/// whole hub: the second Connect and every operator mutation hung.
#[test]
fn serve_mode_survives_a_looping_connect_and_operator_mutations_together() {
    let server = FakeServer::start();
    let home = std::env::temp_dir().join(format!("tangent-connector-looping-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&home);
    std::fs::create_dir_all(&home).expect("temp dir");

    let mut peer = Peer::spawn(&["serve"], &home);
    // The startup URL and its note are the only stderr lines.
    let mut operator_url = None;
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
    while std::time::Instant::now() < deadline {
        let line = peer.stderr.recv_timeout(std::time::Duration::from_secs(10)).expect("stderr startup line");
        if let Some(url) = line.split("operator page: ").nth(1) {
            operator_url = Some(url.trim().to_string());
            break;
        }
    }
    let url = operator_url.expect("the operator page URL is on stderr");
    let port: u16 = url.trim_start_matches("http://127.0.0.1:").split(['/', '?']).next().unwrap_or_default().parse().expect("port");

    peer.send(&json!({
        "jsonrpc": "2.0", "id": 1, "method": "initialize",
        "params": { "protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": { "name": "zcode", "version": "0" } }
    }));
    let initialized = peer.receive();
    assert_eq!(initialized["result"]["serverInfo"]["name"], json!("tangent-connector"));
    peer.send(&json!({ "jsonrpc": "2.0", "method": "notifications/initialized" }));

    // Operator setup through the page API this same process hosts: one identity — the
    // single identity every connect then acts as automatically.
    let http = |request: &str| {
        let mut stream = std::net::TcpStream::connect(("127.0.0.1", port)).expect("connect");
        stream.write_all(request.as_bytes()).expect("write");
        stream.flush().expect("flush");
        stream.set_read_timeout(Some(std::time::Duration::from_secs(5))).expect("deadline");
        let mut raw = Vec::new();
        stream
            .read_to_end(&mut raw)
            .map(|_| String::from_utf8_lossy(&raw).to_string())
            .map_err(|_| "no answer within 5s — the operator API is frozen".to_string())
    };
    let create_body = json!({ "handle": "ox_omega", "displayName": null }).to_string();
    let created = http(&format!(
        "POST /api/identities HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{create_body}",
        create_body.len()
    ))
    .expect("identity creation answered");
    assert!(created.contains("\"status\":\"ok\""), "create was: {created}");
    assert!(created.contains("ox_omega"), "create was: {created}");

    // Connect #1: waiting for the operator — the honest blocked return, page popped.
    peer.send(&json!({
        "jsonrpc": "2.0", "id": 2, "method": "tools/call",
        "params": { "name": "Connect", "arguments": { "serverUrl": server.origin() } }
    }));
    let waiting = peer.receive();
    assert_eq!(waiting["result"]["isError"], json!(true));
    let text = waiting["result"]["content"][0]["text"].as_str().expect("text");
    assert!(text.contains("operator action needed") && text.contains("sign in identity 'ox_omega'"), "text was: {text}");
    assert_eq!(waiting["result"]["structuredContent"]["problem"]["code"], json!("operator_action_needed"));

    // The popped tab boots and fetches its identity list — the exact freeze point of
    // the live deadlock. It must answer, with the identity and its binding status.
    let listed = http("GET /api/identities HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n")
        .expect("the popped page's identity fetch answered");
    assert!(listed.starts_with("HTTP/1.1 200"), "identity list was: {listed}");
    assert!(listed.contains("ox_omega") && listed.contains("\"atproto\":null"), "list was: {listed}");

    // The polling client Connects again (and once more): prompt, honest, no new page.
    for id in 3..=4 {
        peer.send(&json!({
            "jsonrpc": "2.0", "id": id, "method": "tools/call",
            "params": { "name": "Connect", "arguments": { "serverUrl": server.origin() } }
        }));
        let again = peer.receive();
        assert_eq!(again["id"], json!(id));
        assert_eq!(again["result"]["structuredContent"]["problem"]["code"], json!("operator_action_needed"));
        let text = again["result"]["content"][0]["text"].as_str().expect("text");
        assert!(text.contains("already opened"), "repeat connect {id} was honest: {text}");
    }

    // The operator's mutation during the pending connect still answers.
    let mutate_body = json!({ "handle": "ox_second", "displayName": null }).to_string();
    let mutated = http(&format!(
        "POST /api/identities HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{mutate_body}",
        mutate_body.len()
    ))
    .expect("operator.create_identity answered during the pending connect");
    assert!(mutated.contains("\"status\":\"ok\""), "mutation was: {mutated}");
}
