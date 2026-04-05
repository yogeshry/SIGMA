using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using System.Collections.Generic;

public static class StreamSerializer
{
    public static string SerializeStreamsToJson(IDictionary<string, object> streams)
    {
        return ToJObject(streams).ToString(Formatting.None);
    }

    public static JObject ToJObject(IDictionary<string, object> streams)
    {
        var jo = new JObject();
        foreach (var kv in streams)
            jo[kv.Key] = ToJToken(kv.Value);
        return jo;
    }

    private static JToken ToJToken(object v)
    {
        if (v == null) return JValue.CreateNull();

        // --- Unity types → JObject directly ---
        if (v is Vector2 v2) return new JObject { ["x"] = v2.x, ["y"] = v2.y };
        if (v is Vector3 v3) return new JObject { ["x"] = v3.x, ["y"] = v3.y, ["z"] = v3.z };
        if (v is Vector4 v4) return new JObject { ["x"] = v4.x, ["y"] = v4.y, ["z"] = v4.z, ["w"] = v4.w };
        if (v is Quaternion q) return new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w };
        if (v is Color c) return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };

        // --- Projection objects ---
        if (v is IProjectionResult proj)
            return SanitizeProjection(proj);

        // --- Dictionaries ---
        if (v is IDictionary<string, object> dict)
        {
            var obj = new JObject();
            foreach (var kv in dict)
                obj[kv.Key] = ToJToken(kv.Value);
            return obj;
        }

        // --- Enumerables ---
        if (v is System.Collections.IEnumerable en && !(v is string))
        {
            var arr = new JArray();
            foreach (var e in en) arr.Add(ToJToken(e));
            return arr;
        }

        // --- JTokens pass through ---
        if (v is JToken jt) return jt;

        // --- Primitives ---
        if (v is bool b) return new JValue(b);
        if (v is int i) return new JValue(i);
        if (v is long l) return new JValue(l);
        if (v is float f) return new JValue(f);
        if (v is double d) return new JValue(d);
        if (v is string s) return new JValue(s);

        return JToken.FromObject(v); // fallback
    }

    private static JToken SanitizeProjection(IProjectionResult proj)
    {
        switch (proj)
        {
            case PointSegmentProjection pseg:
                return new JObject
                {
                    ["type"] = "PointSegment",
                    ["ratio"] = pseg.Ratio,
                    ["point"] = new JObject { ["world"] = ToJToken(pseg.Point), ["pixel"] = ToJToken(pseg.PointPixel) },
                    ["projected"] = new JObject { ["world"] = ToJToken(pseg.ProjectedPoint), ["pixel"] = ToJToken(pseg.ProjectedPointPixel) },
                    ["segment"] = new JObject
                    {
                        ["world"] = new JObject { ["A"] = ToJToken(pseg.Segment.A), ["B"] = ToJToken(pseg.Segment.B) },
                        ["pixel"] = new JObject { ["A"] = ToJToken(pseg.SegmentPixel.A), ["B"] = ToJToken(pseg.SegmentPixel.B) }
                    },
                    ["axis"] = ToJToken(pseg.Axis)
                };
            case PointPolygonProjection poly:
                var worldPoly = new JArray();
                foreach (var p in poly.Polygon) worldPoly.Add(ToJToken(p));
                var pixelPoly = new JArray();
                foreach (var p in poly.PolygonPixel) pixelPoly.Add(ToJToken(p));
                return new JObject
                {
                    ["type"] = "PointPolygon",
                    ["point"] = new JObject { ["world"] = ToJToken(poly.Point), ["pixel"] = ToJToken(poly.PointPixel) },
                    ["projected"] = new JObject { ["world"] = ToJToken(poly.ProjectedPoint), ["pixel"] = ToJToken(poly.ProjectedPointPixel) },
                    ["polygon"] = new JObject { ["world"] = worldPoly, ["pixel"] = pixelPoly },
                    ["axis"] = ToJToken(poly.Axis)
                };
            default:
                return new JValue(proj.ToString());
        }
    }
}
