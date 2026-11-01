# Art audit

Generated 2026-11-02 00:17 by *Tools > Art > Audit Project*. Quality level **High**, pipeline `Assets/_Project/Settings/PC/URP_PC_High.asset`, global texture mip limit 0.
Track scenes include their built-in course (gates and props spawned from the prefabs). Draw calls = renderer submeshes before batching (SRP Batcher/static batching make them cheaper, not fewer). Memory is the editor's runtime estimate.

| Scene | Renderers | Draw calls (est.) | Shadow casters | Triangles | Materials | Textures | Texture MB | Mesh MB | Lightmaps | Lights (realtime/mixed/baked) | Terrains | Refl. probes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Boot | 0 | 0 | 0 | 0 | 1 | 0 | 0.0 | 0.0 | 0 (0.0 MB) | 0/0/0 | 0 | 0 |
| Menu | 59 | 70 | 46 | 25,710 | 22 | 37 | 155.5 | 2.3 | 1 (0.0 MB) | 0/4/0 | 0 | 1 |
| Track_Field | 279 | 296 | 199 | 78,584 | 33 | 55 | 206.0 | 4.4 | 2 (0.1 MB) | 0/1/0 | 1 | 1 |
| Track_Hangar | 265 | 285 | 148 | 128,244 | 43 | 64 | 229.0 | 6.7 | 3 (2.0 MB) | 0/19/0 | 0 | 3 |
| Track_Industrial | 390 | 406 | 285 | 84,145 | 41 | 61 | 297.5 | 3.2 | 3 (0.5 MB) | 0/1/0 | 0 | 2 |
| InputDiagnostics | 0 | 0 | 0 | 0 | 1 | 0 | 0.0 | 0.0 | 0 (0.0 MB) | 1/0/0 | 0 | 0 |

## Boot

0 renderers (0 batching-static), ~0 draw calls, 0 shadow-caster draws, 0 triangles, 0 unique meshes (0.0 MB).

**Materials by shader**

- `Skybox/Procedural`: 1 (Default-Skybox)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|

**Lights**

- none

## Menu

59 renderers (45 batching-static), ~70 draw calls, 46 shadow-caster draws, 25,710 triangles, 24 unique meshes (2.3 MB).

**Materials by shader**

- `GateRush/Env Lit`: 18 (M_Aluminium_Brushed, M_Asphalt_PitLane, M_Fabric_CanvasRed, M_Fabric_GateBlue, M_Fabric_GateOrange, M_Fabric_Sandbag, M_Grass_Lawn_Menu, M_Lamp_Warm, M_Lipo, M_metal_toolbox, M_Plastic_Black, M_Plastic_ConeOrange, M_plastic_crate_02, M_plastic_monobloc_chair_01, …)
- `GateRush/LED`: 2 (M_LED_Blue, M_LED_Gate)
- `Universal Render Pipeline/Unlit`: 1 (PointerLine)
- `Skybox/Cubemap`: 1 (Sky_qwantani_dusk_2_puresky)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|
| Art/HDRI/qwantani_dusk_2_puresky_2k.hdr | 1024×1024 | Cube BC6H | 11 | 16.0 |
| Art/Materials/Ground/Asphalt_PitLane/Asphalt_PitLane_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Ground/Asphalt_PitLane/Asphalt_PitLane_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Ground/Asphalt_PitLane/Asphalt_PitLane_ORM.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Ground/Grass_Field/Grass_Field_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Ground/Grass_Field/Grass_Field_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Ground/Grass_Field/Grass_Field_ORM.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Vegetation/TreeImpostor/TreeImpostor_Albedo.png | 2048×512 | BC7 | 12 | 2.7 |
| Art/Models/plastic_monobloc_chair_01/textures/plastic_monobloc_chair_01_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/plastic_monobloc_chair_01/textures/plastic_monobloc_chair_01_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/metal_toolbox/textures/metal_toolbox_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/metal_toolbox/textures/metal_toolbox_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Canvas/Fabric_Canvas_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Canvas/Fabric_Canvas_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Canvas/Fabric_Canvas_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Brushed/Steel_Brushed_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Brushed/Steel_Brushed_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Brushed/Steel_Brushed_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Wood/Wood_Plywood/Wood_Plywood_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Wood/Wood_Plywood/Wood_Plywood_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Wood/Wood_Plywood/Wood_Plywood_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/plastic_crate_02/textures/plastic_crate_02_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/plastic_crate_02/textures/plastic_crate_02_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Procedural/Macro/Macro_Noise_Mask.png | 512×512 | BC7 | 10 | 0.7 |
| Art/Materials/Procedural/Lipo/Lipo_Albedo.png | 256×128 | BC7 | 9 | 0.1 |
| Scenes/Menu/Lightmap-0_comp_light.exr | 64×64 | BC6H | 7 | 0.0 |

**Lights**

- String Light L: Point, Mixed, intensity 2.20, colour #FFB86B, shadows None
- String Light R: Point, Mixed, intensity 2.20, colour #FFB86B, shadows None
- Light: Directional, Mixed, intensity 0.70, colour #FF9276, shadows Soft
- Task Lamp: Point, Mixed, intensity 3.50, colour #FFB86B, shadows None

## Track_Field

279 renderers (155 batching-static), ~296 draw calls, 199 shadow-caster draws, 78,584 triangles, 122 unique meshes (4.4 MB).

**Materials by shader**

- `GateRush/Env Lit`: 26 (M_Aluminium_Brushed, M_CarbonFiber, M_Chalk, M_Concrete_Blocks, M_Fabric_Canvas, M_Fabric_CanvasRed, M_Fabric_GateBlue, M_Fabric_GateOrange, M_Fabric_GateRed, M_Fabric_Sandbag, M_Fence_Chainlink, M_Glass_Night, M_Lipo, M_Metal_Anodized, …)
- `GateRush/LED`: 2 (M_LED_Gate, M_LED_Red)
- `Universal Render Pipeline/Unlit`: 2 (Ghost, PropBlur)
- `GateRush/AnalogFeed`: 1 (GogglesFeed)
- `Universal Render Pipeline/Terrain/Lit`: 1 (M_Terrain_Field)
- `Skybox/Cubemap`: 1 (Sky_hilly_terrain_01_puresky)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|
| Art/HDRI/hilly_terrain_01_puresky_4k.hdr | 2048×2048 | Cube BC6H | 12 | 32.0 |
| Art/Materials/Ground/Grass_Field/Grass_Field_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Ground/Grass_Field/Grass_Field_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Ground/Grass_Mossy/Grass_Mossy_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Ground/Grass_Mossy/Grass_Mossy_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Ground/Dirt_Field/Dirt_Field_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Ground/Dirt_Field/Dirt_Field_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Vegetation/TreeImpostor/TreeImpostor_Albedo.png | 2048×512 | BC7 | 12 | 2.7 |
| Art/Models/plastic_monobloc_chair_01/textures/plastic_monobloc_chair_01_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/plastic_monobloc_chair_01/textures/plastic_monobloc_chair_01_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Concrete/Concrete_Blocks/Concrete_Blocks_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Concrete/Concrete_Blocks/Concrete_Blocks_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Concrete/Concrete_Blocks/Concrete_Blocks_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Brushed/Steel_Brushed_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Brushed/Steel_Brushed_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Brushed/Steel_Brushed_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Canvas/Fabric_Canvas_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Canvas/Fabric_Canvas_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Canvas/Fabric_Canvas_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/metal_toolbox/textures/metal_toolbox_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/metal_toolbox/textures/metal_toolbox_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fence/Fence_Chainlink/Fence_Chainlink_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fence/Fence_Chainlink/Fence_Chainlink_Albedo.png | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fence/Fence_Chainlink/Fence_Chainlink_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/plastic_crate_02/textures/plastic_crate_02_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/plastic_crate_02/textures/plastic_crate_02_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/painted_wooden_bench/textures/painted_wooden_bench_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/painted_wooden_bench/textures/painted_wooden_bench_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Rubber/Rubber_Mat/Rubber_Mat_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Rubber/Rubber_Mat/Rubber_Mat_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Rubber/Rubber_Mat/Rubber_Mat_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Terrain/TL_Grass_Lawn_Mask.png | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Terrain/TL_Grass_Worn_Mask.png | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Terrain/TL_Dirt_Field_Mask.png | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Terrain/TL_Chalk_Mask.png | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Procedural/Macro/Macro_Noise_Mask.png | 512×512 | BC7 | 10 | 0.7 |
| Art/Materials/Procedural/CarbonFiber/CarbonFiber_Normal.png | 512×512 | BC5 | 10 | 0.7 |
| Art/Materials/Procedural/CarbonFiber/CarbonFiber_Albedo.png | 512×512 | BC7 | 10 | 0.7 |
| Art/Materials/Procedural/CarbonFiber/CarbonFiber_ORM.png | 512×512 | BC7 | 10 | 0.7 |
| Art/Materials/Procedural/Chalk/Chalk_Albedo.png | 256×256 | BC7 | 9 | 0.2 |
| Art/Materials/Procedural/Chalk/Chalk_Normal.png | 256×256 | BC5 | 9 | 0.2 |
| Art/Materials/Procedural/GrassBlades/GrassBlades_Albedo.png | 256×256 | BC7 | 9 | 0.2 |
| Art/Materials/Procedural/Lipo/Lipo_Albedo.png | 256×128 | BC7 | 9 | 0.1 |
| Scenes/Track_Field/Lightmap-0_comp_light.exr | 128×128 | BC6H | 8 | 0.0 |
| Scenes/Track_Field/Lightmap-1_comp_light.exr | 64×64 | BC6H | 7 | 0.0 |

**Lights**

- Sun: Directional, Mixed, intensity 3.20, colour #FFCD7E, shadows Soft

## Track_Hangar

265 renderers (130 batching-static), ~285 draw calls, 148 shadow-caster draws, 128,244 triangles, 73 unique meshes (6.7 MB).

**Materials by shader**

- `GateRush/Env Lit`: 37 (M_Aluminium_Brushed, M_Barrel_01_explosive, M_barrel_03, M_CarbonFiber, M_Concrete_Blocks, M_Concrete_HangarFloor_HangarGround, M_Fabric_GateBlue, M_Fabric_GateOrange, M_Fabric_GateRed, M_Fabric_Sandbag, M_Glass_Night, M_Lamp_Emitter, M_Lipo, M_Metal_Anodized, …)
- `GateRush/LED`: 2 (M_LED_Gate, M_LED_Red)
- `Universal Render Pipeline/Unlit`: 2 (Ghost, PropBlur)
- `GateRush/AnalogFeed`: 1 (GogglesFeed)
- `Skybox/Cubemap`: 1 (Sky_qwantani_night_puresky)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|
| Art/Materials/Metal/Steel_Corrugated/Steel_Corrugated_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Metal/Steel_Corrugated/Steel_Corrugated_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Metal/Steel_Corrugated/Steel_Corrugated_ORM.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Metal/Metal_ShutterPainted/Metal_ShutterPainted_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Metal/Metal_ShutterPainted/Metal_ShutterPainted_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Metal/Metal_ShutterPainted/Metal_ShutterPainted_ORM.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Concrete/Concrete_GarageFloor/Concrete_GarageFloor_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Concrete/Concrete_GarageFloor/Concrete_GarageFloor_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Concrete/Concrete_GarageFloor/Concrete_GarageFloor_ORM.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/HDRI/qwantani_night_puresky_1k.hdr | 512×512 | Cube BC6H | 10 | 4.0 |
| Art/Models/plastic_monobloc_chair_01/textures/plastic_monobloc_chair_01_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/plastic_monobloc_chair_01/textures/plastic_monobloc_chair_01_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/WetFloorSign_01/textures/WetFloorSign_01_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/WetFloorSign_01/textures/WetFloorSign_01_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/barrel_03/textures/barrel_03_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/barrel_03/textures/barrel_03_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/metal_trash_can/textures/metal_trash_can_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/metal_trash_can/textures/metal_trash_can_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/metal_toolbox/textures/metal_toolbox_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/metal_toolbox/textures/metal_toolbox_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/metal_trash_can/textures/metal_trash_can_rust_nor_gl_1k.png | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/metal_trash_can/textures/metal_trash_can_rust_diff_1k.png | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/metal_tool_chest/textures/metal_tool_chest_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/metal_tool_chest/textures/metal_tool_chest_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/wooden_crate_02/textures/wooden_crate_02_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/wooden_crate_02/textures/wooden_crate_02_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Plate/Steel_Plate_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Plate/Steel_Plate_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Plate/Steel_Plate_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/Barrel_01/textures/Barrel_01_explosive_nor_gl_1k.png | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/Barrel_01/textures/Barrel_01_explosive_diff_1k.png | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/wooden_ladder/textures/wooden_ladder_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/wooden_ladder/textures/wooden_ladder_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Wood/Wood_Planks/Wood_Planks_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Wood/Wood_Planks/Wood_Planks_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Wood/Wood_Planks/Wood_Planks_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Concrete/Concrete_Blocks/Concrete_Blocks_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Concrete/Concrete_Blocks/Concrete_Blocks_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Concrete/Concrete_Blocks/Concrete_Blocks_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/plastic_crate_02/textures/plastic_crate_02_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/plastic_crate_02/textures/plastic_crate_02_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Rubber/Rubber_Mat/Rubber_Mat_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Rubber/Rubber_Mat/Rubber_Mat_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Rubber/Rubber_Mat/Rubber_Mat_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/utility_box_01/textures/utility_box_01_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/utility_box_01/textures/utility_box_01_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Procedural/Macro/Macro_Noise_Mask.png | 512×512 | BC7 | 10 | 0.7 |
| Art/Materials/Procedural/CarbonFiber/CarbonFiber_Normal.png | 512×512 | BC5 | 10 | 0.7 |
| Art/Materials/Procedural/CarbonFiber/CarbonFiber_Albedo.png | 512×512 | BC7 | 10 | 0.7 |
| Art/Materials/Procedural/CarbonFiber/CarbonFiber_ORM.png | 512×512 | BC7 | 10 | 0.7 |
| Scenes/Track_Hangar/Lightmap-0_comp_light.exr | 512×512 | BC6H | 10 | 0.7 |
| Scenes/Track_Hangar/Lightmap-1_comp_light.exr | 512×512 | BC6H | 10 | 0.7 |
| Scenes/Track_Hangar/Lightmap-2_comp_light.exr | 512×512 | BC6H | 10 | 0.7 |
| Art/Materials/Procedural/SafetyStripes/SafetyStripes_Albedo.png | 256×256 | BC7 | 9 | 0.2 |
| Art/Materials/Procedural/Lipo/Lipo_Albedo.png | 256×128 | BC7 | 9 | 0.1 |

**Lights**

- Lamp -3 N light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp 1 S light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows Soft
- Lamp 0 S light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows Soft
- Lamp -3 S light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Sun: Directional, Mixed, intensity 2.00, colour #DCFFFC, shadows Soft
- Lamp -2 N light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp 2 N light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp -2 S light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows Soft
- Lamp 2 S light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp -4 N light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp -4 S light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp 3 S light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp 4 S light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp -1 S light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows Soft
- Lamp -1 N light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp 3 N light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp 1 N light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp 0 N light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None
- Lamp 4 N light: Spot, Mixed, intensity 260.00, colour #FFF2E0, shadows None

## Track_Industrial

390 renderers (264 batching-static), ~406 draw calls, 285 shadow-caster draws, 84,145 triangles, 133 unique meshes (3.2 MB).

**Materials by shader**

- `GateRush/Env Lit`: 35 (M_Aluminium_Brushed, M_Asphalt_Old, M_Barrel_01_explosive, M_barrel_03, M_Brick_Factory, M_CarbonFiber, M_Concrete_Wall, M_Container_Blue, M_Container_Green, M_Container_Red, M_Container_Yellow, M_Crane_Yellow, M_Fabric_GateBlue, M_Fabric_GateOrange, …)
- `GateRush/LED`: 2 (M_LED_Gate, M_LED_Red)
- `Universal Render Pipeline/Unlit`: 2 (Ghost, PropBlur)
- `GateRush/AnalogFeed`: 1 (GogglesFeed)
- `Skybox/Cubemap`: 1 (Sky_kloofendal_overcast_puresky)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|
| Art/HDRI/kloofendal_overcast_puresky_4k.hdr | 2048×2048 | Cube BC6H | 12 | 32.0 |
| Art/Materials/Concrete/Concrete_Wall/Concrete_Wall_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Concrete/Concrete_Wall/Concrete_Wall_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Concrete/Concrete_Wall/Concrete_Wall_ORM.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Metal/Steel_Corrugated/Steel_Corrugated_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Metal/Steel_Corrugated/Steel_Corrugated_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Metal/Steel_Corrugated/Steel_Corrugated_ORM.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Ground/Asphalt_Old/Asphalt_Old_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Ground/Asphalt_Old/Asphalt_Old_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Ground/Asphalt_Old/Asphalt_Old_ORM.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Brick/Brick_Factory/Brick_Factory_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Brick/Brick_Factory/Brick_Factory_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Brick/Brick_Factory/Brick_Factory_ORM.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Metal/Iron_CorrugatedRusty/Iron_CorrugatedRusty_Normal.jpg | 2048×2048 | BC5 | 12 | 10.7 |
| Art/Materials/Metal/Iron_CorrugatedRusty/Iron_CorrugatedRusty_Albedo.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Metal/Iron_CorrugatedRusty/Iron_CorrugatedRusty_ORM.jpg | 2048×2048 | BC7 | 12 | 10.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Aluminium_Brushed/Aluminium_Brushed_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/old_tyre/textures/old_tyre_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/old_tyre/textures/old_tyre_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Plate/Steel_Plate_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Plate/Steel_Plate_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Plate/Steel_Plate_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Linen/Fabric_Linen_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Fabric/Fabric_Woven/Fabric_Woven_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Plastic/Plastic_Matte/Plastic_Matte_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/barrel_03/textures/barrel_03_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/barrel_03/textures/barrel_03_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Metal_ShutterRusty/Metal_ShutterRusty_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Metal/Metal_ShutterRusty/Metal_ShutterRusty_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Metal_ShutterRusty/Metal_ShutterRusty_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Wood/Wood_Planks/Wood_Planks_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Wood/Wood_Planks/Wood_Planks_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Wood/Wood_Planks/Wood_Planks_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/metal_trash_can/textures/metal_trash_can_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/metal_trash_can/textures/metal_trash_can_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/metal_trash_can/textures/metal_trash_can_rust_nor_gl_1k.png | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/metal_trash_can/textures/metal_trash_can_rust_diff_1k.png | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/Barrel_01/textures/Barrel_01_explosive_nor_gl_1k.png | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/Barrel_01/textures/Barrel_01_explosive_diff_1k.png | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/security_light/textures/security_light_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/security_light/textures/security_light_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Brushed/Steel_Brushed_Normal.jpg | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Brushed/Steel_Brushed_Albedo.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Metal/Steel_Brushed/Steel_Brushed_ORM.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Models/utility_box_01/textures/utility_box_01_nor_gl_1k.exr | 1024×1024 | BC5 | 11 | 2.7 |
| Art/Models/utility_box_01/textures/utility_box_01_diff_1k.jpg | 1024×1024 | BC7 | 11 | 2.7 |
| Art/Materials/Procedural/DecalPuddle/DecalPuddle_Albedo.png | 512×512 | BC7 | 10 | 0.7 |
| Art/Materials/Procedural/Macro/Macro_Noise_Mask.png | 512×512 | BC7 | 10 | 0.7 |
| Art/Materials/Procedural/CarbonFiber/CarbonFiber_Normal.png | 512×512 | BC5 | 10 | 0.7 |
| Art/Materials/Procedural/CarbonFiber/CarbonFiber_Albedo.png | 512×512 | BC7 | 10 | 0.7 |
| Art/Materials/Procedural/CarbonFiber/CarbonFiber_ORM.png | 512×512 | BC7 | 10 | 0.7 |
| Art/Materials/Procedural/Water/Water_Normal.png | 512×512 | BC5 | 10 | 0.7 |
| Scenes/Track_Industrial/Lightmap-0_comp_light.exr | 256×256 | BC6H | 9 | 0.2 |
| Scenes/Track_Industrial/Lightmap-1_comp_light.exr | 256×256 | BC6H | 9 | 0.2 |
| Scenes/Track_Industrial/Lightmap-2_comp_light.exr | 256×256 | BC6H | 9 | 0.2 |
| Art/Materials/Procedural/Lipo/Lipo_Albedo.png | 256×128 | BC7 | 9 | 0.1 |

**Lights**

- Sun: Directional, Mixed, intensity 1.10, colour #F2E6D8, shadows Soft

## InputDiagnostics

0 renderers (0 batching-static), ~0 draw calls, 0 shadow-caster draws, 0 triangles, 0 unique meshes (0.0 MB).

**Materials by shader**

- `Skybox/Procedural`: 1 (Default-Skybox)

**Textures**

| Texture | Size | Format | Mips | MB |
|---|---|---|---|---|

**Lights**

- Light: Directional, Realtime, intensity 1.00, colour #FFFFFF, shadows None

## Materials under Assets/_Project

| Material | Shader | Textures |
|---|---|---|
| Art/HDRI/Sky_hilly_terrain_01_puresky.mat | `Skybox/Cubemap` | _Tex=hilly_terrain_01_puresky_4k |
| Art/HDRI/Sky_kloofendal_overcast_puresky.mat | `Skybox/Cubemap` | _Tex=kloofendal_overcast_puresky_4k |
| Art/HDRI/Sky_qwantani_dusk_2_puresky.mat | `Skybox/Cubemap` | _Tex=qwantani_dusk_2_puresky_2k |
| Art/HDRI/Sky_qwantani_night_puresky.mat | `Skybox/Cubemap` | _Tex=qwantani_night_puresky_1k |
| Art/Materials/Accent.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Barrel.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Brick/Brick_Factory/M_Brick_Factory.mat | `GateRush/Env Lit` | _BumpMap=Brick_Factory_Normal, _BaseMap=Brick_Factory_Albedo, _MetallicGlossMap=Brick_Factory_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Concrete/Concrete_Blocks/M_Concrete_Blocks.mat | `GateRush/Env Lit` | _BumpMap=Concrete_Blocks_Normal, _BaseMap=Concrete_Blocks_Albedo, _MetallicGlossMap=Concrete_Blocks_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Concrete/Concrete_Damaged/M_Concrete_Damaged.mat | `GateRush/Env Lit` | _BumpMap=Concrete_Damaged_Normal, _BaseMap=Concrete_Damaged_Albedo, _MetallicGlossMap=Concrete_Damaged_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Concrete/Concrete_Damaged/M_Concrete_Pillar.mat | `GateRush/Env Lit` | _BumpMap=Concrete_Damaged_Normal, _BaseMap=Concrete_Damaged_Albedo, _MetallicGlossMap=Concrete_Damaged_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Concrete/Concrete_GarageFloor/M_Concrete_HangarFloor.mat | `GateRush/Env Lit` | _BumpMap=Concrete_GarageFloor_Normal, _BaseMap=Concrete_GarageFloor_Albedo, _MetallicGlossMap=Concrete_GarageFloor_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Concrete/Concrete_PaintedFloor/M_Concrete_PaintedFloor.mat | `GateRush/Env Lit` | _BumpMap=Concrete_PaintedFloor_Normal, _BaseMap=Concrete_PaintedFloor_Albedo, _MetallicGlossMap=Concrete_PaintedFloor_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Concrete/Concrete_Wall/M_Concrete_Pillar.mat | `GateRush/Env Lit` | _BumpMap=Concrete_Wall_Normal, _BaseMap=Concrete_Wall_Albedo, _MetallicGlossMap=Concrete_Wall_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Concrete/Concrete_Wall/M_Concrete_Wall.mat | `GateRush/Env Lit` | _BumpMap=Concrete_Wall_Normal, _BaseMap=Concrete_Wall_Albedo, _MetallicGlossMap=Concrete_Wall_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/ContainerBlue.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/ContainerGreen.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/ContainerRed.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Crane.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Crate.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/EditorMarker.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/EditorPanel.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/EditorPointer.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/Fabric/Fabric_Canvas/M_Fabric_Canvas.mat | `GateRush/Env Lit` | _BumpMap=Fabric_Canvas_Normal, _BaseMap=Fabric_Canvas_Albedo, _MetallicGlossMap=Fabric_Canvas_ORM |
| Art/Materials/Fabric/Fabric_Canvas/M_Fabric_CanvasRed.mat | `GateRush/Env Lit` | _BumpMap=Fabric_Canvas_Normal, _BaseMap=Fabric_Canvas_Albedo, _MetallicGlossMap=Fabric_Canvas_ORM |
| Art/Materials/Fabric/Fabric_Linen/M_Fabric_Sandbag.mat | `GateRush/Env Lit` | _BumpMap=Fabric_Linen_Normal, _BaseMap=Fabric_Linen_Albedo, _MetallicGlossMap=Fabric_Linen_ORM |
| Art/Materials/Fabric/Fabric_Woven/M_Fabric_Black.mat | `GateRush/Env Lit` | _BumpMap=Fabric_Woven_Normal, _BaseMap=Fabric_Woven_Albedo, _MetallicGlossMap=Fabric_Woven_ORM |
| Art/Materials/Fabric/Fabric_Woven/M_Fabric_GateBlue.mat | `GateRush/Env Lit` | _BumpMap=Fabric_Woven_Normal, _BaseMap=Fabric_Woven_Albedo, _MetallicGlossMap=Fabric_Woven_ORM |
| Art/Materials/Fabric/Fabric_Woven/M_Fabric_GateOrange.mat | `GateRush/Env Lit` | _BumpMap=Fabric_Woven_Normal, _BaseMap=Fabric_Woven_Albedo, _MetallicGlossMap=Fabric_Woven_ORM |
| Art/Materials/Fabric/Fabric_Woven/M_Fabric_GateRed.mat | `GateRush/Env Lit` | _BumpMap=Fabric_Woven_Normal, _BaseMap=Fabric_Woven_Albedo, _MetallicGlossMap=Fabric_Woven_ORM |
| Art/Materials/Fence/Fence_Chainlink/M_Fence_Chainlink.mat | `GateRush/Env Lit` | _BumpMap=Fence_Chainlink_Normal, _BaseMap=Fence_Chainlink_Albedo, _MetallicGlossMap=Fence_Chainlink_ORM |
| Art/Materials/FieldPillar.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Frame.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/GateFrame.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Ghost.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/GogglesFeed.mat | `GateRush/AnalogFeed` |  |
| Art/Materials/GogglesScreen.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/Ground.mat | `Universal Render Pipeline/Lit` | _MainTex=Grid, _BaseMap=Grid |
| Art/Materials/Ground/Asphalt_Old/M_Asphalt_Old.mat | `GateRush/Env Lit` | _BumpMap=Asphalt_Old_Normal, _BaseMap=Asphalt_Old_Albedo, _MetallicGlossMap=Asphalt_Old_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Ground/Asphalt_PitLane/M_Asphalt_PitLane.mat | `GateRush/Env Lit` | _BumpMap=Asphalt_PitLane_Normal, _BaseMap=Asphalt_PitLane_Albedo, _MetallicGlossMap=Asphalt_PitLane_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Ground/Dirt_Field/M_Dirt_Field.mat | `GateRush/Env Lit` | _BumpMap=Dirt_Field_Normal, _BaseMap=Dirt_Field_Albedo, _MetallicGlossMap=Dirt_Field_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Ground/Grass_Field/M_Grass_Lawn.mat | `GateRush/Env Lit` | _BumpMap=Grass_Field_Normal, _BaseMap=Grass_Field_Albedo, _MetallicGlossMap=Grass_Field_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Ground/Grass_Worn/M_Grass_Worn.mat | `GateRush/Env Lit` | _BumpMap=Grass_Worn_Normal, _BaseMap=Grass_Worn_Albedo, _MetallicGlossMap=Grass_Worn_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Ground/Gravel/M_Gravel.mat | `GateRush/Env Lit` | _BumpMap=Gravel_Normal, _BaseMap=Gravel_Albedo, _MetallicGlossMap=Gravel_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
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
| Art/Materials/Library/M_CarbonFiber.mat | `GateRush/Env Lit` | _BumpMap=CarbonFiber_Normal, _BaseMap=CarbonFiber_Albedo, _MetallicGlossMap=CarbonFiber_ORM |
| Art/Materials/Library/M_Chalk.mat | `GateRush/Env Lit` | _BaseMap=Chalk_Albedo |
| Art/Materials/Library/M_Checker.mat | `GateRush/Env Lit` | _BaseMap=Checker_Albedo |
| Art/Materials/Library/M_Copper.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Decal_Crack.mat | `Shader Graphs/Decal` | Base_Map=DecalCrack_Albedo, Normal_Map=DecalCrack_Normal |
| Art/Materials/Library/M_Decal_Oil.mat | `Shader Graphs/Decal` | Base_Map=DecalOil_Albedo |
| Art/Materials/Library/M_Decal_Tyre.mat | `Shader Graphs/Decal` | Base_Map=DecalTyre_Albedo |
| Art/Materials/Library/M_Glass_Dirty.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Glass_Night.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Lamp_Emitter.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Lamp_Warm.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_LED_Blue.mat | `GateRush/LED` |  |
| Art/Materials/Library/M_LED_Gate.mat | `GateRush/LED` |  |
| Art/Materials/Library/M_LED_Red.mat | `GateRush/LED` |  |
| Art/Materials/Library/M_Lipo.mat | `GateRush/Env Lit` | _BaseMap=Lipo_Albedo |
| Art/Materials/Library/M_Metal_Anodized.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Metal_MotorBell.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Paint_White.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Paint_Yellow.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Plastic_ConeOrange.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Plastic_Grey.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Plastic_White.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Prop_Front.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Prop_Polycarbonate.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Prop_Rear.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_Puddle.mat | `GateRush/Env Lit` | _BaseMap=DecalPuddle_Albedo |
| Art/Materials/Library/M_Reflective_White.mat | `GateRush/Env Lit` |  |
| Art/Materials/Library/M_SafetyStripes.mat | `GateRush/Env Lit` | _BaseMap=SafetyStripes_Albedo |
| Art/Materials/Library/M_Water.mat | `GateRush/Env Lit` | _BumpMap=Water_Normal |
| Art/Materials/Library/Variants/M_Concrete_HangarFloor_HangarGround.mat | `GateRush/Env Lit` | _BumpMap=Concrete_GarageFloor_Normal, _BaseMap=Concrete_GarageFloor_Albedo, _MetallicGlossMap=Concrete_GarageFloor_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Library/Variants/M_Grass_Lawn_Menu.mat | `GateRush/Env Lit` | _BumpMap=Grass_Field_Normal, _BaseMap=Grass_Field_Albedo, _MetallicGlossMap=Grass_Field_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/MenuFloor.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/MenuRing.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Metal/Aluminium_Brushed/M_Aluminium_Brushed.mat | `GateRush/Env Lit` | _BumpMap=Aluminium_Brushed_Normal, _BaseMap=Aluminium_Brushed_Albedo, _MetallicGlossMap=Aluminium_Brushed_ORM |
| Art/Materials/Metal/Iron_CorrugatedRusty/M_Iron_CorrugatedRusty.mat | `GateRush/Env Lit` | _BumpMap=Iron_CorrugatedRusty_Normal, _BaseMap=Iron_CorrugatedRusty_Albedo, _MetallicGlossMap=Iron_CorrugatedRusty_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Metal_ShutterPainted/M_Metal_ShutterPainted.mat | `GateRush/Env Lit` | _BumpMap=Metal_ShutterPainted_Normal, _BaseMap=Metal_ShutterPainted_Albedo, _MetallicGlossMap=Metal_ShutterPainted_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Metal_ShutterRusty/M_Metal_ShutterRusty.mat | `GateRush/Env Lit` | _BumpMap=Metal_ShutterRusty_Normal, _BaseMap=Metal_ShutterRusty_Albedo, _MetallicGlossMap=Metal_ShutterRusty_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_Brushed/M_Steel_Brushed.mat | `GateRush/Env Lit` | _BumpMap=Steel_Brushed_Normal, _BaseMap=Steel_Brushed_Albedo, _MetallicGlossMap=Steel_Brushed_ORM |
| Art/Materials/Metal/Steel_Corrugated/M_Container_Blue.mat | `GateRush/Env Lit` | _BumpMap=Steel_Corrugated_Normal, _BaseMap=Steel_Corrugated_Albedo, _MetallicGlossMap=Steel_Corrugated_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_Corrugated/M_Container_Green.mat | `GateRush/Env Lit` | _BumpMap=Steel_Corrugated_Normal, _BaseMap=Steel_Corrugated_Albedo, _MetallicGlossMap=Steel_Corrugated_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_Corrugated/M_Container_Red.mat | `GateRush/Env Lit` | _BumpMap=Steel_Corrugated_Normal, _BaseMap=Steel_Corrugated_Albedo, _MetallicGlossMap=Steel_Corrugated_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_Corrugated/M_Container_Yellow.mat | `GateRush/Env Lit` | _BumpMap=Steel_Corrugated_Normal, _BaseMap=Steel_Corrugated_Albedo, _MetallicGlossMap=Steel_Corrugated_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_Corrugated/M_Steel_Corrugated.mat | `GateRush/Env Lit` | _BumpMap=Steel_Corrugated_Normal, _BaseMap=Steel_Corrugated_Albedo, _MetallicGlossMap=Steel_Corrugated_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_Corrugated/M_Steel_CorrugatedDark.mat | `GateRush/Env Lit` | _BumpMap=Steel_Corrugated_Normal, _BaseMap=Steel_Corrugated_Albedo, _MetallicGlossMap=Steel_Corrugated_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_CorrugatedRusty/M_Steel_CorrugatedRusty.mat | `GateRush/Env Lit` | _BumpMap=Steel_CorrugatedRusty_Normal, _BaseMap=Steel_CorrugatedRusty_Albedo, _MetallicGlossMap=Steel_CorrugatedRusty_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_PaintedGreen/M_Steel_PaintedGreen.mat | `GateRush/Env Lit` | _BumpMap=Steel_PaintedGreen_Normal, _BaseMap=Steel_PaintedGreen_Albedo, _MetallicGlossMap=Steel_PaintedGreen_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_PaintedRed/M_Steel_PaintedRed.mat | `GateRush/Env Lit` | _BumpMap=Steel_PaintedRed_Normal, _BaseMap=Steel_PaintedRed_Albedo, _MetallicGlossMap=Steel_PaintedRed_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_PaintedYellow/M_Steel_PaintedYellow.mat | `GateRush/Env Lit` | _BumpMap=Steel_PaintedYellow_Normal, _BaseMap=Steel_PaintedYellow_Albedo, _MetallicGlossMap=Steel_PaintedYellow_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_Plate/M_Crane_Yellow.mat | `GateRush/Env Lit` | _BumpMap=Steel_Plate_Normal, _BaseMap=Steel_Plate_Albedo, _MetallicGlossMap=Steel_Plate_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_Plate/M_Steel_Plate.mat | `GateRush/Env Lit` | _BumpMap=Steel_Plate_Normal, _BaseMap=Steel_Plate_Albedo, _MetallicGlossMap=Steel_Plate_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_Plate/M_Steel_Truss.mat | `GateRush/Env Lit` | _BumpMap=Steel_Plate_Normal, _BaseMap=Steel_Plate_Albedo, _MetallicGlossMap=Steel_Plate_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Metal/Steel_Rusty/M_Steel_Rusty.mat | `GateRush/Env Lit` | _BumpMap=Steel_Rusty_Normal, _BaseMap=Steel_Rusty_Albedo, _MetallicGlossMap=Steel_Rusty_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Pillar.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Plastic/Plastic_Matte/M_Plastic_Black.mat | `GateRush/Env Lit` | _BumpMap=Plastic_Matte_Normal, _BaseMap=Plastic_Matte_Albedo, _MetallicGlossMap=Plastic_Matte_ORM |
| Art/Materials/PointerLine.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/Pole.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/PoleLamp.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/PropBlur.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/PropFront.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/PropRear.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Quay.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Rubber/Rubber_Black/M_Rubber_Black.mat | `GateRush/Env Lit` | _BumpMap=Rubber_Black_Normal, _BaseMap=Rubber_Black_Albedo, _MetallicGlossMap=Rubber_Black_ORM |
| Art/Materials/Rubber/Rubber_Mat/M_Rubber_Mat.mat | `GateRush/Env Lit` | _BumpMap=Rubber_Mat_Normal, _BaseMap=Rubber_Mat_Albedo, _MetallicGlossMap=Rubber_Mat_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Sparks.mat | `Universal Render Pipeline/Particles/Unlit` |  |
| Art/Materials/TreeLine.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Vegetation/TreeImpostor/M_TreeImpostor.mat | `GateRush/Env Lit` | _BaseMap=TreeImpostor_Albedo |
| Art/Materials/Vignette.mat | `Universal Render Pipeline/Unlit` |  |
| Art/Materials/Wall.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Warehouse.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/WarehouseRoof.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Water.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/Wood/Wood_Planks/M_Wood_Planks.mat | `GateRush/Env Lit` | _BumpMap=Wood_Planks_Normal, _BaseMap=Wood_Planks_Albedo, _MetallicGlossMap=Wood_Planks_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Wood/Wood_Plywood/M_Wood_Plywood.mat | `GateRush/Env Lit` | _BumpMap=Wood_Plywood_Normal, _BaseMap=Wood_Plywood_Albedo, _MetallicGlossMap=Wood_Plywood_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/Wood/Wood_Tabletop/M_Wood_Tabletop.mat | `GateRush/Env Lit` | _BumpMap=Wood_Tabletop_Normal, _BaseMap=Wood_Tabletop_Albedo, _MetallicGlossMap=Wood_Tabletop_ORM, _DetailAlbedoMap=Macro_Noise_Mask |
| Art/Materials/YardContainer0.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/YardContainer1.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/YardContainer2.mat | `Universal Render Pipeline/Lit` |  |
| Art/Materials/YardContainer3.mat | `Universal Render Pipeline/Lit` |  |
| Art/Models/Barrel_01/Barrel_01_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=Barrel_01_explosive_diff_1k, _BumpMap=Barrel_01_explosive_nor_gl_1k, _BaseMap=Barrel_01_explosive_diff_1k |
| Art/Models/Barrel_01/M_Barrel_01_explosive.mat | `GateRush/Env Lit` | _BumpMap=Barrel_01_explosive_nor_gl_1k, _BaseMap=Barrel_01_explosive_diff_1k |
| Art/Models/barrel_03/barrel_03_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=barrel_03_diff_1k, _BumpMap=barrel_03_nor_gl_1k, _BaseMap=barrel_03_diff_1k |
| Art/Models/barrel_03/M_barrel_03.mat | `GateRush/Env Lit` | _BumpMap=barrel_03_nor_gl_1k, _BaseMap=barrel_03_diff_1k |
| Art/Models/metal_tool_chest/M_metal_tool_chest.mat | `GateRush/Env Lit` | _BumpMap=metal_tool_chest_nor_gl_1k, _BaseMap=metal_tool_chest_diff_1k |
| Art/Models/metal_tool_chest/metal_tool_chest_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=metal_tool_chest_diff_1k, _BumpMap=metal_tool_chest_nor_gl_1k, _BaseMap=metal_tool_chest_diff_1k |
| Art/Models/metal_toolbox/M_metal_toolbox.mat | `GateRush/Env Lit` | _BumpMap=metal_toolbox_nor_gl_1k, _BaseMap=metal_toolbox_diff_1k |
| Art/Models/metal_toolbox/metal_toolbox_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=metal_toolbox_diff_1k, _BumpMap=metal_toolbox_nor_gl_1k, _BaseMap=metal_toolbox_diff_1k |
| Art/Models/metal_trash_can/M_metal_trash_can.mat | `GateRush/Env Lit` | _BumpMap=metal_trash_can_nor_gl_1k, _BaseMap=metal_trash_can_diff_1k |
| Art/Models/metal_trash_can/M_metal_trash_can_rust.mat | `GateRush/Env Lit` | _BumpMap=metal_trash_can_rust_nor_gl_1k, _BaseMap=metal_trash_can_rust_diff_1k |
| Art/Models/metal_trash_can/metal_trash_can_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=metal_trash_can_diff_1k, _BumpMap=metal_trash_can_nor_gl_1k, _BaseMap=metal_trash_can_diff_1k |
| Art/Models/old_tyre/M_old_tyre.mat | `GateRush/Env Lit` | _BumpMap=old_tyre_nor_gl_1k, _BaseMap=old_tyre_diff_1k |
| Art/Models/old_tyre/old_tyre_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=old_tyre_diff_1k, _BumpMap=old_tyre_nor_gl_1k, _BaseMap=old_tyre_diff_1k |
| Art/Models/painted_wooden_bench/M_painted_wooden_bench.mat | `GateRush/Env Lit` | _BumpMap=painted_wooden_bench_nor_gl_1k, _BaseMap=painted_wooden_bench_diff_1k |
| Art/Models/painted_wooden_bench/painted_wooden_bench_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=painted_wooden_bench_diff_1k, _BumpMap=painted_wooden_bench_nor_gl_1k, _BaseMap=painted_wooden_bench_diff_1k |
| Art/Models/plastic_crate_02/M_plastic_crate_02.mat | `GateRush/Env Lit` | _BumpMap=plastic_crate_02_nor_gl_1k, _BaseMap=plastic_crate_02_diff_1k |
| Art/Models/plastic_crate_02/plastic_crate_02_1k.fbx | `Universal Render Pipeline/Lit` | _BumpMap=plastic_crate_02_nor_gl_1k |
| Art/Models/plastic_monobloc_chair_01/M_plastic_monobloc_chair_01.mat | `GateRush/Env Lit` | _BumpMap=plastic_monobloc_chair_01_nor_gl_1k, _BaseMap=plastic_monobloc_chair_01_diff_1k |
| Art/Models/plastic_monobloc_chair_01/plastic_monobloc_chair_01_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=plastic_monobloc_chair_01_diff_1k, _BumpMap=plastic_monobloc_chair_01_nor_gl_1k, _BaseMap=plastic_monobloc_chair_01_diff_1k |
| Art/Models/security_light/M_security_light.mat | `GateRush/Env Lit` | _BumpMap=security_light_nor_gl_1k, _BaseMap=security_light_diff_1k |
| Art/Models/security_light/security_light_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=security_light_diff_1k, _BumpMap=security_light_nor_gl_1k, _BaseMap=security_light_diff_1k |
| Art/Models/street_lamp_02/street_lamp_02_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=street_lamp_02_diff_1k, _BumpMap=street_lamp_02_nor_gl_1k, _BaseMap=street_lamp_02_diff_1k |
| Art/Models/utility_box_01/M_utility_box_01.mat | `GateRush/Env Lit` | _BumpMap=utility_box_01_nor_gl_1k, _BaseMap=utility_box_01_diff_1k |
| Art/Models/utility_box_01/utility_box_01_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=utility_box_01_diff_1k, _BumpMap=utility_box_01_nor_gl_1k, _BaseMap=utility_box_01_diff_1k |
| Art/Models/WetFloorSign_01/M_WetFloorSign_01.mat | `GateRush/Env Lit` | _BumpMap=WetFloorSign_01_nor_gl_1k, _BaseMap=WetFloorSign_01_diff_1k |
| Art/Models/WetFloorSign_01/WetFloorSign_01_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=WetFloorSign_01_diff_1k, _BumpMap=WetFloorSign_01_nor_gl_1k, _BaseMap=WetFloorSign_01_diff_1k |
| Art/Models/wooden_crate_02/M_wooden_crate_02.mat | `GateRush/Env Lit` | _BumpMap=wooden_crate_02_nor_gl_1k, _BaseMap=wooden_crate_02_diff_1k |
| Art/Models/wooden_crate_02/wooden_crate_02_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=wooden_crate_02_diff_1k, _BumpMap=wooden_crate_02_nor_gl_1k, _BaseMap=wooden_crate_02_diff_1k |
| Art/Models/wooden_ladder/M_wooden_ladder.mat | `GateRush/Env Lit` | _BumpMap=wooden_ladder_nor_gl_1k, _BaseMap=wooden_ladder_diff_1k |
| Art/Models/wooden_ladder/wooden_ladder_1k.fbx | `Universal Render Pipeline/Lit` | _MainTex=wooden_ladder_diff_1k, _BumpMap=wooden_ladder_nor_gl_1k, _BaseMap=wooden_ladder_diff_1k |
| Art/Terrain/M_Terrain_Field.mat | `Universal Render Pipeline/Terrain/Lit` |  |
