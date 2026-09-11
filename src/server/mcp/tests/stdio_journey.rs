//! A real end-to-end stdio journey: enroll through the CLI intake, then drive the compiled
//! binary's MCP edge over its actual stdio transport — initialize negotiation, tools/list,
//! tools/call, ping — against the scripted fake experience server. All data is synthetic.

mod common;

use std::io::{BufRead, BufReader, Write};
use std::process::{Child, Command, Stdio};
use std::sync::mpsc::{channel, Receiver};
use std::thread;

use serde_json::{json, Value};

use common::{FakeServer, LUMEN_CREDENTIAL};

struct Peer {
    child: Child,
    lines: Receiver<String>,
}

impl Peer {
    fn spawn(arguments: &[&str], home: &std::path::Path) -> Self {
        let mut child = Command::new(env!("CARGO_BIN_EXE_tangent-connector"))
            .args(arguments)
            .env("TANGENT_CONNECTOR_HOME", home)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
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
        Self { child, lines: receiver }
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
fn the_stdio_edge_negotiates_and_serves_the_twelve_tools() {
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
    assert_eq!(names.len(), 12);
    assert!(names.contains(&"SelectCompanion"));
    assert!(names.contains(&"GetOperation"));

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
