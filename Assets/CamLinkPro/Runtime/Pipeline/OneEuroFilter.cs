using UnityEngine;

namespace CamLinkPro.Pipeline
{
    /// A One Euro filter on a single scalar channel (Casiez, Roussel, Vogel 2012).
    /// Filters hard while the signal is nearly still (killing hand tremor) and opens
    /// up automatically the instant real motion starts (keeping lag low during an
    /// actual move) — unlike a fixed-blend low-pass, which forces a single tradeoff
    /// between the two. Ported from the original uGUI build's proven pipeline.
    public sealed class OneEuroFilter
    {
        const float DCutoff = 1f;

        float prevValue;
        float prevDerivative;
        bool hasPrevious;

        public float MinCutoff { get; set; }
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

        public void Reset()
        {
            hasPrevious = false;
            prevDerivative = 0f;
        }

        /// Maps the user-facing "Steadiness" 0-1 slider to minimum cutoff
        /// frequency: ~6Hz at 0 (most responsive) down to ~0.3Hz at 1 (heavily
        /// smoothed). Beta held fixed — not useful to expose separately.
        public static float SteadinessToMinCutoff(float steadiness01)
        {
            steadiness01 = Mathf.Clamp01(steadiness01);
            return 6f * Mathf.Pow(0.05f, steadiness01);
        }

        public const float FixedBeta = 4.0f;
    }
}
