using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class StreamSerializer
{
    public static string SerializeStreamsToJson(IDictionary<string, object> streams)
    {
        var safeDict = streams.ToDictionary(kv => kv.Key, kv => SanitizeValue(kv.Value));
        return JsonConvert.SerializeObject(safeDict, Formatting.None);
    }

    public static JObject ToJObject(IDictionary<string, object> streams)
    {
        var json = SerializeStreamsToJson(streams);
        return JObject.Parse(json);
    }

    private static object SanitizeValue(object v)
    {
        if (v == null) return null;

        // --- Unity Vectors (stripped down to x,y,z only) ---
        if (v is Vector2 v2) return new { x = v2.x, y = v2.y };
        if (v is Vector3 v3) return new { x = v3.x, y = v3.y, z = v3.z };
        if (v is Vector4 v4) return new { x = v4.x, y = v4.y, z = v4.z, w = v4.w };
        if (v is Quaternion q) return new { x = q.x, y = q.y, z = q.z, w = q.w };
        if (v is Color c) return new { r = c.r, g = c.g, b = c.b, a = c.a };

        // --- Projection objects ---
        if (v is IProjectionResult proj)
            return SanitizeProjection(proj);

        // --- Dictionaries / Arrays ---
        if (v is IDictionary<string, object> dict)
            return dict.ToDictionary(k => k.Key, k => SanitizeValue(k.Value));

        if (v is System.Collections.IEnumerable en && !(v is string))
        {
            var list = new List<object>();
            foreach (var e in en) list.Add(SanitizeValue(e));
            return list;
        }

        // --- JTokens ---
        if (v is JValue jv) return jv.Value;
        if (v is JObject jo)
            return jo.Properties().ToDictionary(p => p.Name, p => SanitizeValue(p.Value));
        if (v is JArray ja)
            return ja.Select(SanitizeValue).ToList();

        return v; // fallback
    }

    private static object SanitizeProjection(IProjectionResult proj)
    {
        switch (proj)
        {
            case PointSegmentProjection pseg:
                return new
                {
                    type = "PointSegment",
                    ratio = pseg.Ratio,
                    point = new { world = SanitizeValue(pseg.Point), pixel = SanitizeValue(pseg.PointPixel) },
                    projected = new { world = SanitizeValue(pseg.ProjectedPoint), pixel = SanitizeValue(pseg.ProjectedPointPixel) },
                    segment = new
                    {
                        world = new { A = SanitizeValue(pseg.Segment.A), B = SanitizeValue(pseg.Segment.B) },
                        pixel = new { A = SanitizeValue(pseg.SegmentPixel.A), B = SanitizeValue(pseg.SegmentPixel.B) }
                    },
                    axis = SanitizeValue(pseg.Axis)
                };
            case PointPolygonProjection poly:
                return new
                {
                    type = "PointPolygon",
                    point = new { world = SanitizeValue(poly.Point), pixel = SanitizeValue(poly.PointPixel) },
                    projected = new { world = SanitizeValue(poly.ProjectedPoint), pixel = SanitizeValue(poly.ProjectedPointPixel) },
                    polygon = new
                    {
                        world = poly.Polygon.Select(v => SanitizeValue(v)).ToList(),
                        pixel = poly.PolygonPixel.Select(v => SanitizeValue(v)).ToList()
                    },
                    axis = SanitizeValue(poly.Axis)
                };
            default:
                return proj.ToString();
        }
    }
}
