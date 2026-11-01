using GateRush.EditorTools.Builders;
using UnityEditor;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Tools > Art > Rebuild Everything: GateRush's builders regenerate every scene and prefab from code (dropping
    /// the art), so this runs Build All Scenes, then the art pass, then the lighting bake, in that order. Use it
    /// after changing a builder; use Apply Art Pass + Bake Lighting after changing only art.
    /// </summary>
    public static class ArtRebuild
    {
        [MenuItem("Tools/Art/Rebuild Everything (scenes, art, lighting)")]
        private static void Menu() => RebuildDeferred();

        public static string RebuildDeferred() => ArtJobs.RunDeferred("rebuild", () =>
        {
            string built = BootSceneBuilder.BuildAll();
            ArtJobs.ReturnToBoot();
            string art = ArtPass.ApplyAll();
            EditorApplication.delayCall += () => ArtBake.BakeDeferred("Field,Hangar,Industrial,Menu");
            return built + "\n" + art + "\nlighting bake queued (see this file when it finishes)";
        });
    }
}
