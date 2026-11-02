using System.Reflection;
using GateRush.Input;
using GateRush.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GateRush.Tests
{
    /// <summary>Re-arming after a crash: held keyboard throttle, arm presses during the respawn delay, refusal feedback.</summary>
    public class ArmingTests : InputTestFixture
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void Tick(PilotInputSource s) => typeof(PilotInputSource).GetMethod("Update", Private).Invoke(s, null);

        private static void SetKeyboardThrottle(KeyboardGamepadSource s, float v) =>
            typeof(KeyboardGamepadSource).GetField("_kbThrottle", Private).SetValue(s, v);

        private void PressSpace(Keyboard kb, PilotInputSource s)
        {
            Press(kb.spaceKey);
            Tick(s);
            Release(kb.spaceKey);
            Tick(s);
        }

        [Test]
        public void Keyboard_ThrottleHeldAtCrash_IsDroppedByTheDisarm_SoSpaceArmsAgain()
        {
            var kb = InputSystem.AddDevice<Keyboard>();
            var go = new GameObject("source");
            try
            {
                var source = go.AddComponent<KeyboardGamepadSource>();
                SetKeyboardThrottle(source, 0.6f); // W held during the flight that ended in a crash

                PressSpace(kb, source);
                Assert.IsFalse(source.IsArmed, "arming is refused with throttle up");
                Assert.AreEqual(PilotInputSource.ArmRefusal.ThrottleHigh, PilotInputSource.LastRefusal, "the OSD can say why");

                source.Disarm(); // crash / respawn
                Tick(source);
                Assert.AreEqual(0f, source.Current.Throttle, "the held keyboard throttle is cut with the disarm");
                PressSpace(kb, source);
                Assert.IsTrue(source.IsArmed, "Space arms again right after the crash");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ArmPressedDuringTheRespawnDelay_SurvivesTheCrashRespawn()
        {
            var kb = InputSystem.AddDevice<Keyboard>();
            var go = new GameObject("quad");
            try
            {
                var source = go.AddComponent<KeyboardGamepadSource>();
                var vehicle = go.AddComponent<QuadVehicle>();
                vehicle.InputSource = source;
                var router = go.AddComponent<VehicleInputRouter>();
                typeof(VehicleInputRouter).GetField("vehicle", Private).SetValue(router, vehicle);
                typeof(VehicleInputRouter).GetField("sources", Private).SetValue(router, new PilotInputSource[] { source });
                typeof(VehicleInputRouter).GetMethod("OnEnable", Private).Invoke(router, null);

                // The pilot re-arms (throttle low) while the crashed quad waits to respawn...
                PressSpace(kb, source);
                Assert.IsTrue(source.IsArmed);
                // ...and the crash-timer respawn keeps it.
                typeof(QuadVehicle).GetMethod("Respawn", Private, null, new[] { typeof(bool) }, null).Invoke(vehicle, new object[] { false });
                Assert.IsFalse(vehicle.LastRespawnDisarmed);
                Assert.IsTrue(source.IsArmed, "arm press made during the respawn delay is not wiped");

                // A reset / restart respawn still disarms everything.
                vehicle.Respawn();
                Assert.IsTrue(vehicle.LastRespawnDisarmed);
                Assert.IsFalse(source.IsArmed);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
