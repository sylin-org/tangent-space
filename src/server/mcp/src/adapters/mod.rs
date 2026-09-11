//! The adapters (spokes): stdio MCP edge, command-line edge, HTTP experience client, durable
//! store, credential custody, background checker, diagnostics journal, and host delivery.

pub mod credentials;
pub mod delivery;
pub mod diagnostics;
pub mod experience;
pub mod mcp;
pub mod poller;
pub mod store;
