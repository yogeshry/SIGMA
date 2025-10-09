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
using WebSocketSharp.Server;
using static UnityEngine.Rendering.CoreUtils;

public class ObservableManager
{
    readonly PrimitiveFactory _primitives = new PrimitiveFactory();
    readonly RuleBuilder _builder;
    public bool VerboseLogs = true;   // lifecycle logs
    public bool TraceValues = true; // per-primitive log taps
    // store your rule streams
    readonly Dictionary<string, IObservable<RuleEvent>> _observables
      = new Dictionary<string, IObservable<RuleEvent>>();



    public ObservableManager()
    {
        _builder = new RuleBuilder(_primitives);
        //_builder = new RuleBuilder();

        // if you like, preload everything now:
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
            // example subscription
            stream.Subscribe(evt =>
            {
                // format the streams dictionary nicely
                string streamsDump = string.Join(", ",
                    evt.Streams.Select(kv => $"{kv.Key}={FormatForLog(kv.Value)}"));

                Debug.Log($"[Rule {spec.id}@{Time.time:F2}] State={evt.State} Streams=[{streamsDump}]");
                WsHub.Broadcast(new { type="spatial", state = !evt.State });

            });
        }

    }
    // ---------------- Logging helpers ----------------

    private const int MaxLogDepth = 4;
    private const int MaxItemsPerLevel = 8;
    private const int MaxStringLen = 256;

    private static string FormatForLog(object obj) =>
        FormatForLog(obj, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static string FormatForLog(object obj, int depth, HashSet<object> seen)
    {
        if (depth > MaxLogDepth) return "…";

        if (obj == null) return "null";

        // Avoid cycles (reference types only)
        if (!(obj is ValueType))
        {
            if (!seen.Add(obj)) return "↻";
        }

        // Common Unity types
        switch (obj)
        {
            case Vector3 v3: return $"({v3.x:F3},{v3.y:F3},{v3.z:F3})";
            case Vector2 v2: return $"({v2.x:F3},{v2.y:F3})";
            case Vector4 v4: return $"({v4.x:F3},{v4.y:F3},{v4.z:F3},{v4.w:F3})";
            case Quaternion q: return $"quat({q.x:F3},{q.y:F3},{q.z:F3},{q.w:F3})";
            case Color c: return $"rgba({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3})";
            case Bounds b: return $"bounds(center={FormatForLog(b.center, depth + 1, seen)}, size={FormatForLog(b.size, depth + 1, seen)})";
        }

        // Numeric & primitives
        if (obj is string s) return Truncate(s);
        if (obj is char ch) return ch.ToString();
        if (obj is bool || obj is int || obj is long || obj is float || obj is double || obj is decimal)
            return Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture);

        // Nullable<T>
        var t = obj.GetType();
        if (Nullable.GetUnderlyingType(t) != null)
        {
            var hasValueProp = t.GetProperty("HasValue");
            var valueProp = t.GetProperty("Value");
            bool hasValue = (bool)(hasValueProp?.GetValue(obj) ?? false);
            return hasValue ? FormatForLog(valueProp.GetValue(obj), depth + 1, seen) : "null";
        }

        // Tuples (ValueTuple)
        if (t.FullName != null && t.FullName.StartsWith("System.ValueTuple"))
            return FormatValueTuple(obj, depth, seen);

        // Dictionary-like
        if (obj is System.Collections.IDictionary dict)
            return FormatDictionary(dict, depth, seen);

        // IEnumerable (arrays, lists, etc.)
        if (obj is System.Collections.IEnumerable en && !(obj is string))
            return FormatEnumerable(en, depth, seen);

        // Known app-specific: projection results
        if (obj is IProjectionResult proj) return FormatProjection(proj, depth, seen);

        // Fallback: compact JSON if possible; else ToString()
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
                return
                    $"Poly⊓Poly(" +
                    $"ratio={pp.Ratio:F4}, areaA={pp.AreaA:F4}, projArea={pp.ProjectedArea:F4}, " +
                    $"axis={FormatForLog(pp.Axis, depth + 1, seen)}, " +
                    $"A={FormatEnumerable(pp.PolygonA, depth + 1, seen)}, " +
                    $"B={FormatEnumerable(pp.PolygonB, depth + 1, seen)}, " +
                    $"inter={FormatEnumerable(pp.ProjectedPolygon, depth + 1, seen)})";

            case SegmentPolygonProjection sp:
                return
                    $"Seg→Poly(" +
                    $"ratio={sp.Ratio:F4}, segLen={sp.SegmentLength:F4}, projLen={sp.ProjectedLength:F4}, " +
                    $"axis={FormatForLog(sp.Axis, depth + 1, seen)}, " +
                    $"seg=({FormatForLog(sp.Segment.A, depth + 1, seen)}→{FormatForLog(sp.Segment.B, depth + 1, seen)}), " +
                    $"proj=({FormatForLog(sp.ProjectedSegment.A, depth + 1, seen)}→{FormatForLog(sp.ProjectedSegment.B, depth + 1, seen)}), " +
                    $"poly={FormatEnumerable(sp.Polygon, depth + 1, seen)})";

            case SegmentSegmentProjection ss:
                return
                    $"Seg⊓Seg(" +
                    $"ratio={ss.Ratio:F4}, lenA={ss.LengthA:F4}, projLen={ss.ProjectedLength:F4}, " +
                    $"axis={FormatForLog(ss.Axis, depth + 1, seen)}, " +
                    $"A=({FormatForLog(ss.SegmentA.A, depth + 1, seen)}→{FormatForLog(ss.SegmentA.B, depth + 1, seen)}), " +
                    $"B=({FormatForLog(ss.SegmentB.A, depth + 1, seen)}→{FormatForLog(ss.SegmentB.B, depth + 1, seen)}), " +
                    $"proj=({FormatForLog(ss.ProjectedSegment.A, depth + 1, seen)}→{FormatForLog(ss.ProjectedSegment.B, depth + 1, seen)}))";

            case PointPolygonProjection ppoly:
                return
                    $"Pt→Poly(" +
                    $"ratio={ppoly.Ratio:F4}, " +
                    $"point={FormatForLog(ppoly.Point, depth + 1, seen)}, " +
                    $"proj={FormatForLog(ppoly.ProjectedPoint, depth + 1, seen)}, " +
                    $"axis={FormatForLog(ppoly.Axis, depth + 1, seen)})";

            case PointSegmentProjection pseg:
                return
                    $"Pt→Seg(" +
                    $"ratio={pseg.Ratio:F4}, " +
                    $"point={FormatForLog(pseg.Point, depth + 1, seen)}, " +
                    $"proj={FormatForLog(pseg.ProjectedPoint, depth + 1, seen)}, " +
                    $"axis={FormatForLog(pseg.Axis, depth + 1, seen)}, " +
                    $"seg=({FormatForLog(pseg.Segment.A, depth + 1, seen)}→{FormatForLog(pseg.Segment.B, depth + 1, seen)}))";

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
        // reflect Item1..ItemN
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