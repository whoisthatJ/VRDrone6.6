"""Making and checking gate datasets.

    ushqyr-bench dataset --sim Ushqyr.exe --track H1_Oval --out data/h1 --count 2000 --seed 7 [--masks]
    ushqyr-bench dataset-check data/h1 [--draw 100]

`dataset` starts the simulator in dataset mode and waits for it. `dataset-check` reads a finished dataset: it makes
sure the YOLO and COCO files agree with the images and with each other, reports the simulator's own label check, and
(with opencv installed) draws the labels over random images for a look by eye.
"""
from __future__ import annotations

import json
import random
import subprocess
from pathlib import Path
from typing import Dict, List, Tuple

SPLITS = ("train", "val", "test")


def make(sim: Path, track: str, out: Path, count: int, seed: int, profile: str, masks: bool, timeout: float) -> int:
    """Runs the simulator in dataset mode. Returns its exit code (0 = the dataset was written)."""
    out.mkdir(parents=True, exist_ok=True)
    args = [str(sim), "-bench", "-bench-track", track, "-bench-dataset", str(out.resolve()), "-bench-dataset-count", str(count),
            "-bench-dataset-seed", str(seed), "-bench-dataset-profile", profile, "-bench-exit",
            "-screen-fullscreen", "0", "-screen-width", "960", "-screen-height", "540"]
    if masks:
        args.append("-bench-dataset-masks")
    return subprocess.run(args, timeout=timeout, check=False).returncode


def check(folder: Path) -> Tuple[List[str], Dict]:
    """Returns (problems, summary). An empty list of problems means the dataset is consistent."""
    problems: List[str] = []
    meta_path = folder / "dataset.json"
    if not meta_path.is_file():
        return ["no dataset.json: the dataset is not finished"], {}
    meta = json.loads(meta_path.read_text(encoding="utf-8"))
    width, height = int(meta["width"]), int(meta["height"])
    images = labels = boxes = 0
    for split in SPLITS:
        coco_path = folder / "annotations" / f"instances_{split}.json"
        try:
            coco = json.loads(coco_path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as e:
            problems.append(f"{coco_path.name}: {e}")
            continue
        by_image: Dict[int, List[dict]] = {}
        for a in coco["annotations"]:
            by_image.setdefault(a["image_id"], []).append(a)
            x, y, w, h = a["bbox"]
            if w <= 0 or h <= 0 or x < -0.5 or y < -0.5 or x + w > width + 0.5 or y + h > height + 0.5:
                problems.append(f"{split}: annotation {a['id']} has a box outside the picture")
            if len(a["keypoints"]) != 12:
                problems.append(f"{split}: annotation {a['id']} does not have 4 keypoints")
        files = sorted((folder / "images" / split).glob("*.jpg"))
        if len(files) != len(coco["images"]):
            problems.append(f"{split}: {len(files)} image files but {len(coco['images'])} COCO images")
        if int(meta.get(split, -1)) != len(files):
            problems.append(f"{split}: dataset.json says {meta.get(split)} images, found {len(files)}")
        for image in coco["images"]:
            stem = Path(image["file_name"]).stem
            if not (folder / image["file_name"]).is_file():
                problems.append(f"{split}: missing image {image['file_name']}")
            label = folder / "labels" / split / f"{stem}.txt"
            if not label.is_file():
                problems.append(f"{split}: missing label file for {stem}")
                continue
            lines = [ln.split() for ln in label.read_text(encoding="utf-8").splitlines() if ln.strip()]
            anns = by_image.get(image["id"], [])
            if len(lines) != len(anns):
                problems.append(f"{split}/{stem}: {len(lines)} YOLO labels but {len(anns)} COCO annotations")
                continue
            for ln, a in zip(lines, anns):
                if len(ln) != 17 or ln[0] != "0":
                    problems.append(f"{split}/{stem}: a YOLO line needs class 0, a box and 4 keypoints (17 values)")
                    break
                cx, cy, w, h = (float(v) for v in ln[1:5])
                if not all(0.0 <= v <= 1.0 for v in (cx, cy, w, h)):
                    problems.append(f"{split}/{stem}: YOLO box outside 0..1")
                # The two formats must describe the same box.
                x, y, bw, bh = a["bbox"]
                if abs((cx - w / 2) * width - x) > 1.0 or abs((cy - h / 2) * height - y) > 1.0 or abs(w * width - bw) > 1.0:
                    problems.append(f"{split}/{stem}: YOLO and COCO boxes differ")
                    break
            labels += 1
            boxes += len(lines)
            if meta.get("masks") and not (folder / "masks" / split / f"{stem}.png").is_file():
                problems.append(f"{split}: missing mask for {stem}")
        images += len(files)
    if images != int(meta["images"]):
        problems.append(f"dataset.json says {meta['images']} images, found {images}")
    if boxes != int(meta["gates_labelled"]):
        problems.append(f"dataset.json says {meta['gates_labelled']} labelled gates, found {boxes}")
    lc = meta.get("label_check", {})
    checked, good = int(lc.get("boxes_checked", 0)), int(lc.get("boxes_within_2px", 0))
    corners, on_gate = int(lc.get("corners_checked", 0)), int(lc.get("corners_on_gate", 0))
    if checked and good / checked < 0.98:
        problems.append(f"label check: only {good} of {checked} boxes match their gate's painted outline within 2 px")
    if corners and on_gate / corners < 0.98:
        problems.append(f"label check: only {on_gate} of {corners} visible corners lie on their gate")
    summary = {"images": images, "gates": boxes, "boxes_checked": checked, "boxes_within_2px": good,
               "corners_checked": corners, "corners_on_gate": on_gate}
    return problems[:50], summary


def draw(folder: Path, count: int, seed: int = 1) -> Path:
    """Draws boxes and corners over `count` random images into <folder>/check. Needs opencv."""
    import cv2  # only this function needs it

    out = folder / "check"
    out.mkdir(exist_ok=True)
    files = sorted(p for split in SPLITS for p in (folder / "images" / split).glob("*.jpg"))
    rng = random.Random(seed)
    rng.shuffle(files)
    colours = {2: (0, 255, 0), 1: (0, 165, 255)}
    for path in files[:count]:
        image = cv2.imread(str(path))
        h, w = image.shape[:2]
        label = folder / "labels" / path.parent.name / f"{path.stem}.txt"
        for ln in label.read_text(encoding="utf-8").splitlines():
            v = [float(x) for x in ln.split()]
            cx, cy, bw, bh = v[1] * w, v[2] * h, v[3] * w, v[4] * h
            cv2.rectangle(image, (int(cx - bw / 2), int(cy - bh / 2)), (int(cx + bw / 2), int(cy + bh / 2)), (255, 255, 0), 1)
            for k in range(4):
                x, y, vis = v[5 + 3 * k] * w, v[6 + 3 * k] * h, int(v[7 + 3 * k])
                if vis:
                    cv2.circle(image, (int(round(x)), int(round(y))), 4, colours[vis], 1)
                    cv2.putText(image, str(k), (int(x) + 5, int(y) - 5), cv2.FONT_HERSHEY_SIMPLEX, 0.4, colours[vis], 1, cv2.LINE_AA)
        mask = folder / "masks" / path.parent.name / f"{path.stem}.png"
        if mask.is_file():
            m = cv2.imread(str(mask), cv2.IMREAD_GRAYSCALE)
            tint = image.copy()
            tint[m > 0] = (255, 0, 255)
            # The mask beside the labelled picture, so a shift between the two shows at once.
            image = cv2.hconcat([image, cv2.addWeighted(image, 0.4, tint, 0.6, 0)])
        cv2.imwrite(str(out / f"{path.parent.name}_{path.stem}.jpg"), image)
    return out
