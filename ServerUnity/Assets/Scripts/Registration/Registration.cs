using Newtonsoft.Json;
using System;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

/// <summary>
/// Handles WebSocket connections on the /register endpoint.
/// Delegates logic to RegistrationHandler.
/// </summary>
public class RegistrationBehavior : WebSocketBehavior
{
    private static bool VerboseLogs = true;
    private RegistrationHandler handler;

    protected override void OnOpen()
    {
        handler = new RegistrationHandler(ID);
        if (VerboseLogs)
            Debug.Log($"[WS] Client connected: {ID}");
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        try
        {
            if (VerboseLogs)
                Debug.Log($"[WS] Client {ID} message: {e.Data}");

            WsMessage msg = JsonConvert.DeserializeObject<WsMessage>(e.Data);
            if (msg == null)
            {
                Debug.LogWarning($"[WS] Invalid message format from {ID}");
                return;
            }

            switch (msg.commandType)
            {
                case "register":
                    MainThreadDispatcher.Enqueue(() =>
                    {
                        var ok = handler.HandleRegister(msg); // make it return bool or set handler.LastError
                        if (ok)
                        {
                            var ack = new AckMessage
                            {
                                type = "registerAck",
                                deviceSessionId = ID,
                                timestamp = DateTime.UtcNow.ToString("o"),
                            };
                            Send(JsonConvert.SerializeObject(ack));     // reply to the same client
                        }
                    });
                    break;

                case "createTarget":
                    handler.HandleCreateTarget(msg);
                    break;

                default:
                    Debug.LogWarning($"[WS] Unknown command '{msg.commandType}' from {ID}");
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
            Debug.LogWarning($"[WS] Parse error from {ID}: {ex.Message}");
        }
    }

    protected override void OnClose(CloseEventArgs e)
    {
        if (VerboseLogs)
            Debug.Log($"[WS] Device disconnected: {ID}");
    }

    public void Broadcast(object obj)
    {
        Sessions.Broadcast(JsonConvert.SerializeObject(obj));
    }

    public static void SetVerboseLogs(bool active)
    {
        VerboseLogs = active;
        Debug.Log($"[WS] Logger {(active ? "enabled" : "disabled")}");
    }
}
