// ===============================
// FILE: Scatter3DColor.cs
// Explicit continent → color mapping (matches web colorMap.continent)
// ===============================
using System;
using UnityEngine;

public static class Scatter3DColor
{
    // Web palette:
    // Europe   "#f7e00a" (yellow)
    // Americas "#90e82c" (green)
    // Asia     "#ff5972" (pink)
    // Africa   "#34deed" (blue-ish)
    // Oceania  "#8161ff" (violet)
    // Unknown  "#999999" (gray)

    static readonly Color EUROPE = Scatter3DUtil.Hex("#f7e00a");
    static readonly Color AMERICAS = Scatter3DUtil.Hex("#90e82c");
    static readonly Color ASIA = Scatter3DUtil.Hex("#ff5972");
    static readonly Color AFRICA = Scatter3DUtil.Hex("#34deed");
    static readonly Color OCEANIA = Scatter3DUtil.Hex("#8161ff");
    static readonly Color UNKNOWN = Scatter3DUtil.Hex("#999999");

    public static Color ColorForKey(string key, ScatterPalette pal)
    {
        // Keep your HSVHash option as-is (useful for arbitrary categories)
        //if (pal == ScatterPalette.HSVHash)
        //{
        //    float hue = ((key?.Length ?? 0) == 0)
        //        ? 0f
        //        : (Scatter3DUtil.StableIndex(key, 65535) / 65535f);
        //    return Color.HSVToRGB(hue, 0.65f, 0.95f);
        //}

        // Otherwise: treat key as continent/region string and map explicitly
        return ColorForContinent(key);
    }

    public static Color ColorForContinent(string continent)
    {
        if (string.IsNullOrWhiteSpace(continent)) return UNKNOWN;

        var c = continent.Trim();

        // Normalize a few common variants
        if (EqualsIC(c, "North America") || EqualsIC(c, "South America"))
            return AMERICAS;

        if (EqualsIC(c, "Europe")) return EUROPE;
        if (EqualsIC(c, "Americas")) return AMERICAS;
        if (EqualsIC(c, "Asia")) return ASIA;
        if (EqualsIC(c, "Africa")) return AFRICA;
        if (EqualsIC(c, "Oceania")) return OCEANIA;
        if (EqualsIC(c, "Unknown")) return UNKNOWN;

        // Fallback for unexpected labels
        return UNKNOWN;
    }

    static bool EqualsIC(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
