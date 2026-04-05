using System.Collections.Generic;
using UnityEngine;

public class Scatter3DTrails : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Scatter3DAxes axes;
    [SerializeField] private Scatter3DPoints points;
    [SerializeField] private Scatter3DSelection selection;

    [Header("Trail Visibility")]
    public bool onlyForSelected = true;
    public bool showWhenNothingSelected = true;

    [Header("Trail Line")]
    public bool scaleWidthWithXLen = true;
    public float width = 0.005f;
    public float widthScale = 1.0f;
    public float minWidth = 0.001f;
    [Range(0, 1)] public float trailAlpha = 0.5f;

    [Header("Trail Nodes")]
    public bool showNodes = true;
    public bool scaleNodeWithXLen = true;
    public float nodeScale = 2.2f;
    public float nodeGlobalScale = 1.0f;
    public float nodeMinDiameter = 0.004f;
    public int nodeEveryNYears = 5;

    [SerializeField] private Material bubbleMaterial;

    public System.Func<float, float, float, Vector3> Map;

    Transform _root;

    readonly Dictionary<string, LineRenderer> _lrByCountry = new();

    // ---- caches ----
    readonly Dictionary<string, List<ScatterPoint>> _byCountry = new();
    readonly Dictionary<string, Color> _trailColorByCountry = new();
    readonly Dictionary<string, Vector3[]> _posBufByCountry = new();
    readonly List<string> _keyBuf = new();

    // ---- GPU-batched trail nodes (group by color, one draw per color) ----
    static Mesh _sphereMesh;
    static MaterialPropertyBlock _nodeMpb;
    static int _BaseColorID, _ColorID;
    const int BATCH_MAX = 1023;

    // color -> matrices for this frame's trail nodes
    readonly Dictionary<Color, List<Matrix4x4>> _nodeGroups = new();

    // reusable sort delegate
    static readonly System.Comparison<ScatterPoint> YearCompare = (a, b) => a.year.CompareTo(b.year);

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
        if (_nodeMpb == null) _nodeMpb = new MaterialPropertyBlock();
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

    public void UpdateTrails(IReadOnlyList<ScatterPoint> data, int uptoYear)
    {
        EnsureRoot();
        if (Map == null) return;

        // clear node groups
        foreach (var kv in _nodeGroups) kv.Value.Clear();

        bool anySelected = selection && selection.AnySelected;
        if (onlyForSelected && !anySelected && !showWhenNothingSelected)
        {
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

        // Build by-country <= uptoYear
        for (int i = 0; i < data.Count; i++)
        {
            var p = data[i];
            if (p.year > uptoYear) continue;

            if (onlyForSelected && anySelected && (selection != null) && !selection.IsSelected(p.country))
                continue;

            if (!_byCountry.TryGetValue(p.country, out var list))
            {
                list = new List<ScatterPoint>(32);
                _byCountry[p.country] = list;

                var c = p.color; c.a = trailAlpha;
                _trailColorByCountry[p.country] = Scatter3DUtil.BoostColor(c, 1f, 1f);
            }

            list.Add(p);
        }

        // Sort each country's rows by year
        foreach (var kv in _byCountry)
            kv.Value.Sort(YearCompare);

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

            Color col;
            if (!_trailColorByCountry.TryGetValue(country, out col))
            {
                var c = rows[0].color; c.a = trailAlpha;
                col = Scatter3DUtil.BoostColor(c, 1f, 1f);
            }

            lr.startColor = lr.endColor = col;
            Scatter3DUtil.ApplyColor(lr, col);

            // Nodes — add to color-grouped batch for instanced rendering
            if (showNodes)
            {
                float nodeFactor = Mathf.Max(0.001f, axes.xLen * nodeScale);

                if (!_nodeGroups.TryGetValue(col, out var nodeList))
                {
                    nodeList = new List<Matrix4x4>(32);
                    _nodeGroups[col] = nodeList;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    bool place = (nodeEveryNYears <= 1) || (rows[i].year % nodeEveryNYears == 0) || i == 0 || i == rows.Count - 1;
                    if (!place) continue;

                    float diameter = rows[i].size * 2f * nodeFactor * nodeGlobalScale;
                    diameter = Mathf.Max(nodeMinDiameter, diameter);

                    nodeList.Add(Matrix4x4.TRS(positions[i], Quaternion.identity, Vector3.one * diameter));
                }
            }
        }
    }

    void LateUpdate()
    {
        if (!showNodes || !bubbleMaterial) return;
        EnsureSphereMesh();

        foreach (var kv in _nodeGroups)
        {
            var matrices = kv.Value;
            if (matrices.Count == 0) continue;

            var col = kv.Key;
            _nodeMpb.SetColor(_BaseColorID, col);
            _nodeMpb.SetColor(_ColorID, col);

            int offset = 0;
            while (offset < matrices.Count)
            {
                int batch = Mathf.Min(BATCH_MAX, matrices.Count - offset);

                var batchArr = GetBatchArray(batch);
                for (int i = 0; i < batch; i++)
                    batchArr[i] = matrices[offset + i];

                Graphics.DrawMeshInstanced(
                    _sphereMesh, 0, bubbleMaterial,
                    batchArr, batch, _nodeMpb,
                    UnityEngine.Rendering.ShadowCastingMode.Off, false);

                offset += batch;
            }
        }
    }

    static Matrix4x4[] _batchBuf;
    static Matrix4x4[] GetBatchArray(int minSize)
    {
        if (_batchBuf == null || _batchBuf.Length < minSize)
            _batchBuf = new Matrix4x4[Mathf.Max(BATCH_MAX, Mathf.NextPowerOfTwo(minSize))];
        return _batchBuf;
    }

    public void ClearTrail(string country)
    {
        if (_lrByCountry.TryGetValue(country, out var lr) && lr)
        {
#if UNITY_EDITOR
            DestroyImmediate(lr.gameObject);
#else
            Destroy(lr.gameObject);
#endif
        }
        _lrByCountry.Remove(country);
        _posBufByCountry.Remove(country);
        _trailColorByCountry.Remove(country);
    }
}
