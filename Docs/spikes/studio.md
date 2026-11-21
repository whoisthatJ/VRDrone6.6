# Spike: Studio (ED0, 21 November 2026)

**Question.** Can the simulator serve a local page, open it in an app-style window, run a student's Python program as
a separate process, and have that program move a drone, with no internet?

**Result: yes, every link of the chain works in a player build.**

| Link | Result |
|---|---|
| Local server | `HttpListener` on `127.0.0.1` at a free port, inside the player |
| Session token | A request without the token, or with a wrong one, gets 403 |
| Page | Opened in the app's browser pane and in an Edge window started with `--app=<address>` (no address bar, its own window, titled "Ushqyr Studio") |
| Run | The page posts the program; the player writes it to a file and starts `python -u` as a separate process; Stop kills it |
| Output | The program's `print` lines, Kazakh text included, appear on the page |
| Drone | `drone.takeoff(2); drone.forward(3); drone.right(1)` ended at x 1, y 2, z 3. Each command blocks until the drone has arrived |
| Offline | Nothing was loaded from the internet: the page is one string inside the player |

## What the spike did not include
- **Blockly.** It has to be downloaded and bundled (Apache-2.0), and downloads wait for the owner's go-ahead. The
  page has a plain text box where the blocks will be.
- **A bundled Python.** The spike used the Python installed on this PC. The embeddable Python package and its
  libraries (numpy, OpenCV, pymavlink) are also downloads.
- **Real flight.** The spike's drone is a point moving at 1 m/s. Since the spike, the autopilot exists (`Autopilot`),
  so the real Studio commands will fly the real drone.
- **The transport.** The spike's Python helper talks HTTP to the page's server. The plan (§8) wants the `gaterush`
  package on the AI Bench transport; that is ED7 work.

## Recommendation
Build Studio as planned (D7). Decide the two downloads (Blockly; Python with its packages) when ED7 starts.
