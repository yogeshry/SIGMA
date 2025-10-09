using UnityEngine;
using System;

/// <summary>
/// Converts values in various units into a consistent “base” unit:
///  • lengths ? meters  
///  • angles  ? degrees  
/// </summary>
public static class UnitConverter
{
    /// <summary>
    /// Convert the raw <paramref name="value"/> in the given <paramref name="unit"/>
    /// into a base unit: meters if it’s a length unit, degrees if it’s an angle unit.
    /// If the unit is unknown, logs a warning and returns the original value.
    /// </summary>
    public static float ToBaseUnit(float value, string unit)
    {
        if (string.IsNullOrEmpty(unit))
        {
            Debug.LogWarning("[UnitConverter] Empty unit—returning original value");
            return value;
        }

        switch (unit.Trim().ToLowerInvariant())
        {
            // --- length units ? meters ---
            case "mm": return value * 0.001f;
            case "cm": return value * 0.01f;
            case "m": return value;
            case "inch":
            case "in": return value * 0.0254f;

            // --- angle units ? degrees ---
            case "deg": return value;
            case "°": return value;
            case "rad": return value * Mathf.Rad2Deg;

            // --- velocity units ? cm/s ---
            case "cm/s": return value;
            case "cm/s^2": return value;
            default:
                Debug.LogWarning($"[UnitConverter] Unknown unit '{unit}', returning original value");
                return value;
        }
    }

    public static float FromBaseUnit(float value, string unit)
    {
        if (string.IsNullOrEmpty(unit))
        {
            Debug.LogWarning("[UnitConverter] Empty unit—returning original value");
            return value;
        }
        switch (unit.Trim().ToLowerInvariant())
        {
            // --- meters ? length units ---
            case "mm": return value * 1000f;
            case "cm": return value * 100f;
            case "m": return value;
            case "inch":
            case "in": return value / 0.0254f;
            // --- degrees ? angle units ---
            case "deg":
            case "°": return value;
            case "rad": return value * Mathf.Deg2Rad;
            // --- ratio ? projection units ---
            case "ratio": return value;
            case "%":
            case "percent": return value * 100f;
            // --- velocity ?  ---
            case "m/s": return value;
            case "cm/s": return value * 100f;
            case "mm/s": return value * 1000f;
            case "inch/s": return value / 0.0254f;


            default:
                Debug.LogWarning($"[UnitConverter] Unknown unit '{unit}', returning original value");
                return value;
        }
    }
}
