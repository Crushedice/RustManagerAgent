---
title: Rust server operations
category: rust-servers
tags: [rust, servers, operations]
confidence: 0.9
importance: 0.85
sourceType: ManualImport
approval: Active
lastVerifiedUtc: 2026-04-29T00:00:00Z
---

# Rust Servers

Use the RustOps agent and rust manager API for bounded server lifecycle, health, log, plugin, and RCON tasks.

## Lifecycle

Prefer graceful Rust restart commands when RCON is available before using an immediate process restart.

