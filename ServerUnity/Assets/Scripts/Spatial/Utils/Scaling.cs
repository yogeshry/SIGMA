using UnityEngine;

public static class RangeMap
{
    /// <summary>
    /// Remap a value from [srcMin, srcMax] to [dstMin, dstMax].
    /// If clamp = true, the input is clamped to the source range before mapping.
    /// Works even if ranges are reversed (e.g., srcMin > srcMax).
    /// </summary>
    public static float Remap(float value, float srcMin, float srcMax, float dstMin, float dstMax, bool clamp = true)
    {
        float t = Normalize(value, srcMin, srcMax, clamp);
        // Use LerpUnclamped so "clamp" only affects t, not the output
        return Mathf.LerpUnclamped(dstMin, dstMax, t);
    }

    /// <summary>
    /// Remap a value using Vector2 ranges: src = (min,max), dst = (min,max).
    /// </summary>
    public static float Remap(float value, Vector2 srcRange, Vector2 dstRange, bool clamp = true)
        => Remap(value, srcRange.x, srcRange.y, dstRange.x, dstRange.y, clamp);

    /// <summary>
    /// Normalize a value to t in [0,1] from [a,b]. If clamp=true, t is clamped to [0,1].
    /// Handles reversed ranges and degenerate ranges (a?b) by returning 0.
    /// </summary>
    public static float Normalize(float value, float a, float b, bool clamp = true)
    {
        // Degenerate range ? treat as 0
        if (Mathf.Approximately(a, b)) return 0f;

        float t = (value - a) / (b - a);   // works even if (b < a)
        return clamp ? Mathf.Clamp01(t) : t;
    }

    /// <summary>
    /// Denormalize t in [0,1] to [min,max]. If clamp=true, t is clamped to [0,1].
    /// </summary>
    public static float Denormalize(float t, float min, float max, bool clamp = false)
    {
        if (clamp) t = Mathf.Clamp01(t);
        return Mathf.LerpUnclamped(min, max, t);
    }

    /// <summary>
    /// Convenience: map from source range to [0,1].
    /// </summary>
    public static float To01(float value, Vector2 srcRange, bool clamp = true)
        => Normalize(value, srcRange.x, srcRange.y, clamp);

    /// <summary>
    /// Convenience: map from [0,1] to destination range.
    /// </summary>
    public static float From01(float t, Vector2 dstRange, bool clamp = false)
        => Denormalize(t, dstRange.x, dstRange.y, clamp);
}
