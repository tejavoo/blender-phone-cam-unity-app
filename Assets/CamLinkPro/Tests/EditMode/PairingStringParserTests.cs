using CamLinkPro.Networking;
using NUnit.Framework;

namespace CamLinkPro.Tests
{
    public class PairingStringParserTests
    {
        [Test]
        public void ParsesTheExactSpecExample()
        {
            bool ok = PairingStringParser.TryParse("camlink://192.168.1.42:5005:5006?t=9f3a7c21", out PairingInfo info);
            Assert.IsTrue(ok);
            Assert.AreEqual("192.168.1.42", info.Ip);
            Assert.AreEqual(5005, info.PosePort);
            Assert.AreEqual(5006, info.VideoPort);
            Assert.AreEqual("9f3a7c21", info.Token);
            Assert.IsTrue(info.IsValid);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("not a qr code at all")]
        [TestCase("http://example.com")]
        [TestCase("camlink://192.168.1.42:5005:5006")] // missing token
        [TestCase("camlink://192.168.1.42:5005?t=abc")] // missing a port
        [TestCase("camlink://192.168.1.42:abcde:5006?t=abc")] // non-numeric port
        [TestCase("camlink://192.168.1.42:0:5006?t=abc")] // out-of-range port
        [TestCase("camlink://192.168.1.42:5005:70000?t=abc")] // out-of-range port
        public void RejectsAnythingNotExactlyThatShape_WithoutThrowing(string text)
        {
            bool ok = PairingStringParser.TryParse(text, out PairingInfo info);
            Assert.IsFalse(ok);
        }

        [Test]
        public void TokenAllowsAlphanumericAndDashUnderscore()
        {
            bool ok = PairingStringParser.TryParse("camlink://10.0.0.5:1:2?t=Ab_c-123", out PairingInfo info);
            Assert.IsTrue(ok);
            Assert.AreEqual("Ab_c-123", info.Token);
        }
    }
}
