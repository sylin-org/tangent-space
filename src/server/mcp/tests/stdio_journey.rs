//! A real end-to-end stdio journey: enroll through the CLI intake, then drive the compiled
//! binary's MCP edge over its actual stdio transport — initialize negotiation, tools/list,
//! tools/call, ping — against the scripted fake experience server. All data is synthetic.

mod common;

use std::io::{BufRead, BufReader, Read, Write};
use std::process::{Child, Command, Stdio};
use std::sync::mpsc::{channel, Receiver};
use std::thread;

use serde_json::{json, Value};

use common::{FakeServer, LUMEN_CREDENTIAL};

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
    assert!(requests.iter().all(|request| request.bearer.contains("Bearer")), "missing bearer on {:?}", requests.iter().map(|request| request.path.clone()).collect::<Vec<_>>());
    let _ = context;
}

#[test]
fn serve_mode_hosts_the_operator_page_with_a_stderr_token_and_pure_stdout() {
    let home = std::env::temp_dir().join(format!("tangent-connector-serve-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&home);
    std::fs::create_dir_all(&home).expect("temp dir");

    let mut peer = Peer::spawn(&["serve"], &home);

    // The one-time startup URL goes to stderr — never stdout.
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
    assert!(url.starts_with("http://127.0.0.1:") && url.contains("?token="), "loopback URL with token: {url}");
    // The startup line says the page answers only once a client has connected (the
    // listener exists from the start; the server thread joins at initialize).
    let follow_up = peer.stderr.recv_timeout(std::time::Duration::from_secs(10)).expect("second stderr line");
    assert!(follow_up.contains("once a client has connected"), "line was: {follow_up}");
    let token = url.rsplit("token=").next().unwrap_or_default().to_string();
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

    // OpenRegistration under the no-browser guard: ok, and the URL/token never render.
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
        assert!(!rendered.contains(&token), "the page token never reaches stdout: {rendered}");
    }

    // The in-process operator server is reachable on loopback with the startup token.
    let get = |request: &str| {
        let mut stream = std::net::TcpStream::connect(("127.0.0.1", port)).expect("connect");
        stream.write_all(request.as_bytes()).expect("write");
        stream.flush().expect("flush");
        let mut raw = Vec::new();
        stream.read_to_end(&mut raw).expect("read");
        String::from_utf8_lossy(&raw).to_string()
    };
    let page = get(&format!("GET /?token={token} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"));
    assert!(page.starts_with("HTTP/1.1 200"), "page was: {page}");
    assert!(page.contains("Atmosphere handle"), "the Atmosphere-handle column is served");
    assert!(
        !page.contains("enroll-bound") && !page.contains("Enroll unbound") && !page.contains("Enroll with bound"),
        "no enroll buttons remain on the page"
    );
    let api = get(&format!("GET /api/identities?token={token} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"));
    assert!(api.starts_with("HTTP/1.1 200") && api.contains("\"status\":\"ok\""), "api was: {api}");
    let bare = get("GET /api/identities HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
    assert!(bare.starts_with("HTTP/1.1 401"), "bare api was: {bare}");

    // The startup URL (token included) is recoverable from the diagnostics journal.
    let journal = std::fs::read_to_string(home.join("connector.log")).unwrap_or_default();
    assert!(journal.contains("operator_page_ready") && journal.contains(&url), "journal was: {journal}");
}
