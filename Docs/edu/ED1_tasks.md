# ED1: Foundations I, task list

Order matters: each task ends with tests that pass in the open editor (`TestRunnerBridge.RunEditMode`). Logic goes into
`Assets/_Project/Scripts/Edu/Core` (no engine types, so it is fast to test); anything that touches the scene goes into
`Scripts/Edu/Runtime`.

## 1. Languages (F7)
| # | Task | Done when |
|---|---|---|
| 1.1 | Move the 60 to 120 hard-coded strings to the tables (calibration wizard, debug overlay, input report, catalogue names, display modes) | A test that scans runtime code for string literals shown to the user finds none outside an allow-list |
| 1.2 | New sheet `Localization/Text/edu.tsv` for this phase; Russian first, Kazakh marked "needs native review" | Existing test "every key in three languages" covers it |
| 1.3 | A static font atlas with Cyrillic and all Kazakh letters, so the changing dynamic fallback is no longer needed for normal text | Screenshot of a Kazakh screen; the fallback asset does not change after a run |
| 1.4 | Clipped-text check: every screen rendered in its longest language, no label wider than its box | An automated pass over the menu screens |

## 2. Local data (F5)
| # | Task | Done when |
|---|---|---|
| 2.1 | `EduDatabase` over `SqliteDb`: schema version table and migrations run in order | Tests: fresh file, upgrade from version 1, a failed migration leaves the file untouched |
| 2.2 | Tables: users, groups, seats, activities, assignments, attempts, replays, settings (tournaments and matches come with ED5 and ED8) | Tests for each store |
| 2.3 | User profiles: pick a name, optional PIN stored as a salted hash; "seat name only" mode with no personal data | Tests; a profile screen |
| 2.4 | Backup, restore, export, "delete this student's data" (rows and replay files) | Tests: restore gives the same rows; delete leaves no row and no file |
| 2.5 | Cut the internet out of the school build: a build profile without the cloud packages, the analytics module and engine diagnostics | The build has no `Unity.Services.*` assemblies; a loopback-only test of the main flows |

## 3. Activities and scoring (F1)
| # | Task | Done when |
|---|---|---|
| 3.1 | `ActivityDefinition` as JSON with a version, a content hash and a schema check | Tests: good file loads, each kind of bad file is refused with a clear message |
| 3.2 | Metric library, one metric at a time, each computed from a recorded flight: reach zone, gates in order, hold position/height/heading, path error, speed band, landing accuracy and touchdown speed, smoothness, crashes and contacts, rule breaches, time, battery left | A test per metric on small hand-made recordings |
| 3.3 | Rubric: metrics → score 0 to 100, pass, stars (60 / 80 / 95), breakdown, two tips from the weakest criteria | Tests, including re-scoring an old attempt after a rubric change |
| 3.4 | `AttemptRecord` saved to the database with the flags of F1 | Tests |
| 3.5 | The quality gate as a reusable test: a reference flight must score 90 or more, a bad flight must fail | One sample activity (hover in a box) passes the gate |

Camera, area, finds and payload metrics wait for ED2 and ED6, where the things they measure appear.

## 4. Recorder and replay (F2)
| # | Task | Done when |
|---|---|---|
| 4.1 | `FlightRecorder` at 100 Hz for any piloted flight: drone state, **stick inputs**, flight mode, events, environment | Test: record a scripted flight, read it back, equal values |
| 4.2 | File `.grreplay`: compressed, versioned, hashed; old versions still open | Tests |
| 4.3 | Rolling "last flight" in free flight, saved on request | Manual check |
| 4.4 | Viewer: timeline, 0.1 to 4× speed, onboard / chase / free / fixed / top-down cameras, stick overlay, graphs of height, speed and throttle, event markers | Screenshots looked at |
| 4.5 | Compare two flights (ghost and time gap); the reference flight of an activity | Screenshots |
| 4.6 | Video export through ffmpeg (already used by the AI Bench recorder) | An exported file plays |

**State on 21 November 2026:** 4.1 to 4.5 are done (`EDU_PLAN.md` §15). 4.3 has no button yet: the recorder keeps the
last ten minutes and `Save` writes them, the button comes with the training hub. 4.6 waits for ffmpeg.

The AI Bench recorder stays as it is (its files are signed evidence). The new recorder shares the reader of the truth
track where that is natural, and nothing in the AI Bench attempt folder changes.

## 5. Basic installer
| # | Task | Done when |
|---|---|---|
| 5.1 | Extend `Tools/Installer/Ushqyr.iss`: firewall rules for the classroom ports, a silent mode | Installs on a second PC and two PCs see each other |

Needs the Inno Setup compiler on this PC (not installed) and a second PC.

## What ED1 does not touch
AI Bench files, protocol and results. Its tests (`BenchTests`, the Python tests) must stay green after every task.
