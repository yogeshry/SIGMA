using System.Collections.Generic;
using UnityEngine;

public class Scatter3DTrails : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Scatter3DAxes axes;             // auto-found if null
    [SerializeField] private Scatter3DPoints points;         // to copy bubble materials
    [SerializeField] private Scatter3DSelection selection;   // to respect selected set

    [Header("Trail Visibility")]
    public bool onlyForSelected = true;
    public bool showWhenNothingSelected = true; // if false: draw none when no selection

    [Header("Trail Line")]
    public bool scaleWidthWithXLen = true;
    public float width = 0.005f;
    public float widthScale = 1.0f;
    public float minWidth = 0.001f;
    [Range(0, 1)] public float trailAlpha = 0.5f;

    [Header("Trail Nodes")]
    public bool showNodes = true;
    public bool scaleNodeWithXLen = true;
    public float nodeScale = 2.2f;           // multiplies underlying bubble diameter
    public float nodeGlobalScale = 1.0f;
    public float nodeMinDiameter = 0.004f;   // bump for HL2 visibility
    public int nodeEveryNYears = 5;

    [SerializeField] private Material bubbleMaterial;  // URP/Unlit, Surface=Transparent (assign in Inspector)

    // Provided by controller
    public System.Func<float, float, float, Vector3> Map;

    Transform _root;

    readonly Dictionary<string, LineRenderer> _lrByCountry = new();
    readonly Dictionary<string, List<Transform>> _nodesByCountry = new();

    // ---- caches to avoid per-frame allocs / repeated component lookups ----
    readonly Dictionary<string, List<ScatterPoint>> _byCountry = new();
    readonly Dictionary<string, Color> _trailColorByCountry = new();

    // per-country node renderer cache
    readonly Dictionary<string, List<Renderer>> _nodeRenderersByCountry = new();

    // per-country position buffer cache
    readonly Dictionary<string, Vector3[]> _posBufByCountry = new();

    // reusable key buffer (avoid new List<string>(Keys))
    readonly List<string> _keyBuf = new();

    void Reset()
    {
        axes = GetComponentInParent<Scatter3DAxes>();
        points = GetComponentInParent<Scatter3DPoints>();
        selection = GetComponentInParent<Scatter3DSelection>();
    }

    void Awake()
    {
        if (!bubbleMaterial) bubbleMaterial = Scatter3DUtil.SharedPointMat;

        EnsureRoot();
        if (!axes) axes = GetComponentInParent<Scatter3DAxes>();
        if (!points) points = GetComponentInParent<Scatter3DPoints>();
        if (!selection) selection = GetComponentInParent<Scatter3DSelection>();
    }

    public void SetEnabled(bool enabledState) => enabled = enabledState;
    public void SetVisible(bool visible) { EnsureRoot(); if (_root) _root.gameObject.SetActive(visible); }
    public void SetPoints(Scatter3DPoints p) => points = p;

    void EnsureRoot()
    {
        if (!_root)
        {
            var go = GameObject.Find($"{name}_Trails") ?? new GameObject($"{name}_Trails");
            _root = go.transform; _root.SetParent(transform, false);
        }
    }

    Color TrailColorFor(string country, IReadOnlyList<ScatterPoint> data)
    {
        for (int i = 0; i < data.Count; i++)
            if (string.Equals(data[i].country, country, System.StringComparison.OrdinalIgnoreCase))
            {
                var c = data[i].color; c.a = trailAlpha;
                return Scatter3DUtil.BoostColor(c, 1f, 1f);
            }

        // Fallback: keep palette consistency (use first row’s color), not white
        if (data.Count > 0)
        {
            var c = data[0].color; c.a = trailAlpha / 1.2f; return c;
        }
        return new Color(1f, 1f, 1f, trailAlpha);
    }

    public void UpdateTrails(IReadOnlyList<ScatterPoint> data, int uptoYear)
    {
        EnsureRoot();
        if (Map == null) return;

        bool anySelected = selection && selection.AnySelected;
        if (onlyForSelected && !anySelected && !showWhenNothingSelected)
        {
            // Clear everything when nothing is selected
            _keyBuf.Clear();
            foreach (var k in _lrByCountry.Keys) _keyBuf.Add(k);
            for (int i = 0; i < _keyBuf.Count; i++) ClearTrail(_keyBuf[i]);
            return;
        }

        float xScale = axes ? Mathf.Max(0.001f, axes.xLen) : 1f;
        float lineWidth = Mathf.Max(minWidth, width * (scaleWidthWithXLen ? xScale : 1f) * widthScale);

        // clear/reuse per-update structures
        foreach (var kv in _byCountry) kv.Value.Clear();
        _byCountry.Clear();
        _trailColorByCountry.Clear();

        // Build by-country <= uptoYear (and cache trail color once per country)
        for (int i = 0; i < data.Count; i++)
        {
            var p = data[i];
            if (p.year > uptoYear) continue;

            // Respect selection
            if (onlyForSelected && anySelected && (selection != null) && !selection.IsSelected(p.country))
                continue;

            if (!_byCountry.TryGetValue(p.country, out var list))
            {
                list = new List<ScatterPoint>(32);
                _byCountry[p.country] = list;

                // compute color once for this country (same rule as TrailColorFor)
                var c = p.color; c.a = trailAlpha;
                _trailColorByCountry[p.country] = Scatter3DUtil.BoostColor(c, 1f, 1f);
            }

            list.Add(p);
        }

        // Clear removed countries
        _keyBuf.Clear();
        foreach (var k in _lrByCountry.Keys) _keyBuf.Add(k);
        for (int i = 0; i < _keyBuf.Count; i++)
            if (!_byCountry.ContainsKey(_keyBuf[i])) ClearTrail(_keyBuf[i]);

        // Update/create trails
        foreach (var kv in _byCountry)
        {
            var country = kv.Key;
            var rows = kv.Value;
            rows.Sort((a, b) => a.year.CompareTo(b.year));
            if (rows.Count < 2) { ClearTrail(country); continue; }

            // reuse per-country position buffer
            if (!_posBufByCountry.TryGetValue(country, out var positions) || positions == null || positions.Length < rows.Count)
            {
                positions = new Vector3[rows.Count];
                _posBufByCountry[country] = positions;
            }

            for (int i = 0; i < rows.Count; i++)
                positions[i] = Map(rows[i].x, rows[i].z, rows[i].y);

            // Line
            if (!_lrByCountry.TryGetValue(country, out var lr) || !lr)
            {
                var go = new GameObject($"trail_{country}");
                go.transform.SetParent(_root, false);
                lr = go.AddComponent<LineRenderer>();
                _lrByCountry[country] = lr;
                Scatter3DUtil.SetupLR(lr, Color.white, lineWidth, Scatter3DUtil.SharedLineMat);
                lr.textureMode = LineTextureMode.Stretch;
                lr.alignment = LineAlignment.View;
            }

            lr.positionCount = rows.Count;
            lr.SetPositions(positions);
            lr.startWidth = lr.endWidth = lineWidth;

            // color from cached map, fallback to original method if missing
            Color col;
            if (!_trailColorByCountry.TryGetValue(country, out col))
                col = TrailColorFor(country, data);

            lr.startColor = lr.endColor = col;
            lr.material.color = col; // also set on material for proper blending

            // Nodes
            if (showNodes) UpdateNodesForCountry(country, rows, positions, col, xScale);
            else HideNodesForCountry(country);
        }
    }

    void UpdateNodesForCountry(string country, List<ScatterPoint> rows, Vector3[] positions, Color c, float xScale)
    {
        if (!_nodesByCountry.TryGetValue(country, out var nodes))
            _nodesByCountry[country] = nodes = new List<Transform>();

        if (!_nodeRenderersByCountry.TryGetValue(country, out var renderers))
            _nodeRenderersByCountry[country] = renderers = new List<Renderer>();

        // precompute scale factor (same math as before, just once)
        float nodeFactor = Mathf.Max(0.001f, axes.xLen * nodeScale);

        // figure out how many nodes we need (same placement rule, but no keepIdx list)
        int needed = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            bool place = (nodeEveryNYears <= 1) || (rows[i].year % nodeEveryNYears == 0) || i == 0 || i == rows.Count - 1;
            if (place) needed++;
        }

        // Ensure pool (create spheres + cache renderer)
        while (nodes.Count < needed)
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            t.name = "trailNode";
            t.SetParent(_root, false);

            var rr = t.GetComponent<Renderer>();
            rr.sharedMaterial = bubbleMaterial;

            nodes.Add(t);
            renderers.Add(rr);
        }

        // Place + style
        int k = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            bool place = (nodeEveryNYears <= 1) || (rows[i].year % nodeEveryNYears == 0) || i == 0 || i == rows.Count - 1;
            if (!place) continue;

            var t = nodes[k];
            t.gameObject.SetActive(true);
            t.position = positions[i];

            // Diameter based on original point size * nodeScale * xLen (optional) * global scale
            float radius = rows[i].size;
            float diameter = radius * 2f;

            diameter *= nodeFactor;                 // same math, moved out
            diameter *= nodeGlobalScale;            // preserve your public knob (was previously unused; harmless multiply by 1)
            diameter = Mathf.Max(nodeMinDiameter, diameter);

            t.localScale = Vector3.one * diameter;

            // Color via MPB (keeps shared material intact)
            Scatter3DUtil.ApplyColor(renderers[k], c);

            k++;
        }

        // Disable leftovers
        for (int i = needed; i < nodes.Count; i++)
            if (nodes[i]) nodes[i].gameObject.SetActive(false);
    }

    void HideNodesForCountry(string country)
    {
        if (_nodesByCountry.TryGetValue(country, out var nodes))
            for (int i = 0; i < nodes.Count; i++) if (nodes[i]) nodes[i].gameObject.SetActive(false);
    }

    public void ClearTrail(string country)
    {
        if (_lrByCountry.TryGetValue(country, out var lr) && lr) DestroyImmediate(lr.gameObject);
        _lrByCountry.Remove(country);

        if (_nodesByCountry.TryGetValue(country, out var nodes))
        {
            foreach (var n in nodes) if (n) DestroyImmediate(n.gameObject);
            _nodesByCountry.Remove(country);
        }

        if (_nodeRenderersByCountry.TryGetValue(country, out var rrs))
            _nodeRenderersByCountry.Remove(country);

        if (_posBufByCountry.TryGetValue(country, out var buf))
            _posBufByCountry.Remove(country);

        _trailColorByCountry.Remove(country);
    }
}
