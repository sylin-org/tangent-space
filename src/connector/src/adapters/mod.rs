//! The adapters (spokes): stdio MCP edge, command-line edge, operator web page, tray,
//! browser opener, HTTP experience client, atproto OAuth client, durable store (with
//! per-enrollment sessions), the data-directory lock, background checker, diagnostics
//! journal, and host delivery.

pub mod atproto_oauth;
pub mod browser;
pub mod diagnostics;
pub mod experience;
pub mod lockfile;
pub mod mcp;
pub mod operator;
pub mod poller;
pub mod store;
pub mod tray;
