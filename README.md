# AutoTask/DattoRMM/IT Glue Agent — Implementation Plan

## Context

The user is transforming the existing **RustOpsAgent** (currently focused on Rust game server management) into a **Kaseya-integrated IT operations agent** handling three core functions in a German-language IT hosting/security/maintenance environment:

1. **Severity Classification**: Rate incoming DattoRMM warnings (CPU, disk, patch alerts) by severity, trigger AutoTask tickets, learn from admin feedback and self-research
2. **Maintenance Completion**: Track German "Wartungsarbeiten" (maintenance work) tickets in AutoTask tied to organizations, monitor DattoRMM for OS patch completion on all org devices, auto-complete tickets + optionally push to IT Glue
3. **Query Answering**: LLM-based responses to admin questions (placeholder for now)

**Target Stack**: Cross-platform (Windows Service + Debian VM), mixed C#/.NET (agent service + API) + Python (self-research module), REST API + small web UI, hybrid polling (periodic fetch + webhook-ready), API-key auth, dry-run support.

---

## Existing Foundation

The codebase already provides:
- ✅ **C# Agent Service** with intent classification, tool routing, inbox/outbox messaging, NeoCortex memory
- ✅ **AutoTask & DattoRMM Connectors** (pattern: `ApiConnectorBase` + `IConnectorLogSource`)
- ✅ **ASP.NET Core API + Dashboard**
- ✅ **Feedback-driven learning** (admin approval/rejection via decision-inbox)
- ✅ **Systemd deployment** (Linux ready)
- ❌ **IT Glue Connector** (needs implementation)
- ❌ **Python research module** (self-learning for standard IT knowledge)
- ❌ **Severity rule engine** (classification + learning)
- ❌ **Maintenance ticket state machine** (Wartungsarbeiten tracking)
- ❌ **Dry-run mode** (for testing completion without side effects)
- ⚠️ **Web UI enhancements** (feedback backlog, severity rules, maintenance dashboard)

---

## Implementation Plan

### Phase 1: IT Glue Connector (C#)

**Goal**: Enable agent to query/ingest IT Glue assets and link them to AutoTask/DattoRMM.

**Files to Create/Modify**:
- **New**: `agent/RustOpsAgent/Infrastructure/Connectors/ITGlueConnector.cs`
  - Implement `ApiConnectorBase` + `IConnectorLogSource`
  - Auth: x-api-key header
  - Endpoints: `/api/v2/organizations`, `/api/v2/configurations` (devices), `/api/v2/logs`
  - Fetch org → device → patch status mappings
  
- **Modify**: `Core/Contracts/ConfigContracts.cs`
  - Add `ITGlueSettings` (enabled, baseUrl, apiKey)
  
- **Modify**: `agentsettings.example.json`
  - Add itGlue section with env var placeholders

- **Modify**: `Infrastructure/ConfigLoader.cs` (if needed)
  - Register ITGlueConnector at startup (if not already generic)

**Acceptance**: Connector can query org/device list, fetch patch status, appear in health check.

---

### Phase 2: Memory Models for Maintenance + Severity (C#)

**Goal**: Define state contracts for maintenance tracking and severity rules.

**Files to Create/Modify**:
- **Modify**: `Core/Contracts/MemoryContracts.cs`
  - Add `MaintenanceTrackingState` (maintenance/tracking-state.json)
    - Dictionary\<string orgId, OrgMaintenanceState\>
    - OrgMaintenanceState: ticketId, ticketName, organizationId, affectedDeviceIds, targetPatchVersion, lastPolledAt, completionStatus, dryRunMode
    - Device patches: deviceId → patchStatus (Pending, InProgress, Completed, Failed)
    - Last updated timestamp per org
  
  - Add `SeverityRuleState` (policy/severity-rules.json)
    - Rules: pattern (CPU > 85% for 30min) → severity (Critical/High/Medium/Low) → action (CreateTicket, AlertAdmin, AutoResolve)
    - Admin-learned rules vs. hardcoded defaults
    - Per-rule enablement, last-learned-from, confidence
  
  - Add `WarningsKnowledgeState` (logs/warnings-knowledge.json)
    - Standard IT warnings patterns (CPU, disk, memory, patch, security)
    - Self-learned research: what each warning means, typical remediation, urgency
    - Linked to Python research module results

- **Modify**: `AgentRuntime.cs`
  - Add periodic persistence of maintenance state (every poll cycle)
  - Hook to update device patch status from DattoRMM logs

---

### Phase 3: Severity Classifier Tool (C#)

**Goal**: Rate RMM warnings by severity, create/update AutoTask tickets, learn from feedback.

**Files to Create/Modify**:
- **New**: `Domains/Integrations/SeverityClassifierToolHandler.cs`
  - Eligible for: StatusCheck, Troubleshooting (flagged incoming warnings)
  - Inputs: warning type (CPU, disk, patch), device, value, threshold, org
  - Logic:
    1. Load SeverityRuleState
    2. Match warning against rules (pattern matching or LLM-assisted if enabled)
    3. Assign severity + recommended action
    4. If action = CreateTicket: call AutotaskConnector to create/update ticket
    5. Record decision in memory for feedback loop
  - Output: ToolExecutionResult with assigned severity, action taken, ticket ID (if created)

- **New**: `Domains/Integrations/WarningIngestorToolHandler.cs`
  - Parses DattoRMM logs for "warning" keywords (CPU %, disk %, etc.)
  - Feeds to SeverityClassifierToolHandler
  - Stores raw warning in memory for later analysis

- **Modify**: `Core/Interaction/ToolRegistry.cs`
  - Register SeverityClassifierToolHandler, WarningIngestorToolHandler

**Acceptance**: Agent classifies incoming DattoRMM alerts, creates/updates AutoTask tickets with severity labels.

---

### Phase 4: Maintenance Ticket Tracker (C#)

**Goal**: Monitor Wartungsarbeiten tickets in AutoTask, track device patches in DattoRMM, auto-complete when all devices done.

**Files to Create/Modify**:
- **New**: `Domains/Integrations/MaintenanceTrackerToolHandler.cs`
  - Eligible for: StatusCheck, Troubleshooting (manual refresh), auto-invoked on each poll cycle
  - Inputs: optional org filter, dry-run flag
  - Logic:
    1. Query AutoTask for tickets with description="Wartungsarbeiten" (via AutotaskConnector)
    2. Extract organization from ticket
    3. Load MaintenanceTrackingState
    4. For each org:
       - Query ITGlueConnector for devices in org
       - Query DattoRmmConnector for "OS Patch" messages for those devices
       - Determine patch completion status per device
       - Compute org-wide patch status (all done? partial? pending?)
       - If all done:
         - In dry-run: report what *would* be completed, don't modify
         - In live: call AutotaskConnector to mark ticket complete, post to IT Glue (optional)
    5. Update MaintenanceTrackingState with progress
  - Output: ToolExecutionResult with org → device → patch status table, completion candidates, dry-run notice

- **Modify**: `AgentRuntime.cs`
  - Auto-invoke MaintenanceTrackerToolHandler every N polls (configurable, e.g., every 5 polls = ~2 min if poll = 20s)
  - Or trigger on decision-inbox approval

- **Modify**: `agentsettings.example.json`
  - Add maintenance section: pollIntervalMultiplier, dryRunDefault, autoCompleteThreshold

**Acceptance**: Agent identifies Wartungsarbeiten tickets, tracks per-org device patch progress, reports & auto-completes when ready, respects dry-run.

---

### Phase 5: Python Research Module (Python + C#)

**Goal**: Autonomous research of standard IT warnings (CPU thresholds, disk space best practices, patch urgency, etc.). Populate WarningsKnowledgeState.

**Files to Create**:
- **New**: `research/research_agent.py`
  - Async loop (every hour or on demand)
  - Known warning types (CPU, disk, memory, patch, security, network, service-down)
  - Per warning: query LLM or knowledge base for:
    - What does this mean?
    - Normal thresholds?
    - Typical remediation?
    - Urgency level?
    - Related ticket type in AutoTask?
  - Write results to `WarningsKnowledgeState` JSON file (shared with C# agent)
  - Include sources (LLM, vendor docs, internal KB)

- **New**: `research/requirements.txt`
  - Dependencies: httpx, aiofiles, anthropic (for Claude API)

- **New**: `deploy/systemd/research_agent.service` (optional)
  - Or run as scheduled job/cron (Python approach favors cron)

- **Modify**: `C# Agent` to read `WarningsKnowledgeState` on startup
  - Pass researched thresholds to SeverityClassifierToolHandler
  - Allow Python module to overwrite defaults

**C# Integration Point**:
- Add config: `research.enabled`, `research.stateFile`, `research.pollInterval`
- AgentRuntime checks if file updated; reloads WarningsKnowledgeState
- Optionally trigger Python module from C# via subprocess

**Acceptance**: Agent applies learned thresholds to severity classification; Python module autonomously maintains knowledge.

---

### Phase 6: Web UI Enhancements (C# / HTML+JS)

**Goal**: Admin feedback forms, severity rule configuration, maintenance tracking dashboard, dry-run toggle.

**Files to Modify**:
- **Modify**: `api/Program.cs` → `BuildDashboardHtml()`
  - Add sections:
    1. **Severity Rules Dashboard**
       - Table: pattern → severity → action → enabled → last-learned
       - Buttons: Edit, Disable, Delete, Learn from This Pattern
       - Form to create new rule (pattern input, severity dropdown, action dropdown)
    
    2. **Maintenance Tracking Dashboard**
       - Per-org table: org name → ticket id → affected devices → patch completion % → status (Pending/In-Progress/Complete/DryRun)
       - Expandable row: device → patch status → last-checked
       - Dry-run toggle (affects all future updates until toggled off)
       - Manual refresh button
    
    3. **Feedback Backlog**
       - Table: warning type → severity → action taken → feedback (pending/good/bad) → date
       - Buttons: Mark Good, Mark Bad, Ignore Pattern
       - Admin notes field (free text feedback)
    
    4. **Warnings Knowledge Viewer**
       - Table: warning type → meaning → thresholds (from Python research) → last-updated
       - Refresh button (trigger Python research run)

- **Modify**: `api/Program.cs` → Add REST endpoints
  - `POST /api/severity-rules/{id}/feedback` — record admin feedback
  - `POST /api/severity-rules` — create new rule
  - `PUT /api/maintenance/{orgId}/dry-run?enabled=true|false` — toggle dry-run
  - `GET /api/maintenance/preview` — preview what would complete
  - `GET /api/warnings-knowledge` — list researched warnings

**UI Language**: German strings for all labels, button text, messages.

**Acceptance**: Admin can view, learn rules, see maintenance progress, toggle dry-run, provide feedback all from web.

---

### Phase 7: German Language Support (Config + UI)

**Goal**: System operates in German (config keys, UI labels, messages).

**Files to Modify**:
- **New**: `i18n/de.json` (or inline strings in appsettings)
  - Severity levels: Kritisch, Hoch, Mittel, Niedrig
  - Status labels: Ausstehend, In Arbeit, Abgeschlossen, Fehler
  - Action types: TicketErstellen, AdminAlerting, AutoAbschließen
  - Warning types: CPU-Auslastung, Speicherplatz, Speicher, Patch, Sicherheit, Netzwerk, Service-Ausfall
  - Messages: "Wartungsarbeiten für %org% abgeschlossen", "Trockentest-Modus aktiv", etc.

- **Modify**: `api/Program.cs`
  - Load German strings on startup
  - Pass to HTML template renderer

- **Modify**: `Core/Contracts/ConfigContracts.cs`
  - Add optional `language` field (default "de" for German)

**Acceptance**: Dashboard, logs, messages appear in German.

---

### Phase 8: Dry-Run Mode

**Goal**: Test maintenance ticket completion without actually completing tickets.

**Files to Modify**:
- **Modify**: `Core/Contracts/MemoryContracts.cs`
  - Add `dryRunMode: bool` to MaintenanceTrackingState
  - Per-ticket override (optional)

- **Modify**: `MaintenanceTrackerToolHandler.cs`
  - Check dryRunMode before calling AutotaskConnector.CompleteTicket
  - Log what *would* happen
  - Mark in state as "DryRun-Completed" instead of "Completed"

- **Modify**: `agentsettings.example.json`
  - Add `maintenance.dryRunDefault: true` (safe default)

- **Modify**: `api/Program.cs`
  - Expose dry-run toggle in UI + REST endpoint
  - Display "DRY-RUN MODE ACTIVE" banner when enabled

**Acceptance**: Toggle dry-run on/off; when enabled, no tickets are modified, but UI shows what would happen.

---

## Architecture Decisions

### Mixed C# + Python
- **C# .NET 8.0**: Agent orchestration, connectors, ticket handling (performance, Azure-ready, existing codebase)
- **Python**: Autonomous research module (lighter, easier to extend with new LLM integrations, can run separately)
- **Integration**: Shared JSON state files (WarningsKnowledgeState), subprocess invocation from C#, or scheduled cron job

### Hybrid Polling + Webhook-Ready
- **Pull**: C# agent polls AutoTask/DattoRMM/IT Glue on schedule (20-120s intervals)
- **Push**: API has structure for webhook receivers (future enhancement)
- **Rationale**: Pull is reliable; webhooks can add later without refactoring

### Inbox/Outbox for Admin Feedback
- **Existing Pattern**: feedback-inbox, decision-inbox, chat-inbox
- **Extend**: feedback-inbox carries "learned rules" after admin approval
- **Store**: Rules persist in SeverityRuleState (NeoCortex)

### Organizational Scoping
- **Key**: org ID from AutoTask Wartungsarbeiten ticket
- **State**: MaintenanceTrackingState\[orgId\] tracks all devices in that org + patch progress
- **Dynamic**: Admin can see per-org listing in UI, adjust thresholds, exclude orgs/devices

---

## Critical Files to Create/Modify

### C# (Agent Service)
1. `Infrastructure/Connectors/ITGlueConnector.cs` — NEW
2. `Domains/Integrations/SeverityClassifierToolHandler.cs` — NEW
3. `Domains/Integrations/WarningIngestorToolHandler.cs` — NEW
4. `Domains/Integrations/MaintenanceTrackerToolHandler.cs` — NEW
5. `Core/Contracts/MemoryContracts.cs` — MODIFY (add MaintenanceTrackingState, SeverityRuleState, WarningsKnowledgeState)
6. `Core/Contracts/ConfigContracts.cs` — MODIFY (add ITGlueSettings, maintenance config)
7. `Core/Interaction/ToolRegistry.cs` — MODIFY (register new handlers)
8. `AgentRuntime.cs` — MODIFY (auto-invoke maintenance tracker, load warnings knowledge)
9. `agentsettings.example.json` — MODIFY (add sections for IT Glue, maintenance, research)

### C# (API)
1. `api/Program.cs` — MODIFY (web UI sections, REST endpoints for rules/feedback/maintenance, German strings)

### Python
1. `research/research_agent.py` — NEW (autonomous research loop)
2. `research/requirements.txt` — NEW
3. `deploy/systemd/research_agent.service` — NEW (optional, or use cron)

### Deployment
1. `deploy/systemd/rustopsagent.service` — MODIFY (ensure research module startup if systemd service)
2. `rustops.env` — Example additions for research module config

---

## Implementation Order

1. **Phase 1** (IT Glue Connector) — Foundation for asset queries
2. **Phase 2** (Memory Models) — State contracts to support all downstream logic
3. **Phase 4** (Maintenance Tracker) — Core business logic for user requirement #2
4. **Phase 3** (Severity Classifier) — Core logic for user requirement #1
5. **Phase 5** (Python Research) — Autonomous learning for user requirement #1
6. **Phase 6** (Web UI) — Admin control plane
7. **Phase 7** (German) — Localization (can be done incrementally)
8. **Phase 8** (Dry-Run) — Safety feature for maintenance completion

---

## Verification & Testing

### Phase 1 (IT Glue)
- [ ] IT Glue connector appears in `/api/dashboard/summary` health check
- [ ] Can query org list and device list via connector
- [ ] Config loads correctly with env vars

### Phase 2 (Memory)
- [ ] NeoCortex creates JSON files on startup
- [ ] AgentRuntime reads/writes MaintenanceTrackingState, SeverityRuleState
- [ ] Files persist across restart

### Phase 4 (Maintenance)
- [ ] Query AutoTask for Wartungsarbeiten tickets
- [ ] Dry-run mode: show what would complete, don't modify
- [ ] Live mode: actually complete tickets
- [ ] Track per-org, per-device patch status
- [ ] Web UI displays org → device → patch % table

### Phase 3 (Severity)
- [ ] DattoRMM warning logs parsed and classified
- [ ] Severity assigned per rule
- [ ] AutoTask ticket created/updated with severity label
- [ ] Admin feedback stored and applied to rules

### Phase 5 (Python Research)
- [ ] Research script runs autonomously
- [ ] Outputs WarningsKnowledgeState JSON
- [ ] C# agent reads and applies thresholds
- [ ] Updates reflected in severity classification

### Phase 6 (Web UI)
- [ ] Dashboard loads without errors
- [ ] Severity rules table displays, CRUD works
- [ ] Maintenance tracking shows orgs, devices, patch %
- [ ] Dry-run toggle prevents actual ticket updates
- [ ] Feedback buttons work, state persists

### Phase 7 (German)
- [ ] All UI labels in German
- [ ] Messages in German
- [ ] Config keys support language setting

### Phase 8 (Dry-Run)
- [ ] Toggle dry-run on/off from UI
- [ ] In dry-run: no tickets modified, UI shows "DRY-RUN"
- [ ] In live: tickets actually completed

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| IT Glue API auth differs from AutoTask/DattoRMM | Use ApiConnectorBase pattern; test auth with sandbox API first |
| Maintenance tracker marks org complete too early (not all devices patched) | Strict device-level filtering; require ALL devices done; manual dry-run review |
| Python research module outdates; thresholds become stale | Version research results with date; allow admin to manually refresh; log research sources |
| Performance: polling 3 APIs every 20-120s | Stagger polls; cache org/device lists; monitor poll duration |
| German UI strings scattered across codebase | Centralize in config file or i18n module; lint for untranslated strings |

---

## Confirmed Implementation Details

### AutoTask Maintenance Tickets
- **Queue**: "Wartung"
- **Title**: "Wartungsarbeiten"
- Query: `GET /atservicesrest/v1.0/Tickets?filter=queue eq 'Wartung' AND title eq 'Wartungsarbeiten'`

### DattoRMM Patch Detection
- **Log messages**: Look for "Patch Schedule: start" and "Patch Schedule: end" in device activity logs
- Query: Extract device ID + patch type from message
- Status logic: 
  - "Patch Schedule: start" = patch in progress
  - "Patch Schedule: end" = patch completed
  - No "start" but "end" seen = completed without tracking start
- State: Track latest timestamp per device; compare against ticket creation date

### Python Research Module Trigger
- **Startup**: Initialize WarningsKnowledgeState on agent launch
- **Cron job**: Run independently (hourly recommended) to update knowledge
- **Manual**: Admin button in web UI to refresh on-demand
- **Integration**: C# agent reads JSON file; subscribes to file-change events or checks timestamp on each poll

### Code Location
- **New branch from main**: `feature/autotask-maintenance-tracking`
- Keep Python research module in `research/` directory
- All C# code in `agent/RustOpsAgent/` following existing structure

---

## Next Steps

1. ✅ Clarify AutoTask Wartungsarbeiten identification
2. ✅ Confirm DattoRMM patch log format
3. ✅ Confirm Python research trigger approach
4. ✅ Confirm code location
5. Approval: Review plan and confirm readiness to proceed
