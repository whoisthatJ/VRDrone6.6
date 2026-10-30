using GateRush.Input;
using NUnit.Framework;

namespace GateRush.Tests
{
    public class InputDeviceReportTests
    {
        [Test]
        public void VidPid_FromHidCapabilities()
        {
            const string caps = "{\"vendorId\":1155,\"productId\":22352,\"usage\":4,\"usagePage\":1,\"inputReportSize\":20}";
            Assert.IsTrue(InputDeviceReport.TryGetVidPid(caps, out int vid, out int pid));
            Assert.AreEqual(0x0483, vid);
            Assert.AreEqual(0x5750, pid);
        }

        [Test]
        public void VidPid_FromAndroidCapabilities()
        {
            const string caps = "{\"deviceDescriptor\":\"abc\",\"productId\": 21, \"vendorId\": 7247,\"isVirtual\":false,\"motionAxes\":[0,1,11,14]}";
            Assert.IsTrue(InputDeviceReport.TryGetVidPid(caps, out int vid, out int pid));
            Assert.AreEqual(7247, vid);
            Assert.AreEqual(21, pid);
        }

        [Test]
        public void VidPid_MissingIsFalse()
        {
            Assert.IsFalse(InputDeviceReport.TryGetVidPid("", out _, out _));
            Assert.IsFalse(InputDeviceReport.TryGetVidPid("{\"foo\":1}", out _, out _));
        }
    }
}
