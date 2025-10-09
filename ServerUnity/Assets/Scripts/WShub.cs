using Newtonsoft.Json;
using UnityEngine;
using WebSocketSharp.Server;

public static class WsHub
{
    static WebSocketSharp.Server.WebSocketSessionManager Sessions =>
        UnityWebSocketServer.Instance?.RegisterSessions;

    public static void Broadcast(object payload)
    {
        WebSocketSessionManager sessions = UnityWebSocketServer.Instance?.RegisterSessions;
        var json = JsonConvert.SerializeObject(payload);
        if (sessions == null) { Debug.LogWarning("WS: no sessions"); return; }
        sessions.Broadcast(json);
    }

    public static bool SendToSession(string sessionId, object payload)
    {
        var json = JsonConvert.SerializeObject(payload);
        if (Sessions == null) return false;
        Sessions.SendTo(json, sessionId);
        return true;
    }

    // Optional: broadcast to everyone except one session
    public static void BroadcastExcept(string excludeSessionId, object payload)
    {
        var json = JsonConvert.SerializeObject(payload);
        if (Sessions == null) return;
        foreach (var id in Sessions.ActiveIDs)
            if (id != excludeSessionId) Sessions.SendTo(json, id);
    }

    public static bool SendToDevice(string deviceId, object payload)
    {
        if (!DeviceManager.TryGetSession(deviceId, out var sid)) return false;
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
        if (Sessions == null) return false;
        Sessions.SendTo(json, sid);
        return true;
    }
}
