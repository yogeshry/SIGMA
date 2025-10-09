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
}
