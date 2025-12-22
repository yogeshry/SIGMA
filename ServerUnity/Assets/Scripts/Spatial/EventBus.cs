using System;
using UnityEngine;

public struct SpatialEvent
{
    public string id;          // e.g., "bboxPublishTab"
    public string type;        // e.g., "spatial_observable"
    public bool state;       // evt.State
    public string streamsJson; // serialized streams JSON
    public SpatialEvent(string id, string type, bool state, string streamsJson)
    { this.id = id; this.type = type; this.state = state; this.streamsJson = streamsJson; }
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
