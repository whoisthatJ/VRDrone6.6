using UnityEngine;

namespace Ushqyr.Bench
{
    /// <summary>The venue's floor and wall surfaces and the other materials they may be given (background randomisation).</summary>
    public sealed class BenchVenueLook : MonoBehaviour
    {
        [SerializeField] private Renderer[] floors = new Renderer[0];
        [SerializeField] private Renderer[] walls = new Renderer[0];
        [SerializeField] private Material[] floorMaterials = new Material[0];
        [SerializeField] private Material[] wallMaterials = new Material[0];

        public int FloorChoices => Mathf.Max(1, floorMaterials.Length);
        public int WallChoices => Mathf.Max(1, wallMaterials.Length);

        public void Configure(Renderer[] floorRenderers, Renderer[] wallRenderers, Material[] floorChoices, Material[] wallChoices)
        {
            floors = floorRenderers;
            walls = wallRenderers;
            floorMaterials = floorChoices;
            wallMaterials = wallChoices;
        }

        /// <summary>Index 0 is the venue's own look.</summary>
        public void Apply(int floor, int wall)
        {
            if (floorMaterials.Length > 0)
                foreach (Renderer r in floors) r.sharedMaterial = floorMaterials[Mathf.Clamp(floor, 0, floorMaterials.Length - 1)];
            if (wallMaterials.Length > 0)
                foreach (Renderer r in walls) r.sharedMaterial = wallMaterials[Mathf.Clamp(wall, 0, wallMaterials.Length - 1)];
        }
    }
}
