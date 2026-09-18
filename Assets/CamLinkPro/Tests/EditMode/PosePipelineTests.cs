using CamLinkPro.Pipeline;
using NUnit.Framework;
using UnityEngine;

namespace CamLinkPro.Tests
{
    public class PosePipelineTests
    {
        const float Dt = 1f / 30f;
        static readonly Quaternion Identity = Quaternion.identity;

        [Test]
        public void WarmsUpForExactlyTwentyFrames()
        {
            var p = new PosePipeline();
            for (int i = 0; i < PosePipeline.WarmupFrames; i++)
            {
                bool ok = p.Process(Vector3.zero, Identity, Dt, true, out _);
                Assert.IsFalse(ok, $"frame {i} should still be warming up");
            }
            bool readyNow = p.Process(Vector3.zero, Identity, Dt, true, out _);
            Assert.IsTrue(readyNow, "warm-up should be over after exactly WarmupFrames frames");
        }

        [Test]
        public void UnreliableTracking_NeverProducesAPacket_AndRestartsWarmup()
        {
            var p = new PosePipeline();
            for (int i = 0; i < PosePipeline.WarmupFrames; i++) p.Process(Vector3.zero, Identity, Dt, true, out _);
            bool wasReady = p.Process(Vector3.zero, Identity, Dt, true, out _);
            Assert.IsTrue(wasReady);

            // Tracking drops out for one frame -- must not produce a packet, and
            // must restart warm-up once it resumes.
            bool duringLoss = p.Process(Vector3.zero, Identity, Dt, false, out _);
            Assert.IsFalse(duringLoss);

            for (int i = 0; i < PosePipeline.WarmupFrames; i++)
            {
                bool ok = p.Process(Vector3.zero, Identity, Dt, true, out _);
                Assert.IsFalse(ok, $"post-recovery frame {i} should be warming up again");
            }
            bool readyAgain = p.Process(Vector3.zero, Identity, Dt, true, out _);
            Assert.IsTrue(readyAgain);
        }

        [Test]
        public void FirstFrame_BecomesTheOrigin()
        {
            var p = new PosePipeline { LevelHorizon = false };
            Vector3 start = new Vector3(3f, 1.6f, -2f);

            BlenderPose pose = default;
            for (int i = 0; i <= PosePipeline.WarmupFrames; i++)
                p.Process(start, Identity, Dt, true, out pose);

            // Constant input the whole time -> after convergence, output position
            // relative to the anchored origin should be (near) zero.
            for (int i = 0; i < 200; i++) p.Process(start, Identity, Dt, true, out pose);

            Assert.AreEqual(0f, pose.Position.x, 0.001f);
            Assert.AreEqual(0f, pose.Position.y, 0.001f);
            Assert.AreEqual(0f, pose.Position.z, 0.001f);
        }

        [Test]
        public void JumpLargerThanThreshold_IsAbsorbedIntoOrigin_NotVisibleInOutput()
        {
            var p = new PosePipeline { LevelHorizon = false };
            Vector3 start = Vector3.zero;
            BlenderPose pose = default;

            for (int i = 0; i <= PosePipeline.WarmupFrames + 50; i++)
                p.Process(start, Identity, Dt, true, out pose);

            Vector3 beforeJump = pose.Position;

            // A single-frame move far beyond what a hand can do -- must be
            // absorbed, not appear as a visible jump in the output. Distance is
            // preserved by the axis-conversion rotation, so the axis chosen here
            // doesn't matter for this check.
            Vector3 teleported = start + new Vector3(0f, 0f, 5f);
            p.Process(teleported, Identity, Dt, true, out pose);

            Assert.Less(Vector3.Distance(beforeJump, pose.Position), 0.05f,
                "a >0.35m single-frame jump must be absorbed into the origin, not shown to the receiver");
        }

        [Test]
        public void SubThresholdMotion_IsNotTreatedAsAJump()
        {
            var p = new PosePipeline { LevelHorizon = false, Steadiness = 0f };
            BlenderPose pose = default;
            for (int i = 0; i <= PosePipeline.WarmupFrames + 50; i++)
                p.Process(Vector3.zero, Identity, Dt, true, out pose);

            // A real, deliberate 0.2m move (under the 0.35m teleport threshold)
            // should show up in the output, not be swallowed as a "jump".
            Vector3 moved = new Vector3(0f, 0f, 0.2f);
            BlenderPose afterPose = default;
            for (int i = 0; i < 60; i++) p.Process(moved, Identity, Dt, true, out afterPose);

            Assert.Greater(afterPose.Position.magnitude, 0.1f);
        }

        [Test]
        public void RigPresetTripod_FreezesAllThreePositionChannels()
        {
            var p = new PosePipeline { LevelHorizon = false, Steadiness = 0f };
            BlenderPose pose = default;
            for (int i = 0; i <= PosePipeline.WarmupFrames + 20; i++)
                p.Process(Vector3.zero, Identity, Dt, true, out pose);

            p.SetRigPreset(RigPreset.Tripod);
            Vector3 heldPosition = pose.Position;

            // Move a lot after freezing -- position must not follow.
            Vector3 moved = new Vector3(1f, 0.5f, 2f);
            for (int i = 0; i < 60; i++) p.Process(moved, Identity, Dt, true, out pose);

            Assert.AreEqual(heldPosition.x, pose.Position.x, 0.0001f);
            Assert.AreEqual(heldPosition.y, pose.Position.y, 0.0001f);
            Assert.AreEqual(heldPosition.z, pose.Position.z, 0.0001f);
        }

        [Test]
        public void ReFreezing_RecapturesHoldValue_AtTheNewMoment()
        {
            var p = new PosePipeline { LevelHorizon = false, Steadiness = 0f };
            BlenderPose pose = default;
            for (int i = 0; i <= PosePipeline.WarmupFrames + 20; i++)
                p.Process(Vector3.zero, Identity, Dt, true, out pose);

            var freezeZ = new AxisFreezeState { PositionZ = true };
            p.SetFreeze(freezeZ);

            // Height is up in AR (Y-up) space, which the pipeline's axis
            // conversion maps to Blender Z. Ramp it up gradually (well under the
            // 0.35m teleport threshold per frame, like a real person standing up)
            // rather than jumping straight there, which would otherwise trip jump
            // absorption instead of exercising the freeze/recapture path at all.
            float height = 0f;
            for (int i = 0; i < 10; i++)
            {
                height += 0.02f;
                p.Process(new Vector3(0f, height, 0f), Identity, Dt, true, out pose);
            }
            // Let the One Euro filter settle after the freeze's step change before
            // treating its output as "the held value" -- filtering runs after the
            // freeze clamp, so it takes a few frames to converge onto a new hold,
            // same as it would for any other sudden control input.
            float originalHoldZ = pose.Position.z;

            // Z must stay pinned at that hold value from here on, even as the real
            // (unfiltered) height keeps rising underneath the freeze.
            for (int i = 0; i < 90; i++)
            {
                height += 0.02f; // 0.6m/s at 30fps -- well under the teleport threshold per frame
                p.Process(new Vector3(0f, height, 0f), Identity, Dt, true, out pose);
                Assert.AreEqual(originalHoldZ, pose.Position.z, 0.0001f, $"should still be frozen at frame {i}");
            }
            Assert.Greater(height, 1.5f, "sanity check: the simulated walk-up should have reached a real height by now");

            p.SetFreeze(freezeZ); // re-freeze the same channel -- should recapture *now*, not reuse the stale original hold
            for (int i = 0; i < 60; i++) p.Process(new Vector3(0f, height, 0f), Identity, Dt, true, out pose);

            Assert.Greater(pose.Position.z, 1.5f, "the recaptured hold value should reflect the walked-to height, not the original hold");
            Assert.AreNotEqual(originalHoldZ, pose.Position.z, "re-freezing must actually pick up a new value, not repeat the old one");
        }

        [Test]
        public void Reset_RestartsWarmupAndReanchorsOrigin()
        {
            var p = new PosePipeline { LevelHorizon = false };
            for (int i = 0; i <= PosePipeline.WarmupFrames + 5; i++)
                p.Process(new Vector3(2f, 0f, 0f), Identity, Dt, true, out _);

            p.Reset();

            for (int i = 0; i < PosePipeline.WarmupFrames; i++)
            {
                bool ok = p.Process(new Vector3(5f, 0f, 0f), Identity, Dt, true, out _);
                Assert.IsFalse(ok, "should be warming up again immediately after Reset()");
            }
        }

        [Test]
        public void DollyOffset_PushesPositionForward()
        {
            var p = new PosePipeline { LevelHorizon = false, Steadiness = 0f };
            BlenderPose baseline = default;
            for (int i = 0; i <= PosePipeline.WarmupFrames + 20; i++)
                p.Process(Vector3.zero, Identity, Dt, true, out baseline);

            p.DollyOffsetMetres = 2f;
            BlenderPose withDolly = default;
            for (int i = 0; i < 30; i++) p.Process(Vector3.zero, Identity, Dt, true, out withDolly);

            Assert.Greater(Vector3.Distance(baseline.Position, withDolly.Position), 1.5f);
        }

        // -- pure static math (internal, via InternalsVisibleTo) ----------------

        [Test]
        public void UnwrapAngles_KeepsAContinuousIncreasePastThe180Boundary()
        {
            Vector3 prev = new Vector3(0, 0, 179f * Mathf.Deg2Rad);
            Vector3 next = new Vector3(0, 0, -179f * Mathf.Deg2Rad); // wrapped

            Vector3 unwrapped = PosePipeline.UnwrapAngles(prev, next);
            float unwrappedDeg = unwrapped.z * Mathf.Rad2Deg;

            Assert.AreEqual(181f, unwrappedDeg, 0.01f);
        }

        [Test]
        public void EulerXYZRoundTrip_MatrixToEulerAndBack()
        {
            Vector3 originalDeg = new Vector3(15f, -35f, 120f);
            Vector3 originalRad = originalDeg * Mathf.Deg2Rad;

            Matrix4x4 m = PosePipeline.EulerXYZToMatrix(originalRad);
            Vector3 decoded = PosePipeline.MatrixToEulerXYZ(m);

            Assert.AreEqual(originalRad.x, decoded.x, 0.001f);
            Assert.AreEqual(originalRad.y, decoded.y, 0.001f);
            Assert.AreEqual(originalRad.z, decoded.z, 0.001f);
        }

        [Test]
        public void AxisConvert_MapsArYupToBlenderZup()
        {
            // AR "up" (0,1,0) should become Blender "up", i.e. land entirely in Z.
            Vector3 up = AxisConvert.YupToZup(new Vector3(0, 1, 0));
            Assert.AreEqual(0f, up.x, 1e-5f);
            Assert.AreEqual(0f, up.y, 1e-5f);
            Assert.AreEqual(1f, up.z, 1e-5f);
        }
    }
}
