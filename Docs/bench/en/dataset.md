# Datasets

The simulator itself makes labelled images of gates. They are taken with the same camera as in flight: with the lens,
the exposure and the analog video link. These are the images to train a detector on.

Datasets are available in training mode only.

## How to make one

**In the app.** Key **T**, then "Dataset" next to the track you need: 500 images into the results folder. The path is
shown when the dataset is ready.

**From the command line.** You need the tools: `pip install ./bench/runner`.

```
ushqyr-bench dataset --sim Ushqyr.exe --track H1_Oval --out data/h1 --count 2000 --seed 7 --masks
ushqyr-bench dataset-check data/h1 --draw 100
```

| Option | Default | What it is |
|---|---|---|
| `--sim` | required | Path to the simulator |
| `--track` | `H1_Oval` | The name of a ready-made track or the path to a track file |
| `--out` | required | Where to write |
| `--count` | 500 | How many images |
| `--seed` | 1 | The seed. The same seed gives the same dataset |
| `--profile` | `strong` | Scene randomisation: `none`, `mild`, `strong` |
| `--masks` | off | Write masks as well |

## What is in the images

- **Camera poses.** 70% of the images are taken along the oracle pilot's flight with random offsets, 30% from random
  places in the venue.
- **Scene.** Every 25 images the scene changes: gate shifts, lighting, floor and wall materials, clutter along the
  walls, LED colours (see "Randomisation" in [tracks.md](tracks.md)).
- **Picture.** Every image has its own strength of video link interference, exposure and motion blur.

## Labels

For every gate in the image:

| Field | What it is |
|---|---|
| Box | A rectangle around the gate |
| The four corners of the opening | On the face towards the camera, in the order: top-left, top-right, bottom-right, bottom-left |
| Corner visibility | 0 = outside the picture, 1 = hidden, 2 = visible |
| `occluded`, `truncated` | The gate is partly hidden; the gate is cut off by the edge of the picture |
| Distance | Metres to the gate |

## Formats

| Format | Files |
|---|---|
| YOLO | `images/`, `labels/`, `data.yaml`; box and keypoints (the "pose" layout) |
| COCO | `annotations/instances_train.json`, `instances_val.json`, `instances_test.json` |
| Masks (`--masks`) | A PNG per image: 0 = no gate, n = the n-th gate of the track |

The 80 / 10 / 10 split (training, validation, test) is set by the seed.

## Label check

The simulator renders every image a second time: the lights are out and each gate glows in its own colour. The box of
each gate that is wholly in view must match the outline of the painted pixels within 2 pixels, and each visible corner
must lie on its gate. The counts are written in `dataset.json`.

`dataset-check` checks the files against each other and fails if less than 98% matched. With the option `--draw N` it
draws the labels over N random images into the `check/` folder (needs OpenCV): look at them by eye before training.

## Tips

- Make datasets on several tracks, in the hall and in the parking: the lighting and the background differ there.
- The qualification tracks are hidden. The more varied the data, the less the detector depends on a familiar picture.
- Do not remove the interference from the data: it will be there in flight.
