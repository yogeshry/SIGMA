using System.Collections.Generic;
using UnityEngine;

public static class SpatialHelper
{
    /// <summary>
    /// Returns the offset vector from point p to its closest point on segment [a→b].
    /// (i.e. projection – p).  
    /// Distance is then simply offset.magnitude.
    /// </summary>
    public static Vector3 PointToSegmentOffset(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(p - a, ab) / ab.sqrMagnitude;
        t = Mathf.Clamp01(t);

        Vector3 proj = a + t * ab;
        return proj - p;
    }

    /// <summary>
    /// Returns the shortest offset vector between segments [p1→p2] and [q1→q2]
    /// (i.e. closestQ – closestP).  
    /// Distance is then offset.magnitude.
    /// </summary>
    public static Vector3 NearestSegmentOffset(
        Vector3 p1, Vector3 p2,
        Vector3 q1, Vector3 q2)
    {
        Vector3 u = p2 - p1;
        Vector3 v = q2 - q1;
        Vector3 w = p1 - q1;

        float a = Vector3.Dot(u, u), b = Vector3.Dot(u, v), c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w), e = Vector3.Dot(v, w);
        float D = a * c - b * b;

        float sN = D < Mathf.Epsilon ? 0f : (b * e - c * d);
        float tN = D < Mathf.Epsilon ? e : (a * e - b * d);
        float sD = D < Mathf.Epsilon ? 1f : D;
        float tD = D < Mathf.Epsilon ? c : D;

        // clamp sN to [0, sD]
        if (sN < 0f) { sN = 0f; tN = e; tD = c; }
        else if (sN > sD) { sN = sD; tN = e + b; tD = c; }

        // clamp tN to [0, tD]
        if (tN < 0f)
        {
            tN = 0f;
            if (-d < 0f) { sN = 0f; }
            else if (-d > a) { sN = sD; }
            else { sN = -d; sD = a; }
        }
        else if (tN > tD)
        {
            tN = tD;
            if ((-d + b) < 0f) { sN = 0f; }
            else if ((-d + b) > a) { sN = sD; }
            else { sN = -d + b; sD = a; }
        }

        float sc = Mathf.Approximately(sD, 0f) ? 0f : sN / sD;
        float tc = Mathf.Approximately(tD, 0f) ? 0f : tN / tD;

        Vector3 closestP = p1 + sc * u;
        Vector3 closestQ = q1 + tc * v;
        return closestQ - closestP;
    }

    /// <summary>
    /// For the shorter of edges A:[a1→a2] and B:[b1→b2], returns the two
    /// endpoint‐to‐other‐segment offset vectors.  
    /// Distances are their magnitudes.
    /// </summary>
    public static (Vector3 first, Vector3 second) ShorterEdgeCornerVectorsToOther(
        Vector3 a1, Vector3 a2,
        Vector3 b1, Vector3 b2)
    {
        // compare squared lengths to avoid a sqrt
        if ((a2 - a1).sqrMagnitude <= (b2 - b1).sqrMagnitude)
        {
            // A is shorter → project A’s endpoints onto B
            var v1 = PointToSegmentOffset(a1, b1, b2);
            var v2 = PointToSegmentOffset(a2, b1, b2);
            return (v1, v2);
        }
        else
        {
            // B is shorter → project B’s endpoints onto A
            var v1 = PointToSegmentOffset(b1, a1, a2);
            var v2 = PointToSegmentOffset(b2, a1, a2);
            return (v1, v2);
        }
    }

    /// <summary>
    /// Returns the farthest of those two corner‐to‐other‐segment vectors
    /// (i.e. the one with the larger magnitude).
    /// </summary>
    public static Vector3 FarthestSegmentOffset(
        Vector3 a1, Vector3 a2,
        Vector3 b1, Vector3 b2)
    {
        var (v1, v2) = ShorterEdgeCornerVectorsToOther(a1, a2, b1, b2);
        return v1.sqrMagnitude >= v2.sqrMagnitude ? v1 : v2;
    }












    // ---------------------------------------------------------------------
    // Point ↔ Polygon REGION (filled polygon on a plane)
    // - If projection of p onto polygon plane lies inside polygon: distance is perpendicular.
    // - Else: distance is to nearest polygon edge segment in 3D.
    // Works best when polygon vertices are ordered and mostly planar.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Newell normal for a polygon (robust for convex/concave if ordered).
    /// Returns Vector3.zero if degenerate.
    /// </summary>
    public static Vector3 PolygonNormal(IList<Vector3> poly)
    {
        if (poly == null || poly.Count < 3) return Vector3.zero;

        Vector3 n = Vector3.zero;
        int count = poly.Count;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = poly[i];
            Vector3 b = poly[(i + 1) % count];

            n.x += (a.y - b.y) * (a.z + b.z);
            n.y += (a.z - b.z) * (a.x + b.x);
            n.z += (a.x - b.x) * (a.y + b.y);
        }

        float mag = n.magnitude;
        return mag > Mathf.Epsilon ? (n / mag) : Vector3.zero;
    }

    /// <summary>
    /// Projects p onto plane (p0, n). Returns projected point and signed distance along n.
    /// signedDist > 0 means p is in direction of n.
    /// </summary>
    private static Vector3 ProjectPointToPlane(Vector3 p, Vector3 p0, Vector3 n, out float signedDist)
    {
        signedDist = Vector3.Dot(n, p - p0);
        return p - signedDist * n;
    }

    /// <summary>
    /// Tests if a point on the polygon plane lies inside the polygon (convex or concave),
    /// using a 2D ray-cast after choosing a stable projection axis.
    /// Assumes 'ptOnPlane' is already on/near the plane.
    /// </summary>
    public static bool PointInPolygonOnPlane(Vector3 ptOnPlane, IList<Vector3> poly, Vector3 planeNormal)
    {
        if (poly == null || poly.Count < 3) return false;
        if (planeNormal == Vector3.zero) return false;

        // Choose projection to 2D by dropping the dominant normal axis (largest abs component).
        Vector3 an = new Vector3(Mathf.Abs(planeNormal.x), Mathf.Abs(planeNormal.y), Mathf.Abs(planeNormal.z));
        int drop; // 0=x,1=y,2=z
        if (an.x >= an.y && an.x >= an.z) drop = 0;
        else if (an.y >= an.x && an.y >= an.z) drop = 1;
        else drop = 2;

        // 2D ray cast: count crossings of horizontal ray to +infinity in x
        bool inside = false;
        int count = poly.Count;

        Vector2 P = To2D(ptOnPlane, drop);

        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            Vector2 A = To2D(poly[i], drop);
            Vector2 B = To2D(poly[j], drop);

            // Check if edge (B->A) crosses the horizontal line at P.y
            bool intersect = ((A.y > P.y) != (B.y > P.y)) &&
                             (P.x < (B.x - A.x) * (P.y - A.y) / ((B.y - A.y) + 1e-20f) + A.x);
            if (intersect) inside = !inside;
        }

        return inside;

        static Vector2 To2D(Vector3 v, int dropAxis)
        {
            // dropAxis=0 -> use (y,z), dropAxis=1 -> (x,z), dropAxis=2 -> (x,y)
            return dropAxis switch
            {
                0 => new Vector2(v.y, v.z),
                1 => new Vector2(v.x, v.z),
                _ => new Vector2(v.x, v.y),
            };
        }
    }

    /// <summary>
    /// Returns the offset vector from p to the closest point on the polygon REGION (filled).
    /// Offset = closestPointOnRegion - p. Distance is offset.magnitude.
    ///
    /// If the projection of p onto the polygon plane is inside the polygon, the closest point
    /// is that projection (perpendicular to plane).
    /// Otherwise, closest point lies on the nearest polygon edge segment in 3D.
    ///
    /// Outputs:
    /// - closestPoint: closest point on the polygon region
    /// - onFace: true if closest point is on the face interior (projection inside), false if on an edge
    /// </summary>
    public static Vector3 PointToPolygonRegionOffset(
        Vector3 p,
        IList<Vector3> polygon,
        out Vector3 closestPoint,
        out bool onFace)
    {
        closestPoint = p;
        onFace = false;

        if (polygon == null || polygon.Count < 3)
            return Vector3.zero;

        Vector3 n = PolygonNormal(polygon);
        if (n == Vector3.zero)
            return Vector3.zero;

        Vector3 p0 = polygon[0];
        Vector3 proj = ProjectPointToPlane(p, p0, n, out float signedDist);

        // If projection lands inside the polygon, closest point is the projection.
        if (PointInPolygonOnPlane(proj, polygon, n))
        {
            onFace = true;
            closestPoint = proj;
            return proj - p; // perpendicular offset
        }

        // Otherwise, find closest point on polygon boundary edges (segments) in 3D.
        float bestSqr = float.PositiveInfinity;
        Vector3 bestPoint = proj;

        int count = polygon.Count;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = polygon[i];
            Vector3 b = polygon[(i + 1) % count];

            // Closest point on segment to p is p + offset
            Vector3 off = PointToSegmentOffset(p, a, b);
            Vector3 cand = p + off;
            float sq = off.sqrMagnitude;
            if (sq < bestSqr)
            {
                bestSqr = sq;
                bestPoint = cand;
            }
        }

        onFace = false;
        closestPoint = bestPoint;
        return bestPoint - p;
    }


    public static Vector3 PointToPolygonRegionOffset(Vector3 p, IList<Vector3> polygon)
    {
        return PointToPolygonRegionOffset(p, polygon, out _, out _);
    }

}
