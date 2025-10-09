using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using UnityEngine;
using Vuforia;
using WebSocketSharp;
using WebSocketSharp.Server;
using static Unity.Burst.Intrinsics.X86.Avx;

/// <summary>
/// Handles WebSocket connections on the /register endpoint.
/// Not a MonoBehaviour—it is registered dynamically by UnityWebSocketServer.
/// </summary>
public class RegistrationBehavior : WebSocketBehavior
{
    protected override void OnOpen()
    {
        //Debug.Log($"[WS] Device connected: {ID}");
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        try
        {
            Debug.Log($"[WS] Device {ID} registering: {e.Data}");

            //var msg = JsonUtility.FromJson<WsMessage>(e.Data);
            WsMessage msg = JsonConvert.DeserializeObject<WsMessage>(e.Data);

            if (msg != null && msg.commandType == "register")
            {
                //Debug.Log($"[WS] Device {ID} registering: {msg.info.deviceId}");
                // Add or update registry entry
                DeviceInfo info = msg.payload.ToObject<DeviceInfo>();
                Debug.Log($"[WS] Device {ID} registering: {info.deviceId}, Type: {info.deviceType}, Screen: {info.screenWidth}x{info.screenHeight}");
                DeviceManager.RegisterDevice(info.deviceId, info);

                // Send acknowledgment
                var ack = new AckMessage
                {
                    type = "registerAck",
                    deviceSessionId = ID,
                    timestamp = DateTime.UtcNow.ToString("o")
                };

                Send(JsonUtility.ToJson(ack));

                // Log current devices
                var all = DeviceManager.GetAllDevices();
                Debug.Log("[WS] Current devices: " +
                    string.Join(", ", all.Select(kvp => $"{kvp.Key}: {kvp.Value.ToString()}")));

            }
            else if (msg != null && msg.commandType == "createTarget")
            {
                Debug.Log("[WS] createTarget...");
                ImageTargetInfo imageTargetInfo = JsonUtility.FromJson<ImageTargetInfo>(msg.payload.ToString());
                Device device = DeviceManager.GetDevice(imageTargetInfo.deviceId);
                if (device.deviceType == "Mobile" && device.screenWidth<800 )
                {
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        GameObject irtracker = GameObject.Find("MobileIRTracker");
                        if (irtracker == null)
                        {
                            Debug.LogWarning($"[WS] IR Tracker not found for device {ID}. Cannot create target.");
                            return;
                        }
                        DeviceManager.addTransform(imageTargetInfo.deviceId, irtracker.transform);
                        Debug.Log($"[WS] Device {ID} creating target on IR Tracker: {msg.payload}");
                        Utils.UpdateDeviceOutline(irtracker.transform, imageTargetInfo.deviceId);

                    });
                }
                else if(device.deviceType == "Mobile")
                {
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        GameObject irtracker = GameObject.Find("TabIRTracker");
                        if (irtracker == null)
                        {
                            Debug.LogWarning($"[WS] IR Tracker not found for device {ID}. Cannot create target.");
                            return;
                        }
                        DeviceManager.addTransform(imageTargetInfo.deviceId, irtracker.transform);
                        Debug.Log($"[WS] Device {ID} creating target on IR Tracker: {msg.payload}");
                        Utils.UpdateDeviceOutline(irtracker.transform, imageTargetInfo.deviceId);
                    });
                }
                else { 
                    Debug.Log($"[WS] Device {ID} creating target: {msg.payload}");
                    VuforiaTargetManager.CreateRuntimeImageTarget(imageTargetInfo);
                }
            }
            else
            {
                Debug.LogWarning($"[WS] Unexpected message from {ID}: {e.Data}");
            }
            MainThreadDispatcher.Enqueue(() =>
            {
                //DeviceResolver.ResolveDevices();
                // Resolve device pair

                //Device tempResolvedA, tempResolvedB;
                //DeviceResolver.ResolveDevices(out tempResolvedA, out tempResolvedB);

                //if (tempResolvedA == null || tempResolvedB == null)
                //{
                //    Debug.Log("[DeviceResolver] DeviceManager failed to resolve a valid pair.");
                //    return;
                //}
                //Debug.Log("lol1");
                SpatialObserver.Instance.RegisterRulesFromJson();

                RefStreamLogger.EnsureStarted();

            });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WS] Parse error from {ID}: {ex.Message}");
        }
    }

    protected override void OnClose(CloseEventArgs e)
    {
        //Debug.Log($"[WS] Device disconnected: {ID}");
        //DeviceManager.UnregisterDevice(ID);
    }

    // Broadcast to everyone:
    public void Broadcast(object obj)
    {
        Sessions.Broadcast(JsonConvert.SerializeObject(obj));
    }

}