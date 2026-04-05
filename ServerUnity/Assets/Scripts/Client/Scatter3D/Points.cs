using System.Collections.Generic;
using UnityEngine;
using static Scatter3DDataLoader;

[RequireComponent(typeof(Scatter3DSelection))]
public class Scatter3DPoints : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Scatter3DAxes axes;
    [SerializeField] private Material bubbleMaterial;

    [Header("Sizing")]
    public bool scaleWithXLen = true;
    [Min(0.0005f)] public float bubbleScale = 2.2f;
    [Min(0.001f)] public float minDiameter = 0.001f;

    [Header("Population Scale")]
    public bool populationLog10 = true;

    Scatter3DSelection _sel;
    bool _visible = true;

    // GPU batched rendering — group by color, one DrawMeshInstanced per group
    static Mesh _sphereMesh;
    static MaterialPropertyBlock _mpb;
    static int _BaseColorID, _ColorID;
    const int BATCH_MAX = 1023;

    // color -> list of matrices (reused each frame)
    readonly Dictionary<Color, List<Matrix4x4>> _groups = new();
    readonly List<Color> _groupKeys = new();

    // filtered rows buffer
    readonly List<ScatterPoint> _rows = new();

    void Reset() { axes = GetComponentInParent<Scatter3DAxes>(); }
    void Awake()
    {
        _sel = GetComponent<Scatter3DSelection>();
        if (!axes) axes = GetComponentInParent<Scatter3DAxes>();
        if (!bubbleMaterial) bubbleMaterial = Scatter3DUtil.SharedPointMat;
        Scatter3DUtil.ForceTransparent(bubbleMaterial, zWrite: true);
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        _BaseColorID = Shader.PropertyToID("_BaseColor");
        _ColorID = Shader.PropertyToID("_Color");
        EnsureSphereMesh();
    }

    static void EnsureSphereMesh()
    {
        if (_sphereMesh) return;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Destroy(tmp);
        else DestroyImmediate(tmp);
    }

    public void EnsureRoot() { }
    public void SetEnabled(bool enabledState) => enabled = enabledState;
    public void SetVisible(bool visible) { _visible = visible; }

    public void Redraw(IReadOnlyList<ScatterPoint> data, int year)
    {
        if (!axes) return;

        // filter by year
        _rows.Clear();
        for (int i = 0; i < data.Count; i++)
            if (data[i].year == year) _rows.Add(data[i]);

        // clear groups for reuse
        foreach (var kv in _groups) kv.Value.Clear();

        float xLenFactor = scaleWithXLen ? Mathf.Max(minDiameter, axes.xLen * bubbleScale) : 1f;

        if (!_sel) _sel = GetComponent<Scatter3DSelection>();
        bool any = _sel != null && _sel.fadeWhenAnySelected && _sel.AnySelected;
        float fadedAlpha = _sel != null ? Mathf.Clamp01(_sel.fadeOthersAlpha / 1.2f) : 0.25f;

        var basePos = axes.origin ? axes.origin.position : transform.position;
        var normX = axes.axisX.normalized;
        var normZ = axes.axisZ.normalized;
        var normY = axes.axisY.normalized;
        float xLen = axes.xLen, zLen = axes.zLen, yLen = axes.yLen;
        Vector2 xDom = axes.xDomain, zDom = axes.zDomain, yDom = axes.yDomain;
        ScaleType xSc = axes.xScale, zSc = axes.zScale, ySc = axes.yScale;

        for (int i = 0; i < _rows.Count; i++)
        {
            var p = _rows[i];

            float nx = Normalize(p.x, xDom, xSc);
            float nz = Normalize(p.z, zDom, zSc);
            float ny = Normalize(p.y, yDom, ySc);
            var pos = basePos + normX * (nx * xLen) + normZ * (nz * zLen) + normY * (ny * yLen);

            float diameter = p.size * 2f;
            if (scaleWithXLen) diameter *= xLenFactor;
            diameter = Mathf.Max(minDiameter, diameter);

            var col = p.color;
            col.a = (any && _sel != null && !_sel.IsSelected(p.country)) ? fadedAlpha : 1f;
            col = Scatter3DUtil.BoostColor(col, 1.2f, 1f);

            if (!_groups.TryGetValue(col, out var list))
            {
                list = new List<Matrix4x4>(64);
                _groups[col] = list;
            }
            list.Add(Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * diameter));
        }
    }

    void LateUpdate()
    {
        if (!_visible || !bubbleMaterial) return;
        EnsureSphereMesh();

        foreach (var kv in _groups)
        {
            var matrices = kv.Value;
            if (matrices.Count == 0) continue;

            var col = kv.Key;
            _mpb.SetColor(_BaseColorID, col);
            _mpb.SetColor(_ColorID, col);

            int offset = 0;
            while (offset < matrices.Count)
            {
                int batch = Mathf.Min(BATCH_MAX, matrices.Count - offset);

                // Build contiguous array for this batch
                var batchArr = GetBatchArray(batch);
                for (int i = 0; i < batch; i++)
                    batchArr[i] = matrices[offset + i];

                Graphics.DrawMeshInstanced(
                    _sphereMesh, 0, bubbleMaterial,
                    batchArr, batch, _mpb,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false);

                offset += batch;
            }
        }
    }

    // reusable batch array to avoid per-frame allocs
    static Matrix4x4[] _batchBuf;
    static Matrix4x4[] GetBatchArray(int minSize)
    {
        if (_batchBuf == null || _batchBuf.Length < minSize)
            _batchBuf = new Matrix4x4[Mathf.Max(BATCH_MAX, Mathf.NextPowerOfTwo(minSize))];
        return _batchBuf;
    }

    public Vector3 Map(float x, float z, float y)
    {
        float nx = Normalize(x, axes.xDomain, axes.xScale);
        float nz = Normalize(z, axes.zDomain, axes.zScale);
        float ny = Normalize(y, axes.yDomain, axes.yScale);

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
            return Mathf.InverseLerp(la, lb, lv);
        }
        else if (scale == ScaleType.Sqrt)
        {
            return Mathf.InverseLerp(Mathf.Sqrt(a), Mathf.Sqrt(b), Mathf.Sqrt(Mathf.Max(v, 0f)));
        }
        else
        {
            return Mathf.InverseLerp(a, b, v);
        }
    }
}
