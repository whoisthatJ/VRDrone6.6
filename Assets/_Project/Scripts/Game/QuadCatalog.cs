using System;
using System.Collections.Generic;
using GateRush.Vehicle;
using UnityEngine;

namespace GateRush.Game
{
    /// <summary>The selectable quad classes and how each one looks and collides.</summary>
    [CreateAssetMenu(menuName = "GateRush/Quad Catalog", fileName = "QuadCatalog")]
    public sealed class QuadCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Id;
            public string DisplayName;
            [TextArea] public string Description;
            public QuadProfileAsset Profile;
            [Tooltip("Scale applied to the quad and ghost visuals (5\" = 1).")]
            public float VisualScale = 1f;
            [Tooltip("Collision sphere radius (m).")]
            public float CollisionRadius = 0.12f;
            [Tooltip("FPV camera position relative to the quad centre (m).")]
            public Vector3 CameraOffset = new Vector3(0f, 0.02f, 0.095f);
            [Tooltip("Motor RPM at full speed, for the motor sound.")]
            public float MotorRpm = 28000f;
        }

        public List<Entry> Quads = new List<Entry>();

        public Entry Find(string id)
        {
            foreach (var q in Quads)
                if (q.Id == id) return q;
            return Quads.Count > 0 ? Quads[0] : null;
        }
    }
}
