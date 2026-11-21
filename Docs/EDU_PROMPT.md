# Prompt for the Claude Code agent: Education Suite (TZ items 5–11)

> Before pasting:
> 1. Put `EDU_PLAN.md` in the project root.
> 2. Put `EDU_TZ_ru.md` in `Docs/reference/`.
> 3. Download the current official **FAI Sporting Code, Volume F9 Drone Sports** PDF from fai.org and put it in `Docs/reference/fai/`.
> 4. Install **QGroundControl** (current stable) on the PC. Optional for the LAN tests: a second PC on the same network.
> 5. Paste everything below the line into Claude Code on the PC with the project.

---

You are a senior Unity engineer continuing **GateRush**, a **desktop PC** drone simulator (no VR). This phase turns it into a **classroom product** with seven modules: Training, Racing, Drone soccer, Scenarios, Programming, Teacher console, Platform.

- **Requirements:** `Docs/reference/EDU_TZ_ru.md` (items 5–11 as received, in Russian). If anything conflicts with it, the TZ wins.
- **Design, decisions, milestones, tests:** `EDU_PLAN.md`. Follow it. If you deviate, update it in the same commit under "Decisions".
- **Priority:** the AI Bench (`AIBENCH_PLAN.md`) has real competition dates. Nothing here may delay or break AI Bench milestones B1–B2. If both are in progress and compete for time, stop and ask me which goes first.

## Process
- Use the **superpowers** skills if installed:
  - `superpowers:writing-plans` to break each milestone into small tasks;
  - `superpowers:test-driven-development` for all logic (scoring metrics, rubrics, timing, tournament advancement, soccer rules, placement rules, licence checks, protocol messages, reports);
  - `superpowers:dispatching-parallel-agents` for independent work (the ED0 spikes; after ED2 the modules ED3, ED6 and ED7);
  - `superpowers:systematic-debugging` for bugs;
  - `superpowers:verification-before-completion` before you report a milestone as done.
- Load `unity-cli`. Run `unity status` before touching scenes. Drive a connected Editor with `unity command` / `unity command eval`. Never hand-edit `.unity`, `.prefab`, `.mat` or `.asset` YAML. Generate scenes, arenas, exercises and scenarios with **repeatable Editor tools**.
- Other skills:
  - `localization` and `optimize-text-mesh-pro` for the three languages and Kazakh letters;
  - `ui` / `ui-uitk` for every new screen;
  - `setup-multiplayer-services` for LAN sessions (LAN and direct IP only; no cloud services);
  - `initialize-ai-navigation` for walking people and vehicles in scenarios;
  - `physics-3d-collision` for collision and trigger problems;
  - `unity-package-management` for packages; `urp-postprocessing` for weather and thermal view.
- One commit per milestone (`ED<n>: <summary>`), LFS for binaries. Keep **all existing tests green**.

## First: orient (ED0)
1. Read `Docs/reference/EDU_TZ_ru.md`, then `EDU_PLAN.md`, then skim the other plan files it lists under "Builds on".
2. Inspect the project and update `Docs/project_state.md`. For this phase, list what exists of each item in plan §12 (flight controller modes, bots, LAN sessions, API server and Python transport, recorder and replay, locations, track editor, UI system, any localisation, any save system, build profiles and quality presets).
3. Check the machine and tell me what's missing: QGroundControl, Python, ffmpeg, a second PC or a way to run two instances, an installer tool, the Linux build module, and whether the PC has only integrated graphics.
4. Read the FAI PDF in `Docs/reference/fai/`. Write `Docs/reference/fai/f9a_values.md` with every value used in plan §6 (and the ring heights the plan leaves open), each with its paragraph number and the edition. List every difference from the plan's table.
5. Run these **spikes in parallel**, each in a small time box, and write each result to `Docs/spikes/<name>.md`:
   - **MAVLink / QGroundControl.** A minimal endpoint on UDP 14550 that presents an ArduPilot-compatible multirotor: heartbeat, parameter list, mission list, a few telemetry messages, arm and take-off commands. Test the sequences with `pymavlink`, then give me a 5-minute checklist to try in QGroundControl. Report what still fails. Recommend path A (finish our endpoint) or path B (bundle the real ArduPilot simulator and drive it through its JSON interface).
   - **LAN collisions.** Two instances with simulated latency (then two PCs if I have them): two spheres pushing and bouncing. Compare "each PC simulates its own ball" with "the host simulates both". Measure added stick latency and how far the two PCs disagree.
   - **Studio.** A local HTTP server on `127.0.0.1`, a Blockly page opened in an app-style Edge window beside the simulator, a bundled Python running the generated code, and a drone that moves. No internet used.
   - **Reports.** Write an XLSX and a PDF containing Kazakh, Russian and English text from a player build. Confirm they open in Excel or LibreOffice and a PDF reader with every Kazakh letter correct.
   - **Licence crypto.** Signature check and hardware fingerprint working in a player build.
   - **Database.** SQLite working in a player build (LiteDB if it doesn't).
   - **Weak PCs.** Frame rate of the lightest location at the lowest settings on integrated graphics, if this PC or another has it.
6. Post the spike results, your recommendation for each open choice, and the ED1 task list. Then continue; don't wait for answers to plan §13 unless a spike result needs my decision.

## Hard rules
- **Same physics for everyone.** People, bots, the autopilot and students' programs all fly through the existing flight controller and flight model. No kinematic shortcuts.
- **Activities are data.** Exercises, scenarios, tasks, soccer rules and tournament formats are JSON or assets validated by a schema, not code branches. Each shipped activity has a reference flight that must score ≥ 90 and a bad flight that must fail, as automated tests.
- **Scoring is deterministic** and computed from the recorded flight.
- **Sport rules follow the FAI text.** Use `f9a_values.md`, not the plan's table, if they differ. Keep the edition in the rules file.
- **Offline always.** No internet access at runtime: no CDN links, no web fonts, no analytics, no crash reporting, no cloud multiplayer. Add an automated test that runs the main flows with networking limited to loopback and LAN.
- **Three languages from the start.** Every user-facing string goes through the string tables in Kazakh, Russian and English, including existing screens you touch. Write Russian first; mark Kazakh "needs native review". Add the checks from plan F7.
- **Student data stays local and minimal.** Capture only the simulator's own picture for monitoring, never the desktop.
- **Security basics:**
  - teacher commands are signed, and seats reject unsigned ones;
  - local servers bind to `127.0.0.1` and need a session token;
  - the licence **private key is never in the repo or in any build**. Tell me where the licence tool and key will live before you create them.
- **Students' programs** run as a separate process with Run, Stop and a time limit. Grades come from the simulator's checks only.
- **Content.** People in scenarios are simple stylised figures in rescue situations; nothing graphic. Drones, vehicles and brands are generic or our own.
- **Licences of what we bundle.** Record every third-party component and its licence in `Assets/_Project/Art/ASSET_LICENSES.md` and `THIRD_PARTY_NOTICES.md` (Blockly, the code editor, Python and its packages, the PDF and XLSX libraries, fonts, the installer tool, ArduPilot if path B is used). Check each licence yourself; don't rely on the plan. Don't link copyleft code into the app.
- **Performance.** Keep the earlier frame-rate targets with 8 racers or 10 soccer balls plus spectators. Add the "iGPU" quality preset and report its numbers.
- **Visual verification.** For arenas, exercises, scenario objects, the Studio and the teacher console, take screenshots, open them yourself, and tell me what you see.

## Milestones (details and "done when" lists in EDU_PLAN.md §3–§11)
### ED1: Foundations I
- Three-language framework; convert the existing screens; fonts with Kazakh letters; the automated string checks.
- Local database with migrations; user profiles; backup and restore.
- Activity model, metric library, rubric engine, attempt records.
- Flight recorder and replay viewer with stick overlay and flight comparison.
- A basic Windows installer that sets the firewall rules, so LAN tests can run on other PCs.

### ED2: Foundations II
- Flight-mode ladder (Position, Altitude hold, Angle, Horizon, Acro) and the built-in autopilot (take off, land, hold, go to, velocity, orbit, mission, return), with failsafes and position sources.
- Camera, delivery and edu drones; gimbal, photo, thermal view, spotlight, payload drop.
- Weather and failure systems with scripted and live changes.
- Tests from plan F3.

### ED3: Training (TZ 5)
- Training hub; the 30 exercises of plan §4 as data, each with briefing, guidance, rubric, tips, reference and bad flights.
- NIST-style bucket targets for block C (build them as simple props; the alignment check is "the ring inside the bucket is fully visible to the camera"). If you can reach NIST's public "sUAS Open Test Lane" documents, use their lane layout and test procedures; otherwise use the plan's simplified versions and say so.
- Free flight launcher; replay library.

### ED4: Teacher console (TZ 10)
- Classroom link with discovery, class code and signed commands.
- Groups and seat map; assigning and lesson plans; the seat grid with live pictures; watching a seat live; live weather and failures; pause and attention mode.
- Journal with replays and manual grades; reports in XLSX, CSV and PDF.
- A "simulated seat" test client so 30 seats can be tested on one or two PCs.

### ED5: Racing (TZ 6)
- Bring the track list to the 10 of plan §5 with medal times, bot lines and ghosts.
- The race track editor for users, with sharing to seats.
- LAN race for up to 8 with the FAI-style start, missed-gate rule, timing, race director and big-screen views.
- Tournament manager: qualification, elimination bracket (4 per race or up to 8), optional double elimination, final, points format for small classes; printable bracket and results; resume after a power cut.

### ED6: Scenarios (TZ 8)
- Location Analyzer and a `LocationProfile` for every location.
- Scenario templates with placement rules, seed and validator; people, vehicles, fire and smoke, rising water, defects, parcels; pilot tools (map, mark, photo, thermal, drop).
- The 10 scenarios of plan §7 and the teacher's scenario editor.
- The scenario × location × seed test matrix.

### ED7: Programming (TZ 9)
- The `gaterush` Python API on the shared transport; per-task permissions enforced in the simulator.
- Studio: blocks, Python editor, console, task panel, help in three languages; bundled Python.
- Mission planner on the location map with QGroundControl `.plan` import and export.
- MAVLink on the path chosen in ED0; the `pymavlink` tests; then walk me through the QGroundControl checklist in plan §8.
- The 10 tasks with randomised seeds, reference solutions and a "cheat" solution that must fail.

### ED8: Drone soccer (TZ 7)
- Rules file from `f9a_values.md`; both arenas; drone-ball profiles; sphere collision physics.
- LAN match up to 5 v 5 on the collision model chosen in ED0; set and match flow.
- Referee mode (Auto, Assisted, Manual), instant replay, match protocol PDF.
- The 30-case goal-decision test and the contact-agreement test from plan §6.

### ED9: Platform (TZ 11)
- Final installer with components, silent mode, firewall rules and clean uninstall; offline update files.
- Offline licence: request file, signed licence file, module gating, class bundle activation through the teacher console, clock-rollback check; the private licence tool.
- The no-internet check with a network monitor; the full three-language pass.

### ED10: Classroom pilot
- A written lesson script for me to run with a real class on real school PCs, a problem-report form, then the fix list.

## Stop and ask me
- After ED0, if a spike result needs my decision (MAVLink path A or B; soccer collision model).
- If the FAI PDF differs from the plan in a way that changes gameplay.
- Before creating the licence key pair and choosing where the private tool lives.
- Before anything that needs something from me: a second PC, the school network, QGroundControl checks, a code-signing certificate, a Kazakh reviewer.
- When a module is ready to try (end of ED3, ED4, ED5, ED6, ED7, ED8).
- If this work would delay AI Bench milestones.

Otherwise decide, record the decision in `EDU_PLAN.md` under "Decisions", and keep going.
