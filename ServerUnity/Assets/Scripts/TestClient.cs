using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class LocalLogicTester : MonoBehaviour
{
    void Start()
    {
        // 1) mock‐register a mobile device
        TestMessage(@"
        {
          ""commandType"": ""register"",
          ""payload"": {
            ""deviceId"": ""mobile-123"",
            ""deviceType"": ""Mobile"",
            ""screenWidth"": 350,
            ""screenHeight"": 800,
            ""ppi"": 100
          }
        }");
        // 3) mock-createTarget for the mobile
        TestMessage(@"
        {
          ""commandType"": ""createTarget"",
          ""payload"": {
            ""deviceId"": ""mobile-123""
          }
        }");
        //// 2) mock-register a desktop device
        TestMessage(@"
        {
          ""commandType"": ""register"",
          ""payload"": {
            ""deviceId"": ""desktop-456"",
            ""deviceType"": ""Desktop"",
            ""screenWidth"": 1250,
            ""screenHeight"": 1000,
            ""ppi"": 100
          }
        }");



        // 4) mock-createTarget for the desktop
        TestMessage(@"
        {
          ""commandType"": ""createTarget"",
          ""payload"": {
            ""deviceId"": ""desktop-456"",
            ""name"": ""DesktopImageTarget"",
            ""textureBase64"": ""desk/path.png"",
            ""size"": 0.5
          }
        }");
    }

    void TestMessage(string json)
    {
        WsMessage msg = JsonConvert.DeserializeObject<WsMessage>(json);

        string ID = msg.payload["deviceId"].ToString();
        Debug.Log($"--- Processing mock message: {json}");

        try
        {
            // exactly your WebSocketBehavior logic:

            if (msg != null && msg.commandType == "register")
            {
                DeviceInfo info = msg.payload.ToObject<DeviceInfo>();
                Debug.Log($"[WS] Device {ID} registering: {info.deviceId}, Type: {info.deviceType}, Screen: {info.screenWidth}x{info.screenHeight}");
                DeviceManager.RegisterDevice(ID, info);

                var ack = new AckMessage
                {
                    type = "registerAck",
                    deviceSessionId = ID,
                    timestamp = DateTime.UtcNow.ToString("o")
                };
                Debug.Log($"[WS] Sending Ack: {JsonUtility.ToJson(ack)}");

                var all = DeviceManager.GetAllDevices();
                Debug.Log($"[WS] Current devices: {string.Join(", ", all.Keys)}");
            }
            else if (msg != null && msg.commandType == "createTarget")
            {
                // here use Newtonsoft for consistency:
                ImageTargetInfo iti = msg.payload.ToObject<ImageTargetInfo>();

                if (DeviceManager.GetDevice(iti.deviceId).deviceType == "Mobile")
                {
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        GameObject irtracker = GameObject.Find("MobileIRTracker");
                        if (irtracker == null)
                        {
                            Debug.LogWarning($"[WS] IR Tracker not found for device {ID}. Cannot create target.");
                            return;
                        }
                        DeviceManager.addTransform(iti.deviceId, irtracker.transform);
                        // get linerenderer named outline which is child of irtracker
                        Utils.UpdateDeviceOutline(irtracker.transform, iti.deviceId);
                        Debug.Log($"[WS] Device {ID} creating target on IR Tracker: {msg.payload}");
                    });
                }
                else
                {
                    Debug.Log($"[WS] Device {ID} creating target: {msg.payload}");
                    VuforiaTargetManager.CreateRuntimeImageTarget(iti);
                }
            }
            else
            {
                Debug.LogWarning($"[WS] Unexpected message from {ID}: {json}");
            }

            // finally your post‐message logic
            MainThreadDispatcher.Enqueue(() =>
            {
                //Device a, b;
                //DeviceResolver.ResolveDevices(out a, out b);
                //if (a == null || b == null)
                //{
                //    Debug.LogWarning("[DeviceResolver] DeviceManager failed to resolve a valid pair.");
                //    return;
                //}
                SpatialObserver.Instance.RegisterRulesFromJson();
                RefStreamLogger.EnsureStarted();

            });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WS] Parse error from {ID}: {ex.Message}");
        }
    }
}
