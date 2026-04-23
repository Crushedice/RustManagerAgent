

---
PROJECT GOAL:
Refactor the current agent from a monolithic Rust-specific implementation into a modular agent architecture that can conceptually operate beyond Rust, while preserving and improving the Rust-specific functionality in this branch.

The desired end state is:
- a modular agent core
- a Rust-specific module layer in this branch
- a clean replacement of the weak interaction/tool-selection model
- separated memory banks
- isolated operational modules for Rust server management, RCON, logs, plugins, GitOps, and focused network inspection
- a future-safe foundation for further expansion

This full plan is intended to be completed before any unrelated feature work.

======================================================================
GLOBAL EXECUTION RULES
======================================================================

- Execute the full plan phase-by-phase from top to bottom.
- Do NOT skip ahead to later phases until the current phase is complete.
- Inside each phase, complete all listed tasks before moving on.
- You may choose the internal order of tasks within a phase only when dependencies allow it.
- Do NOT redesign the system beyond what is explicitly described here.
- Preserve existing Rust functionality unless a replacement is explicitly part of the plan.
- Prefer moving/refactoring existing code over deleting behavior without replacement.
- Keep the intended outcome close to this document.
- If something is ambiguous, infer conservatively and in line with the modular target architecture.
- Avoid broad speculative refactors outside the current phase.
- Dangerous or architecture-shaping work must follow the defined constraints exactly.
- Show concise progress after each completed phase, not after every small task.
- Continue executing the plan until all phases are completed.

======================================================================
PHASE 1 — FOUNDATION RESTRUCTURE
WHY THIS PHASE EXISTS:
The current code is packed into one file and the current interaction model is weak. Building more functionality on top of that would cause rework and unstable integration. This phase must happen first.
======================================================================

PHASE GOAL:
Create the structural and architectural foundation that all later modules will plug into.

--------------------------------------------------
WORKSTREAM 1A — CORE MODULARIZATION
--------------------------------------------------

Intent:
Take the agent apart in a way that supports modularity. The agent itself should become a larger reusable package that could conceptually run with other data, not only Rust. In this branch, Rust remains the active domain and should be represented as its own category/module.

Implementation requirements:
- The monolithic file must be broken apart.
- The base agent layer must not be tightly coupled to Rust-specific logic.
- Rust-specific concerns must be grouped under a mental and code-level category/module such as "Rust".
- The result should support future non-Rust expansion without implementing non-Rust support now.

Tasks:
1. Analyze the current single-file implementation and identify logical boundaries.
2. Define the module layout for:
   - agent core
   - Rust domain/module
   - memory/state handling
   - tool/action execution
   - interaction/routing
3. Extract the base agent logic into its own core module(s).
4. Extract Rust-specific logic into a Rust module layer.
5. Remove unnecessary cross-coupling between core and Rust-specific components.
6. Ensure the project structure reflects modular ownership clearly.

Done criteria:
- The code is no longer centered around one giant file.
- Core agent logic and Rust-specific logic are clearly separated.
- The resulting structure is viable for the later phases.

Constraints:
- Do not remove Rust behavior just to make the split cleaner.
- Do not invent unrelated abstraction layers that do not support the stated goal.

--------------------------------------------------
WORKSTREAM 1B — AGENT INTERACTION / ROUTING REMODEL
--------------------------------------------------

Intent:
Replace the weak model for tool selection, admin replies, and intent inference with a Semantic Kernel + OpenAI function calling / Structured Outputs architecture.

This is not optional. It is the new control model the rest of the agent should be built against.

Implementation requirements:
- Replace hardcoded phrase matching.
- Replace the current model where the agent iterates through tools and asks the LLM to pick one.
- Use a router + executor architecture.
- The agent should behave more like a sysadmin helper: stateful, structured, operational.
- Avoid giant regex banks and reply-phrase trees.
- Avoid exposing every tool every turn.
- Avoid letting the model guess server names from weak memory when code can track active target state.

Target architecture:
1. AdminIntentClassifier
   - Takes raw admin text plus compact context
   - Returns a structured route object

2. ToolRegistry
   - Knows available tools
   - Knows required arguments
   - Knows when a tool is eligible for an intent

3. ActionExecutor
   - Runs deterministic actions directly where possible
   - Uses function calling only where needed

4. ResponseComposer
   - Produces the final assistant reply in the intended operational style

Required first-pass classifier behavior:
- classify into a narrow intent set such as:
  - chat
  - server_control
  - player_lookup
  - rcon_command
  - file_edit
  - status_check
  - troubleshooting
  - clarification
- extract slots such as:
  - serverName
  - playerName
  - commandText
  - timeRange
  - severity

Required execution behavior:
- tool eligibility must be filtered by intent
- if an action is deterministic, skip a second model decision and execute directly
- dangerous actions should follow deterministic execution paths
- model-generated prose should mainly be used for explanation and clarification

Required state behavior:
- conversation state must support references like:
  - restart that one
  - show me the logs again
  - run it on monthly instead

Tasks:
1. Define the structured classifier schema.
2. Implement AdminIntentClassifier.
3. Implement ToolRegistry.
4. Implement ActionExecutor.
5. Implement ResponseComposer.
6. Add conversation/selection state handling.
7. Rewire the existing interaction flow to use this architecture.
8. Remove or retire the old hardcoded interaction logic.

Done criteria:
- The old tool-iteration approach is no longer the main interaction path.
- Intent extraction is structured.
- Tool access is filtered by intent.
- Response generation is separated from action routing.
- Conversation state supports follow-up operational language reliably.

Constraints:
- Do not leave the old matching logic half-active as the main fallback unless strictly needed during migration.
- Do not keep the system dependent on large hardcoded phrase lists.

======================================================================
PHASE 2 — STATE / MEMORY / INTERNAL LEARNING FOUNDATION
WHY THIS PHASE EXISTS:
Later modules need structured state, categorized memory, and a replacement for the misleading self-repair concept.
======================================================================

PHASE GOAL:
Split memory ownership and replace the current self-repair concept with a more accurate and useful evolution/incident-learning model.

--------------------------------------------------
WORKSTREAM 2A — MEMORY SYSTEM REFACTOR
--------------------------------------------------

Intent:
The current agent-state memory file should be split apart. The agent should have multiple memory files with their own fields of information for easier management and readability. As development grows, memory banks should work by category, potentially under a parent folder such as NeoCortex.

Implementation requirements:
- Replace the monolithic agent-state memory file.
- Use categorized memory banks.
- Organize under a parent folder such as NeoCortex.
- Memory categories should be readable and maintainable.

Suggested categories:
- active operational state
- selected server/player/session state
- logs/log-derived knowledge
- incidents/mistakes/evolution notes
- ignore patterns / admin feedback
- tool or domain-specific cached knowledge

Tasks:
1. Define memory bank categories.
2. Define folder/file structure under NeoCortex.
3. Split existing state/memory responsibilities.
4. Migrate existing relevant information into the categorized structure.
5. Update reads/writes across the codebase to use the new memory layout.

Done criteria:
- No single monolithic memory file remains as the primary memory store.
- Memory ownership is clearly categorized.
- Later modules can store their data in the correct bank.

--------------------------------------------------
WORKSTREAM 2B — EVOLUTION SYSTEM (REPLACES SELF-REPAIR)
--------------------------------------------------

Intent:
The current self-repair function is seemingly useless and the name is misleading. Replace it with an Evolution tool/system.

Implementation requirements:
- Do not keep the concept named self-repair.
- The replacement should log incidents, errors, and mistakes separately.
- It should classify what went wrong.
- It should store:
  - request
  - intended outcome
  - failure reason
  - what was missing
  - what would prevent recurrence
- When idle or when there is time, the agent should be able to revisit those mishaps and learn from them.
- If a mishap can be solved, the agent may apply changes to source code under:
  /opt/rust-manager/src/*
- If it applies such a fix:
  - ensure the agent branch exists if needed
  - use the agent branch
  - commit the changes
  - open a PR

Tasks:
1. Remove or retire the misleading self-repair concept.
2. Create the incident/evolution data model.
3. Implement incident recording and classification.
4. Implement a review path for revisiting stored mishaps.
5. Integrate with GitOps flow for code-change proposals.

Done criteria:
- The old self-repair concept is gone or clearly replaced.
- Incidents and mistakes are stored meaningfully.
- The system supports later learning/revisit behavior.
- Code changes resulting from this path use the GitOps workflow.

======================================================================
PHASE 3 — SAFE CODE CHANGE WORKFLOW
WHY THIS PHASE EXISTS:
The agent must not directly modify main. This needs to be in place before autonomous source changes are relied on.
======================================================================

PHASE GOAL:
Establish a safe GitHub workflow for agent-authored changes.

--------------------------------------------------
WORKSTREAM 3 — GITOPS
--------------------------------------------------

Intent:
Handle pulling/pushing with the GitHub repo safely. If the agent pushes something it wrote itself, it should always use a branch other than main, such as an agent branch.

Implementation requirements:
- Never push direct agent-authored changes to main.
- Use a separate branch strategy, e.g. agent or agent/*.
- Support:
  - ensuring agent branch exists
  - selecting the correct branch
  - committing changes
  - creating a PR

Tasks:
1. Implement branch handling for agent-authored changes.
2. Implement commit flow for agent-authored changes.
3. Implement PR creation flow.
4. Integrate GitOps into code-changing subsystems.

Done criteria:
- Agent-authored changes go through a branch + PR workflow.
- Main is not the direct write target for autonomous changes.

Constraints:
- Do not treat direct push to main as acceptable agent behavior.

======================================================================
PHASE 4 — RUST OPERATIONAL MODULES
WHY THIS PHASE EXISTS:
After the architecture and state foundations are in place, the Rust-specific capability modules can be implemented cleanly.
======================================================================

PHASE GOAL:
Implement the Rust domain modules in isolated components that plug into the new architecture.

--------------------------------------------------
WORKSTREAM 4A — RCON REIMPLEMENTATION
--------------------------------------------------

Intent:
The current RCON implementation is flawed and should be replaced with a fresh implementation, not copied from the existing one.

Implementation requirements:
- Create a custom RustRconClient over ClientWebSocket.
- Keep it isolated behind an interface such as IRconClient.
- It should include:
  - ConnectAsync(uri, password)
  - auth immediately after websocket opens
  - SendCommandAsync(string command)
  - a background receive loop
  - reply correlation by identifier
  - unsolicited messages/log lines exposed as events
  - heartbeat/reconnect handled separately
- The agent should have a small separate process/component that keeps a rolling RCON log available and monitors it.
- That rolling log should be useful later for triggers and automatic command execution.
- The agent should know that RCON credentials are in:
  /opt/rust-manager/config/{servername}.json

Tasks:
1. Define IRconClient.
2. Implement fresh RustRconClient using ClientWebSocket.
3. Implement authentication flow.
4. Implement command send/response correlation.
5. Implement receive/event pipeline.
6. Implement reconnect/heartbeat support.
7. Implement rolling RCON log monitor component.
8. Wire config-based credential lookup from server config files.

Done criteria:
- The old flawed RCON path is replaced by a new isolated implementation.
- Commands and responses are reliable.
- Rolling RCON output is available for observation and future automation.

Constraints:
- Do not copy the current flawed implementation into the new one.

--------------------------------------------------
WORKSTREAM 4B — COMMAND EXECUTION / SERVER MANAGEMENT
--------------------------------------------------

Intent:
Servers will be handled with tmux sessions. The agent should be able to read/write those sessions to execute commands and observe the console.

Important runtime caveat:
Unity bootstrapper starts first, and the long-lived Rust server is on a different process. This must be accounted for.

Implementation requirements:
- Handle tmux session-based interaction.
- Support console observation.
- Support command injection where appropriate.
- Implement:
  - start
  - stop
  - restart
  - restart countdown with minimum 3 minutes
  - kill
  - update

Tasks:
1. Implement tmux session discovery and mapping.
2. Implement read access for console/session output.
3. Implement write access for command execution.
4. Account for bootstrapper vs long-lived server process behavior.
5. Implement start/stop/restart/kill/update operations.
6. Implement restart countdown behavior with minimum 3 minutes.

Done criteria:
- Servers can be managed through tmux-backed controls.
- Console observation works.
- Restart behavior respects the countdown requirement.

--------------------------------------------------
WORKSTREAM 4C — LOG MANAGEMENT & EVALUATION
--------------------------------------------------

Intent:
The agent should understand server logs dynamically and learn which logs matter.

Implementation requirements:
- Dynamically resolve log paths such as:
  - /srv/{Servername}/Log.txt
  - /srv/{Servername}/oxide/logs/*
- Maintain dedicated memory for those logs and derived understanding.
- Assign importance to events dynamically.
- Importance can be influenced by acquired knowledge and admin feedback.
- If the admin indicates a log or partially matching string cutout should be ignored, the agent should internalize that and act accordingly.

Tasks:
1. Implement dynamic log path resolution.
2. Implement log ingestion/parsing for the relevant Rust log sources.
3. Store logs and log-derived observations in dedicated memory.
4. Implement importance scoring/classification.
5. Implement admin-provided ignore handling using partial matching where reasonable.
6. Persist the learned ignore/importance behavior.

Done criteria:
- Relevant logs are discovered dynamically.
- Logs are stored/understood in dedicated memory.
- Importance is assigned and can evolve.
- Admin feedback changes future handling.

--------------------------------------------------
WORKSTREAM 4D — UMOD MODULE
--------------------------------------------------

Intent:
Create a Umod module for plugin verification, config verification, and update management.

Implementation requirements:
- Handle plugin verification.
- Handle JSON config verification.
- Handle uMod plugin update management by checking plugin version against a uMod GET returning JSON.
- Not all plugins are from uMod, so missing results must be handled gracefully.
- This area can later support a coding assistant for auto-repair of plugin issues.

Tasks:
1. Implement plugin verification.
2. Implement JSON config verification.
3. Implement uMod version lookup/update check flow.
4. Handle plugins not found on uMod without breaking the flow.
5. Structure the module so later plugin issue repair support can be added cleanly.

Done criteria:
- Plugin verification works.
- Config verification works.
- Update checks work where applicable.
- Non-uMod plugins are treated as expected, not as hard failures.

--------------------------------------------------
WORKSTREAM 4E — NETWORK INSPECTION
--------------------------------------------------

Intent:
The current network inspection feature is weak and not useful. It should be simplified and focused.

Implementation requirements:
- This does not need to become its own separate file unless that clearly improves the result.
- Do not monitor the whole network broadly.
- Focus only on interfaces:
  - eth0
  - wt1
  - wg1
- Monitoring focus:
  - up/down throughput
  - player latency if possible

Tasks:
1. Remove or reduce broad/unnecessary network monitoring behavior.
2. Limit monitoring scope to eth0, wt1, wg1.
3. Implement throughput-focused observation.
4. Implement latency-focused observation if feasible.

Done criteria:
- Network inspection is focused and useful.
- It only covers the relevant interfaces and metrics.

======================================================================
PHASE 5 — FINAL INTEGRATION PASS
WHY THIS PHASE EXISTS:
After all modules exist, they need to be aligned with the architecture rather than merely coexisting.
======================================================================

PHASE GOAL:
Ensure the new modules are properly integrated into the modular core, router/executor flow, memory banks, and GitOps behavior.

Tasks:
1. Ensure all new modules register cleanly with the ToolRegistry / execution flow where appropriate.
2. Ensure operational state is stored in the correct memory banks.
3. Ensure code-changing paths use the GitOps workflow.
4. Ensure Rust-specific functionality remains under the Rust domain/module boundary.
5. Remove obsolete monolithic leftovers, dead interaction paths, and misleading names.
6. Verify the project structure reflects the intended modular design.

Done criteria:
- The system behaves as one coherent modular agent rather than a collection of disconnected pieces.
- The old monolithic design is no longer the active reality.
- The architecture matches the intended outcome of this plan.

======================================================================
ADDITIONAL AUTHORITY / FLEXIBILITY NOTE
======================================================================

Any further splitting of the agent can be done by the coding helper if it clearly benefits the end product, but only when it remains aligned with:
- the modular core + Rust module direction
- the router + executor interaction model
- categorized memory banks
- safe GitOps behavior
- isolated operational modules

Do not use that flexibility as permission to redesign the project away from this intended architecture.
Execute this full plan phase-by-phase from top to bottom.
Do not stop after the first task.
Complete each phase before moving to the next.
After each completed phase, report what was changed, what remains, and then continue automatically.



---
--- The Original Text ---



So, the next steps we should take is taking the agent apart in a way.
Currently, everything is packed into 1 file.
Its not very good to work with , also misses the intended final outcome of being "modular"
So, I've got a few key points to work from
i imagined we split it in a way that , the agent by itself is one big package and could run with other data, not only rust.
But in this branch currently, we keep the rust obviously.
so, we mentally make a category "Rust"
And we split from the agent:

_Umod Module_

- The plugin verification ( And later coding assistant for auto repair of plugin issues)
- Config Verification ( which is JSON ) for help with config mishaps
- The Umod Plugin Update management, where the agent can check the plugin version against a umod GET (wich results in a json) and potentially update the plugin
  ( Be aware, that NOT all plugins are from umod, so it can happen that nothing will be found)

_Command Execution / Server Management_
 - The servers will be handled with tmux session  ( be aware that unity bootstrapper will get started first and the long lived rust server is on a different process.)
   The agent should have a handle on these sessions with read/write capabilities to execute commands and observe the console.
 - The starting / stopping /  restarting ( with a restart countdown of minimum 3 minutes) / killing / updating  The servers.

_RCON_
- The current Rcon implementation is Flawed, To cleanup , and to avoid issues in the future, this should be a new implementation and not copied from the current one.
	- this new one should use a custom `RustRconClient` over `ClientWebSocket`.
		And should include:
			- `ConnectAsync(uri, password)`
			- send auth packet immediately after websocket opens
			- `SendCommandAsync(string command)`
			- background receive loop
			- correlate replies by identifier
			- expose unsolicited messages/log lines as events
			- add heartbeat/reconnect separately
			- keep this isolated behind an interface like `IRconClient`
	- The agent should then have a small separate process, that keeps a rolling Log from the Rcon available and monitor it. 
			- Can then in the future be used to setup automatic command execution with trigger and more. 
	 - The agent should have the awareness, that the credentials for the rcon are in the server configs ( /opt/rust-manager/config/{servername}.json )
	   

_Log Management & Evaluation_
- The agent should have the Path's for all the logs dynamically e.g. /srv/{Servername}/Log.txt + /srv/{Servername}/oxide/logs/* 
- A Dedicated memory for those logs, plus the understanding of it 
		Additionally to those memories, the importance of the logged event, and dynamically assigning an importance based on own acquired knowledge and/or admin feedback,
		if admin tells the agent that some log ( partially matching string cutout ) is to be ignored,  agent should internalize this and act accordingly .

_GitOps_
 - The whole process  of handling pulling and pushing with the github repo.
	 The agent , if pushing something he wrote himself, should always push on a different branch then main e.g.  its own "agent" branch. 

_Network-Inspection_
 - The current network inspection feature is weak and useless.
	  This doesn't need to be a separate file
 - The monitoring of the whole network is unnecessary and should focus only on the interfaces "eth0" "wt1" and "wg1"
 - The monitoring should focus ( if able ) on specifically the up/down throughput , and player latency 

----- Any further splitting of the agent can be done, by the coding helper(codex) if it would benefit the end product

Some further work needs to be done and will be listed to work trough;

##### Additional Points to work on / Consider 
 - As we are splitting the agent, the agents "agent-state" memory file, should also be split apart. 
	- The agent should have multiple memory files with each their own field of information for easier management and readability. 
			Specially as we get further into development it will be better if the "memory banks" work each in their own category maybe use parent folder "NeoCortex"
- The self-repair function is seemingly useless. This function should be replaced and the name should not be used as " self-repair" its misleading.
	- This function  was intended as an "Evolution" tool.
	- The agent should file incidents, errors, and mistakes in a separate memory and classifying it
	- When idle or if there's time the agent can get back to those saved mishaps and ponder over them to learn
		- What was the request and its intend,  what went wrong, why went it wrong, what was the agent missing / what does the agent need so it not happens again.
	- If any mishap can be solved, the agent should apply the needed changes to the source code ( /opt/rust-manager/src/* )
		- After that , the agent should (if it doesn't exist, create the agent branch) select its branch, commit, and do a PR on git.
	


#### Remodel of Agent integration / Interaction
The current model for tool selection, admin chat reply, and intent inferrence is weak, and should be replaced. 
Re replacement we will be using is a _Semantic Kernel + OpenAI function calling / Structured Outputs_ implementation 
	
build a **router + executor** architecture:
- A small first-pass model call classifies the admin message into a narrow intent set such as `chat`, `server_control`, `player_lookup`, `rcon_command`, `file_edit`, `status_check`, `troubleshooting`, `clarification`.
- That same pass extracts slots like `serverName`, `playerName`, `commandText`, `timeRange`, `severity`.
- Your app then decides which tools are eligible for that intent.
- Only those tools get exposed to the second model call, or you skip the second call entirely and execute directly when the intent is deterministic.
That pattern is aligned with how agent runtimes and graph-based orchestrators separate routing from tool execution instead of letting every turn roam across the whole toolset.

In practice, hat means:
- Replace hardcoded phrase matching with a **classifier schema**.
- Replace “iterate through tools and ask the LLM to pick one” with **tool eligibility filtering** based on intent.
- Replace freeform agent replies with **role-conditioned response templates** fed by structured state.
- 
The other important piece is **conversation state**. A sysadmin-style helper should behave statefully: “restart that one,” “show me the logs again,” “run it on monthly instead.” Semantic Kernel has chat history primitives and history reducers specifically to manage conversation history without stuffing everything back into the prompt forever.
For this project, we should implement it as four services:

`AdminIntentClassifier`  
Receives raw admin text plus compact context and returns the structured route object.

`ToolRegistry`  
Knows which tools exist, what arguments they require, and when they are allowed. This is code, not prompt text.

`ActionExecutor`  
Runs the chosen operation directly or via function calling.

`ResponseComposer`  
Turns the result into the final assistant reply in the persona you want: sysadmin helper, ops console assistant, anticheat operator, whatever.

That gives us a much more stable behavior model:
- admin says something messy
- classifier normalizes it
- executor acts on structured intent
- composer replies consistently

avoid:
- giant regex banks
- reply-phrase trees
- exposing every tool every turn
- letting the model “figure out” server names from weak memory when your code can track active target state

use:
- structured intent extraction
- per-intent allowed tool sets
- state memory for selected server/player/session
- deterministic execution paths for dangerous actions
- model-generated prose only for explanation and clarification



---
