using UnityEngine;

namespace CamLinkPro.Pipeline
{
    /// <summary>
    /// A One Euro filter on a single scalar channel (Casiez, Roussel, Vogel 2012).
    /// Filters hard while the signal is nearly still (killing hand tremor) and opens
    /// up automatically the instant real motion starts (keeping lag low during an
    /// actual move) -- unlike a fixed-blend low-pass, which forces a single tradeoff
    /// between the two. Pure, no Unity lifecycle dependency: safe to unit test with
    /// synthetic value/dt sequences.
    /// </summary>
    public sealed class OneEuroFilter
    {
        // Cutoff frequency of the derivative low-pass. 1Hz is the standard default
        // for this filter and isn't something the app needs to expose.
        const float DCutoff = 1f;

        float prevValue;
        float prevDerivative;
        bool hasPrevious;

        /// <summary>Minimum cutoff frequency (Hz) -- how hard the filter smooths
        /// when the signal is still. Lower = smoother but laggier.</summary>
        public float MinCutoff { get; set; }

        /// <summary>Speed-adaptivity constant -- how fast the filter opens up as
        /// the signal starts moving. Higher = opens up faster during fast motion.</summary>
        public float Beta { get; set; }

        public OneEuroFilter(float minCutoff, float beta)
        {
            MinCutoff = minCutoff;
            Beta = beta;
        }

        static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / dt);
        }

        /// <summary>Filters one new sample. <paramref name="dt"/> is the elapsed time
        /// in seconds since the previous call. The very first call (or any call with
        /// a non-positive dt) passes the value through unfiltered and primes state.</summary>
        public float Filter(float value, float dt)
        {
            if (!hasPrevious || dt <= 0f)
            {
                prevValue = value;
                prevDerivative = 0f;
                hasPrevious = true;
                return value;
            }

            float derivative = (value - prevValue) / dt;
            float aD = Alpha(DCutoff, dt);
            prevDerivative += aD * (derivative - prevDerivative);

            float cutoff = MinCutoff + Beta * Mathf.Abs(prevDerivative);
            float a = Alpha(cutoff, dt);
            prevValue += a * (value - prevValue);
            return prevValue;
        }

        /// <summary>Drops all history -- the next Filter() call behaves like the
        /// first one ever made (passes the value through, primes state).</summary>
        public void Reset()
        {
            hasPrevious = false;
            prevDerivative = 0f;
        }

        /// <summary>Maps the single user-facing "Steadiness" 0-1 slider to this
        /// filter's minimum cutoff frequency: ~6Hz at 0 (barely smoothed, most
        /// responsive) down to ~0.3Hz at 1 (heavily smoothed). Beta (speed
        /// adaptivity) is held fixed at 4.0 -- exposing it separately isn't useful
        /// to a camera operator.</summary>
        public static float SteadinessToMinCutoff(float steadiness01)
        {
            steadiness01 = Mathf.Clamp01(steadiness01);
            return 6f * Mathf.Pow(0.05f, steadiness01);
        }

        public const float FixedBeta = 4.0f;
    }
}
