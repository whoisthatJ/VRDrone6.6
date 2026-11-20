# Tracks and the editor

## Venues

| Venue | Size | Features |
|---|---|---|
| Hall (`hall`) | 40 × 20 m, height 8 m | Open space. Lighting: default, dim, warm |
| Parking (`parking`) | 60 × 34 m, clear height 3.4 m | 28 columns in a grid, cars along the long walls. Lighting: default, dim, bright |

## Ready-made tracks

| Track | Venue | Gates | Idea |
|---|---|---|---|
| `H1_Oval` | hall | 6 | Flat oval; the next gate is always in view |
| `H2_Eight` | hall | 8 | Figure-eight with a crossing |
| `H3_Slalom` | hall | 7 | Offset slalom and a 180° hairpin |
| `X1_Mixed` | hall | 10 | Five LED colours, with painted and glowing panels in the same colours nearby |
| `P1_Columns` | parking | 6 | A loop round the column rows, low light |
| `P2_Maze` | parking | 9 | Turns behind columns: the next gate is often out of sight |

To run on a chosen track: key **T** in AI Bench mode, or `Ushqyr.exe -bench -bench-track H2_Eight`.

**The qualification tracks are hidden.** They are not in the Team Kit. They are made by the same rules and in the
same venues, but the gate layout, the colours and the lighting are different. A solution that remembers a ready-made
track will not fly on them.

## The track list

Key **T** opens the list: the ready-made tracks and your own. For each: "Fly", "Edit", "Dataset". The buttons "New
hall track" and "New parking track" open an empty track in the editor.

## The editor

- **Gates** are dragged with the mouse on the floor plan of the venue. Each gate has its LED colour, brightness, mode
  (steady or blinking) and allowed direction of passing.
- **The start pad** is moved and turned the same way.
- **The lighting** of the venue and **the time limit** are set for the track as a whole.
- **The checks** run as you edit. Errors prevent saving the track, warnings only give hints.

| Check | What is required |
|---|---|
| Number of gates | At least five: a gate gives a point again only after four others |
| Clearance | A gate does not stand right against a wall, a column or a car |
| Passing | There is room in front of the gate and behind it to fly through |
| Spacing | At least 2.6 m between gate centres; less than 4 m gives a warning |
| Start | The pad stands in a free place, no closer than 1.5 m to a gate, and at least one gate is in view from it |
| LEDs and direction | The colour is written as `#RRGGBB`, the direction is `any` or `front`, all gates have different `id`s |

- **"Test flight (oracle pilot)"** starts an autopilot that knows the true coordinates and draws its path. This is
  how you check that the track can be flown at all. The oracle exists only in the editor: it is not connected to the
  API.
- **"Random track"** builds a track from a seed and a difficulty level from 1 to 3. The same seed gives the same
  track, and every such track passes the checks.
- **Saving** writes the file to the `BenchTracks` folder in the user's data; the path is shown after saving.
- **"Export map"** puts a top-down picture of the track next to it.
- **"Fly it"** opens the track for your program.

You cannot fly by hand in AI Bench in either mode: the keyboard, gamepad and radio are not read.

## The track file

A track is a file `<id>.benchtrack.json`. It can also be edited in a text editor:

```json
{
  "schema": 1,
  "id": "H1_Oval",
  "venue": "hall",
  "lighting": "default",
  "start": { "position": [0.0, 0.0, -7.0], "yaw": 78.2 },
  "gates": [
    { "id": "G1", "position": [4.5, 0.0, -6.06], "yaw": 65.8,
      "led": { "color": "#FF00C8", "brightness": 1.0, "mode": "solid" },
      "direction": "any" }
  ]
}
```

The coordinates `[x, y, z]` are in metres from the centre of the venue; `y` is the height (0 for a gate on the
floor). Angles are in degrees. `direction`: `any` (either direction) or `front` (front only). `mode`: `solid`
(steady) or blinking; it is easiest to set it in the editor. To run a track by the path to its file:
`Ushqyr.exe -bench -bench-track C:\tracks\my.benchtrack.json`.

## Randomisation

The organisers may turn randomisation on: then the scene changes a little in every attempt, from the seed. One seed
gives every team the same variation.

| | Mild (`mild`) | Strong (`strong`) |
|---|---|---|
| Gates | shifted up to 10 cm, turned up to 3° | 25 cm, 8° |
| Start pad | 15 cm, 5° | 30 cm, 10° |
| Light | level 0.8 to 1.2, slightly warmer or cooler | 0.45 to 1.6, clearly warmer or cooler, direction changed |
| Gate LED brightness | 75 to 100% | 45 to 100% |
| Video noise | 0.8 to 1.3 times | 0.5 to 2.5 times |
| Floor and wall materials | unchanged | one of five each |
| Clutter along the walls | none | up to 8 coloured boxes and panels, some glowing |

On your own machine randomisation is turned on with a rules file. Create `my_rules.json` with the single line
`{ "randomization": "strong" }` and run `Ushqyr.exe -bench -bench-rules my_rules.json`. Fields that are not in the
file keep their defaults.
