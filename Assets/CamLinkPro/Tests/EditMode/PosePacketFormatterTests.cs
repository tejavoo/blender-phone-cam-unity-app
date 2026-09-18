using CamLinkPro.Networking;
using NUnit.Framework;
using UnityEngine;

namespace CamLinkPro.Tests
{
    public class PosePacketFormatterTests
    {
        [Test]
        public void MatchesExactWireFormat_NineFieldsFiveDecimals()
        {
            string line = PosePacketFormatter.Format(
                42,
                new Vector3(1.5f, -2.25f, 0f),
                new Vector3(10f, -170.125f, 359.999999f),
                50f, 36f);

            var parts = line.Split(',');
            Assert.AreEqual(9, parts.Length, "must be exactly nine comma-separated fields");
            Assert.AreEqual("42", parts[0]);
            Assert.AreEqual("1.50000", parts[1]);
            Assert.AreEqual("-2.25000", parts[2]);
            Assert.AreEqual("0.00000", parts[3]);
            Assert.AreEqual("10.00000", parts[4]);
            Assert.AreEqual("-170.12500", parts[5]);
            Assert.AreEqual("50.00000", parts[7]);
            Assert.AreEqual("36.00000", parts[8]);
        }

        [Test]
        public void NoSpacesAnywhereInLine()
        {
            string line = PosePacketFormatter.Format(1, Vector3.one, Vector3.zero, 24f, 36f);
            StringAssert.DoesNotContain(" ", line);
        }

        [Test]
        public void UsesInvariantCulture_NotCommaDecimalSeparator()
        {
            // Regression guard: a machine with a comma-decimal locale must not
            // corrupt the comma-separated wire format.
            string line = PosePacketFormatter.Format(1, new Vector3(1.5f, 0, 0), Vector3.zero, 24f, 36f);
            Assert.AreEqual(9, line.Split(',').Length);
        }
    }
}
