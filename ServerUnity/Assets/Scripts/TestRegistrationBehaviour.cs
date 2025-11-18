using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
// "Desktop1": {
// "name": "Desktop1",
// "constraint": {
//   "type": "Desktop1",
//   "width": { "eq": 1920 },
//   "height": { "eq": 1200 },
//   "ppi": { "eq": 95 }
// }
// },
// "Tab11": {
// "name": "Tab1",
// "constraint": {
//   "type": "Mobile",
//   "width": { "eq": 1920 },
//   "height": { "eq": 1200 },
//   "ppi": { "eq": 206 }
// }
// },
// "Mobile1": {
// "name": "Mobile1",
// "constraint": {
//   "type": "Mobile",
//   "width": { "eq": 1080 },
//   "height": { "eq": 2340 },
//   "ppi": { "eq": 395 }
// }
// },


public class RegistrationBehaviorTester: MonoBehaviour
{
    public bool VerboseLogs = true;
    private RegistrationHandler handler;

    void Start()
    {
        handler = new RegistrationHandler("testSession");

        // 1) mock‐register a mobile device
        TestMessage(@"
        {
          ""commandType"": ""register"",
          ""payload"": {
            ""deviceId"": ""mobile1-uuid"",
            ""deviceType"": ""Mobile"",
            ""screenWidth"": 1080,
            ""screenHeight"": 2340,
            ""ppi"": 395,
            ""trackerName"": ""MobileIRTracker""
          }
        }");
        TestMessage(@"
        {
          ""commandType"": ""register"",
          ""payload"": {
            ""deviceId"": ""tab1-uuid"",
            ""deviceType"": ""Mobile"",
            ""screenWidth"": 1920,
            ""screenHeight"": 1200,
            ""ppi"": 206,
            ""trackerName"": ""TabMockTracker""
          }
        }");
        //// 3) mock-createTarget for the mobile
        //TestMessage(@"
        //{
        //  ""commandType"": ""createTarget"",
        //  ""payload"": {
        //    ""deviceId"": ""mobile-123""
        //  }
        //}");
        //// 2) mock-register a desktop device
        TestMessage(@"
        {
          ""commandType"": ""register"",
          ""payload"": {
            ""deviceId"": ""desktop1-uuid"",
            ""deviceType"": ""Desktop"",
            ""screenWidth"": 1920,
            ""screenHeight"": 1200,
            ""ppi"": 95,
            ""trackerName"": ""DesktopImageTarget""
          }
        }");



        // 4) mock-createTarget for the desktop
        //TestMessage(@"
        //{
        //  ""commandType"": ""createTarget"",
        //  ""payload"": {
        //    ""deviceId"": ""desktop-456"",
        //    ""name"": ""DesktopImageTarget"",
        //    ""textureBase64"": ""desk/path.png"",
        //    ""size"": 0.5
        //  }
        //}");
    }
    public void TestMessage(string json)
    {
        Debug.Log($"--- Processing mock message: {json}");
        WsMessage msg = JsonConvert.DeserializeObject<WsMessage>(json);
        if (msg == null)
        {
            Debug.LogWarning($"[WS] Invalid message format");
            return;
        }
        try
        {
            switch (msg.commandType)
            {
                case "register":
                    handler.HandleRegister(msg);
                    break;

                case "createTarget":
                    handler.HandleCreateTarget(msg);
                    break;

                default:
                    Debug.LogWarning($"[WS] Unknown command '{msg.commandType}'");
                    break;
            }

            // Post-message logic
            MainThreadDispatcher.Enqueue(() =>
            {
                SpatialObserver.Instance.RegisterRulesFromJson();
                if (VerboseLogs)
                    RefStreamLogger.EnsureStarted();
            });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WS] Parse error: {ex.Message}");
        }
    }
}
