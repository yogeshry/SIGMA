using System.Collections.Generic;
using UnityEngine;

public static class SurfaceProjection
{
    /// <summary>
    /// Projects A onto B’s plane along B’s normal, then intersects.
    /// </summary>
    public static float ProjectAndIntersect(
        Vector3[] cornersA,
        Vector3[] cornersB,
        out Vector3[] intersectionPoints,
        out float area, out float projectedArea)
    {
        // Compute B’s normal
        Vector3 edge1 = cornersB[1] - cornersB[0];
        Vector3 edge2 = cornersB[3] - cornersB[0];
        Vector3 planeNormal = Vector3.Cross(edge1, edge2).normalized;

        // Delegate to the general version
        return ProjectAlongAxisAndIntersect(
            cornersA, cornersB, planeNormal,
            out intersectionPoints, out area, out projectedArea
        );
    }

    /// <summary>
    /// Projects A onto B’s plane along the given axis (unit vector),
    /// then clips and returns the overlap.
    /// </summary>
    public static float ProjectAlongAxisAndIntersect(
        Vector3[] cornersA,
        Vector3[] cornersB,
        Vector3 projectionAxis,
        out Vector3[] intersectionPoints,
        out float area,
        out float projectedArea)
    {
        // 1) Define B’s plane origin & basis
        Vector3 origin = cornersB[0];
        Vector3 e1 = cornersB[1] - origin;
        Vector3 e2 = cornersB[3] - origin;
        Vector3 normal = Vector3.Cross(e1, e2).normalized;

        // area = area of A’s polygon
        area = ComputePolygonArea(cornersA);
        // 2) If axis is parallel to the plane, no valid projection
        float denom = Vector3.Dot(projectionAxis, normal);
        if (Mathf.Approximately(denom, 0f))
        {
            intersectionPoints = new Vector3[0];
            projectedArea = 0f;
            return area;
        }

        // 3) Build B’s local 2D basis (u,v)
        Vector3 uDir = e1.normalized;
        Vector3 vDir = Vector3.Cross(normal, uDir).normalized;

        // 4) Project A’s corners along the axis onto B’s plane → 2D
        var polyA2 = new List<Vector2>(cornersA.Length);
        foreach (var p in cornersA)
        {
            float t = -Vector3.Dot(p - origin, normal) / denom;
            Vector3 pr = p + projectionAxis * t;
            polyA2.Add(new Vector2(
                Vector3.Dot(pr - origin, uDir),
                Vector3.Dot(pr - origin, vDir)
            ));
        }

        // 5) Convert B’s corners to the same 2D coords
        var polyB2 = new List<Vector2>(cornersB.Length);
        foreach (var p in cornersB)
        {
            polyB2.Add(new Vector2(
                Vector3.Dot(p - origin, uDir),
                Vector3.Dot(p - origin, vDir)
            ));
        }

        // 6) Clip A₂ against B₂
        var overlap2D = ClipPolygon(polyA2, polyB2);

        // 7) Compute 2D area
        projectedArea = PolygonArea(overlap2D);

        // 8) Lift overlap back into world‐space
        var worldPts = new List<Vector3>(overlap2D.Count);
        foreach (var q in overlap2D)
        {
            worldPts.Add(origin + uDir * q.x + vDir * q.y);
        }

        intersectionPoints = worldPts.ToArray();
        return area;
    }

    // ------------ Helpers below ------------

    /// <summary>
    /// Compute polygon area from ordered 3D corners (must be coplanar).
    /// Works for triangles, quads, or n-gons.
    /// </summary>
    public static float ComputePolygonArea(Vector3[] corners)
    {
        if (corners == null || corners.Length < 3) return 0f;

        // Find the polygon's normal
        Vector3 normal = Vector3.zero;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 current = corners[i];
            Vector3 next = corners[(i + 1) % corners.Length];
            normal.x += (current.y - next.y) * (current.z + next.z);
            normal.y += (current.z - next.z) * (current.x + next.x);
            normal.z += (current.x - next.x) * (current.y + next.y);
        }

        // Choose the dominant axis to project onto 2D
        Vector3 n = normal.normalized;
        int axis = Mathf.Abs(n.x) > Mathf.Abs(n.y)
            ? (Mathf.Abs(n.x) > Mathf.Abs(n.z) ? 0 : 2)
            : (Mathf.Abs(n.y) > Mathf.Abs(n.z) ? 1 : 2);

        double area = 0.0;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 c = corners[i];
            Vector3 nC = corners[(i + 1) % corners.Length];

            switch (axis)
            {
                case 0: // project to YZ
                    area += (c.y * nC.z - nC.y * c.z);
                    break;
                case 1: // project to XZ
                    area += (c.x * nC.z - nC.x * c.z);
                    break;
                case 2: // project to XY
                    area += (c.x * nC.y - nC.x * c.y);
                    break;
            }
        }

        return Mathf.Abs((float)area) * 0.5f;
    }

    public static float PolygonArea(IList<Vector2> poly)
    {
        int n = poly.Count;
        if (n < 3) return 0f;
        float sum = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % n];
            sum += a.x * b.y - b.x * a.y;
        }
        return Mathf.Abs(sum) * 0.5f;
    }

    private static List<Vector2> ClipPolygon(List<Vector2> subj, List<Vector2> clip)
    {
        var output = new List<Vector2>(subj);
        for (int j = 0; j < clip.Count; j++)
        {
            Vector2 A = clip[j], B = clip[(j + 1) % clip.Count];
            var input = new List<Vector2>(output);
            output.Clear();

            for (int i = 0; i < input.Count; i++)
            {
                Vector2 P = input[i], Q = input[(i + 1) % input.Count];
                bool inP = IsLeft(A, B, P) >= 0;
                bool inQ = IsLeft(A, B, Q) >= 0;
                if (inP) output.Add(P);
                if (inP ^ inQ)
                {
                    var I = LineIntersect(P, Q, A, B);
                    if (I.HasValue) output.Add(I.Value);
                }
            }
        }
        return output;
    }

    private static float IsLeft(Vector2 A, Vector2 B, Vector2 P)
        => (B.x - A.x) * (P.y - A.y) - (B.y - A.y) * (P.x - A.x);

    private static Vector2? LineIntersect(Vector2 P, Vector2 Q, Vector2 A, Vector2 B)
    {
        Vector2 r = Q - P, s = B - A;
        float denom = r.x * s.y - r.y * s.x;
        if (Mathf.Approximately(denom, 0f)) return null;
        float t = ((A.x - P.x) * s.y - (A.y - P.y) * s.x) / denom;
        return P + t * r;
    }









    /// <summary>
    /// Projects point p along the given axis onto the plane of the polygon defined by corners (must be ≥ 3 points),
    /// then tests whether that projected point lies inside the (convex or concave) polygon.
    /// Returns the projected point if it falls within the polygon, or null otherwise.
    /// </summary>
    public static Vector3? ProjectPointAlongAxisOntoPolygonSurface(
        Vector3 p,
        Vector3[] corners,
        Vector3 projectionAxis)
    {
        if (corners == null || corners.Length < 3)
            throw new System.ArgumentException("Polygon must have at least 3 corners", nameof(corners));

        // 1) Compute plane normal from first three corners
        Vector3 origin = corners[0];
        Vector3 e1 = corners[1] - origin;
        Vector3 e2 = corners[2] - origin;
        Vector3 normal = Vector3.Cross(e1, e2).normalized;

        // 2) If the projectionAxis is (nearly) parallel to the plane, we can't project
        float denom = Vector3.Dot(projectionAxis, normal);
        if (Mathf.Approximately(denom, 0f))
            return null;

        // 3) Find t so that (p + t * projectionAxis) lies on the plane:
        //    ( (p + t·axis) - origin ) · normal = 0
        //    → t = -((p - origin)·normal) / (axis·normal)
        float t = -Vector3.Dot(p - origin, normal) / denom;
        Vector3 proj = p + projectionAxis * t;

        // 4) Build local 2D basis (u, v) on the plane for point-in-poly test
        Vector3 u = e1.normalized;
        Vector3 v = Vector3.Cross(normal, u).normalized;

        // 5) Convert polygon corners to 2D
        var poly2D = new List<Vector2>(corners.Length);
        foreach (var c in corners)
        {
            Vector3 d = c - origin;
            poly2D.Add(new Vector2(
                Vector3.Dot(d, u),
                Vector3.Dot(d, v)
            ));
        }

        // 6) Convert projected point to 2D
        Vector3 dProj = proj - origin;
        Vector2 p2D = new Vector2(
            Vector3.Dot(dProj, u),
            Vector3.Dot(dProj, v)
        );

        // 7) Test if the 2D point lies inside the polygon
        return IsPointInPolygon(p2D, poly2D) ? proj : (Vector3?)null;
    }

    // Ray-crossing algorithm for 2D point-in-polygon
    private static bool IsPointInPolygon(Vector2 point, IList<Vector2> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector2 pi = poly[i], pj = poly[j];
            bool intersect = ((pi.y > point.y) != (pj.y > point.y)) &&
                             (point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x);
            if (intersect)
                inside = !inside;
        }
        return inside;
    }










    /// <summary>
    /// Projects a point onto the infinite line defined by two points (A→B),
    /// dropping a perpendicular from the point to the line.
    /// </summary>
    /// <param name="point">The world‐space point to project.</param>
    /// <param name="lineStart">One point on the line.</param>
    /// <param name="lineEnd">A second point on the line.</param>
    /// <returns>
    /// The closest point on the line to the original point (the foot of the perpendicular).
    /// </returns>
    public static Vector3 ProjectPointOntoLine(Vector3 point, (Vector3, Vector3) lineSegment)
    {
        Vector3 lineStart = lineSegment.Item1;
        Vector3 lineEnd = lineSegment.Item2;
        Vector3 lineDir = lineEnd - lineStart;
        float sqrLen = lineDir.sqrMagnitude;
        if (sqrLen < Mathf.Epsilon)
        {
            // Degenerate line: treat as a single point
            return lineStart;
        }

        // Project (point – lineStart) onto the line direction
        float t = Vector3.Dot(point - lineStart, lineDir) / sqrLen;
        // No clamping: this is the infinite line, not a segment
        return lineStart + lineDir * t;
    }

    /// <summary>
    /// Returns the perpendicular offset vector from the point to the line (A→B).
    /// </summary>
    /// <returns>offset = ProjectPointOntoLine(point,A,B) – point</returns>
    public static Vector3 PointLineOffsetVector(Vector3 point, (Vector3, Vector3) lineSegment)
    {
        var proj = ProjectPointOntoLine(point, lineSegment);
        return proj - point;
    }



    /// <summary>
    /// Projects a point onto the finite line segment [start→end], dropping a perpendicular.
    /// If the perpendicular foot lies outside the segment, returns null.
    /// </summary>
    /// <param name="point">The world‐space point to project.</param>
    /// <param name="lineSegment">Tuple of (start, end) defining the segment.</param>
    /// <returns>
    /// The foot of the perpendicular on the segment, or null if it lies outside the segment.
    /// </returns>
    public static Vector3? ProjectPointOntoSegment(Vector3 point, (Vector3 start, Vector3 end) lineSegment)
    {
        Vector3 a = lineSegment.start;
        Vector3 b = lineSegment.end;
        Vector3 ab = b - a;
        float sqrLen = ab.sqrMagnitude;
        if (sqrLen < Mathf.Epsilon)
        {
            // Degenerate segment: no valid projection
            return null;
        }

        // Compute projection parameter t
        float t = Vector3.Dot(point - a, ab) / sqrLen;
        if (t < 0f || t > 1f)
        {
            // Projection falls outside the segment
            return null;
        }

        // Return the perpendicular foot
        return a + t * ab;
    }

    /// <summary>
    /// Returns the perpendicular offset vector from the point to the finite segment [start→end].
    /// If no valid perpendicular falls within the segment, returns null.
    /// </summary>
    public static Vector3? PointToSegmentOffsetVector(Vector3 point, (Vector3 start, Vector3 end) lineSegment)
    {
        var proj = ProjectPointOntoSegment(point, lineSegment);
        if (!proj.HasValue)
            return null;
        return proj.Value - point;
    }



    // project line segment to line segment
    public static (Vector3 A, Vector3 B)? ProjectSegmentOntoSegment((Vector3 start, Vector3 end) segA, (Vector3 start, Vector3 end) segB)
    {
        // Project both endpoints of A onto B
        var p1 = ProjectPointOntoSegment(segA.start, segB);
        var p2 = ProjectPointOntoSegment(segA.end, segB);
        if (!p1.HasValue || !p2.HasValue)
            return null; // no valid projection within segment B
        // Return midpoint of the two projected points
        return (p1.Value, p2.Value);
    }

    // project line segment to line segment along given axis
    public static (Vector3 A, Vector3 B)? ProjectSegmentAlongAxisOntoSegment((Vector3 start, Vector3 end) segA, (Vector3 start, Vector3 end) segB, Vector3 axis)
    {
        // Project both endpoints of A onto B along the given axis
        var p1 = ProjectPointAlongAxisOntoPolygonSurface(segA.start, new Vector3[] { segB.start, segB.end, segB.end + Vector3.Cross(axis, segB.end - segB.start), segB.start + Vector3.Cross(axis, segB.end - segB.start) }, axis);
        var p2 = ProjectPointAlongAxisOntoPolygonSurface(segA.end, new Vector3[] { segB.start, segB.end, segB.end + Vector3.Cross(axis, segB.end - segB.start), segB.start + Vector3.Cross(axis, segB.end - segB.start) }, axis);
        if (!p1.HasValue || !p2.HasValue)
            return null; // no valid projection within segment B
        // Return midpoint of the two projected points
        return (p1.Value, p2.Value);
    }

    // Project a 3D segment along 'axis' onto the plane of 'polygonCorners' and
    // return the portion that lies inside the (convex) polygon. Null => no overlap.
    public static (Vector3 A, Vector3 B)? ProjectLineAlongAxisOntoPolygonSurface(
        (Vector3 start, Vector3 end) line,
        Vector3[] polygonCorners,
        Vector3 axis)
    {
        const float EPS = 1e-6f;

        if (polygonCorners == null || polygonCorners.Length < 3) return null;

        // Build plane (assumes polygonCorners are coplanar and ordered)
        var p0 = polygonCorners[0];
        var n = Vector3.Cross(polygonCorners[1] - p0, polygonCorners[2] - p0);
        if (n.sqrMagnitude < EPS) return null;
        n.Normalize();

        // If axis is parallel to plane, there is no projection onto the plane
        float denomPlane = Vector3.Dot(n, axis);
        if (Mathf.Abs(denomPlane) < EPS) return null;

        // Project a point to the plane along 'axis'
        Vector3 ProjectPoint(Vector3 X)
        {
            float s = Vector3.Dot(n, p0 - X) / denomPlane;
            return X + s * axis;
        }

        // Project endpoints (not an inside test yet)
        var p1 = ProjectPoint(line.start);
        var p2 = ProjectPoint(line.end);

        // Cyrus–Beck clip: build inward edge normals in plane
        var d = p2 - p1;
        float tEnter = 0f, tExit = 1f;

        // Centroid to orient edge normals inward
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < polygonCorners.Length; i++) centroid += polygonCorners[i];
        centroid /= Mathf.Max(1, polygonCorners.Length);

        for (int i = 0; i < polygonCorners.Length; i++)
        {
            var a = polygonCorners[i];
            var b = polygonCorners[(i + 1) % polygonCorners.Length];
            var edge = b - a;

            // In-plane normal perpendicular to edge; start with Cross(edge, n)
            var ni = Vector3.Cross(edge, n);             // lies in plane
            if (ni.sqrMagnitude < EPS) continue;         // degenerate edge
                                                         // Ensure 'ni' points inward (towards centroid)
            if (Vector3.Dot(ni, centroid - a) < 0f) ni = -ni;

            // f(t) = Dot(ni, (p1 + t d) - a)  >= 0  is the inside half-space
            float denomEdge = Vector3.Dot(ni, d);
            float numer = Vector3.Dot(ni, p1 - a);   // <-- corrected sign

            if (Mathf.Abs(denomEdge) < EPS)
            {
                // Segment is parallel to the boundary; accept only if already inside
                if (numer < -EPS) return null;          // outside for all t
                continue;                                // no constraint
            }

            float t = -numer / denomEdge;
            if (denomEdge > 0f)
            {
                // entering
                tEnter = Mathf.Max(tEnter, t);
            }
            else
            {
                // exiting
                tExit = Mathf.Min(tExit, t);
            }

            if (tEnter - tExit > EPS) return null;      // empty window
        }

        // Clamp to [0,1] and build result
        tEnter = Mathf.Clamp01(tEnter);
        tExit = Mathf.Clamp01(tExit);
        if (tExit < tEnter + EPS) return null;

        var A = p1 + d * tEnter;
        var B = p1 + d * tExit;
        return (A, B);
    }


}
