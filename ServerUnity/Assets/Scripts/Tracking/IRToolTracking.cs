using UnityEngine;
using System;
using IRToolTrack;
using UnityEngine.XR;
using System.Collections;
using System.Collections.Generic;

#if ENABLE_WINMD_SUPPORT
using HL2IRToolTracking;
using System.Runtime.InteropServices;
#endif

public class IRToolTracking : MonoBehaviour
{
#if ENABLE_WINMD_SUPPORT
    private HL2IRTracking toolTracking;
#endif

    private bool startToolTracking = false;
    private IRToolController[] tools = null;

    // Reuse this buffer to avoid allocations in GetToolTransform when WINMD disabled.
    private readonly float[] _zeros8 = new float[8];

    // Cache input subsystem once
    private XRInputSubsystem _inputSubsystem;

    // ------------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------------

    public float[] GetToolTransform(string identifier)
    {
#if ENABLE_WINMD_SUPPORT
        if (toolTracking == null) return _zeros8;
        return toolTracking.GetToolTransform(identifier);
#else
        return _zeros8;
#endif
    }

    public string GetLogStream(string identifier)
    {
#if ENABLE_WINMD_SUPPORT
        if (toolTracking == null) return string.Empty;
        return toolTracking.GetLogStream(identifier);
#else
        return string.Empty;
#endif
    }

    public long GetTimestamp()
    {
#if ENABLE_WINMD_SUPPORT
        if (toolTracking == null) return 0;
        return toolTracking.GetTrackingTimestamp();
#else
        return 0;
#endif
    }

    // ------------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------------

    private void Start()
    {
        tools = FindObjectsOfType<IRToolController>();
        StartCoroutine(StartToolTrackingWhenReady());
    }

    private void OnDestroy()
    {
        UnhookXRCallbacks();
    }

    // ------------------------------------------------------------------------
    // Startup
    // ------------------------------------------------------------------------

    private IEnumerator StartToolTrackingWhenReady()
    {
#if ENABLE_WINMD_SUPPORT
        // Let OpenXR/MRTK settle a bit (avoids whole-session offsets on some boots)
        for (int i = 0; i < 10; i++) yield return null;

        CacheXRInputSubsystem();
        HookXRCallbacksOnce();

        // Wait (best effort) until XR is running
        if (_inputSubsystem != null)
        {
            for (int i = 0; i < 120 && !_inputSubsystem.running; i++)
                yield return null;
        }

        // Ensure tracker instance
        if (toolTracking == null)
            toolTracking = new HL2IRTracking();

        // Set reference coordinate system with non-blocking retries
        yield return StartCoroutine(SetReferenceWorldCoordinateSystemWithRetries(30, 0.01f));

        StartToolTrackingInternal();
#else
        StartToolTrackingInternal();
        yield break;
#endif
    }

    public void StartToolTracking()
    {
        // keep public method (in case you call it from UI), but route to internal
        StartToolTrackingInternal();
    }

    private void StartToolTrackingInternal()
    {
        Debug.Log("Start Tracking");

#if ENABLE_WINMD_SUPPORT
        if (startToolTracking) return;

        if (toolTracking == null)
            toolTracking = new HL2IRTracking();

        // (Reference CS should already be set by coroutine, but safe to call once more)
        // If it fails, it just won't update the reference.
        TrySetReferenceWorldCoordinateSystemOnce();

        toolTracking.RemoveAllToolDefinitions();

        if (tools == null) tools = FindObjectsOfType<IRToolController>();

        foreach (IRToolController tool in tools)
        {
            if (tool == null) continue;

            int minVisible = tool.sphere_count;
            if (tool.max_occluded_spheres > 0 && (tool.sphere_count - tool.max_occluded_spheres) >= 3)
                minVisible = tool.sphere_count - tool.max_occluded_spheres;

            toolTracking.AddToolDefinition(
                tool.sphere_count,
                tool.sphere_positions,
                tool.sphere_radius,
                tool.identifier,
                minVisible,
                tool.lowpass_factor_rotation,
                tool.lowpass_factor_position
            );

            tool.StartTracking();
        }

        toolTracking.StartToolTracking();
        startToolTracking = true;
#else
        // no-op in editor/non-WINMD
        startToolTracking = true;
#endif
    }

    // ------------------------------------------------------------------------
    // Stop
    // ------------------------------------------------------------------------

    public void StopToolTracking()
    {
        if (!startToolTracking)
        {
            Debug.Log("Tracking was not started, so cannot stop it");
            return;
        }

#if ENABLE_WINMD_SUPPORT
        if (toolTracking != null)
        {
            var success = toolTracking.StopToolTracking();
            if (!success) Debug.Log("Could not stop tracking");
        }
#endif
        startToolTracking = false;

        if (tools != null)
        {
            foreach (IRToolController tool in tools)
                tool?.StopTracking();
        }

        Debug.Log("Stopped Tracking");
    }

    public void ExitApplication()
    {
        StopToolTracking();
        Application.Quit();
    }

    // ------------------------------------------------------------------------
    // XR subsystem hookup
    // ------------------------------------------------------------------------

    private void CacheXRInputSubsystem()
    {
        if (_inputSubsystem != null) return;

        var inputs = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(inputs);
        _inputSubsystem = (inputs.Count > 0) ? inputs[0] : null;
    }

    private void HookXRCallbacksOnce()
    {
        if (_inputSubsystem == null) return;

        // Avoid double subscription
        UnhookXRCallbacks();
        _inputSubsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
        _inputSubsystem.boundaryChanged += OnBoundaryChanged;
    }

    private void UnhookXRCallbacks()
    {
        if (_inputSubsystem == null) return;
        _inputSubsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
        _inputSubsystem.boundaryChanged -= OnBoundaryChanged;
    }

    private void OnTrackingOriginUpdated(XRInputSubsystem _)
    {
#if ENABLE_WINMD_SUPPORT
        // Origin changed mid-session: refresh reference coordinate system
        StartCoroutine(SetReferenceWorldCoordinateSystemWithRetries(10, 0.02f));
#endif
    }

    private void OnBoundaryChanged(XRInputSubsystem _)
    {
#if ENABLE_WINMD_SUPPORT
        // Boundary updates can coincide with origin shifts too
        StartCoroutine(SetReferenceWorldCoordinateSystemWithRetries(10, 0.02f));
#endif
    }

    // ------------------------------------------------------------------------
    // Reference coordinate system (non-blocking)
    // ------------------------------------------------------------------------

#if ENABLE_WINMD_SUPPORT
    private IEnumerator SetReferenceWorldCoordinateSystemWithRetries(int attempts, float delaySeconds)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (TrySetReferenceWorldCoordinateSystemOnce())
                yield break;

            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);
            else
                yield return null;
        }

        Debug.LogWarning("Failed to set reference coordinate system after retries; may see offsets.");
    }

    private bool TrySetReferenceWorldCoordinateSystemOnce()
    {
        try
        {
            Windows.Perception.Spatial.SpatialCoordinateSystem unityWorldOrigin = null;

#if UNITY_2021_2_OR_NEWER
            unityWorldOrigin =
                Microsoft.MixedReality.OpenXR.PerceptionInterop
                    .GetSceneCoordinateSystem(UnityEngine.Pose.identity)
                as Windows.Perception.Spatial.SpatialCoordinateSystem;

#elif UNITY_2020_1_OR_NEWER
            IntPtr WorldOriginPtr = UnityEngine.XR.WindowsMR.WindowsMREnvironment.OriginSpatialCoordinateSystem;
            unityWorldOrigin = Marshal.GetObjectForIUnknown(WorldOriginPtr) as Windows.Perception.Spatial.SpatialCoordinateSystem;

#else
            IntPtr WorldOriginPtr = UnityEngine.XR.WSA.WorldManager.GetNativeISpatialCoordinateSystemPtr();
            unityWorldOrigin = Marshal.GetObjectForIUnknown(WorldOriginPtr) as Windows.Perception.Spatial.SpatialCoordinateSystem;
#endif

            if (unityWorldOrigin == null) return false;

            toolTracking.SetReferenceCoordinateSystem(unityWorldOrigin);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("TrySetReferenceWorldCoordinateSystemOnce failed: " + e.Message);
            return false;
        }
    }
#endif
}
