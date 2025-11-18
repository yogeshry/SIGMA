using UnityEngine;
using Vuforia;

public static class CoordinateMapping
{
    // Pixel (x,y) -> world point on the display plane defined by corners
    // x in [0, widthPx), y in [0, heightPx); topOrigin = true if y=0 is top row.
    public static Vector3 PixelToWorldFromCorners(
        Vector2 pixel, int widthPx, int heightPx,
        Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR,
        bool topOrigin = true)
    {
        // Basis on the plane
        var E1 = TR - TL;                // horizontal edge vector
        var E2 = BL - TL;                // vertical edge (left side)
        var ex = E1.normalized;
        var n = Vector3.Cross(E1, E2).normalized;
        var ey = Vector3.Cross(n, ex).normalized; // points "down" the screen

        // Physical size (meters) along ex/ey
        float width_m = E1.magnitude;
        float height_m = Mathf.Abs(Vector3.Dot(E2, ey)); // robust to slight skew

        // Normalize pixel to [0,1]
        float u = Mathf.Clamp01(pixel.x / Mathf.Max(1, (widthPx - 1)));
        float v_norm = pixel.y / Mathf.Max(1, (heightPx - 1));
        float v = topOrigin ? v_norm : (1f - v_norm);

        // Place on plane
        Vector3 offset = (u * width_m) * ex + (v * height_m) * ey;
        return TL + offset;
    }

    public static bool TryWorldToPixelInRect(
        Vector3 world, int widthPx, int heightPx,
        Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR,
        out Vector2 pixel,
        bool topOrigin = true,
        float planeEps = 1e-4f,
        float edgeEps = 1e-6f)
    {
        pixel = default;

        // Edges from TL
        Vector3 E1 = TR - TL; // "width" direction (top edge)
        Vector3 E2 = BL - TL; // "height" direction (left edge)
        float lenE1 = E1.magnitude;
        float lenE2 = E2.magnitude;
        if (lenE1 < 1e-8f || lenE2 < 1e-8f) return false; // degenerate

        // Plane normal
        Vector3 n = Vector3.Cross(E1, E2);
        float nLen = n.magnitude;
        if (nLen < 1e-8f) return false; // points are (nearly) collinear
        n /= nLen;

        // Distance of point to plane
        float d = Vector3.Dot(world - TL, n);
        if (Mathf.Abs(d) > planeEps) return false; // not on the rectangle's plane

        // Project point onto the plane (robust when world is slightly off-plane)
        Vector3 p = world - d * n;

        // Build orthonormal basis in-plane
        Vector3 ex = E1 / lenE1;                // width axis
        Vector3 ey = Vector3.Cross(n, ex);      // height axis (in-plane, perp to ex)

        // Ensure ey points from TL toward BL so height is positive
        float height_m_raw = Vector3.Dot(E2, ey);
        if (height_m_raw < 0f) { ey = -ey; height_m_raw = -height_m_raw; }

        float width_m = lenE1;
        float height_m = height_m_raw;

        // Coordinates of projected point in the rectangle basis
        Vector3 rel = p - TL;
        float u_m = Vector3.Dot(rel, ex); // along width
        float v_m = Vector3.Dot(rel, ey); // along height

        // Inside test with a tiny tolerance (points on the edge count as inside)
        if (u_m < -edgeEps || u_m > width_m + edgeEps ||
            v_m < -edgeEps || v_m > height_m + edgeEps)
            return false;

        // Map to pixel space
        float u = Mathf.Clamp01(u_m / Mathf.Max(1e-6f, width_m));
        float v = Mathf.Clamp01(v_m / Mathf.Max(1e-6f, height_m));

        float px = u * (widthPx - 1);
        float py = (topOrigin ? v : (1f - v)) * (heightPx - 1);

        pixel = new Vector2(px, py);
        return true;
    }



    public static Vector3[] PixelToWorldFromCornersList(Vector2[] pixelPoints, int widthPx, int heightPx,
        Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR,
        bool topOrigin = true)
    {

        if (pixelPoints == null) return null;
        var result = new Vector3[pixelPoints.Length];
        for (int i = 0; i < pixelPoints.Length; i++)
            result[i] = PixelToWorldFromCorners(pixelPoints[i], widthPx, heightPx, TL, TR, BL, BR, topOrigin);
        return result;
    }
    public static Vector2 WorldToPixelFromCorners(Vector3 worldPoint, int widthPx, int heightPx,
        Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR,
        bool topOrigin = true)
    {
        Vector2 pixel;
        bool success = TryWorldToPixelInRect(worldPoint, widthPx, heightPx, TL, TR, BL, BR, out pixel, topOrigin, 0.002f, 0.001f);
        if (!success)
            return default;
        return new Vector2(pixel.x, pixel.y);
    }
    public static Vector2[] WorldToPixelFromCornersList(Vector3[] worldPoints, int widthPx, int heightPx,
        Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR,
        bool topOrigin = true)
    {
        if (worldPoints == null) return null;
        var result = new Vector2[worldPoints.Length];
        for (int i = 0; i < worldPoints.Length; i++)
            TryWorldToPixelInRect(worldPoints[i], widthPx, heightPx, TL, TR, BL, BR,out result[i], topOrigin, 0.002f,0.001f);
        return result;
    }
}
