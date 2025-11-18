// ————————————————————————
// 1) Your grammar types
// ————————————————————————
using System.Collections.Generic;
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DeviceSpatialProvider;
using Newtonsoft.Json;

public class RuleSpec
{
    public string id;
    public string on;
    public ConditionSpec condition;
    public PublicationSpec publish;
    public JToken entities;          // e.g. [{"type":"Mobile"},{"type":"Desktop"}]
}

public class ConditionSpec
{
    public string name;             // e.g. "coplanar"
    public List<object> primitives { get; set; } = new List<object>();
    public string @operator;        // "AND" or "OR"
}

public class PublicationSpec
{
    public string[] streams;
    public object mapping;
}


// ————————————————————————
// 2) Primitive payload
// ————————————————————————
public class PrimitivePayload
{
    public string Id;    // e.g. "coLocalLevel"
    public object Value;  // your measured metric
    public Boolean IsValid; // true if the condition is met, false otherwise
}

[Serializable]
public class PrimitiveParams
{
    // EMA time constant in seconds for *_rms metrics (frequency cutoff ≈ 1/(2π·tauSec)).
    public float? halfLifeSec;
}


[Serializable]
public class PrimitiveSpec
{
    public string id;
    public string description;    // optional
    public string metric;          // e.g. "distance", "angle", "projection:Y", "velocity:diagonal1A", etc.
    public List<string> refs;      // e.g. ["position","position"]
    public ComparatorSpec condition;
    public string unit;            // optional
                                   // JSON key is "params"; @params avoids C# 'params' contextual keyword.
    [JsonProperty("params")]
    public PrimitiveParams @params;  // optional
}
[Serializable]
public class InlinePrimitiveSpec
{
    public string id;
    public string description;    // optional
    public ComparatorSpec condition;
    public string unit;            // optional
                                   // JSON key is "params"; @params avoids C# 'params' contextual keyword.
    [JsonProperty("params")]
    public PrimitiveParams @params;  // optional
}

[Serializable]
public class ComparatorSpec
{
    public float? eq;
    public float? lt;
    public float? gt;
    public float? tolerance;
    public float? area;
}


public enum GeometryType
{
    Point,
    Vector,
    LineSegment,
    Polygon,
    Rotation,
    EulerAngles
}

public class DisplaySpatialData
{
    public int widthPixels;
    public int heightPixels;
    public Corners corners;

    public DisplaySpatialData(int widthPixels, int heightPixels, Corners corners)
    {
        this.widthPixels = widthPixels;
        this.heightPixels = heightPixels;
        this.corners = corners;
    }
}
public class DisplayPairSpatialData
{
    public DisplaySpatialData displayA;
    public DisplaySpatialData displayB;

    public DisplayPairSpatialData(DisplaySpatialData displayA, DisplaySpatialData displayB)
    {
        this.displayA = displayA;
        this.displayB = displayB;
    }
}


// ---- Projection result "union" types ---------------------------------------
public interface IProjectionResult
{
    /// <summary>Normalized measure in [0,1] (area or length ratio depending on type).</summary>
    float Ratio { get; }
}

// ---------- Helpers ----------
internal static class ProjectionMath
{
    public const float Eps = 1e-6f;

    public static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

    public static float SegmentLength((Vector3 A, Vector3 B) seg)
        => (seg.B - seg.A).magnitude;

    // Shoelace area on an arbitrary polygon (assumes coplanar & ordered; if not, caller should order first)
    public static float PolygonArea(IReadOnlyList<Vector3> poly)
    {
        if (poly == null || poly.Count < 3) return 0f;
        // Project to the dominant plane (simple, robust enough for quads)
        // Use XY projection for speed (assumes screen-aligned quads); customize if needed.
        double sum = 0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var pi = poly[i]; var pj = poly[j];
            sum += (double)pj.x * pi.y - (double)pi.x * pj.y;
        }
        return Mathf.Abs((float)(0.5 * sum));
    }
}

// ---------- Polygon ? Polygon ----------
public sealed class PolygonPolygonProjection : IProjectionResult
{
    public Vector3[] PolygonA { get; }
    public Vector2[] PolygonAPixel { get; }
    public Vector3[] PolygonB { get; }
    public Vector2[] PolygonBPixel { get; }
    public Vector3[] ProjectedPolygon { get; }
    public Vector2[] ProjectedPolygonPixel { get; }
    public float AreaA { get; }
    public float ProjectedArea { get; }
    public float Ratio { get; }
    public Vector3 Axis { get; }

    private PolygonPolygonProjection(
        Vector3[] polygonA, Vector3[] polygonB, Vector3[] projectedPolygon,
        float areaA, float projectedArea, float ratio, Vector3 axis, DisplayPairSpatialData displays )
    {
        PolygonA = polygonA ?? Array.Empty<Vector3>();
        PolygonAPixel = CoordinateMapping.WorldToPixelFromCornersList(
                    polygonA, displays.displayA.widthPixels, displays.displayA.heightPixels,
                            displays.displayA.corners.TopLeft, displays.displayA.corners.TopRight, displays.displayA.corners.BottomLeft, displays.displayA.corners.BottomRight, true) ?? Array.Empty<Vector2>();
        PolygonB = polygonB ?? Array.Empty<Vector3>();
        PolygonBPixel = CoordinateMapping.WorldToPixelFromCornersList(
                    polygonB, displays.displayB.widthPixels, displays.displayB.heightPixels,
                            displays.displayB.corners.TopLeft, displays.displayB.corners.TopRight, displays.displayB.corners.BottomLeft, displays.displayB.corners.BottomRight, true) ?? Array.Empty<Vector2>();
        ProjectedPolygon = projectedPolygon ?? Array.Empty<Vector3>();
        ProjectedPolygonPixel = CoordinateMapping.WorldToPixelFromCornersList(
                    projectedPolygon, displays.displayB.widthPixels, displays.displayB.heightPixels,
                            displays.displayB.corners.TopLeft, displays.displayB.corners.TopRight, displays.displayB.corners.BottomLeft, displays.displayB.corners.BottomRight, true) ?? Array.Empty<Vector2>();
        AreaA = areaA;
        ProjectedArea = projectedArea;
        Ratio = ProjectionMath.Clamp01(ratio);
        Axis = axis;
    }

    public static PolygonPolygonProjection Create(
        Vector3[] polygonA, Vector3[] polygonB, Vector3[] projectedPolygon,float areaA, float projA, Vector3 axis, DisplayPairSpatialData displays)
    {
        var ratio = areaA > ProjectionMath.Eps ? projA / areaA : 0f;
        return new PolygonPolygonProjection(polygonA, polygonB, projectedPolygon, areaA, projA, ratio, axis, displays);
    }
}

// ---------- Segment ? Polygon ----------
public sealed class SegmentPolygonProjection : IProjectionResult
{
    public (Vector3 A, Vector3 B) Segment { get; }
    public Vector3[] Polygon { get; }
    public (Vector3 A, Vector3 B) ProjectedSegment { get; }
    public float SegmentLength { get; }
    public float ProjectedLength { get; }
    public float Ratio { get; }
    public Vector3 Axis { get; }

    private SegmentPolygonProjection(
        (Vector3 A, Vector3 B) seg, Vector3[] polygon,
        (Vector3 A, Vector3 B) projectedSeg,
        float len, float projLen, float ratio, Vector3 axis)
    {
        Segment = seg;
        Polygon = polygon ?? Array.Empty<Vector3>();
        ProjectedSegment = projectedSeg;
        SegmentLength = len;
        ProjectedLength = projLen;
        Ratio = ProjectionMath.Clamp01(ratio);
        Axis = axis;
    }

    public static SegmentPolygonProjection Create(
        (Vector3 A, Vector3 B) seg, Vector3[] polygon, (Vector3 A, Vector3 B) projectedSeg, Vector3 axis)
    {
        var len = ProjectionMath.SegmentLength(seg);
        var projLen = ProjectionMath.SegmentLength(projectedSeg);
        var ratio = len > ProjectionMath.Eps ? projLen / len : 0f;
        return new SegmentPolygonProjection(seg, polygon, projectedSeg, len, projLen, ratio, axis);
    }
}

// ---------- Segment ? Segment ----------
public sealed class SegmentSegmentProjection : IProjectionResult
{
    public (Vector3 A, Vector3 B) SegmentA { get; }
    public (Vector3 A, Vector3 B) SegmentB { get; }
    /// <summary>Segment representing the overlapped or closest span along Axis (your solver defines this).</summary>
    public (Vector3 A, Vector3 B) ProjectedSegment { get; }
    public Vector3 Axis { get; }
    public float LengthA { get; }
    public float ProjectedLength { get; }
    /// <summary>ProjectedLength / max(LengthA, eps).</summary>
    public float Ratio { get; }

    private SegmentSegmentProjection(
        (Vector3 A, Vector3 B) segA, (Vector3 A, Vector3 B) segB, (Vector3 A, Vector3 B) projectedSeg,
        Vector3 axis, float lenA, float projLen, float ratio)
    {
        SegmentA = segA;
        SegmentB = segB;
        ProjectedSegment = projectedSeg;
        Axis = axis;
        LengthA = lenA;
        ProjectedLength = projLen;
        Ratio = ProjectionMath.Clamp01(ratio);
    }

    public static SegmentSegmentProjection Create(
        (Vector3 A, Vector3 B) segA, (Vector3 A, Vector3 B) segB, (Vector3 A, Vector3 B) projectedSeg, Vector3 axis)
    {
        var lenA = ProjectionMath.SegmentLength(segA);
        var projLen = ProjectionMath.SegmentLength(projectedSeg);
        var ratio = lenA > ProjectionMath.Eps ? projLen / lenA : 0f;
        return new SegmentSegmentProjection(segA, segB, projectedSeg, axis, lenA, projLen, ratio);
    }
}

// ---------- Point ? Polygon ----------
public sealed class PointPolygonProjection : IProjectionResult
{
    public Vector3 Point { get; }
    public Vector2 PointPixel { get; }
    public Vector3 ProjectedPoint { get; }
    public Vector2 ProjectedPointPixel { get; }
    public Vector3 Axis { get; }
    public Vector3[] Polygon { get; }
    public Vector2[] PolygonPixel { get; }
    /// <summary>For a single point hit, treat as a full hit (1.0) by convention.</summary>
    public float Ratio => 1f;

    public PointPolygonProjection(Vector3 point, Vector3 projectedPoint, Vector3 axis, Vector3[] polygon, DisplayPairSpatialData displays)
    {
        Point = point;
        PointPixel = CoordinateMapping.WorldToPixelFromCorners(
                    point, displays.displayA.widthPixels, displays.displayA.heightPixels,
                            displays.displayA.corners.TopLeft, displays.displayA.corners.TopRight, displays.displayA.corners.BottomLeft, displays.displayA.corners.BottomRight, true);
        ProjectedPoint = projectedPoint;
        ProjectedPointPixel = CoordinateMapping.WorldToPixelFromCorners(
                    projectedPoint, displays.displayB.widthPixels, displays.displayB.heightPixels,
                            displays.displayB.corners.TopLeft, displays.displayB.corners.TopRight, displays.displayB.corners.BottomLeft, displays.displayB.corners.BottomRight, true);
        Axis = axis;
        Polygon = polygon ?? Array.Empty<Vector3>();
        PolygonPixel = CoordinateMapping.WorldToPixelFromCornersList(
                    polygon, displays.displayB.widthPixels, displays.displayB.heightPixels,
                            displays.displayB.corners.TopLeft, displays.displayB.corners.TopRight, displays.displayB.corners.BottomLeft, displays.displayB.corners.BottomRight, true) ?? Array.Empty<Vector2>();
    }
}

// ---------- Point ? Segment ----------
public sealed class PointSegmentProjection : IProjectionResult
{
    public Vector3 Point { get; }
    public Vector2 PointPixel { get; }
    public Vector3 ProjectedPoint { get; }
    public Vector2 ProjectedPointPixel { get; }
    public (Vector3 A, Vector3 B) Segment { get; }
    public (Vector2 A, Vector2 B) SegmentPixel { get; }
    public Vector3 Axis { get; }
    /// <summary>For a single point hit, treat as a full hit (1.0) by convention.</summary>
    public float Ratio => 1f;

    public PointSegmentProjection(Vector3 point, Vector3 projectedPoint, (Vector3 A, Vector3 B) segment, Vector3 axis, DisplayPairSpatialData displays)
    {
        Point = point;
        PointPixel = CoordinateMapping.WorldToPixelFromCorners(
                    point, displays.displayA.widthPixels, displays.displayA.heightPixels,
                            displays.displayA.corners.TopLeft, displays.displayA.corners.TopRight, displays.displayA.corners.BottomLeft, displays.displayA.corners.BottomRight, true);
        ProjectedPoint = projectedPoint;
        ProjectedPointPixel = CoordinateMapping.WorldToPixelFromCorners(
                    projectedPoint, displays.displayB.widthPixels, displays.displayB.heightPixels,
                            displays.displayB.corners.TopLeft, displays.displayB.corners.TopRight, displays.displayB.corners.BottomLeft, displays.displayB.corners.BottomRight, true);
        Segment = segment;
        SegmentPixel = (
            CoordinateMapping.WorldToPixelFromCorners(
                    segment.A, displays.displayB.widthPixels, displays.displayB.heightPixels,
                            displays.displayB.corners.TopLeft, displays.displayB.corners.TopRight, displays.displayB.corners.BottomLeft, displays.displayB.corners.BottomRight, true),
            CoordinateMapping.WorldToPixelFromCorners(
                    segment.B, displays.displayB.widthPixels, displays.displayB.heightPixels,
                            displays.displayB.corners.TopLeft, displays.displayB.corners.TopRight, displays.displayB.corners.BottomLeft, displays.displayB.corners.BottomRight, true)
            );
        Axis = axis;
    }

}