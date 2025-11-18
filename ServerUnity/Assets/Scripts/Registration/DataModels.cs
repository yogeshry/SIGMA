// File: DataModels.cs
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using UnityEngine;

/// <summary>
/// Data sent by devices to register with the server.
/// </summary>
[Serializable]
public class Device
{
    public string deviceId;
    public displayType deviceType;
    public DisplaySize displaySize;
    public displayOrientation displayOrientation; // Optional: device orientation
    public Transform transform; // Optional: used to store device's transform in Unity

    public Device(DeviceInfo info)
    {
        deviceType = info.deviceType;
        deviceId = info.deviceId;
        displaySize = new DisplaySize(info.screenWidth, info.screenHeight, info.ppi);
        displayOrientation = info.orientation;
    }

    public Transform getTransform()
    {
        return transform;
    }

    public bool MatchesConstraint(Constraint c)
    {
        // Convert the displayType enum to a string for comparison  
        string deviceTypeString = deviceType.ToString();

        return (c.type == null || c.type == deviceTypeString) &&
               (c.width == null || c.width.Matches(displaySize.widthPixels)) &&
               (c.height == null || c.height.Matches(displaySize.heightPixels)) &&
               (c.ppi == null || c.ppi.Matches(displaySize.ppi));
    }

    public override string ToString()
    {
        return $"{deviceId} ({deviceType}) - {displaySize.widthPixels}x{displaySize.heightPixels} @ {displaySize.ppi} PPI";
    }
}

[Serializable]
public class DisplaySize
{
    public int widthPixels;
    public int heightPixels;
    public float ppi;

    public float WidthInInches => widthPixels / ppi;
    public float HeightInInches => heightPixels / ppi;
    public float DiagonalInInches => MathF.Sqrt(WidthInInches * WidthInInches + HeightInInches * HeightInInches);

    public float WidthInMeters => WidthInInches * 0.0254f;
    public float HeightInMeters => HeightInInches * 0.0254f;
    public float DiagonalInMeters => DiagonalInInches * 0.0254f;

    public DisplaySize(int w, int h, float ppi)
    {
        widthPixels = w;
        heightPixels = h;
        this.ppi = ppi;
    }

    public override string ToString() =>
        $"{widthPixels}x{heightPixels}, {ppi} PPI ? {DiagonalInInches:F2}\" ({DiagonalInMeters:F3} m)";
}

/// <summary>  
/// Data sent by devices to register with the server.  
/// </summary>  
[Serializable]
public class DeviceInfo
{
    public string deviceId;
    [JsonConverter(typeof(StringEnumConverter))]
    public displayType deviceType;    
    [JsonConverter(typeof(StringEnumConverter))]
    public displayOrientation orientation = displayOrientation.Portrait; // ✅ Default value
    public int screenWidth;
    public int screenHeight;
    public float ppi; 
    public string trackerName; // Optional: tracker identifier for the device
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

//make displayType mapped string 
public enum displayType
{
    Mobile,
    Desktop,
    Immersive
}

public enum displayOrientation
{
    Portrait,
    Landscape
}

public enum trackingType
{
    IRMarker,
    ImageTarget
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