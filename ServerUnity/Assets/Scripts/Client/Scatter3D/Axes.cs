using UnityEngine;
using System;
using System.Collections.Generic;
using static Scatter3DDataLoader;
using Newtonsoft.Json; // already used elsewhere in your project

public class Scatter3DAxes : MonoBehaviour
{
    [Header("Axes frame (world)")]
    public Transform origin;
    public Vector3 axisX = Vector3.right;
    public Vector3 axisZ = Vector3.forward;
    public Vector3 axisY = Vector3.up;
    public float xLen = 1.2f, zLen = 0.8f, yLen = 1.0f;

    [Header("Domains")]
    public Vector2 xDomain = new(1f, 8f);
    public Vector2 zDomain = new(20f, 90f);
    public Vector2 yDomain = new(1e5f, 1.4e9f);
    public Vector2 sizeDomain = new(5f, 500f);

    [Header("Rendering")]
    public Material axisMaterial;
    public float axisWidth = 0.0005f;
    public int axisTicksX = 7, axisTicksZ = 8, axisTicksY = 6;
    public Color axisColor = new(0.15f, 0.15f, 0.15f, 1f);
    public Color labelColor = Color.white;
    public float labelCharSize = 0.005f;
    public Vector3 labelOffset = new(0f, 0f, 0f);

    [Header("Axis Titles")]
    public string xAxisLabel = "X (Fertility)";
    public string zAxisLabel = "Z (Life)";
    public string yAxisLabel = "Y (Population)";
    public float axisTitleCharSize = 0.01f;
    public Vector3 axisTitleOffset = new(0.03f, 0.02f, 0f);
    public Color axisTitleColor = Color.white;
    public float titleOffsetFrac = 0.02f;
    public float titleOffsetMinWorld = 0.005f;
    public float titleOffsetMaxWorld = 0.04f;

    public ScaleType xScale = ScaleType.Linear, yScale = ScaleType.Linear, zScale = ScaleType.Linear;

    Transform _axisRoot;
    bool _lastPopulationLog10;

    // ===== JSON axis-config support =====
    [Serializable]
    public class AxisTickDef { public float value; public string label; public float t; }
    [Serializable]
    public class AxisConfigDef
    {
        public string axis;          // "x" | "y" | "z"
        public string field;         // optional (for external bookkeeping)
        public string scaleType;     // "linear" | "log"
        public float[] domain;             // optional [lo, hi]
        public float axisLength = 0; // optional, overrides xLen/yLen/zLen if >0
        public int tickCount = 0;    // informational
        public List<AxisTickDef> ticks;
    }

    // store overrides by axis tag
    readonly Dictionary<string, AxisConfigDef> _axisCfgByTag = new(StringComparer.OrdinalIgnoreCase);

    public void ApplyAxisConfig(AxisConfigDef cfg, bool rebuild = true)
    {
        if (cfg == null || string.IsNullOrEmpty(cfg.axis)) return;
        var tag = cfg.axis.Trim().ToLowerInvariant();
        _axisCfgByTag[tag] = cfg;

        // adopt scale flag
        var sc = ParseScale(cfg.scaleType, ScaleType.Linear);
        if (tag == "x") xScale = sc;
        else if (tag == "y") yScale = sc;
        else if (tag == "z") zScale = sc;

        // adopt custom axis length
        //if (cfg.axisLength > 0f)
        //{
        //    if (tag == "x") xLen = cfg.axisLength;
        //    else if (tag == "y") yLen = cfg.axisLength;
        //    else if (tag == "z") zLen = cfg.axisLength;
        //}

        // adopt label if provided via 'field'
        if (!string.IsNullOrEmpty(cfg.field))
        {
            var suffix = (sc == ScaleType.Log10) ? " (log)" : "";
            if (tag == "x") xAxisLabel = cfg.field + suffix;
            else if (tag == "y") yAxisLabel = cfg.field + suffix;
            else if (tag == "z") zAxisLabel = cfg.field + suffix;
        }

        if (rebuild) RebuildAxes(_lastPopulationLog10);
    }

    static ScaleType ParseScale(string s, ScaleType def)
    {
        if (string.IsNullOrEmpty(s)) return def;
        var k = s.Trim().ToLowerInvariant();
        return (k is "log" or "log10") ? ScaleType.Log10 : ScaleType.Linear;
    }

    // ===== existing API =====
    public void EnsureRoot()
    {
        if (!_axisRoot)
        {
            var go = GameObject.Find($"{name}_Axes") ?? new GameObject($"{name}_Axes");
            _axisRoot = go.transform;
            _axisRoot.SetParent(transform, false);
        }
        if (!origin) origin = transform;
    }

    public void SetEnabled(bool enabledState) => enabled = enabledState;

    public void SetVisible(bool visible)
    {
        EnsureRoot();
        if (_axisRoot) _axisRoot.gameObject.SetActive(visible);
    }

    public void SetOrigin(Transform newOrigin, bool alignAxesToOrigin = true, bool copyWorldPose = true)
    {
        EnsureRoot();
        if (!origin)
        {
            var go = GameObject.Find("ChartOrigin") ?? new GameObject("ChartOrigin");
            origin = go.transform;
            origin.SetParent(transform, worldPositionStays: true);
        }

        if (newOrigin && copyWorldPose)
            origin.SetPositionAndRotation(newOrigin.position, newOrigin.rotation);

        if (alignAxesToOrigin)
            AlignAxesToOriginBasis();
    }

    public void AlignAxesToOriginBasis()
    {
        if (!origin) return;
        axisX = origin.right;
        axisY = origin.up;
        axisZ = origin.forward;
    }

    public void SetAxisLengths(float xLength, float zLength, float yLength)
    { xLen = xLength; zLen = zLength; yLen = yLength; }

    public void SetAxisDomains(Vector2 xDom, Vector2 zDom, Vector2 yDom)
    { xDomain = xDom; zDomain = zDom; yDomain = yDom; }

    public void SetAxisScale(ScaleType x, ScaleType y, ScaleType z)
    { xScale = x; yScale = y; zScale = z; }

    public void SetAxisLabels(string xLabel, string zLabel, string yLabel)
    {
        if (!string.IsNullOrEmpty(xLabel)) xAxisLabel = xLabel;
        if (!string.IsNullOrEmpty(zLabel)) zAxisLabel = zLabel;
        if (!string.IsNullOrEmpty(yLabel)) yAxisLabel = yLabel;
    }

    public void SetAxisTicks(int xTicks, int zTicks, int yTicks)
    { axisTicksX = xTicks; axisTicksZ = zTicks; axisTicksY = yTicks; }

    public void RebuildAxes(bool populationLog10)
    {
        _lastPopulationLog10 = populationLog10;
        EnsureRoot();

#if UNITY_EDITOR
        for (int i = _axisRoot.childCount - 1; i >= 0; i--) DestroyImmediate(_axisRoot.GetChild(i).gameObject);
#else
        foreach (Transform c in _axisRoot) Destroy(c.gameObject);
#endif

        //DrawAxis(
        //    goName: "X_Axis",
        //    axisTitle: xAxisLabel,
        //    dir: axisX,
        //    len: xLen,
        //    ticks: axisTicksX,
        //    domain: xDomain,
        //    tickNormalHint: axisZ,
        //    axisTag: "x",
        //    scale: xScale,
        //    overrideCfg: _axisCfgByTag.TryGetValue("x", out var cx) ? cx : null,
        //    populationLog10: false // not used on X
        //);

        //DrawAxis(
        //    goName: "Z_Axis",
        //    axisTitle: zAxisLabel,
        //    dir: axisZ,
        //    len: zLen,
        //    ticks: axisTicksZ,
        //    domain: zDomain,
        //    tickNormalHint: axisX,
        //    axisTag: "z",
        //    scale: zScale,
        //    overrideCfg: _axisCfgByTag.TryGetValue("z", out var cz) ? cz : null,
        //    populationLog10: false
        //);

        DrawAxis(
            goName: "Y_Axis",
            axisTitle: yAxisLabel,
            dir: axisY,
            len: yLen,
            ticks: axisTicksY,
            domain: yDomain,
            tickNormalHint: axisX,
            axisTag: "y",
            scale: yScale,
            overrideCfg: _axisCfgByTag.TryGetValue("y", out var cy) ? cy : null,
            populationLog10: populationLog10
        );
    }

    void DrawAxis(
        string goName, string axisTitle, Vector3 dir, float len, int ticks, Vector2 domain,
        Vector3 tickNormalHint, string axisTag, ScaleType scale,
        AxisConfigDef overrideCfg, bool populationLog10)
    {

        
        var axisGO = new GameObject(goName);
        axisGO.transform.SetParent(_axisRoot, false);

        var shaft = axisGO.AddComponent<LineRenderer>();
        if (axisMaterial) shaft.sharedMaterial = axisMaterial;
        Scatter3DUtil.SetupLR(shaft, axisColor, axisWidth);

        var basePos = origin ? origin.position : transform.position;
        Vector3 uDir = (dir.sqrMagnitude > 0 ? dir.normalized : Vector3.right);
        shaft.positionCount = 2;
        shaft.SetPosition(0, basePos);
        shaft.SetPosition(1, basePos + uDir * len);

        // Stable tick normal
        Vector3 tickN = Vector3.Cross(uDir, tickNormalHint);
        if (tickN.sqrMagnitude < 1e-8f) tickN = Vector3.Cross(uDir, Vector3.up);
        if (tickN.sqrMagnitude < 1e-8f) tickN = Vector3.right;
        tickN.Normalize();

        // If JSON override present: use its ticks as-is
        if (overrideCfg != null && overrideCfg.ticks != null && overrideCfg.ticks.Count > 0)
        {
            foreach (var tk in overrideCfg.ticks)
            {
                float t = Mathf.Clamp01(tk.t);
                var at = basePos + uDir * (t * len);

                // tick line
                var tick = new GameObject("tick");
                tick.transform.SetParent(axisGO.transform, false);
                var lr = tick.AddComponent<LineRenderer>();
                Scatter3DUtil.SetupLR(lr, axisColor, axisWidth * 0.8f);
                lr.positionCount = 2;
                lr.SetPositions(new[] { at - tickN * 0.0015f, at + tickN * 0.0015f });

                // label: only if provided (your JSON leaves minor ticks blank)
                if (!string.IsNullOrEmpty(tk.label))
                {
                    var labelPos = at + tickN * 0.015f + axisY.normalized * labelOffset.y;
                    CreateLabel(tk.label, labelPos, axisGO.transform, len);
                }
            }
        }
        else
        {
            // Fallback: auto ticks like before
            int n = Mathf.Max(1, ticks);
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                var at = basePos + uDir * (t * len);

                var tick = new GameObject("tick");
                tick.transform.SetParent(axisGO.transform, false);
                var lr = tick.AddComponent<LineRenderer>();
                Scatter3DUtil.SetupLR(lr, axisColor, axisWidth * 0.8f);
                lr.positionCount = 2;
                lr.SetPositions(new[] { at - tickN * 0.003f, at + tickN * 0.003f });

                float v = Mathf.Lerp(domain.x, domain.y, t);
                string text;
                if (scale == ScaleType.Log10)
                {
                    // show nice powers of 10 when close
                    var pow10 = Mathf.Pow(10f, Mathf.Round(v));
                    text = FormatAsPowerOfTen(pow10);
                }
                else
                {
                    text = Abbrev(v);
                }
                var labelPos = at + tickN * 0.015f + axisY.normalized * labelOffset.y;
                CreateLabel(text, labelPos, axisGO.transform, len);
            }
        }

        // Title (mid, outside, flat)
        CreateAxisTitleFlat(axisTitle, basePos, uDir, len, axisGO.transform, axisTag);
    }

    Transform CreateAxisTitleFlat(string title, Vector3 basePos, Vector3 uDir, float len, Transform parent, string axisTag)
    {
        if (string.IsNullOrEmpty(title)) return null;

        var go = new GameObject("axisTitle");
        go.transform.SetParent(parent, true);

        Vector3 right = (uDir.sqrMagnitude > 0f ? uDir.normalized : Vector3.right);
        Vector3 mid = basePos + right * (len * 0.5f);

        float off = -Mathf.Clamp(Mathf.Max(0f, titleOffsetFrac) * len, titleOffsetMinWorld, titleOffsetMaxWorld);

        Vector3 up, pos, fwd;
        if (axisTag == "x")
        {
            // place in X–Z plane, offset outward along +Z (outside chart)
            up = axisZ.normalized;
            pos = mid + up * off;
            fwd = Vector3.Cross(right, up);
        }
        else if (axisTag == "z")
        {
            // place in X–Z plane, offset outward along -X (left of chart)
            right = (uDir.sqrMagnitude > 0f ? uDir.normalized : axisZ.normalized);
            up = (-axisX).normalized;
            pos = mid + up * Mathf.Abs(off);
            fwd = Vector3.Cross(right, up);
        }
        else
        {
            // Y axis: in X–Y plane, offset along +X, then flip 180° around world Y
            right = (uDir.sqrMagnitude > 0f ? uDir.normalized : axisY.normalized);
            up = axisX.normalized;
            pos = mid + up * off;
            fwd = Vector3.Cross(right, up);
        }

        if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.up;

        var rot = Quaternion.LookRotation(fwd, up);
        if (axisTag == "y") rot = Quaternion.AngleAxis(180f, Vector3.up) * rot;

        go.transform.SetPositionAndRotation(pos, rot);

        var tm = go.AddComponent<TextMesh>();
        tm.text = title;
        tm.fontSize = 60;
        tm.characterSize = Mathf.Max(0.001f, labelCharSize) * (len+0.2f) * 0.2f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = labelColor;

        return go.transform;
    }

    Transform CreateLabel(string text, Vector3 worldPos, Transform parent, float axisLen)
    {
        var go = new GameObject("tickLabel");
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;

        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = Mathf.Max(0.001f, labelCharSize) * (axisLen+0.2f) / 4f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = labelColor;

        var bb = go.AddComponent<BillboardFaceCamera>();
        bb.mode = BillboardFaceCamera.FaceMode.CameraPosition;
        bb.onlyY = true;

        return go.transform;
    }

    static string Abbrev(float v)
    {
        float av = Mathf.Abs(v);
        if (av >= 1e9f) return (v / 1e9f).ToString("0.#") + "B";
        if (av >= 1e6f) return (v / 1e6f).ToString("0.#") + "M";
        if (av >= 1e3f) return (v / 1e3f).ToString("0.#") + "k";
        return v.ToString("0");
    }

    static string FormatAsPowerOfTen(float v)
    {
        v = Mathf.Max(1f, v);
        int p = Mathf.RoundToInt(Mathf.Log10(v));
        return $"10^{p}";
    }
}
