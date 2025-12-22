using UnityEngine;
using static Scatter3DDataLoader;

public static class Scatter3DUtil
{
    // ------- Shared materials (transparent) -------
    static Material sLineMat, sPointMat;
    static MaterialPropertyBlock _mpb;

    static readonly int _BaseColor = Shader.PropertyToID("_BaseColor"); // URP Lit/Unlit
    static readonly int _Color = Shader.PropertyToID("_Color");     // Standard/Sprites

    static Material NewTransparentMatInternal(bool zWrite = false)
    {
        // Prefer URP/Unlit. Avoid Shader.Find failures on device by also allowing Sprites/Default.
        var sh = Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Unlit/Color")
                 ?? Shader.Find("Standard");

        var m = new Material(sh)
        {
            hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
        };
        ForceTransparent(m, zWrite);
        return m;
    }

    public static Material SharedLineMat => sLineMat ??= NewTransparentMatInternal();
    public static Material SharedPointMat => sPointMat ??= NewTransparentMatInternal();

    // Configure a material for transparent rendering; optionally keep ZWrite on for visibility on HL2
    public static void ForceTransparent(Material m, bool zWrite = false)
    {
        if (!m) return;

        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", zWrite ? 1 : 0);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        var name = m.shader ? m.shader.name : "";
        if (name.Contains("Universal Render Pipeline"))
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // 1=Transparent
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            // Shadows on transparents are often undesirable for data points
            m.SetShaderPassEnabled("ShadowCaster", false);
        }
        else if (name == "Standard")
        {
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f); // Transparent
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
    }

    // LineRenderer setup using shared material (no leaks in edit mode)
    public static void SetupLR(LineRenderer lr, Color c, float w, Material shared = null)
    {
        lr.sharedMaterial = shared ? shared : SharedLineMat;
        lr.startColor = lr.endColor = c;
        lr.startWidth = lr.endWidth = w;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.useWorldSpace = true;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
    }

    // Per-renderer color/alpha via MPB (keep shared material)
    public static void ApplyColor(Renderer r, Color c)
    {
        if (!r) return;
        _mpb ??= new MaterialPropertyBlock();
        _mpb.Clear();

        var m = r.sharedMaterial;
        if (m && m.HasProperty(_BaseColor)) _mpb.SetColor(_BaseColor, c);
        else _mpb.SetColor(_Color, c);

        r.SetPropertyBlock(_mpb);
    }

    // -------- Helpers your other scripts rely on --------
    public static Color Hex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return new Color(0.5f, 0.5f, 0.5f, 1f);
        if (hex[0] == '#') hex = hex.Substring(1);
        uint v = System.Convert.ToUInt32(hex, 16);
        return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f);
    }

    public static int StableIndex(string key, int modulo)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        unchecked
        {
            int h = 23;
            foreach (var ch in key) h = h * 31 + ch;
            return (h & 0x7FFFFFFF) % Mathf.Max(1, modulo);
        }
    }

    public static float SafeInvLerp(float a, float b, float v)
    {
        var d = Mathf.Max(1e-6f, b - a);
        return Mathf.Clamp01((v - a) / d);
    }

    public static Vector2 Expand(
       float lo,
       float hi,
       float padFrac = 0.05f,
       bool clampToZero = false,
       bool nice = true
   )
    {
        // Handle degenerate / bad inputs
        if (float.IsInfinity(lo) || float.IsInfinity(hi) ||
            float.IsNaN(lo) || float.IsNaN(hi))
            return new Vector2(0f, 1f);

        if (hi < lo)
        {
            var tmp = lo;
            lo = hi;
            hi = tmp;
        }

        // If all values are (almost) equal, fabricate a small span around them
        if (Mathf.Approximately(lo, hi))
        {
            float baseVal = Mathf.Abs(lo);
            float span = baseVal > 0f ? baseVal * 0.1f : 1f;   // 10% or ±1
            lo -= span * 0.5f;
            hi += span * 0.5f;
        }

        // Recompute span after possible adjustment
        float sspan = Mathf.Max(1e-6f, hi - lo);

        // Add padding around range
        float pad = sspan * padFrac;
        lo -= pad;
        hi += pad;

        // For inherently non-negative data (GDP, CO2, pop)
        if (clampToZero && lo < 0f)
            lo = 0f;

        if (!nice)
            return new Vector2(lo, hi);

        // ---- "Nice" rounding to 1/2/5 * 10^k ----
        float range = Mathf.Max(1e-6f, hi - lo);
        float exponent = Mathf.Floor(Mathf.Log10(range));
        float tenPow = Mathf.Pow(10f, exponent);

        float[] steps = { 1f, 2f, 5f, 10f };
        float targetSteps = 8f;
        float bestStep = tenPow;
        float bestDiff = float.PositiveInfinity;

        foreach (var s in steps)
        {
            float step = s * tenPow;
            float nSteps = range / step;
            float diff = Mathf.Abs(nSteps - targetSteps);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestStep = step;
            }
        }

        float niceLo = Mathf.Floor(lo / bestStep) * bestStep;
        float niceHi = Mathf.Ceil(hi / bestStep) * bestStep;

        if (clampToZero)
        {
            // Guarantee non-negative domain
            if (niceHi <= 0f)
            {
                // Fallback domain if data were all <= 0
                niceLo = 0f;
                niceHi = 1f;
            }
            else if (niceLo < 0f)
            {
                niceLo = 0f;
            }
        }

        return new Vector2(niceLo, niceHi);
    }

    /// <summary>
    /// Normalize a domain from lo/hi for Linear or Log scales.
    /// Ensures positivity & non-degeneracy for log; widens equal bounds for linear.
    /// </summary>
    public static Vector2 DomainFromMinMax(ScaleType scale, float lo, float hi)
    {
        // Guard and order
        if (float.IsNaN(lo) || float.IsNaN(hi) || float.IsInfinity(lo) || float.IsInfinity(hi))
            return new Vector2(0f, 1f);
        if (hi < lo) { var t = lo; lo = hi; hi = t; }

        if (scale == ScaleType.Log10)
        {
            // require strictly positive + non-degenerate
            if (hi <= 0f) return new Vector2(1e-6f, 1f);
            if (lo <= 0f) lo = Mathf.Min(hi, 1e-6f);
            if (Mathf.Approximately(lo, hi))
            {
                lo = lo * 0.5f;   // widen equal bounds
                hi = hi * 2f;
                if (lo <= 0f) lo = Mathf.Min(hi, 1e-6f);
            }
            return new Vector2(lo, hi);
        }
        if (scale == ScaleType.Sqrt)
        {
            // Sqrt scale also requires non-degenerate
            if (Mathf.Approximately(lo, hi))
            {
                float eps = Mathf.Abs(lo);
                if (eps <= 0f) eps = 1f;
                lo -= 0.5f * eps;
                hi += 0.5f * eps;
            }
            return new Vector2(lo, hi);
        }

            // Linear: widen equal bounds a bit
            if (Mathf.Approximately(lo, hi))
        {
            float eps = Mathf.Abs(lo);
            if (eps <= 0f) eps = 1f;
            lo -= 0.5f * eps;
            hi += 0.5f * eps;
        }
        return new Vector2(lo, hi);
    }


    // Utils
    public static Color BoostColor(Color c, float satGain = 1.15f, float valGain = 1.4f)
    {
        Color.RGBToHSV(c, out var h, out var s, out var v);
        s = Mathf.Clamp01(s * satGain);
        v = v * valGain; // allow HDR
        var outCol = Color.HSVToRGB(h, s, v, true); // hdr=true keeps >1 values
        outCol.a = c.a;
        return outCol;
    }

}
