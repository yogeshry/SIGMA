using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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

    public static Device GetDevice(string sessionId)
    {
        if (devices.TryGetValue(sessionId, out var info))
        {
            return info;
        }
        Debug.LogWarning($"[RegMgr] No device found for session {sessionId}");
        return null;
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
        if (configString == null)
        {
            Debug.LogError("Assign configAsset and deviceManager in Inspector.");
            return;
        }
        JObject config = JObject.Parse(configString);
        resolvedA = ResolveSpec(config["deviceA"]);
        resolvedB = ResolveSpec(config["deviceB"]);
        if (VerboseLogs)
            Debug.Log($"Resolved {resolvedA} device(s) for deviceA and {resolvedB} for deviceB.");
    }
    /// <summary>
    /// Resolves a JToken spec into matching devices.
    /// </summary>
    static Device ResolveSpec(JToken spec)
    {
        if (spec == null)
            return null;

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
                if (devices.Count > 0)
                {
                    // Return the first matching device
                    return devices[0];
                }
                else
                {
                    Debug.LogWarning("[RegMgr] No devices found matching the constraints.");
                }
            }
        }
        return null;
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
