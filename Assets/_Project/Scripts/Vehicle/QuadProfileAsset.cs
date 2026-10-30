using GateRush.Flight;
using UnityEngine;

namespace GateRush.Vehicle
{
    [CreateAssetMenu(menuName = "GateRush/Quad Profile", fileName = "QuadProfile")]
    public sealed class QuadProfileAsset : ScriptableObject
    {
        public QuadProfile Profile = QuadProfile.Race5Inch;
    }
}
