# Quad reference (Phase 3, P2)

Dimensions the generated quads use, per class. They are generic real-world construction (no brand copies); the
names on the parts are GateRush's fictional brands. The physics still comes from the garage part data
(`Scripts/Core/Garage/PartCatalog.cs` → `BuildComposer`); the meshes are generated from the same numbers, so what
you see is what flies.

## Classes
| Class | Frame (catalogue) | Wheelbase | Geometry | Arms | Props | Motor (stator, mount) | Stack / mount | FPV camera | Battery, plug | Video |
|---|---|---|---|---|---|---|---|---|---|---|
| 5" race | GRX Race 5 | 220 mm | true-X, low | 5 mm carbon, 13 mm wide | 5.1 × 4.3 × 3 | 2207 (22 × 7 mm), 16 × 16 M3 | 30.5 × 30.5 M3, FC + 4-in-1 ESC | micro 19 mm | 4S 1300 mAh, XT60 | analog VTX + pagoda (HD option) |
| 5" freestyle | Volt Freestyle 5 | 226 mm | stretched X | 6 mm, 16 mm wide | 5 × 4.5 × 3 | 2207 1950 KV, 16 × 16 M3 | 30.5 × 30.5 M3 | micro 19 mm or HD module | 6S 1100 mAh, XT60 | analog or HD air unit |
| 3" toothpick | Needle 3 | 128 mm | true-X, thin | 2 mm, 6 mm wide | 3 × 3 × 3 | 1404 (14 × 4 mm), 9 × 9 M2 | 20 × 20 M2 AIO | nano 14 mm | 4S 650 mAh, XT30 | analog |
| 3.5" cinewhoop | Halo 3.5 | 151 mm | ducted | 3 mm plate + ducts | 3.5 × 2.5 × 5 | 1804 (18 × 4 mm), 12 × 12 M2 | 25.5 × 25.5 M2 | HD camera module | 6S 850 mAh, XT30 | HD air unit |
| 65 mm whoop | Kestrel 65 | 65 mm | ducted, one-piece | moulded frame + canopy | 31 mm × 4 | 0702 (7 × 2 mm), 3-hole 6.6 mm M1.4 | 25.5 whoop AIO | nano 14 mm in the canopy | 1S 300 mAh, BT2.0 | analog AIO |
| 7" long range | Atlas 7 | 315 mm | stretched X | 5 mm, 16 mm wide | 7 × 3.5 × 3 | 2806.5, 16 × 16 M3 | 30.5 × 30.5 M3 | micro 19 mm or HD module | 6S 3000 mAh Li-ion, XT60 | analog or HD air unit + 2 antennas |

## Components (all classes)
| Part | Real size used |
|---|---|
| Stack boards | 36 × 36 mm (30.5 mount), 30 × 30 (25.5), 25.5 × 25.5 (20), 1.6 mm PCB, soft-mount grommets, 8 mm board spacing |
| Standoffs | aluminium, round 5 mm (M3) / 4 mm (M2), stack height of the frame (14–34 mm) |
| FPV camera | micro 19 × 19 mm body, nano 14 × 14 mm, HD module 20 × 20 × 22 mm; 12 mm lens barrel; side plates with screws; tilted 25–35° |
| Analog VTX | 20 × 20 board on the stack, pagoda / stubby antenna 18 mm on a TPU mount at the back, angled 45° up |
| HD air unit | 30 × 30 × 12 mm box with two dipole antennas (T), at the back |
| Receiver | 12 × 18 mm board, T antenna (two 30 mm wires) zip-tied to the rear standoffs |
| Capacitor | low-ESR 8 × 16 mm (35 V) on the battery leads |
| Battery plug | XT60 16 × 8 × 16 mm, XT30 10 × 5 × 12 mm, BT2.0 7 × 3 × 10 mm |
| Motor wires | three 20–22 AWG along each arm, black heat-shrink |
| Strap + grip pad | 15–20 mm wide strap, rubber pad under the pack |
| LiPo sizes | 1S 300: 57 × 12 × 6 mm; 4S 650: 57 × 30 × 25; 4S 1300: 72 × 35 × 33; 6S 1100: 75 × 35 × 40; 6S 3000 Li-ion: 2 × 3 18650 cells, 70 × 55 × 37 |

Motor mounting patterns: 16 × 16 (22xx–28xx), 12 × 12 (18xx), 9 × 9 (14xx), 6.6 mm 3-hole (07xx). Prop sizes as in the catalogue.
