using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class LocalSpatialListener : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Scatter3DPoseUpdate poseApplier;
    [SerializeField] private EdgeBandsFromSpatial updateSeattleCards;
    [SerializeField] private Scatter3DController controller;

    [Header("Filter")]
    public string requiredId = "bboxPublishTab";

    // ===================== TILT ARC (Visuals) =====================
    [Header("Tilt Arc (visuals)")]
    [SerializeField] private Material arcMaterial;
    [SerializeField] private float arcWidth = 0.003f;
    [SerializeField] private int arcSegments = 64;
    [SerializeField] private Color arcBgColor = new(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color arcFgColor = new(0.2f, 0.85f, 0.4f, 0.95f);
    [SerializeField] private Color textColor = Color.white;

    // Wedge (fills gap between edges)
    [SerializeField] private float wedgeRadiusScale = 3.0f; // inner fill size
    [SerializeField] private float wedgeRadiusMin = 0.03f;
    [SerializeField] private float wedgeRadiusMax = 0.20f;

    // >>> ARC-ONLY growth & outward push (does not move the wedge center) <<<
    [SerializeField] private float arcOuterScale = 1.5f;     // >1 makes arc larger
    [SerializeField] private float arcOnlyOutwardOffset = 0.01f; // meters along plane normal

    // z-lift to avoid z-fighting
    [SerializeField] private float labelDepthOffset = 0.003f;

    // Wedge fill
    MeshFilter _wedgeMF;
    MeshRenderer _wedgeMR;
    Material _wedgeMat;

    Transform _tiltArcRoot;
    LineRenderer _arcBg, _arcFg;
    TextMesh _tiltText;

    // ===================== PROX BRIDGE (Visuals) =====================
    [Header("Proximity (bridge)")]
    [SerializeField] private Color proximityColor = new(1f, 0.8f, 0.2f, 0.6f);
    [SerializeField] private Material proximityMaterial;

    Transform _proxRoot;
    MeshFilter _proxMF;
    MeshRenderer _proxMR;
    TextMesh _proxText;
    MaterialPropertyBlock _proxMPB;

    // ===================== PERF KNOBS / STATE =====================
    [Header("Performance")]
    //[SerializeField] private float minUpdateInterval = 0.03f; // 33 ms
    [SerializeField] private float posEps = 0.01f;
    [SerializeField] private float tiltEpsDeg = 3f;
    [SerializeField] private float proxEps = 0.01f;

    float _nextAllowedUpdate;

    // last state (tilt)
    bool _haveLastTilt;
    Vector3 _la0, _la1, _lb0, _lb1;
    float _lastSpeed, _lastTiltDeg;

    // last state (proximity)
    bool _haveLastProx;
    Vector3 _pa0, _pa1, _pb0, _pb1;
    float _lastProxFade;

    // arc point buffers
    Vector3[] _bgPts, _fgPts;

    // shared lists for meshes
    static readonly List<Vector3> _verts = new();
    static readonly List<int> _tris = new();

    static readonly Regex EdgeTupleRegex = new(@"\(\s*([^\)]+?)\s*\)", RegexOptions.Compiled);

    void Awake()
    {
        if (!poseApplier) poseApplier = FindObjectOfType<Scatter3DPoseUpdate>();
        if (!updateSeattleCards) updateSeattleCards = FindObjectOfType<EdgeBandsFromSpatial>();
        if (!controller) controller = FindObjectOfType<Scatter3DController>();
    }

    void OnEnable() => SpatialEventBus.OnSpatial += Handle;
    void OnDisable() => SpatialEventBus.OnSpatial -= Handle;

    void Handle(SpatialEvent e)
    {
        if (e.type != "spatial_observable") return;
        //if (Time.unscaledTime < _nextAllowedUpdate) return;
        //_nextAllowedUpdate = Time.unscaledTime + minUpdateInterval;

        // forward bbox
        if (poseApplier != null &&
            (!string.IsNullOrEmpty(requiredId)))
        {
            var root = new JObject
            {
                ["id"] = e.id,
                ["type"] = e.type,
                ["state"] = e.state,
                ["streams"] = e.GetStreamsJObject()
            };
            if (e.id == "bboxPublishTab") poseApplier.TryApplyFromSpatial(root);
            if (e.id == "selectAlongTopEdge" || e.id == "moveAlongLeftEdge" || e.id == "moveAlongRightEdge" || e.id == "moveAlongBottomEdge") updateSeattleCards.HandleSpatialMessage(root);
        }

        return;
        // ----- TILT -----
        if (e.id == "cornerContactTLtoTRPivot")
        {
            if(e.state == false)
            {
                SetTiltArcActive(false);
                _haveLastTilt = false;
                return;
            }
            else if (TryParseTiltStreams(e.streamsJson, out var a0, out var a1, out var b0, out var b1, out var tiltDeg))
            {
                float t = Mathf.Clamp(tiltDeg, 0f, 90f);
                float speed = (t < 10f) ? 0f : Quantize(Map(t, 10f, 90f, 0.25f, 4f), 0.25f);
                speed = Mathf.Clamp(speed, 0f, 4f);

                if (speed < 1e-3f)
                {
                    SetTiltArcActive(false);
                    return;
                }
                bool edgesChanged = EdgesChanged(a0, a1, b0, b1, true);
                bool tiltChanged = !_haveLastTilt || Mathf.Abs(_lastTiltDeg - t) > tiltEpsDeg || Mathf.Abs(_lastSpeed - speed) > 1e-3f;

                if (edgesChanged || tiltChanged)
                {
                    //DrawTiltVisual(a0, a1, b0, b1, speed, IsOn(e.state), cam);
                    RememberEdges(a0, a1, b0, b1, true);
                    _lastTiltDeg = t; _lastSpeed = speed; _haveLastTilt = true;
                }
            }
        }
        if (e.id == "cornerContactTLtoTR")
        {
            if (e.state == false)
            {
                SetTiltArcActive(false);
                _haveLastTilt = false;
                return;
            }
        }

        // ----- PROX -----
        if (e.id == "rightSideProximityy")
        {
            if (e.state == false)
            {
                SetProxActive(false);
                _haveLastProx = false;
                return;
            }
            else if (TryParseProximityStreams(e.streamsJson, out var aR0, out var aR1, out var bL0, out var bL1, out var prox))
            {
                float fade = ProxToFade(prox);
                if (fade < 1e-3f)
                {
                    SetProxActive(false);
                    if (controller) controller.SetFade(0, redraw: true);
                    return;
                }
                bool edgesChanged = EdgesChanged(aR0, aR1, bL0, bL1, false);
                bool proxChanged = !_haveLastProx || Mathf.Abs(_lastProxFade - fade) > proxEps;

                if (edgesChanged || proxChanged)
                {
                    //DrawProximityBridge(aR0, aR1, bL0, bL1, fade, IsOn(e.state), cam);
                    RememberEdges(aR0, aR1, bL0, bL1, false);
                    _lastProxFade = fade; _haveLastProx = true;

                    if (controller) controller.SetFade(fade, redraw: proxChanged);
                }
            }
        }

        if (e.id == "coplanarParallelEdge")
        {
            if (e.state == false)
            {
                SetProxActive(false);
                _haveLastProx = false;
                return;
            }
        }
    }

    // ===================== TILT ARC =====================
    void DrawTiltVisual(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, float speed, bool active, Camera cam)
    {
        EnsureTiltArc();

        // pick pivot pairing
        float d00 = (a0 - b0).sqrMagnitude, d11 = (a1 - b1).sqrMagnitude;
        float d01 = (a0 - b1).sqrMagnitude, d10 = (a1 - b0).sqrMagnitude;
        int touch = 0; float best = d00;
        if (d11 < best) { best = d11; touch = 1; }
        if (d01 < best) { best = d01; touch = 2; }
        if (d10 < best) { best = d10; touch = 3; }

        Vector3 center, dirA, dirB;
        switch (touch)
        {
            case 0: center = 0.5f * (a0 + b0); dirA = (a1 - a0); dirB = (b1 - b0); break;
            case 1: center = 0.5f * (a1 + b1); dirA = (a0 - a1); dirB = (b0 - b1); break;
            case 2: center = 0.5f * (a0 + b1); dirA = (a1 - a0); dirB = (b0 - b1); break;
            default: center = 0.5f * (a1 + b0); dirA = (a0 - a1); dirB = (b1 - b0); break;
        }
        if (dirA.sqrMagnitude < 1e-12f) dirA = (a1 - a0);
        if (dirB.sqrMagnitude < 1e-12f) dirB = (b1 - b0);
        if (dirA.sqrMagnitude < 1e-12f) dirA = Vector3.right;
        if (dirB.sqrMagnitude < 1e-12f) dirB = Vector3.forward;
        dirA.Normalize(); dirB.Normalize();

        // plane basis
        Vector3 normal = Vector3.Cross(dirA, dirB);
        if (normal.sqrMagnitude < 1e-10f) normal = cam ? cam.transform.forward : Vector3.up;
        normal.Normalize();
        Vector3 right = dirA;
        Vector3 up = Vector3.Cross(normal, right).normalized;
        if (up.sqrMagnitude < 1e-12f) up = Vector3.Cross(right, normal).normalized;

        // angular span
        float angB = Mathf.Atan2(Vector3.Dot(dirB, up), Vector3.Dot(dirB, right)) * Mathf.Rad2Deg;
        angB = Mathf.DeltaAngle(0f, angB);
        float startDeg = 0f, endDeg = angB;
        if (endDeg < 0f) { float t = startDeg; startDeg = endDeg; endDeg = t; }

        // wedge radius (inner)
        float rA = Mathf.Max(wedgeRadiusMin, Mathf.Min((a0 - center).magnitude, (a1 - center).magnitude));
        float rB = Mathf.Max(wedgeRadiusMin, Mathf.Min((b0 - center).magnitude, (b1 - center).magnitude));
        float wedgeRadius = Mathf.Clamp(Mathf.Min(rA, rB) * wedgeRadiusScale, wedgeRadiusMin, wedgeRadiusMax);

        // ARC-ONLY bigger & outward
        float arcRadius = wedgeRadius * Mathf.Max(1f, arcOuterScale);
        Vector3 arcPivot = center + normal * Mathf.Max(0f, arcOnlyOutwardOffset);

        // draw inner wedge (unchanged center)
        //BuildWedgeMesh(_wedgeMF, center, right, up, wedgeRadius, startDeg, endDeg);

        // background + foreground arcs using arcPivot/arcRadius
        DrawAnalogArc(arcPivot, arcRadius, startDeg, endDeg, right, up, _arcBg, arcBgColor);
        float endFill = Mathf.Lerp(startDeg, endDeg, Mathf.Clamp01(speed / 2.2f));
        DrawAnalogArc(arcPivot, arcRadius, startDeg, endFill, right, up, _arcFg, arcFgColor);

        // label on the arc radius, slightly lifted
        float mid = 0.5f * (startDeg + endDeg);
        Vector3 bis = Quaternion.AngleAxis(mid, normal) * right;
        Vector3 pos = arcPivot + bis * (arcRadius * 0.78f) + normal * labelDepthOffset;

        EnsureText(ref _tiltText, "speedLabel", _tiltArcRoot, textColor);
        UpdateSimpleLabel(_tiltText, $"{speed:0.0}×", pos, normal, up,
                          worldCharSize: Mathf.Clamp(arcRadius * 0.025f, 0.002f, 0.05f),
                          flipX: true);

        SetTiltArcActive(active && speed >= 0f); // keep visible even if 0x
    }

    void BuildWedgeMesh(MeshFilter mf, Vector3 center, Vector3 right, Vector3 up,
                        float radius, float startDeg, float endDeg)
    {
        if (!mf) return;
        var mesh = mf.sharedMesh; mesh.Clear();

        if (endDeg < startDeg) { float t = startDeg; startDeg = endDeg; endDeg = t; }
        int segs = Mathf.Max(8, arcSegments);
        float sweep = Mathf.Clamp(endDeg - startDeg, 0.5f, 180f);

        _verts.Clear(); _tris.Clear();
        _verts.Add(center);
        float s = startDeg * Mathf.Deg2Rad, step = (sweep * Mathf.Deg2Rad) / segs;

        for (int i = 0; i <= segs; i++)
        {
            float a = s + step * i;
            Vector3 dir = Mathf.Cos(a) * right + Mathf.Sin(a) * up;
            _verts.Add(center + dir.normalized * radius);
        }
        for (int i = 0; i < segs; i++)
        { _tris.Add(0); _tris.Add(i + 1); _tris.Add(i + 2); }

        mesh.SetVertices(_verts);
        mesh.SetTriangles(_tris, 0, true);

        if (_wedgeMR && _wedgeMat)
        {
            var c = arcFgColor; c.a = 0.25f;
            _wedgeMat.color = c;
            _wedgeMR.sharedMaterial = _wedgeMat;
        }
    }

    // ===================== PROX BRIDGE =====================
    void DrawProximityBridge(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, float fade, bool active, Camera cam)
    {
        EnsureProximityVis();

        // pair endpoints to minimize crossing
        float s1 = (a0 - b0).sqrMagnitude + (a1 - b1).sqrMagnitude;
        float s2 = (a0 - b1).sqrMagnitude + (a1 - b0).sqrMagnitude;
        Vector3 p00, p01, p10, p11;
        if (s1 <= s2) { p00 = a0; p01 = a1; p10 = b0; p11 = b1; }
        else { p00 = a0; p01 = a1; p10 = b1; p11 = b0; }

        var mesh = _proxMF.sharedMesh; mesh.Clear();

        Vector3 n = Vector3.Cross((p01 - p00), (p10 - p00)).normalized;
        if (cam && Vector3.Dot(n, cam.transform.forward) > 0f)
        { (p00, p10) = (p10, p00); (p01, p11) = (p11, p01); n = -n; }

        _verts.Clear(); _tris.Clear();
        _verts.Add(p00); _verts.Add(p01); _verts.Add(p11); _verts.Add(p10);
        _tris.Add(0); _tris.Add(1); _tris.Add(2);
        _tris.Add(0); _tris.Add(2); _tris.Add(3);

        mesh.SetVertices(_verts);
        mesh.SetTriangles(_tris, 0, true);

        // alpha via property block
        var c = proximityColor; c.a = Mathf.Clamp01(fade);
        _proxMPB ??= new MaterialPropertyBlock();
        _proxMPB.SetColor("_Color", c);
        _proxMR.SetPropertyBlock(_proxMPB);

        // ALWAYS show label (even if fade==0)
        Vector3 center = 0.25f * (p00 + p01 + p11 + p10);
        Vector3 along = ((p01 - p00).sqrMagnitude >= (p11 - p10).sqrMagnitude)
                        ? (p01 - p00).normalized : (p11 - p10).normalized;
        if (cam && Vector3.Dot(n, cam.transform.forward) > 0f) n = -n;

        float span = Mathf.Max(0.001f, Mathf.Min((p01 - p00).magnitude, (p11 - p10).magnitude));
        EnsureText(ref _proxText, "fadeLabel", _proxRoot, textColor);
        UpdateSimpleLabel(_proxText, $"α {fade:0.0}", center + n * labelDepthOffset, n, along,
                          worldCharSize: Mathf.Clamp(span * 0.02f, 0.002f, 0.06f),
                          flipX: true);

        SetProxActive(active); // no gating by fade
    }

    // ===================== SIMPLE LABEL HELPERS =====================
    void EnsureText(ref TextMesh field, string name, Transform parent, Color color)
    {
        if (field) return;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tm = go.AddComponent<TextMesh>();
        tm.fontSize = 64;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = color;
        var mr = tm.GetComponent<MeshRenderer>();
        if (mr) mr.sortingOrder = 32767; // render on top
        field = tm;
    }

    void UpdateSimpleLabel(TextMesh tm, string text, Vector3 worldPos, Vector3 planeNormal, Vector3 upInPlane, float worldCharSize, bool flipX)
    {
        if (!tm) return;
        var cam = Camera.main;
        var n = planeNormal;
        if (cam && Vector3.Dot(n, cam.transform.forward) > 0f) n = -n;

        tm.text = text;
        tm.characterSize = Mathf.Max(0.001f, worldCharSize);
        tm.transform.SetPositionAndRotation(worldPos, Quaternion.LookRotation(n, -upInPlane));

        if (flipX)
        {
            var s = tm.transform.localScale;
            s.x = -Mathf.Abs(s.x);
            tm.transform.localScale = s;
        }
    }

    // ===================== ENSURE/TOGGLE VIS =====================
    void EnsureTiltArc()
    {
        if (_tiltArcRoot) return;

        _tiltArcRoot = new GameObject("TiltArcRoot").transform;
        _tiltArcRoot.SetParent(transform, false);

        _arcBg = CreateArc("arcBg", arcBgColor);
        _arcFg = CreateArc("arcFg", arcFgColor);

        var wedge = new GameObject("arcWedgeFill");
        wedge.transform.SetParent(_tiltArcRoot, false);
        _wedgeMF = wedge.AddComponent<MeshFilter>();
        _wedgeMR = wedge.AddComponent<MeshRenderer>();
        _wedgeMF.sharedMesh = new Mesh { name = "WedgeFill" };
        _wedgeMF.sharedMesh.MarkDynamic();

        _wedgeMat = arcMaterial ? new Material(arcMaterial) : new Material(Shader.Find("Sprites/Default"));
        _wedgeMR.sharedMaterial = _wedgeMat;

        EnsureText(ref _tiltText, "speedLabel", _tiltArcRoot, textColor);
    }

    void EnsureProximityVis()
    {
        if (_proxRoot) return;

        _proxRoot = new GameObject("ProximityRoot").transform;
        _proxRoot.SetParent(transform, false);

        var bridge = new GameObject("bridgeQuad");
        bridge.transform.SetParent(_proxRoot, false);
        _proxMF = bridge.AddComponent<MeshFilter>();
        _proxMR = bridge.AddComponent<MeshRenderer>();
        _proxMF.sharedMesh = new Mesh { name = "ProxBridge" };
        _proxMF.sharedMesh.MarkDynamic();

        if (_proxMR.sharedMaterial == null)
            _proxMR.sharedMaterial = proximityMaterial ? new Material(proximityMaterial)
                                                       : new Material(Shader.Find("Sprites/Default"));
        _proxMPB = new MaterialPropertyBlock();

        EnsureText(ref _proxText, "fadeLabel", _proxRoot, textColor);
    }

    void SetTiltArcActive(bool on) { if (_tiltArcRoot) _tiltArcRoot.gameObject.SetActive(on); }
    void SetProxActive(bool on) { if (_proxRoot) _proxRoot.gameObject.SetActive(on); }

    LineRenderer CreateArc(string name, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_tiltArcRoot, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.widthMultiplier = arcWidth;
        lr.startColor = lr.endColor = c;
        if (arcMaterial) lr.sharedMaterial = arcMaterial;
        return lr;
    }

    void DrawAnalogArc(Vector3 center, float radius, float startDeg, float endDeg,
                       Vector3 right, Vector3 up, LineRenderer lr, Color c)
    {
        startDeg = Mathf.Min(startDeg, endDeg);
        int segs = Mathf.Max(4, Mathf.RoundToInt(arcSegments * Mathf.Clamp01((endDeg - startDeg) / 90f)));

        Vector3[] buf = (lr == _arcBg)
            ? (_bgPts == null || _bgPts.Length != segs + 1 ? _bgPts = new Vector3[segs + 1] : _bgPts)
            : (_fgPts == null || _fgPts.Length != segs + 1 ? _fgPts = new Vector3[segs + 1] : _fgPts);

        float s = startDeg * Mathf.Deg2Rad, e = endDeg * Mathf.Deg2Rad, step = (e - s) / segs;
        for (int i = 0; i <= segs; i++)
        {
            float a = s + step * i;
            buf[i] = center + (Mathf.Cos(a) * right + Mathf.Sin(a) * up) * radius;
        }
        lr.positionCount = buf.Length;
        lr.SetPositions(buf);
        lr.startColor = lr.endColor = c;
        lr.widthMultiplier = arcWidth;
    }

    // ===================== PARSING =====================
    static bool TryParseTiltStreams(string streamsJson,
        out Vector3 a0, out Vector3 a1, out Vector3 b0, out Vector3 b1, out float tiltDeg)
    {
        a0 = a1 = b0 = b1 = Vector3.zero; tiltDeg = 0f;
        JObject o; try { o = JObject.Parse(streamsJson); } catch { return false; }
        bool okA = TryParseEdgeString((string?)o["A.leftEdge"], out a0, out a1);
        bool okB = TryParseEdgeString((string?)o["B.rightEdge"], out b0, out b1);

        var tTok = o["primitives.longitudinalTiltTo90.measurement"];
        if (tTok != null)
        {
            if (tTok.Type == JTokenType.Float || tTok.Type == JTokenType.Integer) tiltDeg = tTok.Value<float>();
            else if (tTok.Type == JTokenType.String) float.TryParse(tTok.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out tiltDeg);
        }
        tiltDeg = Mathf.Clamp(tiltDeg, 0f, 90f);
        return okA && okB;
    }

    static bool TryParseProximityStreams(string streamsJson,
        out Vector3 aRight0, out Vector3 aRight1, out Vector3 bLeft0, out Vector3 bLeft1, out float prox)
    {
        aRight0 = aRight1 = bLeft0 = bLeft1 = Vector3.zero; prox = 0f;
        JObject o; try { o = JObject.Parse(streamsJson); } catch { return false; }

        bool okA = TryParseEdgeString((string?)o["A.rightEdge"], out aRight0, out aRight1);
        bool okB = TryParseEdgeString((string?)o["B.leftEdge"], out bLeft0, out bLeft1);

        var tTok = o["primitives.proximateLateralEdge.measurement"];
        if (tTok != null)
        {
            if (tTok.Type == JTokenType.Float || tTok.Type == JTokenType.Integer) prox = tTok.Value<float>();
            else if (tTok.Type == JTokenType.String) float.TryParse(tTok.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out prox);
        }
        return okA && okB;
    }

    static bool TryParseEdgeString(string s, out Vector3 p0, out Vector3 p1)
    {
        p0 = p1 = Vector3.zero; if (string.IsNullOrWhiteSpace(s)) return false;
        var m = EdgeTupleRegex.Matches(s); if (m.Count < 2) return false;

        static bool ParseOne(Match match, out Vector3 v)
        {
            v = Vector3.zero;
            var parts = match.Groups[1].Value.Split(',');
            if (parts.Length < 3) return false;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return false;
            v = new Vector3(x, y, z); return true;
        }
        return ParseOne(m[0], out p0) && ParseOne(m[1], out p1);
    }

    // ===================== CHANGE DETECTION =====================
    bool EdgesChanged(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, bool tiltMode)
    {
        float e = posEps;
        if ((a0 - (tiltMode ? _la0 : _pa0)).sqrMagnitude > e) return true;
        if ((a1 - (tiltMode ? _la1 : _pa1)).sqrMagnitude > e) return true;
        if ((b0 - (tiltMode ? _lb0 : _pb0)).sqrMagnitude > e) return true;
        if ((b1 - (tiltMode ? _lb1 : _pb1)).sqrMagnitude > e) return true;
        return false;
    }

    void RememberEdges(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, bool tiltMode)
    {
        if (tiltMode) { _la0 = a0; _la1 = a1; _lb0 = b0; _lb1 = b1; }
        else { _pa0 = a0; _pa1 = a1; _pb0 = b0; _pb1 = b1; }
    }

    // ===================== MAPS / UTILS =====================
    static float ProxToFade(float p)
    {
        if (p < 0.02f) return 0f;
        float mapped = Map(p, 0.02f, 0.12f, 0.1f, 1.0f);
        return Quantize(Mathf.Clamp01(mapped), 0.1f);
    }
    static float Map(float v, float inMin, float inMax, float outMin, float outMax)
        => Mathf.Lerp(outMin, outMax, Mathf.InverseLerp(inMin, inMax, v));
    static float Quantize(float v, float step) => step <= 0f ? v : Mathf.Round(v / step) * step;

    static bool IsOn(object stateObj)
    {
        if (stateObj is bool b) return b;
        if (stateObj is string s)
        {
            s = s.Trim().ToLowerInvariant();
            return s == "true" || s == "1" || s == "on" || s == "yes";
        }
        if (stateObj is int i) return i != 0;
        if (stateObj is long l) return l != 0;
        return true; // be permissive if unknown
    }
}
