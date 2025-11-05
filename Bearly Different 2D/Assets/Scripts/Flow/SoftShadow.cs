using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.UI
{
    // Add alongside your regular UI effects.
    // Component: Add Component → UI → Effects → Soft Shadow
    [AddComponentMenu("UI/Effects/Soft Shadow", 81)]
    public class SoftShadow : Shadow
    {
        [Tooltip("How far the blur spreads around the main offset.")]
        [Range(0f, 32f)] public float blurRadius = 8f;

        [Tooltip("How many directions to sample (higher = softer but more expensive).")]
        [Range(4, 32)] public int samples = 12;

        [Tooltip("How many rings moving outward (higher = softer but more expensive).")]
        [Range(1, 8)] public int rings = 3;

        [Tooltip("Controls falloff from center; lower = quicker fade.")]
        [Range(0.5f, 4f)] public float softness = 2f;

        // Reuse lists to reduce allocations
        private static readonly List<UIVertex> s_Verts = new List<UIVertex>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0)
                return;

            // Pull current verts
            s_Verts.Clear();
            vh.GetUIVertexStream(s_Verts);

            // Keep the original (on top)
            var originalCount = s_Verts.Count;

            // Precompute directions on the unit circle
            // We’ll layer several “rings” with decreasing alpha.
            for (int r = 1; r <= rings; r++)
            {
                float t = r / (float)rings; // 0..1 outward
                float ringRadius = Mathf.Lerp(0f, blurRadius, t);

                // Gaussian-ish falloff: stronger near the center
                float sigma = Mathf.Max(0.0001f, softness);
                float weight = Mathf.Exp(-(t * t) / (2f * sigma * sigma));

                // Convert base effectColor to Color32 with scaled alpha
                var c = effectColor; // from Shadow base
                byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * 255f * weight), 0, 255);
                var ringColor = new Color32(
                    (byte)Mathf.RoundToInt(c.r * 255f),
                    (byte)Mathf.RoundToInt(c.g * 255f),
                    (byte)Mathf.RoundToInt(c.b * 255f),
                    a
                );

                for (int i = 0; i < samples; i++)
                {
                    float angle = (i / (float)samples) * Mathf.PI * 2f;
                    // Center the blur around your main offset (effectDistance)
                    float dx = effectDistance.x + Mathf.Cos(angle) * ringRadius;
                    float dy = effectDistance.y + Mathf.Sin(angle) * ringRadius;

                    // Apply this shadow sample
                    ApplyShadowZeroAlloc(s_Verts, ringColor, 0, originalCount, dx, dy);
                }
            }

            // Also draw a stronger copy at the exact main offset (optional).
            // Comment out if you want *only* the soft ring.
            ApplyShadowZeroAlloc(
                s_Verts,
                (Color32)new Color(effectColor.r, effectColor.g, effectColor.b, effectColor.a),
                0, originalCount, effectDistance.x, effectDistance.y
            );

            vh.Clear();
            vh.AddUIVertexTriangleStream(s_Verts);
        }
    }
}
