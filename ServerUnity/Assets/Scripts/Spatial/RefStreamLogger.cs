using UnityEngine;
using UniRx;
using System;
using System.Linq;

/// <summary>
/// Subscribes to all RefStreamProvider streams for a given Device
/// and logs every emission for testing and debugging purposes.
/// </summary>
public class RefStreamLogger : MonoBehaviour
{
    [Tooltip("The Device whose streams you want to log.")]
    public Device device;

    private CompositeDisposable _disposables;
    public static RefStreamLogger Instance { get; private set; }

    void Awake()
    {
        // Enforce singleton
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        //_manager = new ObservableManager();
    }
    /// <summary>
    /// Ensures a RefStreamLogger exists, then calls StartLogger().
    /// </summary>
    public static void EnsureStarted()
    {
        if (Instance == null)
        {
            var go = new GameObject("RefStreamLogger");
            Instance = go.AddComponent<RefStreamLogger>();
            DontDestroyOnLoad(go);
        }
        Instance.StartLogger();
    }

    public void StartLogger()
    {
        Debug.Log("[RefStreamLogger] Starting...");

        // Fix: Retrieve a single device from the dictionary instead of assigning the entire dictionary  
        var allDevices = DeviceManager.GetAllDevices();
        if (allDevices.Count > 0)
        {
            device = allDevices.Values.First(); // Assign the first device in the dictionary  
        }
        else
        {
            Debug.LogError("[RefStreamLogger] No devices found. Disabling logger.");
            enabled = false;
            return;
        }

        // Optional: log the computed physical size once  
        try
        {
            Vector2 size = device.physicalSize;
            Debug.Log($"[RefStreamLogger] Device.physicalSize = {size.x:F3}m × {size.y:F3}m");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RefStreamLogger] Could not read physicalSize: {e.Message}");
        }

        _disposables = new CompositeDisposable();

        // 1) Pose (position & rotation)  
        //RefStreamProvider.DevicePoseStream(device)
        //    .Subscribe(p =>
        //        Debug.Log($"[PoseStream] pos={p.pos:F3}, rot={p.rot.eulerAngles:F3}")
        //    )
        //    .AddTo(_disposables);

        //// 2) Axes (up, forward, right)  
        //RefStreamProvider.AxisStream(device)
        //    .Subscribe(a =>
        //        Debug.Log($"[AxisStream] up={a.up:F3}, fwd={a.fwd:F3}, right={a.right:F3}")
        //    )
        //    .AddTo(_disposables);

        //// 3) Euler angles  
        //RefStreamProvider.EulerAnglesStream(device)
        //    .Subscribe(e =>
        //        Debug.Log($"[EulerStream] eulerAngles={e:F3}")
        //    )
        //    .AddTo(_disposables);

        // 4) Corners  
        //RefStreamProvider.CornersStream(device)
        //    .Subscribe(c =>
        //        Debug.Log($"[CornersStream] TR={c.tr:F3}, TL={c.tl:F3}, BR={c.br:F3}, BL={c.bl:F3}")
        //    )
        //    .AddTo(_disposables);

        // 5) Edges  
        //RefStreamProvider.TopEdgeStream(device)
        //    .Subscribe(e => Debug.Log($"[TopEdge] A={e.Item1:F3}, B={e.Item2:F3}"))
        //    .AddTo(_disposables);

        //RefStreamProvider.BottomEdgeStream(device)
        //    .Subscribe(e => Debug.Log($"[BottomEdge] A={e.Item1:F3}, B={e.Item2:F3}"))
        //    .AddTo(_disposables);

        //RefStreamProvider.LeftEdgeStream(device)
        //    .Subscribe(e => Debug.Log($"[LeftEdge] A={e.Item1:F3}, B={e.Item2:F3}"))
        //    .AddTo(_disposables);

        //RefStreamProvider.RightEdgeStream(device)
        //    .Subscribe(e => Debug.Log($"[RightEdge] A={e.Item1:F3}, B={e.Item2:F3}"))
        //    .AddTo(_disposables);
    }

    void OnDestroy()
    {
        _disposables?.Dispose();
    }
}
