using Ushqyr.Flight;
using UnityEngine;

namespace Ushqyr.Vehicle
{
    [CreateAssetMenu(menuName = "Ushqyr/Quad Profile", fileName = "QuadProfile")]
    public sealed class QuadProfileAsset : ScriptableObject
    {
        public QuadProfile Profile = QuadProfile.Race5Inch;
    }
}
