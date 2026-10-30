using GateRush.Flight;
using UnityEngine;

namespace GateRush.Vehicle
{
    /// <summary>Pilot rates. When assigned to a vehicle they replace the quad profile's own rates.</summary>
    [CreateAssetMenu(menuName = "GateRush/Rate Profile", fileName = "RateProfile")]
    public sealed class RateProfileAsset : ScriptableObject
    {
        public RateProfile Rates = RateProfile.BetaflightDefault;
    }
}
