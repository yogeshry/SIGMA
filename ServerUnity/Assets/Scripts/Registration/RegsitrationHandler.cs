using Newtonsoft.Json;
using UnityEngine;
using System;
using System.Linq;
using TMPro;
using Vuforia;

/// <summary>
/// Handles the registration and target creation logic for WebSocket messages.
/// Used by RegistrationBehavior.
/// </summary>
public class RegistrationHandler
{
    private readonly bool verboseLogs = true;
    private readonly string sessionId;

    public RegistrationHandler(string sessionId)
    {
        this.sessionId = sessionId;
    }

    public bool HandleRegister(WsMessage msg)
    {
        try
        {
            DeviceInfo info = msg.payload.ToObject<DeviceInfo>();
            if (info == null)
            {
                Debug.LogWarning($"[WS] Device info missing in register payload from {sessionId}");
                return false;
            }

            if (verboseLogs)
                Debug.Log($"[WS] Registering {info.deviceId}: {info.deviceType} ({info.screenWidth}x{info.screenHeight})");

            DeviceManager.RegisterDevice(info.deviceId, info);
            HandleCreateTargetOnRegistration(info.deviceId, info.trackerName);


            if (verboseLogs)
            {
                var all = DeviceManager.GetAllDevices();
                Debug.Log("[WS] Current devices: " +
                    string.Join(", ", all.Select(kvp => $"{kvp.Key}: {kvp.Value}")));
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WS] Error registering device {sessionId}: {ex.Message}");
            return false;
        }
    }

    public void HandleCreateTarget(WsMessage msg)
    {
        try
        {
            if (verboseLogs)
                Debug.Log($"[WS] Device {sessionId} creating target...");

            ImageTargetInfo imageTargetInfo = JsonUtility.FromJson<ImageTargetInfo>(msg.payload.ToString());
            Device device = DeviceManager.GetDevice(imageTargetInfo.deviceId);

            if (device == null)
            {
                Debug.LogWarning($"[WS] Device {imageTargetInfo.deviceId} not found for createTarget.");
                return;
            }

            MainThreadDispatcher.Enqueue(() =>
            {
                if (device.deviceType == displayType.Mobile)
                {
                    string trackerName = device.displaySize.widthPixels < 800 ? "MobileIRTracker" : "TabIRTracker";
                    GameObject tracker = GameObject.Find(trackerName);

                    if (tracker == null)
                    {
                        Debug.LogWarning($"[WS] IR Tracker '{trackerName}' not found for device {sessionId}.");
                        return;
                    }

                    DeviceManager.addTransform(imageTargetInfo.deviceId, tracker.transform);
                    if (verboseLogs)
                        Debug.Log($"[WS] Device {sessionId} creating target on {trackerName}: {msg.payload}");
                    Utils.UpdateDeviceOutline(tracker.transform, imageTargetInfo.deviceId);
                }
                else
                {
                    if (verboseLogs)
                        Debug.Log($"[WS] Device {sessionId} creating Vuforia target: {msg.payload}");
                    VuforiaTargetManager.CreateRuntimeImageTarget(imageTargetInfo);
                }
            });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WS] Error creating target for {sessionId}: {ex.Message}");
        }
    }

    private void HandleCreateTargetOnRegistration(string deviceId, string trackerName)
    {
        try
        {
            if (verboseLogs)
                Debug.Log($"[WS] Device {deviceId} attaching predefined tracker...");

            MainThreadDispatcher.Enqueue(() =>
            {
                GameObject tracker = GameObject.Find(trackerName);
                if (tracker == null)
                {
                    Debug.LogWarning($"[WS] Tracker '{trackerName}' not found for device {deviceId}.");
                    return;
                }

                DeviceManager.addTransform(deviceId, tracker.transform);

                // if tracker name == "DesktopIRTracker" pause vuforia tracking
                // If tracker name == "DesktopIRTracker", pause Vuforia tracking
                if (trackerName == "DesktopImageTarget")
                { 
                    // Disable the ImageTargetBehaviour to stop tracking.
                    var observerBehaviour = tracker.GetComponent<ImageTargetBehaviour>();
                    if (observerBehaviour != null)
                    {
                        observerBehaviour.enabled = false;
                        Debug.Log($"[WS] Vuforia tracking paused for tracker '{trackerName}' on device {deviceId}.");
                    }
                    Utils.UpdateDeviceSpaceLabel(tracker.transform, deviceId);
                }

                if (verboseLogs)
                    Debug.Log($"[WS] Tracker {trackerName} attached to Device {deviceId}");
                Utils.UpdateDeviceOutline(tracker.transform, deviceId);
                Utils.DisableDeviceOutline(tracker.transform);
            });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WS] Error attaching tracker for {deviceId}: {ex.Message}");
        }
    }
}
