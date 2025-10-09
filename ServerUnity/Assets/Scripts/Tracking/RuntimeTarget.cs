using System;
using UnityEngine;

using Vuforia;

/// <summary>
/// Data structure for incoming WebSocket commands.
/// </summary>
[Serializable]
public class ImageTargetInfo
{
    public string deviceId; // Device ID for reference
    public string name;
    public float size;
    public string textureBase64;
}
/// <summary>
/// Helper class to create Vuforia runtime image targets and apply transforms.
/// </summary>
public static class VuforiaTargetManager
{
    /// <summary>
    /// Creates a Vuforia ImageTarget from base64 texture
    /// </summary>
    public static void CreateRuntimeImageTarget(ImageTargetInfo info)
    {
        if (info == null)
        {
            Debug.LogError("[Vuforia] ImageTargetInfo is null.");
            return;
        }
        MainThreadDispatcher.Enqueue(() =>
        {
            // Decode image
            //byte[] imgBytes = Convert.FromBase64String(info.textureBase64);
            //var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            //if (!tex.LoadImage(imgBytes))
            //{
            //    Debug.LogError("[Vuforia] Failed to load image data.");
            //    return;
            //}


            //// Create target
            //var observer = VuforiaBehaviour.Instance.ObserverFactory.CreateImageTarget(
            //    tex,
            //    info.size,
            //    info.name
            //);
            //// devicemanger link target to device so that the transform of target can be used as a reference
            //observer.gameObject.AddComponent<DefaultObserverEventHandler>();

            //get existing target by name
            GameObject runtimeImageTarget = GameObject.Find(info.name);

            if (runtimeImageTarget == null)
            {
                //Debug.LogError($"[Vuforia] No target found with name '{info.name}'");
                return;
            }
            DeviceManager.addTransform(info.deviceId, runtimeImageTarget.transform);
            Debug.Log($"[Vuforia] Created target '{info.name}' with size {info.size}");

            // get linerenderer named outline which is child of irtracker
            Utils.UpdateDeviceOutline(runtimeImageTarget.transform, info.deviceId);

        }
            );
    }
}
