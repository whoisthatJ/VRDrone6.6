namespace GateRush.EditorTools.Art
{
    /// <summary>Hangar art pass (V5).</summary>
    public static partial class HangarArt
    {
        public static void Apply(ArtContext ctx) => ApplyImpl(ctx);
        static partial void ApplyImpl(ArtContext ctx);
    }

    /// <summary>Industrial art pass (V6).</summary>
    public static partial class IndustrialArt
    {
        public static void Apply(ArtContext ctx) => ApplyImpl(ctx);
        static partial void ApplyImpl(ArtContext ctx);
    }

    /// <summary>Menu / pit tent art pass (V7).</summary>
    public static partial class MenuArt
    {
        public static void Apply(ArtContext ctx) => ApplyImpl(ctx);
        static partial void ApplyImpl(ArtContext ctx);
    }

    /// <summary>Gate, prop and quad prefab art (V7).</summary>
    public static partial class PrefabArt
    {
        public static string Apply()
        {
            string log = "prefabs: not yet";
            ApplyImpl(ref log);
            return log;
        }

        static partial void ApplyImpl(ref string log);
    }
}
