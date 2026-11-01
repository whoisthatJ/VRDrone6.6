using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;

namespace GateRush.EditorTools.Art
{
    /// <summary>
    /// Build-time shader stripping for the Windows player. URP's terrain only uses its add pass for terrains with more
    /// than four layers; the Field terrain has exactly four (EnvironmentAssets.FieldLayers), so every add-pass variant
    /// is dead weight. Compiling them alone took ~40 minutes of a first Windows build on a 4-core laptop.
    /// If a terrain ever gets a fifth layer, this stripper turns itself off.
    /// </summary>
    public sealed class ArtShaderStripping : IPreprocessShaders
    {
        public const string TerrainAddPass = "Hidden/Universal Render Pipeline/Terrain/Lit (Add Pass)";

        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader.name != TerrainAddPass) return;
            if (EnvironmentAssets.FieldLayers.Count > 4) return;
            data.Clear();
        }
    }
}
