// ————————————————————————
// 5) Manager: preload + dynamic registration
// ————————————————————————
using System.Collections.Generic;
using System;
using UniRx;
using UnityEngine;
using System.Linq;
using Unity.VisualScripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;           // <-- add this
using WebSocketSharp.Server;
using static UnityEngine.Rendering.CoreUtils;
using TMPro;
using System.Globalization;
using System.Collections;

public class ObservableManager
{
    readonly PrimitiveFactory _primitives = new PrimitiveFactory();
    readonly RuleBuilder _builder;
    public bool VerboseLogs = true;   // lifecycle logs
    public bool TraceValues = true;   // per-primitive log taps

    // store your rule streams
    readonly Dictionary<string, IObservable<RuleEvent>> _observables
      = new Dictionary<string, IObservable<RuleEvent>>();

    public ObservableManager()
    {
        _builder = new RuleBuilder(_primitives);
        //_builder = new RuleBuilder();
        //_primitives.PreloadAll();
    }

    // call this ANY time with a new spec
    public void RegisterRule(RuleSpec spec)
    {
        var stream = _builder
          .Build(spec)
          .Publish()
          .RefCount();

        _observables[spec.id] = stream;

        if (TraceValues)
        {
            stream.Subscribe(evt =>
            {
                // format the streams dictionary nicely
                string streamsDump = string.Join(", ",
                    evt.Streams.Select(kv => $"{kv.Key}={FormatForLog(kv.Value)}"));
                //var streamsDump = string.Join(", ",
                //    evt.Streams
                //       .Where(kv => string.Equals(kv.Key, "mapping", StringComparison.OrdinalIgnoreCase))
                //       .Select(kv => $"{kv.Key}={FormatForLog(kv.Value)}"));



                Debug.Log($"[Rule {spec.id}@{Time.time:F2}] State={evt.State} Streams=[{streamsDump}]");

                string streamsJson = StreamSerializer.SerializeStreamsToJson(evt.Streams);


                // Optional: broadcast (unchanged)
                WsHub.Broadcast(new { spec.id, type = "spatial_observable", state = evt.State, streams = streamsJson });

                // --- Handle rightSideProximity -> fade ---
                if (evt.State && string.Equals(spec.id, "rightSideProximity", StringComparison.Ordinal))
                {
                    if (TryGetFloat(evt.Streams, "primitives.proximateLateralEdge.measurement", out var prox))
                    {
                        // Map prox (0..0.1) -> fade (0.1..1.0)
                        float t = Mathf.Clamp01(prox / 0.1f);
                        float fade = Mathf.Lerp(0.1f, 1f, t);

                        // Find UI text object by canvas name "FilterOpacity"
                        var canvasGO = GameObject.Find("FilterOpacity");
                        var fadeText = canvasGO ? canvasGO.GetComponentInChildren<TMP_Text>(true) : null;
                        if (fadeText != null)
                            fadeText.text = $"{fade:F2}";
                        Debug.Log($"[canvas] {canvasGO}");
                        Debug.Log($"[edge] {TryGetEdge(evt.Streams, "B.leftEdge", out var b55, out var b12)}");
                        // Align canvas plane to the midpoint between the two edge midpoints
                        if (canvasGO != null &&
                            TryGetEdge(evt.Streams, "B.leftEdge", out var b0, out var b1) &&
                            TryGetEdge(evt.Streams, "A.rightEdge", out var a0, out var a1))
                        {
                            Vector3 midLeft = (b0 + b1) * 0.5f;  // midpoint of B.leftEdge
                            Vector3 midRight = (a0 + a1) * 0.5f;  // midpoint of A.rightEdge
                            Vector3 midBoth = (midLeft + midRight) * 0.5f;

                            canvasGO.transform.position = midBoth;
                            Debug.Log($"[Fade] prox={midBoth} → fade={fade:F3}");

                            // Optional: orient canvas to face normal of polygon formed by the two edges
                            canvasGO.transform.rotation = Quaternion.LookRotation(Vector3.up);

                        }

                        // (Optional) If you also want to broadcast fade:
                        // WsHub.Broadcast(new { type = "fade:update", fade });
                    }
                }

            });
        }
    }
    // --- Helpers ---
    static bool TryGetFloat(IDictionary<string, object> dict, string key, out float value)
    {
        value = 0f;
        if (!dict.TryGetValue(key, out var obj) || obj == null) return false;

        switch (obj)
        {
            case float f: value = f; return true;
            case double d: value = (float)d; return true;
            case int i: value = i; return true;
            case long l: value = l; return true;
            case string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                value = parsed; return true;
            default: return false;
        }
    }

    /// Try to read an edge as two Vector3s from streams under a key like "A.rightEdge" or "B.leftEdge".
    /// Supports:
    ///  - streams["A.rightEdge"] = [ [x,y,z], [x,y,z] ] OR [x,y,z,x,y,z]
    ///  - streams["A.rightEdge"] = { "p0":{x,y,z}, "p1":{x,y,z} }
    ///  - streams["A.rightEdge.p0"], streams["A.rightEdge.p1"] as separate entries
    static bool TryGetEdge(IDictionary<string, object> streams, string baseKey, out Vector3 p0, out Vector3 p1)
    {
        p0 = p1 = default;

        // 1) Single value under baseKey
        if (streams.TryGetValue(baseKey, out var val) && val != null)
        {
            if (TryParseEdgeValue(val, out p0, out p1)) return true;
        }

        // 2) Dotted keys: baseKey.p0 / baseKey.p1
        object v0 = null, v1 = null;
        streams.TryGetValue(baseKey + ".p0", out v0);
        streams.TryGetValue(baseKey + ".p1", out v1);
        if (v0 != null && v1 != null)
        {
            if (TryGetVector3(v0, out p0) && TryGetVector3(v1, out p1)) return true;
        }

        return false;
    }

    static bool TryParseEdgeValue(object val, out Vector3 p0, out Vector3 p1)
    {
        p0 = p1 = default;

        // Arrays/lists
        if (val is IList list)
        {
            // Case A: [[x,y,z],[x,y,z]]
            if (list.Count == 2 && TryGetVector3(list[0], out p0) && TryGetVector3(list[1], out p1))
                return true;

            // Case B: [x,y,z,x,y,z]
            if (list.Count == 6 &&
                TryGetFloatFrom(list[0], out float ax) &&
                TryGetFloatFrom(list[1], out float ay) &&
                TryGetFloatFrom(list[2], out float az) &&
                TryGetFloatFrom(list[3], out float bx) &&
                TryGetFloatFrom(list[4], out float by) &&
                TryGetFloatFrom(list[5], out float bz))
            {
                p0 = new Vector3(ax, ay, az);
                p1 = new Vector3(bx, by, bz);
                return true;
            }
        }

        // Dictionary: { p0:{x,y,z}, p1:{x,y,z} }
        if (val is IDictionary<string, object> dict)
        {
            if (dict.TryGetValue("p0", out var vp0) && dict.TryGetValue("p1", out var vp1) &&
                TryGetVector3(vp0, out p0) && TryGetVector3(vp1, out p1))
                return true;
        }

        // Fallback fail
        return false;
    }

    static bool TryGetVector3(object obj, out Vector3 v)
    {
        v = default;
        if (obj == null) return false;

        // { x:.., y:.., z:.. }
        if (obj is IDictionary<string, object> d &&
            TryGetFloatFrom(d.TryGetValue("x", out var ox) ? ox : null, out float x) &&
            TryGetFloatFrom(d.TryGetValue("y", out var oy) ? oy : null, out float y) &&
            TryGetFloatFrom(d.TryGetValue("z", out var oz) ? oz : null, out float z))
        {
            v = new Vector3(x, y, z);
            return true;
        }

        // [x,y,z]
        if (obj is IList list && list.Count >= 3 &&
            TryGetFloatFrom(list[0], out float lx) &&
            TryGetFloatFrom(list[1], out float ly) &&
            TryGetFloatFrom(list[2], out float lz))
        {
            v = new Vector3(lx, ly, lz);
            return true;
        }

        // "x,y,z"
        if (obj is string s)
        {
            var parts = s.Split(',');
            if (parts.Length >= 3 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float sx) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sy) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float sz))
            {
                v = new Vector3(sx, sy, sz);
                return true;
            }
        }

        return false;
    }

    static bool TryGetFloatFrom(object obj, out float f)
    {
        f = 0f;
        switch (obj)
        {
            case float ff: f = ff; return true;
            case double d: f = (float)d; return true;
            case int i: f = i; return true;
            case long l: f = l; return true;
            case string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                f = parsed; return true;
            default: return false;
        }
    }
    // ---------------- Mapping pretty-printers ----------------

    private static string FormatMapping(object mapping)
    {
        try
        {
            // JObject → iterate properties
            if (mapping is JObject jo)
            {
                var parts = jo.Properties()
                              .Select(p => $"{p.Name}={FormatMapValue(p.Value)}");
                return "{" + string.Join(", ", parts) + "}";
            }

            // IDictionary<string, object> → iterate pairs
            if (mapping is IDictionary<string, object> dict)
            {
                var parts = dict.Select(kv => $"{kv.Key}={FormatMapValue(kv.Value)}");
                return "{" + string.Join(", ", parts) + "}";
            }

            // Fallback: try to format as value
            return FormatMapValue(mapping);
        }
        catch (Exception ex)
        {
            return $"<mapping format error: {ex.Message}>";
        }
    }

    private static string FormatMapValue(object v)
    {
        if (v == null) return "null";

        // Unity vectors
        if (v is Vector2 v2) return $"({v2.x:F3},{v2.y:F3})";
        if (v is Vector3 v3) return $"({v3.x:F3},{v3.y:F3},{v3.z:F3})";

        // Arrays / enumerables
        if (v is IEnumerable<object> enObj)    // e.g., Vector2[]/Vector3[] boxed as object[]
            return "[" + string.Join(", ", enObj.Select(FormatMapValue)) + "]";
        if (v is System.Collections.IEnumerable en && !(v is string))
        {
            var items = new List<string>();
            foreach (var it in en) items.Add(FormatMapValue(it));
            return "[" + string.Join(", ", items) + "]";
        }

        // JTokens: handle JArray/JObject gracefully
        if (v is JArray ja)
            return "[" + string.Join(", ", ja.Select(FormatMapValue)) + "]";
        if (v is JObject jo)
            return FormatMapping(jo);
        if (v is JValue jv)
            return Convert.ToString(jv.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "null";

        // Numbers / bools / strings
        switch (v)
        {
            case float f: return f.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
            case double d: return d.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
            case decimal m: return m.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
            case bool b: return b ? "true" : "false";
            case string s: return $"\"{s}\"";
        }

        // Fallback: compact JSON or ToString
        try { return JsonConvert.SerializeObject(v); }
        catch { return v.ToString(); }
    }

    // ---------------- Logging helpers (unchanged) ----------------

    private const int MaxLogDepth = 4;
    private const int MaxItemsPerLevel = 8;
    private const int MaxStringLen = 256;

    private static string FormatForLog(object obj) =>
        FormatForLog(obj, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static string FormatForLog(object obj, int depth, HashSet<object> seen)
    {
        if (depth > MaxLogDepth) return "…";
        if (obj == null) return "null";
        if (!(obj is ValueType))
        {
            if (!seen.Add(obj)) return "↻";
        }

        switch (obj)
        {
            case Vector3 v3: return $"({v3.x:F3},{v3.y:F3},{v3.z:F3})";
            case Vector2 v2: return $"({v2.x:F3},{v2.y:F3})";
            case Vector4 v4: return $"({v4.x:F3},{v4.y:F3},{v4.z:F3},{v4.w:F3})";
            case Quaternion q: return $"quat({q.x:F3},{q.y:F3},{q.z:F3},{q.w:F3})";
            case Color c: return $"rgba({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3})";
            case Bounds b: return $"bounds(center={FormatForLog(b.center, depth + 1, seen)}, size={FormatForLog(b.size, depth + 1, seen)})";
        }

        if (obj is string s) return Truncate(s);
        if (obj is char ch) return ch.ToString();
        if (obj is bool || obj is int || obj is long || obj is float || obj is double || obj is decimal)
            return Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture);

        var t = obj.GetType();
        if (Nullable.GetUnderlyingType(t) != null)
        {
            var hasValueProp = t.GetProperty("HasValue");
            var valueProp = t.GetProperty("Value");
            bool hasValue = (bool)(hasValueProp?.GetValue(obj) ?? false);
            return hasValue ? FormatForLog(valueProp.GetValue(obj), depth + 1, seen) : "null";
        }

        if (t.FullName != null && t.FullName.StartsWith("System.ValueTuple"))
            return FormatValueTuple(obj, depth, seen);

        if (obj is System.Collections.IDictionary dict)
            return FormatDictionary(dict, depth, seen);

        if (obj is System.Collections.IEnumerable en && !(obj is string))
            return FormatEnumerable(en, depth, seen);

        if (obj is IProjectionResult proj) return FormatProjection(proj, depth, seen);

        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(
                obj,
                new Newtonsoft.Json.JsonSerializerSettings
                {
                    ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
                    MaxDepth = MaxLogDepth - depth
                });
            return Truncate(json);
        }
        catch
        {
            return Truncate(obj.ToString() ?? t.Name);
        }
    }

    private static string FormatProjection(IProjectionResult proj, int depth, HashSet<object> seen)
    {
        switch (proj)
        {
            case PolygonPolygonProjection pp:
                return $"Poly⊓Poly(ratio={pp.Ratio:F4}, areaA={pp.AreaA:F4}, projArea={pp.ProjectedArea:F4}, axis={FormatForLog(pp.Axis, depth + 1, seen)}, A={FormatEnumerable(pp.PolygonA, depth + 1, seen)}, B={FormatEnumerable(pp.PolygonB, depth + 1, seen)}, inter={FormatEnumerable(pp.ProjectedPolygon, depth + 1, seen)})";
            case SegmentPolygonProjection sp:
                return $"Seg→Poly(ratio={sp.Ratio:F4}, segLen={sp.SegmentLength:F4}, projLen={sp.ProjectedLength:F4}, axis={FormatForLog(sp.Axis, depth + 1, seen)}, seg=({FormatForLog(sp.Segment.A, depth + 1, seen)}→{FormatForLog(sp.Segment.B, depth + 1, seen)}), proj=({FormatForLog(sp.ProjectedSegment.A, depth + 1, seen)}→{FormatForLog(sp.ProjectedSegment.B, depth + 1, seen)}), poly={FormatEnumerable(sp.Polygon, depth + 1, seen)})";
            case SegmentSegmentProjection ss:
                return $"Seg⊓Seg(ratio={ss.Ratio:F4}, lenA={ss.LengthA:F4}, projLen={ss.ProjectedLength:F4}, axis={FormatForLog(ss.Axis, depth + 1, seen)}, A=({FormatForLog(ss.SegmentA.A, depth + 1, seen)}→{FormatForLog(ss.SegmentA.B, depth + 1, seen)}), B=({FormatForLog(ss.SegmentB.A, depth + 1, seen)}→{FormatForLog(ss.SegmentB.B, depth + 1, seen)}), proj=({FormatForLog(ss.ProjectedSegment.A, depth + 1, seen)}→{FormatForLog(ss.ProjectedSegment.B, depth + 1, seen)}))";
            case PointPolygonProjection ppoly:
                return
                    $"Pt→Poly(" +
                    $"point={FormatForLog(ppoly.Point, depth + 1, seen)} " +
                    $"[px={FormatForLog(ppoly.PointPixel, depth + 1, seen)}], " +
                    $"proj={FormatForLog(ppoly.ProjectedPoint, depth + 1, seen)} " +
                    $"[px={FormatForLog(ppoly.ProjectedPointPixel, depth + 1, seen)}], " +
                    $"polygon={FormatEnumerable(ppoly.Polygon, depth + 1, seen)}, " +
                    $"axis={FormatForLog(ppoly.Axis, depth + 1, seen)})";
            case PointSegmentProjection pseg:
                return
                    $"Pt→Seg(" +
                    $"ratio={pseg.Ratio:F4}, " +
                    $"point={FormatForLog(pseg.Point, depth + 1, seen)} " +
                    $"[px={FormatForLog(pseg.PointPixel, depth + 1, seen)}], " +
                    $"proj={FormatForLog(pseg.ProjectedPoint, depth + 1, seen)} " +
                    $"[px={FormatForLog(pseg.ProjectedPointPixel, depth + 1, seen)}], " +
                    $"axis={FormatForLog(pseg.Axis, depth + 1, seen)}, " +
                    $"seg=({FormatForLog(pseg.Segment.A, depth + 1, seen)}→{FormatForLog(pseg.Segment.B, depth + 1, seen)}) " +
                    $"[px=({FormatForLog(pseg.SegmentPixel.A, depth + 1, seen)}→{FormatForLog(pseg.SegmentPixel.B, depth + 1, seen)})])";

            default:
                return proj?.GetType().Name ?? "null";
        }
    }

    private static string FormatEnumerable(System.Collections.IEnumerable en, int depth, HashSet<object> seen)
    {
        var parts = new List<string>();
        int i = 0;
        foreach (var item in en)
        {
            if (i++ >= MaxItemsPerLevel) { parts.Add("…"); break; }
            parts.Add(FormatForLog(item, depth + 1, seen));
        }
        return "[" + string.Join(", ", parts) + "]";
    }

    private static string FormatDictionary(System.Collections.IDictionary dict, int depth, HashSet<object> seen)
    {
        var parts = new List<string>();
        int i = 0;
        foreach (var key in dict.Keys)
        {
            if (i++ >= MaxItemsPerLevel) { parts.Add("…"); break; }
            var val = dict[key];
            parts.Add($"{FormatForLog(key, depth + 1, seen)}={FormatForLog(val, depth + 1, seen)}");
        }
        return "{" + string.Join(", ", parts) + "}";
    }

    private static string FormatValueTuple(object tuple, int depth, HashSet<object> seen)
    {
        var items = tuple.GetType().GetFields().Where(f => f.Name.StartsWith("Item")).OrderBy(f => f.Name);
        var parts = new List<string>();
        int i = 0;
        foreach (var f in items)
        {
            if (i++ >= MaxItemsPerLevel) { parts.Add("…"); break; }
            parts.Add(FormatForLog(f.GetValue(tuple), depth + 1, seen));
        }
        return "(" + string.Join(", ", parts) + ")";
    }

    private static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        return s.Length > MaxStringLen ? s.Substring(0, MaxStringLen) + "…" : s;
    }

    // optional: unregister if you like
    public void UnregisterRule(string id)
    {
        _observables.Remove(id);
    }
}
