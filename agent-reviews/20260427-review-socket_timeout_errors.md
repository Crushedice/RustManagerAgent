# Incident Trend Review — 2026-04-27

## Summary
The agent repeatedly reports socket timeouts and network timeouts across RCON, WebSocket, and analytics uploads, indicating unstable connectivity.

## Top Pattern
socket_timeout_errors

## Proposed Mitigation
Add exponential backoff retries for all socket operations and log detailed diagnostics when a timeout occurs.

## Config Suggestion
Set socket_timeout to 30s and enable retry_on_timeout for RCON/WebSocket in the server-agent.conf.