# Track_expo quality audit (Phase 3, P1)

Compared: `Docs/reference/ref_expo_aerial.png` (1600 × 1236) against the `expo_reference` bookmark of `Track_Expo`
(summer day preset, `Docs/screenshots/Track_Expo/expo_reference_P1a_src.jpg`), plus the other bookmarks
(`expo_top`, `overview`, `walkway`, `orbit`, `trees`, `lawn`, `spawn`, `fpv`). `Track_Expo_v1` keeps the "before"
look; its references are in `Docs/screenshots/track_expo_v1/reference/`.

## Colour calibration, before (region means, sRGB 0–255)
| Region | Photo | Render | Luminance render / photo |
|---|---|---|---|
| Sky (top band) | (154, 181, 218) | (205, 203, 192) | 1.14: warm grey instead of blue |
| Grass (front lawns) | (96, 120, 85) | (87, 103, 91) | 0.88: darker, greyer |
| Roofs (front-left petal) | (148, 161, 178) | (113, 128, 148) | 0.79: too dark |
| Asphalt | (174, 174, 172) | n/a | the render's box missed the road; re-measured after the framing fix |

## Ranked gaps
Cost: S = under an hour, M = a few hours, L = a day or more. "Perf" is the expected frame-time cost.

| # | Gap (from the reference angle unless noted) | Planned fix | Cost | Perf |
|---|---|---|---|---|
| 1 | The bookmark doesn't match the photo's framing: the photo is closer (the petals span ~76 % of the width, ours ~54 %) and 4:3 | Move `expo_reference` closer and lower; compare on a 4:3 crop | S | none |
| 2 | Sky is a warm hazy yellow-grey; the photo has a clear blue summer sky with a light grey horizon | Procedural daytime sky (blue tint, thin atmosphere, light horizon), cooler and lighter fog colour, lower fog density near the site | S | none |
| 3 | Pavilion roofs and façades read dark and flat; the photo's roofs are bright silver | Brighter, less metallic roof material; lighter curtain wall glass; probe intensity | S | none |
| 4 | City is uniform white boxes | 4–6 façade materials per type (beige, white, pastel, grey, glass), picked per building; coloured roofs | M | +~120 draw calls |
| 5 | Grading: the photo is crisp, cool, slightly more saturated in the greens and blues | Retune the day volume (exposure, contrast, white balance, saturation) against the region means | S | none |
| 6 | Asphalt is near black; the photo's roads are light grey | Lighter asphalt, crisper white markings | S | none |
| 7 | Lawns are a flat saturated green; the photo's are lighter yellow-green with mowing stripes | Lawn tint and macro variation; stripe texture | S | none |
| 8 | Parked cars are dark and heavy on red; the photo's car parks are mostly white and silver | Car colour palette (white, silver, grey, black, a few colours) | S | none |
| 9 | Sphere glass is dark teal; the photo's is a lighter blue with bright mullion lines | Lighter, more reflective sphere glass; brighter mullions | S | none |
| 10 | Pavilion façades lack the mullion grid and the glazed courtyard walls | Façade texture with mullions at the real spacing; courtyard walls glazed | M | none |
| 11 | Roof skylights are dark stripes; the photo has long glazed bands and panel seams | Lighter skylight glass, roof panel seam texture, edge trims | M | none |
| 12 | Plaza / axis paving is one flat grey | Granite paving pattern on the axis and the hub, lighter concrete | S | none |
| 13 | Far city skyline: plain boxes, no windows | Window texture on the far ring and skyline, Astana palette | S | none |
| 14 | Trees: one green, sparse along the roads | Darker and varied greens, tree rows along the radial roads | M | +instances |
| 15 | No moving traffic on the ring roads | Spline-driven instanced cars on the ring roads | M | small |
| 16 | Podium round the sphere reads plain; the photo's is glazed white and blue | Podium glass and white frame materials | S | none |
| 17 | No ambient occlusion: buildings float on the ground | SSAO on at High / Ultra (renderer feature) | S | ~1 ms GPU at 1080p |
| 18 | Water areas (pools in the park) are dark; the photo's reflect the sky | Water material with sky reflection | S | none |
| 19 | Solar fields: barely visible | Lighter panel frames, blue cells | S | none |
| 20 | No clouds | A cloud layer in the procedural sky or a cloud dome | M | small |

## Fix order
Batch 1 (this pass): 1, 2, 3, 5, 6, 7, 8, 9, 12, 16, 18, 19 (materials, sky, grading, bookmark), then calibrate.
Batch 2: 4, 13 (city façade variety and the skyline). Batch 3: 10, 11 (façade and roof detail). Batch 4: 14, 15, 17, 20.
Profile after each batch (Docs/perf/expo_P1_*.csv).

## Result of P1 (this pass)
| # | State |
|---|---|
| 1 | **Fixed.** New bookmark `expo_photo` (camera 450 m out on bearing 194°, 262 m up, 66° vertical FOV), solved from where the sphere, the front petal edge and the horizon sit in the photo; compared on a 4:3 centre crop. `expo_reference` stays for the v1 comparisons |
| 2 | **Fixed.** Procedural summer sky (tint 0.36 / 0.48 / 0.82, atmosphere 0.85, exposure 1.45) and a cool light-grey haze (0.70 / 0.76 / 0.87) |
| 3 | **Fixed.** Roofs lighter and less metallic (tint 0.68–0.74, metallic 0.3), lighter skylight glass and curtain walls |
| 4 | **Fixed (palette).** City slabs pick white, beige, cream or pastel and offices clear or blue glass by OSM id; roofs light or dark grey; the far ring uses the same palette. Balconies and rooftop clutter are not done |
| 5 | **Fixed.** Day grade: exposure 0.05, contrast 12, saturation 8, white balance −4 |
| 6 | **Fixed.** Asphalt 0.47 grey |
| 7 | **Partly.** Lighter, greyer lawn tint; no mowing stripes |
| 8 | **Fixed.** Cars white / silver / grey / black |
| 9 | **Fixed.** Lighter sphere glass |
| 12 | **Partly.** Lighter paving tint; no granite pattern |
| 18 | **Fixed.** `M_Expo_Water` reflects the sky (the shared dark pond material is untouched) |
| 19 | **Fixed.** `M_Expo_Solar` blue cells |
| 10, 11, 13, 14, 15, 16, 17, 20 | **Postponed** (façade mullions and interior mapping, roof seams, skyline windows, tree variety, moving traffic, podium glass, SSAO check, clouds). Next environment pass, or P7 polish |

### Colour calibration, after (photo vs `expo_photo`, 4:3 crop; region means, sRGB)
| Region | Photo | v1 (before) | P1 (after) | Ratio before → after |
|---|---|---|---|---|
| Sky | (155, 181, 219) | (207, 204, 181) | (167, 198, 193) | 1.14 → 1.07 |
| Grass | (96, 120, 85) | (89, 123, 48) | (85, 112, 59) | 0.98 → 0.91 |
| Roofs | (148, 161, 178) | (118, 135, 151) | (143, 163, 191) | 0.83 → 1.01 |
| Asphalt | (174, 174, 172) | (114, 116, 104) | (152, 154, 156) | 0.66 → 0.89 |
| Whole frame | (129, 141, 151) | (129, 136, 121) | (133, 143, 146) | 0.96 → 1.01 |

Remaining colour gaps: the sky band at the horizon is cyan rather than blue, and the lawns are still a little too yellow (blue channel 59 vs 85).

### Screenshots
- `Docs/screenshots/track_expo/P1/photo_vs_render.jpg`: the photo next to `expo_photo` after P1.
- `Docs/screenshots/track_expo/P1/photo_vs_v1.jpg`: the photo next to the same bookmark in `Track_Expo_v1`.
- `Docs/screenshots/track_expo/P1/bookmarks.jpg`: the other bookmarks.

**What matches now:** framing, the site plan, the bright silver roofs, light-grey roads, the white and silver car parks, the Astana palette of the city, the overall brightness. **What doesn't yet:** the façade and roof detail (mullions, glazed bands, seams), the city's rooftop detail and window depth, the trees, the clouds, no traffic.

### Performance (Editor, GTX 1650 Ti laptop, 1080p, summer day; `Docs/perf/expo_P1.csv`)
| Preset | E5 | P1 |
|---|---|---|
| Low | 75.4 fps | 87.6 fps |
| High | 69.3 fps | 75.1 fps |
