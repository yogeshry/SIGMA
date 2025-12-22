using System.Collections.Generic;
using UnityEngine;
using static Scatter3DDataLoader;

[RequireComponent(typeof(Scatter3DSelection))]
public class Scatter3DPoints : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Scatter3DAxes axes;       // assign or auto-find
    [SerializeField] private Material bubbleMaterial;  // URP/Unlit, Surface=Transparent (assign in Inspector)

    [Header("Sizing")]
    public bool scaleWithXLen = true;          // keep, but can disable on HL2
    [Min(0.0005f)] public float bubbleScale = 2.2f; // global multiplier
    [Min(0.001f)] public float minDiameter = 0.001f; // clamp so bubbles are visible on device (>= 1 cm)

    [Header("Population Scale")]
    public bool populationLog10 = true;

    // pool
    readonly List<Transform> _pool = new();
    Transform _pointRoot;
    Scatter3DSelection _sel;

    // ---- added caches for Redraw optimization (no logic change) ----
    readonly List<ScatterPoint> _rows = new();        // reuse instead of allocating every Redraw
    readonly List<Renderer> _poolRenderers = new();   // cache renderers to avoid GetComponent in loop

    void Reset() { axes = GetComponentInParent<Scatter3DAxes>(); }
    void Awake()
    {
        _sel = GetComponent<Scatter3DSelection>();
        EnsureRoot();
        if (!axes) axes = GetComponentInParent<Scatter3DAxes>();

        // Robust fallback if no material is assigned
        if (!bubbleMaterial) bubbleMaterial = Scatter3DUtil.SharedPointMat;

        // Ensure transparent config (with ZWrite ON for HL2 visibility)
        Scatter3DUtil.ForceTransparent(bubbleMaterial, zWrite: true);
    }

    public void EnsureRoot()
    {
        if (!_pointRoot)
        {
            var go = GameObject.Find($"{name}_Points") ?? new GameObject($"{name}_Points");
            _pointRoot = go.transform;
            _pointRoot.SetParent(transform, false);
        }
    }

    public void SetEnabled(bool enabledState) => enabled = enabledState;
    public void SetVisible(bool visible) { EnsureRoot(); if (_pointRoot) _pointRoot.gameObject.SetActive(visible); }

    public void Redraw(IReadOnlyList<ScatterPoint> data, int year)
    {
        EnsureRoot();
        if (!axes) { Debug.LogWarning("[Scatter3DPoints] Missing Scatter3DAxes."); return; }

        // filter by year (reuse list; same logic)
        _rows.Clear();
        for (int i = 0; i < data.Count; i++)
            if (data[i].year == year) _rows.Add(data[i]);

        // pool (same behavior, also cache renderer alongside transform)
        while (_pool.Count < _rows.Count)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "pt";
            go.transform.SetParent(_pointRoot, false);

            // Use shared material; color via MPB to avoid leaks
            var rr = go.GetComponent<Renderer>();
            rr.sharedMaterial = bubbleMaterial;

            _pool.Add(go.transform);
            _poolRenderers.Add(rr);
        }

        for (int i = _rows.Count; i < _pool.Count; i++)
            _pool[i].gameObject.SetActive(false);

        // precompute factor used inside loop (identical math)
        float xLenFactor = 1f;
        if (scaleWithXLen)
            xLenFactor = Mathf.Max(minDiameter, axes.xLen * bubbleScale);

        // place + style (same logic)
        for (int i = 0; i < _rows.Count; i++)
        {
            var p = _rows[i];
            var tf = _pool[i];
            tf.gameObject.SetActive(true);

            tf.position = Map(p.x, p.z, p.y);

            // Later, when computing diameter for rendering:
            float radius = p.size;
            float diameter = radius * 2f;

            if (scaleWithXLen)
                diameter *= xLenFactor;

            tf.localScale = Vector3.one * diameter;

            // color + fade via MPB
            var col = p.color;
            bool any = _sel.fadeWhenAnySelected && _sel.AnySelected;
            bool isSel = _sel.IsSelected(p.country);
            if (any && !isSel)
            {
                col.a = Mathf.Clamp01(_sel.fadeOthersAlpha / 1.2f);
            }
            else col.a = 1f;
            var bright = Scatter3DUtil.BoostColor(col, 1.2f, 1f); // tweak gains

            // use cached renderer (same result, faster)
            Scatter3DUtil.ApplyColor(_poolRenderers[i], bright);
        }
    }

    public Vector3 Map(float x, float z, float y)
    {
        float nx = Normalize(x, axes.xDomain, axes.xScale);
        float nz = Normalize(z, axes.zDomain, axes.zScale);
        float ny = Normalize(y, axes.yDomain, axes.yScale);

        // frame from axes
        var basePos = axes.origin ? axes.origin.position : transform.position;
        return basePos
             + axes.axisX.normalized * (nx * axes.xLen)
             + axes.axisZ.normalized * (nz * axes.zLen)
             + axes.axisY.normalized * (ny * axes.yLen);
    }

    public static float Normalize(float v, Vector2 domain, ScaleType scale = ScaleType.Linear)
    {
        float a = domain.x, b = domain.y;
        if (scale == ScaleType.Log10)
        {
            float la = Mathf.Log10(Mathf.Max(a, 1e-12f));
            float lb = Mathf.Log10(Mathf.Max(b, 1e-12f));
            float lv = Mathf.Log10(Mathf.Max(v, 1e-12f));
            return Mathf.InverseLerp(la, lb, lv);  // same formula
        }
        else if (scale == ScaleType.Sqrt)
        {
            return Mathf.InverseLerp(Mathf.Sqrt(a), Mathf.Sqrt(b), Mathf.Sqrt(Mathf.Max(v, 0f)));
        }
        else // linear
        {
            return Mathf.InverseLerp(a, b, v);
        }
    }
}
