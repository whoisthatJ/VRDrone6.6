# Visual Overhaul Plan: Realistic Textures (PC VR)

**Target:** a Windows PC renders the game; a Meta Quest 2/3/Pro connects through **Link (cable) / Air Link / Steam Link** (any OpenXR headset works too). The goal is photoreal scenes at a **locked 90 Hz** on the target GPU, with gates readable instantly at 150 km/h.

**Recommended PC spec**
| | Minimum (Quest 2 res, "Medium") | Recommended (Quest 3 res, "High") |
|---|---|---|
| GPU | GTX 1070 / RTX 2060 / RX 5700 | RTX 3070 / 4060 Ti / RX 6800 or better |
| CPU | 4-core i5 / Ryzen 5 | 6-core+ |
| RAM | 16 GB | 16–32 GB |
**Every quality feature must have a toggle** in Medium/High/Ultra quality presets, so older school PCs still hold 90 Hz.

## 1. Art direction
- **Realism:** real-world scanned PBR materials, realistic lighting with soft shadows, a sky from a matching HDRI, light post-processing (tonemapping, color grading, subtle bloom), real scale.
- **Readability beats realism for racing elements.** Gates, flags and the start pad keep saturated race colors with emissive LED strips (bloom makes them glow). Only their surfaces are realistic (padded fabric, aluminium).
- **One visual language per track:**

| Scene | Look | Key materials | Lighting |
|---|---|---|---|
| Menu / Goggles room | Pilot's pit tent / workbench at dusk | Fabric, wood, foam, carbon fiber, tools, LiPo bags | Warm practical lights, baked GI |
| Track_Hangar (indoor) | Aircraft hangar at night: steel trusses, LED high-bays, painted concrete with reflections | Polished/painted concrete, corrugated metal, steel, rubber mats, safety paint | Baked GI + realtime spot shadows for key lights, reflection probes |
| Track_Field (outdoor) | Sports field at golden hour, tree line, fences, real grass | Terrain with grass/dirt layers, detail grass, trees, chain-link fence | Realtime sun + soft cascaded shadows, HDRI ambient, APV or light probes |
| Track_Industrial | Abandoned factory yard, overcast | Rusty metal, cracked asphalt, brick, decals, containers, puddles | Overcast HDRI, baked GI, decals |
| Quads | Real 5" race quads | Carbon fiber, anodized aluminium, plastics, prop polycarbonate, LiPo wrap | Reflection probes, realtime shadow |

## 2. Asset sources (licensing matters, since this goes to many students)
| Source | License | Use for |
|---|---|---|
| **Poly Haven** (textures, HDRIs, models) | CC0, no attribution required | Primary source. Free public API, no key |
| **ambientCG** | CC0 | Second source, biggest material library, has an API |
| ShareTextures, cgbookcase, 3DTextures.me | CC0 | Gap fillers |
| **Megascans (Fab)** | Fab Standard License | Only assets you already claimed; most new Megascans are paid since 2025 |
| Unity Asset Store (e.g. vegetation packs) | Asset Store EULA | OK if purchased; keep receipts |
| ❌ FreePBR and similar "personal use" sites | Not commercial | Don't use |

Every asset is logged in `Assets/_Project/Art/ASSET_LICENSES.md`.

## 3. PC VR technical budget
| Item | Rule (High preset, recommended GPU) |
|---|---|
| Frame | **90 Hz locked**, GPU ≤ 10 ms, CPU ≤ 8 ms; no reprojection (ASW) during normal flight. Medium preset must hit 90 Hz on the minimum GPU |
| Render pipeline | **Stay on URP** (already set up, VR-friendly, Forward+). HDRP is *not* recommended: much heavier in VR and a big migration |
| Stereo | Single Pass Instanced; render scale 1.0 (High), 0.8–0.9 + upscaling (Medium); MSAA 4x or Unity's STP/FSR upscaling if supported for XR in the installed Unity version (verify first) |
| Draw calls | ≤ 1500 in view (SRP Batcher + GPU instancing + GPU Resident Drawer if available in the installed Unity 6 version) |
| Triangles | ≤ 3–5 M visible; LODs on everything |
| Textures | **2K standard, 4K for hero/large surfaces** (terrain, hangar floor, quad body); BC7 for albedo/masks, BC5 for normals (DXT5/BC3 fallback); mipmaps; anisotropic 8–16×; texture mipmap streaming on, with a budget per preset (1.5/3/5 GB VRAM) |
| Materials | URP Lit + custom Shader Graphs; height/parallax OK on hero ground surfaces (High+ only); detail maps; up to ~8 samples per material |
| Lighting | **Mixed:** realtime sun/key lights with soft shadows (4 cascades, 2–4K shadow atlas, ~150 m shadow distance on High) + baked indirect GI (lightmaps or **Adaptive Probe Volumes**) + baked reflection probes with box projection indoors; screen space ambient occlusion (URP renderer feature, High+); URP decals renderer feature |
| Post-processing | Neutral/ACES tonemapping, color grading (LUT), subtle bloom (gate LEDs, lights), vignette **off** (comfort vignette is handled by the game separately). **No motion blur, chromatic aberration, lens distortion, film grain or depth of field in the eye view** (VR comfort). These *may* be used on the FPV drone camera image in Goggles mode for realism (analog-camera look) |
| Vegetation | Terrain detail grass (GPU instanced), mesh trees with LODs + billboards, alpha-tested foliage OK in moderation; wind animation |
| Atmosphere | Fog (URP linear/exp) + a skybox; fake volumetric light shafts with meshes/particles in the hangar |

## 4. Techniques that make it look real
1. **Consistent texel density:** about 1024 px/m near (hero), 512 px/m standard.
2. **Tiling breakup:** macro-variation, detail normals, vertex-paint blends, URP decals (cracks, oil, tyre marks, chalk lines, puddles).
3. **Trim sheets** for architecture.
4. **Matching sky and lighting:** the HDRI skybox drives the ambient light and reflections; the sun direction and colour match the HDRI.
5. **Reflection probes everywhere it matters** (box-projected in the hangar), with optional realtime probe refresh for the menu only.
6. **Physically plausible values:** albedo within 30–240 sRGB; light intensities in physical-ish units; one exposure per scene.
7. **Real-scale dressing:** cones, timing gantry, tents, flags, benches, LiPo charging table, spectators (static).

## 5. Milestones
| # | Milestone | Done when |
|---|---|---|
| V0 | Audit and baseline | Scene/material/texture audit; camera bookmarks; "before" screenshots; baseline frame timings in the editor and in a Windows build over Link |
| V1 | Pipeline | Downloader (Poly Haven/ambientCG → 2K/4K), import presets (BC7/BC5, streaming), material builder, license log |
| V2 | Material library and shaders | ~40 master materials, macro-variation Shader Graph, terrain layers, trim sheet, gate materials |
| V3 | Quality presets | Medium/High/Ultra URP assets + an in-game graphics menu (render scale, shadows, SSAO, textures, vegetation density, post-processing) |
| V4 | Track_Field | Terrain, grass, trees, golden-hour lighting; 90 Hz on High |
| V5 | Track_Hangar | Concrete reflections, trusses, lights; occlusion culling |
| V6 | Track_Industrial | Decals, puddles, rust; occlusion culling |
| V7 | Menu + quads + gates | Pit tent, realistic quads, LED gates |
| V8 | Polish and report | Before/after screenshots, per-scene performance per preset, readability check |

## 6. Platform for this overhaul (2026-11-01)
The owner chose **Windows only, monitor only (no VR)**. Everything above that targets PC VR is read with these substitutions:
- **Frame budget:** 60+ fps at 1920×1080 on the monitor. Medium must hold 60 fps on the development laptop (GTX 1650 Ti, 4 GB); High targets an RTX 3060/3070-class GPU at 1080p–1440p; Ultra is for faster GPUs.
- **No XR:** no Single Pass Instanced, no 90 Hz/ASW checks, no headset tests. The drone (FPV) camera renders straight to the monitor; LOS and the menu use the flat head camera.
- **Post-processing:** the VR comfort ban on motion blur, chromatic aberration, lens distortion, film grain and depth of field no longer applies. They stay off by default (racing readability) and none are needed for realism.
- **Measurements:** `GateRush.exe -benchmark` (BenchmarkRunner) flies the drone camera along each course and logs fps, 1 % low and CPU/GPU frame times per scene and preset. It replaces "fly over Link and report timings".

## 7. Decisions
| Date | Decision | Why |
|---|---|---|
| 2026-11-01 | Target **Windows, monitor only**; the Android/Quest build is no longer a target, so the Quest quality level and Quest-tuned URP asset are replaced by PC presets | Owner's choice |
| 2026-11-01 | Screenshots are saved as **JPG (q90)**, not PNG | 1920×1080 PNGs are ~3 MB each in Git LFS; JPG is ~0.5 MB and good enough to compare looks |
| 2026-11-01 | Standalone stays on **Mono** and **DX11** (DX12 also in the list) | IL2CPP for Windows isn't installed; DX11 is the safe default on the GTX 1650 Ti. DX12 can be compared later with the benchmark |
| 2026-11-01 | URP 17.6 checks: **STP** supports single-camera upscaling (and XR); the **GPU Resident Drawer** only needs Forward+ and "keep all" BatchRendererGroup variants | Read from the installed package source (`StpHistory.cs`, `GPUResidentDrawer.Validator.cs`, `UniversalRenderPipelineAsset.IsGPUResidentDrawerSupportedBySRP`) |
| 2026-11-01 | **`GateRush/Env Lit`** = URP 17.6 Lit generated from the package source with one change: `_MetallicGlossMap` is an **ORM** mask (R AO, G roughness, B metallic: Poly Haven's `arm` layout), so one JPG drives AO, smoothness and metallic. Not a Shader Graph | Keeps every URP feature (Forward+, shadows, SSAO, decals, lightmaps, SRP Batcher, DOTS instancing for the GPU Resident Drawer) with no node graph to maintain, and avoids RGBA PNG masks (~10 MB each in LFS) |
| 2026-11-01 | **Macro variation** = URP's detail albedo (×2 overlay, `_DETAIL_SCALED`) fed a tileable linear noise at 18–40 m per repeat; strength per material | Built into Lit, so no custom shader code. **Parallax** and **vertex-colour blending** are not used: the environments are procedural boxes (no painted vertex colours) and decals break up tiling instead |
| 2026-11-01 | **No trim sheet** | The architecture is generated boxes with metric UVs, not UV-mapped modular meshes, so a trim sheet has nothing to map onto |
| 2026-11-01 | 62 master materials (`MaterialLibrary.cs`), 4 terrain layers with packed URP mask maps (height blend), 4 HDRI skies, `GateRush/LED` unlit HDR shader driven by the gates' `_BaseColor` state | V2 scope; values live in code so rebuilds are repeatable |
| 2026-11-01 | **Presets** (Settings/PC): Medium = Forward+, SMAA, 1K shadows ×2 cascades to 70–80 m, no SSAO, 50 % grass; High = MSAA 4×, 2K ×4 to 150 m, SSAO (medium, half-res), additional-light shadows, LOD cross-fade, 85 % grass; Ultra = 4K ×4 to 220 m, SSAO full-res high, 100 % grass. All: HDR, HDR colour grading, decals (DBuffer), soft shadows, reflection-probe blending + box projection, mip streaming (1/2/3 GB budgets) | Monitor targets from §6. Player overrides on top (render scale, AA, shadows, SSAO, grass, post, VSync) in the new **Settings > Graphics** tab, saved in settings.json |
| 2026-11-01 | First launch picks the preset from video memory: < 5.5 GB Medium, < 10 GB High, else Ultra (`GraphicsQuality.DetectPreset`) | Simple and testable; the dev laptop (GTX 1650 Ti 4 GB) gets Medium |
| 2026-11-03 | **Superseded:** every first launch starts on High (`GraphicsQuality.DefaultPreset`); VRAM detection removed | User request: High is the default look; Settings > Graphics lowers it |
| 2026-11-01 | **GPU Resident Drawer off, STP not used** | Both are supported by URP 17.6, but scenes stay far below the draw-call budget, GRD needs "keep all" BRG variants (longer builds), and STP's temporal upscaling smears fast FPV motion on a monitor. MSAA/SMAA instead; render scale covers slow GPUs |
| 2026-11-01 | **Metric-UV meshes instead of triplanar**: every builder cube gets its own box mesh whose UV0 is in metres (continued from its position, so neighbours line up) and whose lightmap UV is unwrapped at real size; the transform and BoxCollider are untouched | Fixes stretched textures on the scaled blockout boxes with the stock-cost single-projection shader; colliders stay exactly as gameplay expects |
| 2026-11-01 | Art pass = **Tools > Art > Apply Art Pass** + **Bake Lighting**: builder materials are remapped by name (idempotent), decorations live under `Art Dressing` (rebuilt each run), gameplay objects are only re-skinned. Re-run both after *Build All Scenes* | Repeatable, reviewable in code, keeps the builders untouched |
| 2026-11-01 | **Indirect light = baked lightmaps + the builder's legacy light-probe grids** in every scene (Progressive GPU, 3–35 s per scene); APV not used | Already integrated with the builders and LightingBaker; probes cover the flight volume for quads, gates and props; APV would add bake complexity and memory for no visible gain at these scene sizes |
| 2026-11-01 | **No occlusion culling** (Hangar, Industrial) | A test bake culled the hangar roof, trusses and walls from parts of the hall (huge thin occluders); both are open spaces with < 1500 draws, so OC brings nothing |
| 2026-11-01 | **Field**: 1 km Unity Terrain, flat (y = 0) over the whole play area so the existing ground collider still matches, hills outside with a TerrainCollider on TrackGeometry; ambientCG `Grass004` lawn + `Ground037` worn layer (Poly Haven's lawns are leaf-litter brown); 520 billboard trees baked from Poly Haven `island_tree_02` (1.7 M tris, cache only) with crown colliders | Real grass and tree line at ~15 k tree triangles; quads crash into hills and trees like any obstacle |
| 2026-11-01 | **Hangar**: night, sun off; 18 LED high-bays = emissive fixture + Mixed spot light (6 shadowed); trusses are 1.8 m lattices with matching colliders (the builder's 0.6 m boxes stay) | Readable, evenly lit hall with real shadows where the course is |
| 2026-11-01 | **Industrial**: the builder's water sat 0.2 m under the ground plane; the asphalt visual now stops at the quay and real water continues to the horizon (ground collider unchanged). Oil/crack/tyre marks are URP DBuffer decals; **puddles are glossy transparent quads** | URP's default decal shader exposes no smoothness, so decal puddles could not reflect the sky |
| 2026-11-01 | **Gates**: padded fabric covers in race colours per type (Square orange, Large blue, Hurdle red), aluminium bases + sandbags, and LED strips on both faces. The LEDs are now the gate's `frameRenderers`, so idle/next/upcoming colours light the LEDs and the fabric keeps its race colour | Readability first: state colour is emissive and blooms |
| 2026-11-01 | **Quads** use shared library materials (carbon, anodized bells, tinted polycarbonate props, LiPo wrap, black plastic, LED) instead of one 2K atlas with ≤ 4 materials | The quad is a few primitives hidden from the pilot's own camera; SRP Batcher makes shared materials as cheap as an atlas, and an atlas would need UV-mapped quad meshes we don't have |
| 2026-11-02 | **V8 verified in the editor, not a player build** (owner's call after the first build ran for hours): benchmark via `BenchmarkRunner.StartInEditor` at 1920×1080 in Play mode, racing-line check, tests, audit | Editor numbers are a conservative lower bound (CPU-bound by the editor itself) |
| 2026-11-02 | **Shader-variant cuts for Windows builds**: DX11 only; one soft-shadow quality (Medium) for all presets; hidden builder renderers point at an Env Lit material so stock URP Lit is no longer compiled; the URP terrain add pass is stripped (`ArtShaderStripping`, only valid while terrains have ≤ 4 layers) | The first clean build compiled ~10 k variants (Env Lit + Lit + three terrain passes × 2 APIs × 3 soft-shadow qualities) for 3+ hours on a 4-core laptop |
