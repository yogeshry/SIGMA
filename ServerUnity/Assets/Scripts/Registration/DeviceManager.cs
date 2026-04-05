using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages registered devices across all WebSocket sessions.
/// </summary>
public static class DeviceManager
{
    public static event Action OnDevicesChanged;
    private static bool VerboseLogs = false;

    // Maps session ID to client info
    private static readonly Dictionary<string, Device> devices = new Dictionary<string, Device>();
    static readonly Dictionary<string, string> _deviceToSession
    = new Dictionary<string, string>();

    public static void MapDeviceToSession(string deviceId, string sessionId)
        => _deviceToSession[deviceId] = sessionId;

    public static bool TryGetSession(string deviceId, out string sessionId)
        => _deviceToSession.TryGetValue(deviceId, out sessionId);
    /// <summary>
    /// Add or update a client's registration.
    /// </summary>
    public static void RegisterDevice(string sessionId, DeviceInfo info)
    {
        //check if already registered
        if (devices.ContainsKey(sessionId))
        {
            Debug.Log($"[RegMgr] Already registered client {sessionId}");
        }

        //parse info and register
        Device device = new Device(info);

        devices[sessionId] = device;
        if (VerboseLogs)
         Debug.Log($"[RegMgr] Registered/Updated client {sessionId}: {device}");

        // Trigger event for listeners like LogToUI
        OnDevicesChanged?.Invoke();
    }

    /// <summary>
    /// Remove a client by session ID.
    /// </summary>
    public static void UnregisterDevice(string sessionId)
    {
        if (devices.Remove(sessionId))
        {
            if (VerboseLogs)
                Debug.Log($"[RegMgr] Unregistered client {sessionId}");
            // Trigger event for listeners like LogToUI
            OnDevicesChanged?.Invoke();
        }
    }

    public static void addTransform(string sessionId, Transform transform)
    {
        if (devices.TryGetValue(sessionId, out var info))
        {
            devices[sessionId].transform = transform;
            if (VerboseLogs)
                Debug.Log($"[RegMgr] Added transform for client {sessionId}");
            OnDevicesChanged?.Invoke();
        }
        else
        {
            Debug.LogWarning($"[RegMgr] No client found for session {sessionId}");
        }
    }

    public static Device GetDevice(string deviceId)
    {
        // Fix: Assign the result of FirstOrDefault to a variable and return it.  
        var device = devices.FirstOrDefault(kvp => kvp.Value.deviceId == deviceId).Value;
        return device;
    }

    public static List<Device> FindDevices(Constraint c)
    {
        var results = new List<Device>();
        //iterate over devices list;
        if (c == null)
        {
            Debug.LogWarning("[RegMgr] Constraint is null, returning all devices.");
            return null;
        }
        if (string.IsNullOrEmpty(c.type) && c.width == null && c.height == null && c.ppi == null)
        {
            Debug.LogWarning("[RegMgr] No constraints specified, returning all devices.");
            return null;
        }
        if (VerboseLogs)
            Debug.Log($"[RegMgr] Finding devices with constraints: {c.type}, {c.width}, {c.height}, {c.ppi}");
        //filter devices based on constraints
        //foreach device in devices

        if (VerboseLogs)
            Debug.Log($"[RegMgr] Total devices registered: {devices.Count}");
        if (devices.Count == 0)
        {
            Debug.LogWarning("[RegMgr] No devices registered.");
            return results;
        }
        foreach (var kvp in devices)
        {
            var device = kvp.Value;
            if (device.MatchesConstraint(c))
            {
                results.Add(device);
                if (VerboseLogs)
                    Debug.Log($"[RegMgr] Device {device.deviceId} matches constraints.");
            }
            else
            {
                if (VerboseLogs)
                    Debug.Log($"[RegMgr] Device {device.deviceId} does not match constraints.");
            }
        }
       
        return results;
    }

    public static Dictionary<string, Device> GetAllDevices()
    {
        return new Dictionary<string, Device>(devices);
    }

    /// <summary>
    /// Get a snapshot of all registered devices.
    /// </summary>
    public static IReadOnlyDictionary<string, Device> GetAllClients()
    {
        return devices;
    }

    public static void GetDevicePair(string configString, out Device resolvedA, out Device resolvedB)
    {
        resolvedA = null;
        resolvedB = null;

        if (string.IsNullOrEmpty(configString))
        {
            Debug.LogError("Assign configAsset and deviceManager in Inspector.");
            return;
        }

        JObject config = JObject.Parse(configString);
        var specA = config["deviceA"];
        var specB = config["deviceB"];

        int indexA = 0;
        int indexB = 0;

        // If equivalent constraints, pick sequential devices: 0,1,2...
        if (TryGetConstraint(specA, out var cA) &&
            TryGetConstraint(specB, out var cB) &&
            ConstraintsEquivalent(cA, cB))
        {
            Debug.Log("[RegMgr] Device specs have equivalent constraints; selecting sequential devices.");
            indexA = 0;
            indexB = 1; // next device in same match set
        }

        resolvedA = ResolveSpec(specA, indexA);
        resolvedB = ResolveSpec(specB, indexB);

        if (VerboseLogs)
            Debug.Log($"[RegMgr] Resolved A={resolvedA?.deviceId} (idx {indexA}), B={resolvedB?.deviceId} (idx {indexB})");
    }

    /// <summary>
    /// Resolves a JToken spec into matching devices.
    /// </summary>
    static Device ResolveSpec(JToken spec, int matchIndex = 0)
    {
        if (spec == null)
            return null;
        // Defensive: negative index makes no sense
        if (matchIndex < 0) matchIndex = 0;

        if (spec.Type == JTokenType.String)
        {
            string id = spec.ToString();
            Device dev = GetDevice(id);
            return dev;
        }
        else if (spec.Type == JTokenType.Object)
        {
            var constraintToken = spec["constraint"];
            if (constraintToken != null)
            {
                Constraint c = constraintToken.ToObject<Constraint>();
                var devices = FindDevices(c);
                if (devices != null && devices.Count > 0)
                {
                    if (matchIndex < devices.Count)
                        return devices[matchIndex];
                    else
                    {
                        return devices[0];
                    }

                    Debug.LogWarning($"[RegMgr] Constraint matched {devices.Count} device(s) but matchIndex={matchIndex} is out of range.");
                    return null;
                }
                else
                {
                    Debug.LogWarning("[RegMgr] No devices found matching the constraints.");
                }
            }
        }
        return null;
    }
    static bool TryGetConstraint(JToken spec, out Constraint c)
    {
        c = null;
        if (spec == null || spec.Type != JTokenType.Object) return false;

        var ct = spec["constraint"];
        if (ct == null) return false;

        c = ct.ToObject<Constraint>();
        return c != null;
    }

    static bool ConstraintsEquivalent(Constraint a, Constraint b)
    {
        if (a == null || b == null) return false;

        return string.Equals(a.type, b.type, StringComparison.OrdinalIgnoreCase)
            && ComparatorEquivalent(a.width, b.width)
            && ComparatorEquivalent(a.height, b.height)
            && ComparatorEquivalent(a.ppi, b.ppi);
    }

    static bool ComparatorEquivalent(object x, object y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;

        var A = Canon(x);
        var B = Canon(y);

        if (A.Count != B.Count) return false;
        for (int i = 0; i < A.Count; i++)
            if (A[i].op != B[i].op || A[i].val != B[i].val) return false;

        return true;
    }

    static List<(string op, double val)> Canon(object o)
    {
        var jo = o as JObject ?? JObject.FromObject(o);

        // supports: { op:"lte", value:1200 }  
        if (jo.TryGetValue("op", StringComparison.OrdinalIgnoreCase, out var opTok) &&
            jo.TryGetValue("value", StringComparison.OrdinalIgnoreCase, out var valTok))
        {
            if (TryDouble(valTok, out var v))
                return new List<(string op, double val)> { (NormOp(opTok.ToString()), v) };

            return new List<(string op, double val)>(); // value is null/invalid -> treat as empty comparator  
        }

        // supports: { lte:1200, gte:10 } (skip null/non-numeric)  
        var pairs = new List<(string op, double val)>();
        foreach (var p in jo.Properties())
            if (TryDouble(p.Value, out var v))
                pairs.Add((NormOp(p.Name), v));

        return pairs.OrderBy(p => p.op).ThenBy(p => p.val).ToList();
    }

    static bool TryDouble(JToken t, out double v)
    {
        v = 0;
        if (t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined) return false;

        if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
        {
            v = t.Value<double>();
            return true;
        }

        if (t.Type == JTokenType.String)
            return double.TryParse(t.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        return false;
    }
    static string NormOp(string op)
    {
        op = (op ?? "").Trim().ToLowerInvariant();
        return op switch
        {
            "==" or "=" or "eq" => "eq",
            "<=" or "lte" => "lte",
            ">=" or "gte" => "gte",
            "<" or "lt" => "lt",
            ">" or "gt" => "gt",
            _ => op
        };
    }



    ///// <summary>
    ///// Computes the pose of deviceB in the local frame of deviceA.
    ///// </summary>
    //public static bool TryGetRelativePose(
    //    string sessionA,
    //    string sessionB,
    //    out Vector3 relativePosition,
    //    out Quaternion relativeRotation)
    //{
    //    relativePosition = Vector3.zero;
    //    relativeRotation = Quaternion.identity;

    //    if (!devices.TryGetValue(sessionA, out var infoA) || infoA.transform == null)
    //    {
    //        Debug.LogWarning($"[RegMgr] Missing transform for session '{sessionA}'");
    //        return false;
    //    }
    //    if (!devices.TryGetValue(sessionB, out var infoB) || infoB.transform == null)
    //    {
    //        Debug.LogWarning($"[RegMgr] Missing transform for session '{sessionB}'");
    //        return false;
    //    }

    //    Transform ta = infoA.transform;
    //    Transform tb = infoB.transform;

    //    // Relative rotation: rotation from A's space to B's
    //    relativeRotation = Quaternion.Inverse(ta.rotation) * tb.rotation;

    //    // Relative position: B's position in A's local coordinates
    //    Vector3 worldDelta = tb.position - ta.position;
    //    relativePosition = Quaternion.Inverse(ta.rotation) * worldDelta;
    //    if (VerboseLogs)
    //        Debug.Log($"[RegMgr] Relative pose from {sessionA} to {sessionB}: " +
    //              $"Position: {relativePosition}, Rotation: {relativeRotation}");

    //    return true;
    //}
}
