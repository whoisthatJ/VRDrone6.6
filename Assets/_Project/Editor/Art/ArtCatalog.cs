using System.Collections.Generic;

namespace GateRush.EditorTools.Art
{
    public enum ArtProvider { PolyHaven, AmbientCG }
    public enum ArtKind { Texture, Hdri, Model }

    /// <summary>One CC0 source asset. Textures land in Art/Materials/&lt;Category&gt;/&lt;Name&gt;/&lt;Name&gt;_{Albedo,Normal,ORM,Height}.</summary>
    public sealed class ArtSource
    {
        public string Name;
        public string Category;
        public ArtProvider Provider;
        public ArtKind Kind;
        public string Id;
        /// <summary>"1k"/"2k"/"4k" (Poly Haven) or "1K"/"2K" (ambientCG).</summary>
        public string Resolution;
        /// <summary>Real-world size of one texture tile in metres (texel density: 2K over 2 m ≈ 1024 px/m).</summary>
        public float TileMeters = 2f;
        /// <summary>Keep the height map (terrain height blending, parallax).</summary>
        public bool Height;
        /// <summary>Download into the git-ignored cache (source for baked impostors only).</summary>
        public bool CacheOnly;

        public string Url => Provider == ArtProvider.PolyHaven ? $"https://polyhaven.com/a/{Id}" : $"https://ambientcg.com/view?id={Id}";
        public string Folder => Kind switch
        {
            ArtKind.Hdri => "Assets/_Project/Art/HDRI",
            ArtKind.Model => (CacheOnly ? ArtCatalog.CacheRoot : "Assets/_Project/Art/Models") + "/" + Id,
            _ => $"Assets/_Project/Art/Materials/{Category}/{Name}",
        };
    }

    /// <summary>
    /// Every external asset the visual overhaul uses. All are CC0 (Poly Haven, ambientCG); see Art/ASSET_LICENSES.md.
    /// Tile sizes come from the providers' published dimensions (ambientCG ones without dimensions are estimates).
    /// </summary>
    public static class ArtCatalog
    {
        public const string CacheRoot = "Assets/_ArtCache";

        private static ArtSource Ph(string category, string name, string id, string res, float meters, bool height = false) =>
            new ArtSource { Category = category, Name = name, Provider = ArtProvider.PolyHaven, Kind = ArtKind.Texture, Id = id, Resolution = res, TileMeters = meters, Height = height };

        private static ArtSource Acg(string category, string name, string id, string res, float meters, bool height = false) =>
            new ArtSource { Category = category, Name = name, Provider = ArtProvider.AmbientCG, Kind = ArtKind.Texture, Id = id, Resolution = res, TileMeters = meters, Height = height };

        private static ArtSource Model(string id, bool cacheOnly = false) =>
            new ArtSource { Category = "Model", Name = id, Provider = ArtProvider.PolyHaven, Kind = ArtKind.Model, Id = id, Resolution = "1k", CacheOnly = cacheOnly };

        private static ArtSource Hdri(string id, string res) =>
            new ArtSource { Category = "HDRI", Name = id, Provider = ArtProvider.PolyHaven, Kind = ArtKind.Hdri, Id = id, Resolution = res };

        public static readonly List<ArtSource> Textures = new List<ArtSource>
        {
            // Ground and terrain
            Acg("Ground", "Grass_Field", "Grass004", "2K", 2f, height: true),
            Acg("Ground", "Grass_Mossy", "Ground037", "2K", 2f, height: true),
            Ph("Ground", "Grass_Worn", "sparse_grass", "2k", 2f, height: true),
            Ph("Ground", "Dirt_Field", "dirt_floor", "2k", 2.07f, height: true),
            Ph("Ground", "Asphalt_PitLane", "asphalt_pit_lane", "2k", 2f),
            Ph("Ground", "Asphalt_Old", "asphalt_02", "2k", 3f),
            Ph("Ground", "Gravel", "gravel_floor_02", "1k", 2f),
            // Concrete and masonry
            Ph("Concrete", "Concrete_GarageFloor", "garage_floor", "2k", 1.89f),
            Ph("Concrete", "Concrete_PaintedFloor", "painted_concrete_02", "2k", 4f),
            Ph("Concrete", "Concrete_Wall", "concrete_wall_006", "2k", 2f),
            Ph("Concrete", "Concrete_Damaged", "concrete_floor_damaged_01", "2k", 5f),
            Ph("Concrete", "Concrete_Blocks", "concrete_block_wall", "1k", 2f),
            Ph("Brick", "Brick_Factory", "red_bricks_04", "2k", 2.5f),
            // Metal
            Acg("Metal", "Steel_Corrugated", "CorrugatedSteel005", "2K", 2f),
            Acg("Metal", "Steel_CorrugatedRusty", "CorrugatedSteel007B", "2K", 2f),
            Ph("Metal", "Iron_CorrugatedRusty", "rusty_corrugated_iron", "2k", 2f),
            Ph("Metal", "Metal_ShutterRusty", "rusty_metal_shutter", "1k", 1.9f),
            Ph("Metal", "Metal_ShutterPainted", "painted_metal_shutter", "2k", 2f),
            Acg("Metal", "Steel_PaintedYellow", "PaintedMetal009", "1K", 1.5f),
            Acg("Metal", "Steel_PaintedGreen", "PaintedMetal006", "1K", 1.5f),
            Acg("Metal", "Steel_PaintedRed", "PaintedMetal004", "1K", 1.5f),
            Acg("Metal", "Aluminium_Brushed", "Metal009", "1K", 1f),
            Acg("Metal", "Steel_Brushed", "Metal011", "1K", 1f),
            Ph("Metal", "Steel_Rusty", "rusty_metal_04", "1k", 2f),
            Ph("Metal", "Steel_Plate", "metal_plate_02", "1k", 2f),
            // Wood
            Ph("Wood", "Wood_Planks", "wood_planks", "1k", 1.5f),
            Ph("Wood", "Wood_Plywood", "plywood", "1k", 0.5f),
            Ph("Wood", "Wood_Tabletop", "wood_table_001", "1k", 1.5f),
            // Fabric, rubber, plastic, fence
            Acg("Fabric", "Fabric_Woven", "Fabric030", "1K", 0.5f),
            Acg("Fabric", "Fabric_Canvas", "Fabric062", "1K", 0.4f),
            Acg("Fabric", "Fabric_Linen", "Fabric066", "1K", 0.4f),
            Ph("Rubber", "Rubber_Mat", "rubber_tiles", "1k", 2f),
            Acg("Rubber", "Rubber_Black", "Rubber004", "1K", 0.5f),
            Acg("Plastic", "Plastic_Matte", "Plastic006", "1K", 0.5f),
            Acg("Fence", "Fence_Chainlink", "Fence007A", "1K", 1f),
        };

        public static readonly List<ArtSource> Hdris = new List<ArtSource>
        {
            Hdri("hilly_terrain_01_puresky", "4k"),   // Field, golden hour
            Hdri("kloofendal_overcast_puresky", "4k"), // Industrial, overcast
            Hdri("qwantani_dusk_2_puresky", "2k"),     // Menu, dusk
            Hdri("qwantani_night_puresky", "1k"),      // Hangar, night outside
        };

        public static readonly List<ArtSource> Models = new List<ArtSource>
        {
            Model("wooden_crate_02"),
            Model("Barrel_01"),
            Model("barrel_03"),
            Model("old_tyre"),
            Model("painted_wooden_bench"),
            Model("plastic_monobloc_chair_01"),
            Model("metal_toolbox"),
            Model("metal_tool_chest"),
            Model("wooden_ladder"),
            Model("WetFloorSign_01"),
            Model("metal_trash_can"),
            Model("utility_box_01"),
            Model("security_light"),
            Model("street_lamp_02"),
            Model("plastic_crate_02"),
            Model("island_tree_02", cacheOnly: true), // impostor source only, never committed
        };

        public static IEnumerable<ArtSource> All()
        {
            foreach (var s in Textures) yield return s;
            foreach (var s in Hdris) yield return s;
            foreach (var s in Models) yield return s;
        }

        public static ArtSource Find(string name)
        {
            foreach (var s in All())
                if (s.Name == name || s.Id == name) return s;
            return null;
        }
    }
}
