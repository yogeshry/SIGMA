// File: DataModels.cs
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data sent by devices to register with the server.
/// </summary>
[Serializable]
public class Device
{
    public string deviceId;
    public string deviceType;
    public int screenWidth;
    public int screenHeight;
    public float ppi; // Pixels per inch, optional for device resolution matching
    public Vector2 physicalSize;
    public Transform transform; // Optional: used to store device's transform in Unity

    public Device(DeviceInfo info)
    {
        deviceType = info.deviceType;
        deviceId = info.deviceId;
        screenHeight = info.screenHeight;
        screenWidth = info.screenWidth;
        ppi = info.ppi;
        SetPhysicalSize(info);
    }
    private const float InchesToMeters = 0.0254f;

    public void SetPhysicalSize(DeviceInfo info)
    {
        if (info.ppi <= 0)
            throw new ArgumentException("Device.ppi must be > 0 to compute physical size");

        float widthInches = info.screenWidth / info.ppi;
        float heightInches = info.screenHeight / info.ppi;

        physicalSize = new Vector2(
            widthInches * InchesToMeters,
            heightInches * InchesToMeters
        );
    }
    public bool MatchesConstraint(Constraint c)
    {
        //foreach (var d in devices)
        //{
        //    if (!string.IsNullOrEmpty(c.type) && d.value.type != c.type) continue;
        //    if (c.width != null && !c.width.Matches(d.width)) continue;
        //    if (c.height != null && !c.height.Matches(d.height)) continue;
        //    if (c.ppi != null && !c.ppi.Matches(d.ppi)) continue;
        //    results.Add(d);
        //}
        // Implement logic to check if this device matches the given constraints
        // This is a placeholder implementation. You should replace it with actual logic.
        // Example: return c.type == deviceType && c.width.Matches(screenWidth) && c.height.Matches(screenHeight);
        return (c.type == null || c.type == deviceType) &&
               (c.width == null || c.width.Matches(screenWidth)) &&
               (c.height == null || c.height.Matches(screenHeight)) &&
               (c.ppi == null || c.ppi.Matches(ppi));
    }

    public override string ToString()
    {
        return $"{deviceId} ({deviceType}) - {screenWidth}x{screenHeight} @ {ppi} PPI";
    }
}
/// <summary>
/// Data sent by devices to register with the server.
/// </summary>
[Serializable]
public class DeviceInfo
{
    public string deviceId;
    public string deviceType;
    public int screenWidth;
    public int screenHeight;
    public float ppi; // Pixels per inch, optional for device resolution matching
}

/// <summary>
/// Generic message wrapper for WebSocket commands.
/// </summary>
[Serializable]
public class WsMessage
{
    public string commandType;      // e.g., "createTarget"
    public JToken payload;         // JSON object containing command-specific data
}

/// <summary>
/// Acknowledgment response sent back to the device.
/// </summary>
[Serializable]
public class AckMessage
{
    public string type;
    public string deviceSessionId;
    public string timestamp;
}





/// <summary>
/// Validation result containing detailed error information
/// </summary>
[Serializable]
public class ValidationResult
{
    public bool IsValid;
    public List<ValidationError> Errors = new List<ValidationError>();
    public List<ValidationWarning> Warnings = new List<ValidationWarning>();
    public double ValidationDuration;    // milliseconds
    public string SchemaVersion;
    public string JsonPath;
}

/// <summary>
/// Detailed validation error information
/// </summary>
[Serializable]
public class ValidationError
{
    public string Message;
    public string Path;
    public string SchemaPath;
    public ValidationErrorType ErrorType;
    public object ExpectedValue;
    public object ActualValue;
    public int LineNumber;
    public int LinePosition;
}

/// <summary>
/// Validation warning information
/// </summary>
[Serializable]
public class ValidationWarning
{
    public string Message;
    public string Path;
    public WarningType WarningType;
}

public enum ValidationErrorType
{
    Unknown,
    Required,
    Type,
    Format,
    Pattern,
    Minimum,
    Maximum,
    MinLength,
    MaxLength,
    MinItems,
    MaxItems,
    UniqueItems,
    Enum,
    Const,
    AdditionalProperties,
    AdditionalItems,
    Dependencies,
    OneOf,
    AnyOf,
    AllOf,
    Not
}

public enum WarningType
{
    Deprecated,
    Performance,
    BestPractice,
    Security
}


/// <summary>
/// Constraint block for dynamic device matching.
/// </summary>
[System.Serializable]
public class Constraint
{
    public string type;
    public Comparator width;
    public Comparator height;
    public Comparator ppi;
}


/// <summary>
/// Represents a Comparator rule (eq, lt, lte, gt, gte).
/// </summary>
[System.Serializable]
public class Comparator
{
    public float? eq;
    public float? lt;
    public float? lte;
    public float? gt;
    public float? gte;

    /// <summary>
    /// Checks if a numeric value matches this comparator.
    /// </summary>
    public bool Matches(float value)
    {
        if (eq.HasValue && !Mathf.Approximately(value, eq.Value)) return false;
        if (lt.HasValue && !(value < lt.Value)) return false;
        if (lte.HasValue && !(value <= lte.Value)) return false;
        if (gt.HasValue && !(value > gt.Value)) return false;
        if (gte.HasValue && !(value >= gte.Value)) return false;
        return true;
    }
}