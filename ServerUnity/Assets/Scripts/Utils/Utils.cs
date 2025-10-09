using UnityEngine;

public static class Utils
{
    /// <summary>
    /// Updates the outline LineRenderer (child named "outline") of the given runtime image target.
    /// The outline is drawn as a rectangle based on the device's physical size.
    /// </summary>
    /// <param name="transform">The parent transform that should contain a child named "outline".</param>
    /// <param name="deviceId">ID of the device to fetch its physical size.</param>
    public static void UpdateDeviceOutline(Transform transform, string deviceId)
    {
        if (transform == null)
        {
            Debug.LogError("[WS] transform transform is null.");
            return;
        }

        if (deviceId == null)
        {
            Debug.LogError("[WS] deviceId is null.");
        }

        // Try to find the "outline" child with a LineRenderer
        var outlineTransform = transform.Find("outline");
        if (outlineTransform == null)
        {
            Debug.LogWarning($"[WS] Child 'outline' not found under {transform.name} for device {deviceId}.");
            return;
        }

        var outline = outlineTransform.GetComponent<LineRenderer>();
        if (outline == null)
        {
            Debug.LogWarning($"[WS] No LineRenderer component on child 'outline' for device {deviceId}.");
            return;
        }

        // Get device physical size
        var device = DeviceManager.GetDevice(deviceId);
        if (device == null)
        {
            Debug.LogWarning($"[WS] Device not found for id {deviceId}.");
            return;
        }

        Vector2 size = device.physicalSize;

        // Define rectangle corners in local space (X-Z plane, centered at origin)
        outline.positionCount = 5;
        outline.SetPosition(0, new Vector3(-size.x / 2, 0, size.y / 2));
        outline.SetPosition(1, new Vector3(size.x / 2, 0, size.y / 2));
        outline.SetPosition(2, new Vector3(size.x / 2, 0, -size.y / 2));
        outline.SetPosition(3, new Vector3(-size.x / 2, 0, -size.y / 2));
        outline.SetPosition(4, new Vector3(-size.x / 2, 0, size.y / 2)); // close loop

        outline.enabled = true;
    }
}
