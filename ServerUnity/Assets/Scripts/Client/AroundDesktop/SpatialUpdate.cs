using Newtonsoft.Json.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class EdgeBandsFromSpatial : MonoBehaviour
{
    [Header("Target UI")]
    [SerializeField] private EdgeSpaceAllEdgesBands edgeBands;

    [Header("Year selection payload")]
    public string yearSelectId = "selectAlongTopEdge";
    public string[] yearSelectStreamKeys =
    {
        "primitives.forwardPointerToTopEdge.measurement",
        "primitives.forwardPointerToTopEdge",
        "primitives.forwardPointerToTopEdgeMeasurement"
    };

    public enum YearTuckMode { ShowOnly, TuckOnly }
    public YearTuckMode yearTuckMode = YearTuckMode.ShowOnly;

    [Header("Edge move payloads (fade band when state==true)")]
    public string moveLeftId = "moveAlongLeftEdge";
    public string moveRightId = "moveAlongRightEdge";
    public string moveBottomId = "moveAlongBottomEdge";

    [Range(0f, 1f)] public float activeFadeAlpha = 0.25f;

    [Header("When ANY move band is active, dim the other bands")]
    [Range(0f, 1f)] public float otherBandsAlphaWhenDimmed = 0.45f;

    [Header("Top-edge binning")]
    public float fallbackWidthPx = 1920f;
    public int binsOverride = 0;
    public bool invertBins = false;

    [Header("Animation pumping (needed if edgeBands.followEveryFrame is false)")]
    [Range(0, 120)] public int refreshPumpFrames = 25;

    [Header("Debug")]
    public bool verboseLogs = false;

    private int _lastYearIdx = -1;

    private bool _leftActive;
    private bool _rightActive;
    private bool _bottomActive;

    private int _pumpLeft;

    private void Reset()
    {
        if (!edgeBands) edgeBands = FindFirstObjectByType<EdgeSpaceAllEdgesBands>();
    }

    private void Awake()
    {
        if (!edgeBands) edgeBands = FindFirstObjectByType<EdgeSpaceAllEdgesBands>();
    }

    private void Update()
    {
        if (!edgeBands || edgeBands.followEveryFrame) return;

        if (_pumpLeft > 0)
        {
            _pumpLeft--;
            edgeBands.Refresh();
        }
    }

    /// <summary>Call for every spatial payload you receive.</summary>
    public bool HandleSpatialMessage(JObject payload)
    {
        if (!edgeBands || payload == null) return false;

        bool changed = false;
        changed |= HandleMoveFade(payload);
        changed |= HandleYearSelect(payload);

        if (changed)
        {
            RequestPump();
            if (!edgeBands.followEveryFrame) edgeBands.Refresh();
        }

        return changed;
    }

    // ------------------------
    // Move fade
    // ------------------------
    private bool HandleMoveFade(JObject payload)
    {
        string id = (string)payload["id"];
        if (string.IsNullOrEmpty(id)) return false;

        bool isLeft = id == moveLeftId;
        bool isRight = id == moveRightId;
        bool isBottom = id == moveBottomId;
        if (!isLeft && !isRight && !isBottom) return false;

        if (!TryGetState(payload, out bool state))
        {
            if (verboseLogs) Debug.Log($"[EdgeBandsFromSpatial] '{id}': state not found.");
            return false;
        }

        // set initial state to false
        _leftActive = isLeft && state; 
        _rightActive = isRight && state; 
        _bottomActive = isBottom && state;
        ApplyEdgeFades();

        if (verboseLogs)
            Debug.Log($"[EdgeBandsFromSpatial] Move state: L={_leftActive} R={_rightActive} B={_bottomActive}");

        return true;
    }

    private void ApplyEdgeFades()
    {
        bool any = _leftActive || _rightActive || _bottomActive;

        if (!any)
        {
            // Back to normal: clear fades
            edgeBands.SetBandFade(EdgeSpaceAllEdgesBands.EdgeBand.Left, false, 1f, refreshNow: false);
            edgeBands.SetBandFade(EdgeSpaceAllEdgesBands.EdgeBand.Right, false, 1f, refreshNow: false);
            edgeBands.SetBandFade(EdgeSpaceAllEdgesBands.EdgeBand.Bottom, false, 1f, refreshNow: false);
            return;
        }

        float dimA = Mathf.Clamp01(otherBandsAlphaWhenDimmed);

        edgeBands.SetBandFade(EdgeSpaceAllEdgesBands.EdgeBand.Left, true,
            _leftActive ? activeFadeAlpha : dimA, refreshNow: false);

        edgeBands.SetBandFade(EdgeSpaceAllEdgesBands.EdgeBand.Right, true,
            _rightActive ? activeFadeAlpha : dimA, refreshNow: false);

        edgeBands.SetBandFade(EdgeSpaceAllEdgesBands.EdgeBand.Bottom, true,
            _bottomActive ? activeFadeAlpha : dimA, refreshNow: false);
    }

    // ------------------------
    // Year select (no toggle)
    // ------------------------
    private bool HandleYearSelect(JObject payload)
    {
        string id = (string)payload["id"];
        if (string.IsNullOrEmpty(id) || id != yearSelectId) return false;

        if (!TryGetStreams(payload, out JObject streams)) return false;

        JToken pt = null;
        for (int i = 0; i < yearSelectStreamKeys.Length; i++)
        {
            pt = streams[yearSelectStreamKeys[i]];
            if (pt != null) break;
        }
        if (pt == null) return false;

        int bins = GetBins();
        if (!TryComputeBinIndex(pt, bins, out int idx, out float t)) return false;

        if (idx == _lastYearIdx) return false;
        _lastYearIdx = idx;

        ApplyYearTuck(idx, bins);

        if (verboseLogs)
            Debug.Log($"[EdgeBandsFromSpatial] Year bin: t={t:F3} idx={idx}/{bins} mode={yearTuckMode}");

        return true;
    }

    private void ApplyYearTuck(int selectedIndex, int bins)
    {
        bool tuckAll = (yearTuckMode == YearTuckMode.ShowOnly);

        // First set everything to a baseline
        for (int i = 0; i < bins; i++)
            edgeBands.SetYearCardTuckByIndex(i, tuckAll, refreshNow: false);

        // Then invert just the selected one
        edgeBands.SetYearCardTuckByIndex(selectedIndex, !tuckAll, refreshNow: false);
    }

    private void RequestPump()
    {
        if (!edgeBands || edgeBands.followEveryFrame) return;
        if (refreshPumpFrames <= 0) return;
        _pumpLeft = Mathf.Max(_pumpLeft, refreshPumpFrames);
    }

    // ------------------------
    // Helpers
    // ------------------------
    private int GetBins()
    {
        if (binsOverride > 0) return Mathf.Max(1, binsOverride);
        int n = (edgeBands.yearTexts != null && edgeBands.yearTexts.Length > 0) ? edgeBands.yearTexts.Length : 4;
        return Mathf.Max(1, n);
    }

    private bool TryComputeBinIndex(JToken pt, int bins, out int idx, out float t)
    {
        idx = 0;
        t = 0f;

        var seg = pt["segment"]?["pixel"];
        float ax = ReadFloat(seg?["A"]?["x"]);
        float bx = ReadFloat(seg?["B"]?["x"]);

        bool haveEnds = float.IsFinite(ax) && float.IsFinite(bx);
        float minX = haveEnds ? Mathf.Min(ax, bx) : 0f;
        float maxX = haveEnds ? Mathf.Max(ax, bx) : fallbackWidthPx;

        float projX = ReadFloat(pt["projected"]?["pixel"]?["x"]);
        if (!float.IsFinite(projX)) return false;

        float denom = Mathf.Max(1f, maxX - minX);
        t = Mathf.Clamp01((projX - minX) / denom);
        if (invertBins) t = 1f - t;

        idx = Mathf.Clamp(Mathf.FloorToInt(t * bins), 0, bins - 1);
        return true;
    }

    private static bool TryGetStreams(JObject payload, out JObject streamsObj)
    {
        streamsObj = null;
        var tok = payload["streams"];
        if (tok == null) return false;

        if (tok.Type == JTokenType.Object) { streamsObj = (JObject)tok; return true; }
        if (tok.Type == JTokenType.String)
        {
            try { streamsObj = JObject.Parse((string)tok); return true; }
            catch { return false; }
        }
        return false;
    }

    private static bool TryGetState(JObject payload, out bool state)
    {
        // direct fields
        if (TryParseBoolish(payload["state"], out state)) return true;
        if (TryParseBoolish(payload["active"], out state)) return true;
        if (TryParseBoolish(payload["value"], out state)) return true;

        // streams
        if (!TryGetStreams(payload, out JObject streams)) return false;

        if (TryParseBoolish(streams["state"], out state)) return true;

        foreach (var p in streams.Properties())
        {
            if (p.Name != null && p.Name.EndsWith(".state", System.StringComparison.OrdinalIgnoreCase))
                if (TryParseBoolish(p.Value, out state)) return true;
        }

        return false;
    }

    private static bool TryParseBoolish(JToken tok, out bool value)
    {
        value = false;
        if (tok == null) return false;

        if (tok.Type == JTokenType.Boolean) { value = (bool)tok; return true; }
        if (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float) { value = ((float)tok) != 0f; return true; }

        if (tok.Type == JTokenType.String)
        {
            var s = ((string)tok).Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "on") { value = true; return true; }
            if (s == "false" || s == "0" || s == "no" || s == "off") { value = false; return true; }
            return false;
        }

        if (tok.Type == JTokenType.Object)
        {
            var o = (JObject)tok;
            var inner = o["value"] ?? o["v"] ?? o["current"];
            return inner != null && TryParseBoolish(inner, out value);
        }

        return false;
    }

    private static float ReadFloat(JToken tok)
    {
        if (tok == null) return float.NaN;
        if (tok.Type == JTokenType.Float || tok.Type == JTokenType.Integer) return (float)tok;

        if (tok.Type == JTokenType.String &&
            float.TryParse((string)tok,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;

        return float.NaN;
    }
}
