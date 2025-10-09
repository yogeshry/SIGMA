using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices; // ConditionalWeakTable
using UniRx;
using UnityEngine;

/// <summary>
/// Optimized provider of reactive streams derived from a Device's Transform.
/// Caches per-device streams to avoid redundant polling and allocations.
/// </summary>
public static class RefStreamProvider
{
    // ------------------------- Shared helpers -------------------------

    private static IObservable<T> EmptyStream<T>(string name)
    {
        Debug.LogWarning($"[RefStreamProvider] {name} called with invalid Device or parameters.");
        return Observable.Empty<T>().Publish().RefCount();
    }

    // Store one set of hot observables per Device without preventing GC of the Device.
    private sealed class DeviceStreams
    {
        public readonly Device Device;
        public readonly Transform T;

        // Hot, shared observables
        public readonly IObservable<(Vector3 pos, Quaternion rot)> Pose;
        public readonly IObservable<(Vector3 up, Vector3 fwd, Vector3 right, Vector3 diag1, Vector3 diag2)> Axes;
        public readonly IObservable<(Vector3 tr, Vector3 tl, Vector3 br, Vector3 bl)> Corners;
        public readonly IObservable<Vector3> Euler;
        public readonly IObservable<Vector3> Velocity;
        public readonly IObservable<Vector3> Acceleration;

        public DeviceStreams(Device d,
                             float posEps = 0.005f,   // 5 mm
                             float rotEpsDeg = 2f,    // 2°
                             float eulerEpsDeg = 2f,  // 2°
                             float velMinDt = 1e-4f,
                             float velAlpha = 0f,
                             float velEps = 1e-3f,
                             float accMinDt = 1e-4f,
                             float accAlpha = 0f,
                             float accEps = 1e-3f)
        {
            Device = d ?? throw new ArgumentNullException(nameof(d));
            T = d.transform;
            if (T == null) throw new ArgumentException("Device has null transform", nameof(d));

            // --- Pose (position + rotation) with numeric gating (no acos) ---
            float posEps2 = posEps * posEps;
            float cosHalfEps = Mathf.Cos(0.5f * rotEpsDeg * Mathf.Deg2Rad);

            Pose = Observable.EveryUpdate()
                .Select(_ => (T.position, T.rotation))
                .Scan(
                    (hasPrev: false, lp: Vector3.zero, lr: Quaternion.identity,
                     emit: false, curr: (pos: Vector3.zero, rot: Quaternion.identity)),
                    (acc, curr) =>
                    {
                        if (!acc.hasPrev)
                            return (true, curr.position, curr.rotation, true, (curr.position, curr.rotation));

                        bool posChanged = (curr.position - acc.lp).sqrMagnitude > posEps2;
                        float dot = Mathf.Abs(Quaternion.Dot(curr.rotation, acc.lr));
                        bool rotChanged = dot < cosHalfEps;

                        bool emit = posChanged || rotChanged;
                        // Update last values when emitting, keep previous when not (for stable gating)
                        var lp = emit ? curr.position : acc.lp;
                        var lr = emit ? curr.rotation : acc.lr;
                        return (true, lp, lr, emit, (curr.position, curr.rotation));
                    })
                .Where(s => s.emit)
                .Select(s => s.curr)
                .Publish()
                .RefCount();

            // --- Axes/diagonals (one pass, gated) ---
            const float axisEps2 = 1e-8f;  // for diagonal normalization
            const float axesGateEps2 = 1e-8f;

            Axes = Observable.EveryUpdate()
                .Select(_ =>
                {
                    var up = T.up;           // already normalized by Unity
                    var fwd = T.forward;
                    var right = T.right;

                    // Diagonals in device plane (right–forward plane)
                    var d1 = fwd - right;
                    var d2 = fwd + right;
                    d1 = d1.sqrMagnitude > axisEps2 ? d1.normalized : Vector3.zero;
                    d2 = d2.sqrMagnitude > axisEps2 ? d2.normalized : Vector3.zero;

                    return (up, fwd, right, d1, d2);
                })
                .Scan(((Vector3, Vector3, Vector3, Vector3, Vector3))default, (prev, curr) =>
                    NearlyEqualAxes(prev, curr, axesGateEps2) ? prev : curr)
                .DistinctUntilChanged()
                .Publish()
                .RefCount();

            // --- Corners: cache local geometry, transform each frame, gate ---
            var size = d.physicalSize;
            if (size.x > 0f && size.y > 0f)
            {
                float hw = size.x * 0.5f, hh = size.y * 0.5f;
                var L_tr = new Vector3(hw, 0f, hh);
                var L_tl = new Vector3(-hw, 0f, hh);
                var L_br = new Vector3(hw, 0f, -hh);
                var L_bl = new Vector3(-hw, 0f, -hh);
                const float cornerGateEps2 = 1e-6f;

                Corners = Observable.EveryUpdate()
                    .Select(_ => (
                        tr: T.TransformPoint(L_tr),
                        tl: T.TransformPoint(L_tl),
                        br: T.TransformPoint(L_br),
                        bl: T.TransformPoint(L_bl)))
                    .Scan(((Vector3, Vector3, Vector3, Vector3))default, (prev, curr) =>
                        NearlyEqualCorners(prev, curr, cornerGateEps2) ? prev : curr)
                    .DistinctUntilChanged()
                    .Publish()
                    .RefCount();
            }
            else
            {
                Corners = Observable.Never<(Vector3, Vector3, Vector3, Vector3)>()
                          .Publish().RefCount();
                Debug.LogWarning($"[RefStreamProvider] Corners disabled due to invalid physicalSize for device {d.deviceId}.");
            }

            // --- Euler angles (avoid if not needed; here we gate by ~2 degrees) ---
            float eulerGateEps2 = eulerEpsDeg * eulerEpsDeg;
            Euler = Observable.EveryUpdate()
                .Select(_ => T.eulerAngles)
                .Scan(Vector3.positiveInfinity, (prev, curr) =>
                    (curr - prev).sqrMagnitude > eulerGateEps2 ? curr : prev)
                .DistinctUntilChanged()
                .Publish()
                .RefCount();

            // --- Velocity from Pose ---
            float velEps2 = velEps * velEps;
            Velocity = Pose
                .Select(p => p.pos)
                .Scan((has: false, prev: Vector3.zero, t: 0f, v: Vector3.zero),
                      (s, curr) =>
                      {
                          float now = Time.time;
                          if (!s.has) return (true, curr, now, Vector3.zero);
                          float dt = Mathf.Max(now - s.t, velMinDt);
                          var inst = (curr - s.prev) / dt;
                          var v = velAlpha > 0f ? Vector3.Lerp(s.v, inst, velAlpha) : inst;
                          return (true, curr, now, v);
                      })
                .Select(s => s.v)
                .Scan(Vector3.positiveInfinity, (prev, curr) =>
                    (curr - prev).sqrMagnitude > velEps2 ? curr : prev)
                .DistinctUntilChanged()
                .Publish()
                .RefCount();

            // --- Acceleration from Velocity ---
            float accEps2 = accEps * accEps;
            Acceleration = Velocity
                .Scan((has: false, prev: Vector3.zero, t: 0f, a: Vector3.zero),
                      (s, v) =>
                      {
                          float now = Time.time;
                          if (!s.has) return (true, v, now, Vector3.zero);
                          float dt = Mathf.Max(now - s.t, accMinDt);
                          var instA = (v - s.prev) / dt;
                          var a = accAlpha > 0f ? Vector3.Lerp(s.a, instA, accAlpha) : instA;
                          return (true, v, now, a);
                      })
                .Select(s => s.a)
                .Scan(Vector3.positiveInfinity, (prev, curr) =>
                    (curr - prev).sqrMagnitude > accEps2 ? curr : prev)
                .DistinctUntilChanged()
                .Publish()
                .RefCount();
        }

        private static bool NearlyEqualAxes(
            (Vector3, Vector3, Vector3, Vector3, Vector3) a,
            (Vector3, Vector3, Vector3, Vector3, Vector3) b,
            float eps2)
        {
            return
                (a.Item1 - b.Item1).sqrMagnitude <= eps2 &&
                (a.Item2 - b.Item2).sqrMagnitude <= eps2 &&
                (a.Item3 - b.Item3).sqrMagnitude <= eps2 &&
                (a.Item4 - b.Item4).sqrMagnitude <= eps2 &&
                (a.Item5 - b.Item5).sqrMagnitude <= eps2;
        }

        private static bool NearlyEqualCorners(
            (Vector3, Vector3, Vector3, Vector3) a,
            (Vector3, Vector3, Vector3, Vector3) b,
            float eps2)
        {
            return
                (a.Item1 - b.Item1).sqrMagnitude <= eps2 &&
                (a.Item2 - b.Item2).sqrMagnitude <= eps2 &&
                (a.Item3 - b.Item3).sqrMagnitude <= eps2 &&
                (a.Item4 - b.Item4).sqrMagnitude <= eps2;
        }
    }

    // Per-device cache; automatically releases entries when Device is GC'd.
    private static readonly ConditionalWeakTable<Device, DeviceStreams> _cache =
        new ConditionalWeakTable<Device, DeviceStreams>();

    private static DeviceStreams Streams(Device device)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        if (device.transform == null) throw new ArgumentException("Device has null transform", nameof(device));
        return _cache.GetValue(device, d => new DeviceStreams(d));
    }

    // ------------------------- Public Streams (cached per device) -------------------------

    public static IObservable<(Vector3 pos, Quaternion rot)> DevicePoseStream(Device device)
        => device?.transform ? Streams(device).Pose : EmptyStream<(Vector3, Quaternion)>("DevicePoseStream");

    public static IObservable<(Vector3 up, Vector3 fwd, Vector3 right, Vector3 diag1, Vector3 diag2)> AxisStream(Device device)
        => device?.transform ? Streams(device).Axes : EmptyStream<(Vector3, Vector3, Vector3, Vector3, Vector3)>("AxisStream");

    public static IObservable<Vector3> EulerAnglesStream(Device device)
        => device?.transform ? Streams(device).Euler : EmptyStream<Vector3>("EulerAnglesStream");

    public static IObservable<(Vector3 tr, Vector3 tl, Vector3 br, Vector3 bl)> CornersStream(Device device)
        => device?.transform ? Streams(device).Corners : EmptyStream<(Vector3, Vector3, Vector3, Vector3)>("CornersStream");

    // Edges derived from Corners (no extra polling)
    public static IObservable<(Vector3, Vector3)> TopEdgeStream(Device dev) =>
        EdgeStream(dev, c => (c.tl, c.tr));
    public static IObservable<(Vector3, Vector3)> BottomEdgeStream(Device dev) =>
        EdgeStream(dev, c => (c.br, c.bl));
    public static IObservable<(Vector3, Vector3)> LeftEdgeStream(Device dev) =>
        EdgeStream(dev, c => (c.bl, c.tl));
    public static IObservable<(Vector3, Vector3)> RightEdgeStream(Device dev) =>
        EdgeStream(dev, c => (c.tr, c.br));

    public static IObservable<(Vector3 a, Vector3 b)> EdgeStream(
        Device device,
        Func<(Vector3 tr, Vector3 tl, Vector3 br, Vector3 bl), (Vector3, Vector3)> selector)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        if (device.transform == null) return EmptyStream<(Vector3, Vector3)>("EdgeStream");
        return Streams(device).Corners.Select(selector).DistinctUntilChanged().Publish().RefCount();
    }

    // ------------------------- Velocity / Acceleration -------------------------

    public static IObservable<Vector3> VelocityStream(Device device)
        => device?.transform ? Streams(device).Velocity : EmptyStream<Vector3>("VelocityStream");

    public static IObservable<Vector3> AccelerationStream(Device device)
        => device?.transform ? Streams(device).Acceleration : EmptyStream<Vector3>("AccelerationStream");

    // ------------------------- Generic accessors -------------------------

    public static IObservable<T> GetStream<T>(Device device, string streamName)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        switch (streamName)
        {
            case "position":
                return DevicePoseStream(device).Select(p => (T)(object)p.pos);
            case "rotation":
                return DevicePoseStream(device).Select(p => (T)(object)p.rot);

            case "eulerAngles":
                return EulerAnglesStream(device).Select(e => (T)(object)e);

            case "upVector":
                return AxisStream(device).Select(a => (T)(object)a.up);
            case "forwardVector":
                return AxisStream(device).Select(a => (T)(object)a.fwd);
            case "rightVector":
                return AxisStream(device).Select(a => (T)(object)a.right);

            case "corners":
                return CornersStream(device).Select(c => (T)(object)c);

            case "topLeftCorner":
                return CornersStream(device).Select(c => (T)(object)c.tl);
            case "topRightCorner":
                return CornersStream(device).Select(c => (T)(object)c.tr);
            case "bottomLeftCorner":
                return CornersStream(device).Select(c => (T)(object)c.bl);
            case "bottomRightCorner":
                return CornersStream(device).Select(c => (T)(object)c.br);

            case "topEdge":
                return TopEdgeStream(device).Select(e => (T)(object)e);
            case "bottomEdge":
                return BottomEdgeStream(device).Select(e => (T)(object)e);
            case "leftEdge":
                return LeftEdgeStream(device).Select(e => (T)(object)e);
            case "rightEdge":
                return RightEdgeStream(device).Select(e => (T)(object)e);

            default:
                throw new ArgumentException($"Unknown stream name '{streamName}'", nameof(streamName));
        }
    }

    /// <summary>Returns geometric category for a named stream.</summary>
    public static GeometryType GetStreamGeometryType(string streamName)
    {
        switch (streamName)
        {
            case "position":
            case "topLeftCorner":
            case "topRightCorner":
            case "bottomLeftCorner":
            case "bottomRightCorner":
                return GeometryType.Point;

            case "upVector":
            case "forwardVector":
            case "rightVector":
                return GeometryType.Vector;

            case "topEdge":
            case "bottomEdge":
            case "leftEdge":
            case "rightEdge":
                return GeometryType.LineSegment;

            case "surface":
            case "corners":
                return GeometryType.Polygon;

            case "rotation":
                return GeometryType.Rotation;
            case "eulerAngles":
                return GeometryType.EulerAngles;

            default:
                throw new ArgumentException($"Unknown stream name '{streamName}'", nameof(streamName));
        }
    }
}
