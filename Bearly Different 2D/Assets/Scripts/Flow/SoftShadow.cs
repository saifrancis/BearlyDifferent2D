using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.UI
{
    [AddComponentMenu("UI/Effects/Soft Shadow", 81)]
    public class SoftShadow : Shadow
    {
        [Range(0f, 32f)] public float blurRadius = 8f;
        [Range(4, 32)] public int samples = 12;
        [Range(1, 8)] public int rings = 3;
        [Range(0.5f, 4f)] public float softness = 2f;

        private static readonly List<UIVertex> s_Verts = new List<UIVertex>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0)
                return;

            s_Verts.Clear();
            vh.GetUIVertexStream(s_Verts);

            var originalCount = s_Verts.Count;

            for (int r = 1; r <= rings; r++)
            {
                float t = r / (float)rings; 
                float ringRadius = Mathf.Lerp(0f, blurRadius, t);

                float sigma = Mathf.Max(0.0001f, softness);
                float weight = Mathf.Exp(-(t * t) / (2f * sigma * sigma));

                var c = effectColor; 
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
                    float dx = effectDistance.x + Mathf.Cos(angle) * ringRadius;
                    float dy = effectDistance.y + Mathf.Sin(angle) * ringRadius;

                    ApplyShadowZeroAlloc(s_Verts, ringColor, 0, originalCount, dx, dy);
                }
            }

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
