using CamLinkPro.Pipeline;
using NUnit.Framework;

namespace CamLinkPro.Tests
{
    public class OneEuroFilterTests
    {
        [Test]
        public void FirstSample_PassesThroughUnfiltered()
        {
            var f = new OneEuroFilter(1f, 4f);
            Assert.AreEqual(5f, f.Filter(5f, 0.016f));
        }

        [Test]
        public void NonPositiveDt_PassesThroughAndReprimes()
        {
            var f = new OneEuroFilter(1f, 4f);
            f.Filter(1f, 0.016f);
            Assert.AreEqual(9f, f.Filter(9f, 0f));
        }

        [Test]
        public void ConstantInput_ConvergesToInput()
        {
            var f = new OneEuroFilter(1f, 4f);
            float value = 3f;
            float last = f.Filter(0f, 0.016f);
            for (int i = 0; i < 200; i++) last = f.Filter(value, 0.016f);
            Assert.AreEqual(value, last, 0.01f);
        }

        [Test]
        public void SmallJitter_IsDampedMoreThanLargeMotion()
        {
            // Same filter, two different signals: tiny noise around 0, and a
            // steady ramp. The jitter should end up damped far more than the ramp
            // keeps up with real motion -- the whole point of the One Euro filter.
            var jitterFilter = new OneEuroFilter(1f, 4f);
            var rampFilter = new OneEuroFilter(1f, 4f);

            float dt = 1f / 60f;
            var rnd = new System.Random(1);
            float jitterOut = 0f;
            float rampOut = 0f;
            float ramp = 0f;
            for (int i = 0; i < 120; i++)
            {
                float jitterIn = ((float)rnd.NextDouble() - 0.5f) * 0.01f; // +-5mm noise
                jitterOut = jitterFilter.Filter(jitterIn, dt);

                ramp += 0.5f * dt; // 0.5 units/sec steady motion
                rampOut = rampFilter.Filter(ramp, dt);
            }

            float jitterError = UnityEngine.Mathf.Abs(jitterOut);
            float rampLag = UnityEngine.Mathf.Abs(ramp - rampOut);

            // The filtered jitter should be much closer to zero (its true mean)
            // than the filtered ramp is to catching up with real steady motion.
            Assert.Less(jitterError, 0.01f);
            Assert.Greater(rampLag, 0f);
        }

        [Test]
        public void Reset_ForgetsHistory()
        {
            var f = new OneEuroFilter(1f, 4f);
            f.Filter(10f, 0.016f);
            f.Filter(10f, 0.016f);
            f.Reset();
            // After reset, behaves like brand new: first sample passes through raw.
            Assert.AreEqual(-3f, f.Filter(-3f, 0.016f));
        }

        [TestCase(0f, 6f)]
        [TestCase(1f, 0.3f)]
        public void SteadinessToMinCutoff_MatchesSpecEndpoints(float steadiness, float expected)
        {
            Assert.AreEqual(expected, OneEuroFilter.SteadinessToMinCutoff(steadiness), 0.001f);
        }
    }
}
