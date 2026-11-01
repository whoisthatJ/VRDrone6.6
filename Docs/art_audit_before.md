# Art audit

Generated 2026-11-01 19:21 by *Tools > Art > Audit Project*. Quality level **Quest**, pipeline `Assets/_Project/Settings/URP_Quest.asset`, global texture mip limit 1.
Track scenes include their built-in course (gates and props spawned from the prefabs). Draw calls = renderer submeshes before batching (SRP Batcher/static batching make them cheaper, not fewer). Memory is the editor's runtime estimate.

| Scene | Renderers | Draw calls (est.) | Shadow casters | Triangles | Materials | Textures | Texture MB | Mesh MB | Lightmaps | Lights (realtime/mixed/baked) | Terrains | Refl. probes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Boot | 0 | 0 | 0 | 0 | 1 | 0 | 0,0 | 0,0 | 0 (0,0 MB) | 0/0/0 | 0 | 0 |
| Menu | 4 | 4 | 4 | 160 | 4 | 0 | 0,0 | 0,0 | 0 (0,0 MB) | 1/0/0 | 0 | 0 |
| Track_Field | 178 | 178 | 1 | 3 400 | 11 | 1 | 0,2 | 0,0 | 2 (0,8 MB) | 0/1/0 | 0 | 0 |
| Track_Hangar | 117 | 117 | 1 | 2 872 | 18 | 1 | 0,2 | 0,0 | 3 (1,3 MB) | 0/1/0 | 0 | 0 |
| Track_Industrial | 183 | 183 | 1 | 3 596 | 24 | 1 | 0,2 | 0,0 | 1 (1,7 MB) | 0/1/0 | 0 | 0 |
| InputDiagnostics | 0 | 0 | 0 | 0 | 1 | 0 | 0,0 | 0,0 | 0 (0,0 MB) | 1/0/0 | 0 | 0 |

## Boot

0 renderers (0 batching-static), ~0 draw calls, 0 shadow-caster draws, 0 triangles, 0 unique meshes (0,0 MB).

**Materials by shader**

- `Skybox/Procedural`: 1 (Default-Skybox)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|

**Lights**

- none

## Menu

4 renderers (0 batching-static), ~4 draw calls, 4 shadow-caster draws, 160 triangles, 1 unique meshes (0,0 MB).

**Materials by shader**

- `Universal Render Pipeline/Lit`: 2 (MenuFloor, MenuRing)
- `Universal Render Pipeline/Unlit`: 1 (PointerLine)
- `Skybox/Procedural`: 1 (Default-Skybox)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|

**Lights**

- Light: Directional, Realtime, intensity 0,80, colour #FFFFFF, shadows None

## Track_Field

178 renderers (155 batching-static), ~178 draw calls, 1 shadow-caster draws, 3 400 triangles, 3 unique meshes (0,0 MB).

**Materials by shader**

- `Universal Render Pipeline/Lit`: 7 (FieldPillar, Frame, GateFrame, GroundField, PropFront, PropRear, TreeLine)
- `Universal Render Pipeline/Unlit`: 2 (Ghost, PropBlur)
- `GateRush/AnalogFeed`: 1 (GogglesFeed)
- `Skybox/Procedural`: 1 (Default-Skybox)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|
| Scenes/Track_Field/Lightmap-0_comp_light.exr | 512×512 | BC6H | 10 | 0,4 |
| Scenes/Track_Field/Lightmap-1_comp_light.exr | 512×512 | BC6H | 10 | 0,4 |
| Art/Materials/Grid.asset | 128×128 | RGBA32 | 8 | 0,2 |

**Lights**

- Sun: Directional, Mixed, intensity 1,20, colour #FFF7EB, shadows None

## Track_Hangar

117 renderers (94 batching-static), ~117 draw calls, 1 shadow-caster draws, 2 872 triangles, 3 unique meshes (0,0 MB).

**Materials by shader**

- `Universal Render Pipeline/Lit`: 14 (Barrel, Crate, Frame, GateFrame, GroundHangar, HangarDoor, HangarLamp, HangarRoof, HangarSteel, HangarStripe, HangarWall, PropFront, PropRear, Wall)
- `Universal Render Pipeline/Unlit`: 2 (Ghost, PropBlur)
- `GateRush/AnalogFeed`: 1 (GogglesFeed)
- `Skybox/Procedural`: 1 (Default-Skybox)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|
| Scenes/Track_Hangar/Lightmap-0_comp_light.exr | 512×512 | BC6H | 10 | 0,4 |
| Scenes/Track_Hangar/Lightmap-1_comp_light.exr | 512×512 | BC6H | 10 | 0,4 |
| Scenes/Track_Hangar/Lightmap-2_comp_light.exr | 512×512 | BC6H | 10 | 0,4 |
| Art/Materials/Grid.asset | 128×128 | RGBA32 | 8 | 0,2 |

**Lights**

- Sun: Directional, Mixed, intensity 0,60, colour #FFF2D9, shadows None

## Track_Industrial

183 renderers (160 batching-static), ~183 draw calls, 1 shadow-caster draws, 3 596 triangles, 3 unique meshes (0,0 MB).

**Materials by shader**

- `Universal Render Pipeline/Lit`: 20 (Barrel, ContainerBlue, ContainerGreen, ContainerRed, Crane, Frame, GateFrame, GroundAsphalt, Pole, PoleLamp, PropFront, PropRear, Quay, Warehouse, …)
- `Universal Render Pipeline/Unlit`: 2 (Ghost, PropBlur)
- `GateRush/AnalogFeed`: 1 (GogglesFeed)
- `Skybox/Procedural`: 1 (Default-Skybox)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|
| Scenes/Track_Industrial/Lightmap-0_comp_light.exr | 1024×1024 | BC6H | 11 | 1,7 |
| Art/Materials/Grid.asset | 128×128 | RGBA32 | 8 | 0,2 |

**Lights**

- Sun: Directional, Mixed, intensity 1,10, colour #FFE6CC, shadows None

## InputDiagnostics

0 renderers (0 batching-static), ~0 draw calls, 0 shadow-caster draws, 0 triangles, 0 unique meshes (0,0 MB).

**Materials by shader**

- `Skybox/Procedural`: 1 (Default-Skybox)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|

**Lights**

- Light: Directional, Realtime, intensity 1,00, colour #FFFFFF, shadows None

## Materials under Assets/_Project

| Material | Shader | Textures |
|---|---|---|
| Art/Materials/Accent.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Barrel.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/ContainerBlue.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/ContainerGreen.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/ContainerRed.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Crane.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Crate.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/EditorMarker.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/EditorPanel.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/EditorPointer.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/FieldPillar.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Frame.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/GateFrame.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Ghost.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/GogglesFeed.mat | `GateRush/AnalogFeed` |  |
| Art/Materials/GogglesScreen.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/Ground.mat | `Universal Render Pipeline/Lit` | _MainTex=Grid, _BaseMap=Grid |
| Art/Materials/GroundAsphalt.mat | `Universal Render Pipeline/Lit` | _MainTex=Grid, _BaseMap=Grid |
| Art/Materials/GroundField.mat | `Universal Render Pipeline/Lit` | _MainTex=Grid, _BaseMap=Grid |
| Art/Materials/GroundHangar.mat | `Universal Render Pipeline/Lit` | _MainTex=Grid, _BaseMap=Grid |
| Art/Materials/HangarDoor.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/HangarLamp.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/HangarRoof.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/HangarSteel.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/HangarStripe.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/HangarWall.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/HorizonLine.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/MenuFloor.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/MenuRing.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Pillar.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/PointerLine.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/Pole.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/PoleLamp.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/PropBlur.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/PropFront.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/PropRear.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Quay.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Sparks.mat | `Universal Render Pipeline/Particles/Unlit` |  |
| Art/Materials/TreeLine.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Vignette.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/Wall.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Warehouse.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/WarehouseRoof.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Water.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/YardContainer0.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/YardContainer1.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/YardContainer2.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/YardContainer3.mat | `Universal Render Pipeline/Lit` |  |
