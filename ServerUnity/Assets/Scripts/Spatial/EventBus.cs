using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public struct SpatialEvent
{
    public string id;          // e.g., "bboxPublishTab"
    public string type;        // e.g., "spatial_observable"
    public bool state;       // evt.State
    public string streamsJson; // serialized streams JSON (lazy from streamsJObject)
    public JObject streamsJObject; // parsed streams (avoids re-parsing streamsJson)

    public SpatialEvent(string id, string type, bool state, string streamsJson)
    { this.id = id; this.type = type; this.state = state; this.streamsJson = streamsJson; this.streamsJObject = null; }

    public SpatialEvent(string id, string type, bool state, JObject streamsJObject)
    { this.id = id; this.type = type; this.state = state; this.streamsJObject = streamsJObject; this.streamsJson = null; }

    /// <summary>Returns the JSON string, serializing from JObject only on first access.</summary>
    public string GetStreamsJson()
    {
        if (streamsJson == null && streamsJObject != null)
            streamsJson = streamsJObject.ToString(Formatting.None);
        return streamsJson;
    }

    /// <summary>Returns the JObject, parsing from JSON string only on first access.</summary>
    public JObject GetStreamsJObject()
    {
        if (streamsJObject == null && !string.IsNullOrEmpty(streamsJson))
            streamsJObject = JObject.Parse(streamsJson);
        return streamsJObject;
    }
}

public static class SpatialEventBus
{
    public static event Action<SpatialEvent> OnSpatial;

    public static void Publish(SpatialEvent e)
    {
        var handler = OnSpatial;
        if (handler == null) return;

        // Try to marshal to main thread if available in your project
        try
        {
            MainThreadDispatcher.Enqueue(() => handler(e));
        }
        catch
        {
            // Fallback (assumes already on main thread)
            handler(e);
        }
    }
}
