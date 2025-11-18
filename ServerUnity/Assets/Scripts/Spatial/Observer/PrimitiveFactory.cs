using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UniRx;
using Unity.VisualScripting;
using UnityEngine;

// ------------------------- DevicePair & PrimitiveKey -------------------------

public sealed class DevicePair
{
    public readonly Device A;
    public readonly Device B;
    public readonly string Key;

    public DevicePair(Device a, Device b)
    {
        A = a ?? throw new ArgumentNullException(nameof(a));
        B = b;

        var aId = string.IsNullOrEmpty(a.deviceId) ? a.GetHashCode().ToString() : a.deviceId;
        var bId = b == null
            ? "null"
            : (string.IsNullOrEmpty(b.deviceId) ? b.GetHashCode().ToString() : b.deviceId);

        Key = $"{aId}__{bId}";
    }

    public override string ToString() => $"({A?.deviceId ?? "A"} ⟷ {B?.deviceId ?? "B"})";
}

internal readonly struct PrimitiveKey : IEquatable<PrimitiveKey>
{
    public readonly string PrimitiveId;
    public readonly string PairKey;
    private readonly int _hash;

    public PrimitiveKey(string primitiveId, string pairKey)
    {
        PrimitiveId = primitiveId;
        PairKey = pairKey;
        unchecked { _hash = ((primitiveId?.GetHashCode() ?? 0) * 397) ^ (pairKey?.GetHashCode() ?? 0); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(PrimitiveKey other) => PrimitiveId == other.PrimitiveId && PairKey == other.PairKey;
    public override bool Equals(object obj) => obj is PrimitiveKey k && Equals(k);
    public override int GetHashCode() => _hash;
    public override string ToString() => $"{PrimitiveId}@{PairKey}";
}
public class CondRef
{
    public string Id;                  // cache key used for Get/Release
    public bool Negate;                // only for string ids with '!' or '~' prefix
    public InlinePrimitiveSpec InlineSpec;   // non-null if this entry is an inline override
}
// ------------------------------- PrimitiveFactory -------------------------------

public class PrimitiveFactory
{
    // Diagnostics
    public bool VerboseLogs = true;
    public bool TraceValues = false;

    private readonly Dictionary<string, PrimitiveSpec> _specsById =
        new Dictionary<string, PrimitiveSpec>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<PrimitiveKey, Lazy<IObservable<PrimitivePayload>>> _cache =
        new Dictionary<PrimitiveKey, Lazy<IObservable<PrimitivePayload>>>();

    private readonly Dictionary<PrimitiveKey, int> _refCounts =
        new Dictionary<PrimitiveKey, int>();

    // ---------- Parsing helpers ----------
    private enum MetricKind { Distance, Angle, Velocity, Acceleration, Projection, AccelerationRms, AngularVelocityRms }

    private readonly Dictionary<string, MetricKind> _metricMap =
        new Dictionary<string, MetricKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["distance"] = MetricKind.Distance,
            ["angle"] = MetricKind.Angle,
            ["velocity"] = MetricKind.Velocity,
            ["acceleration"] = MetricKind.Acceleration,
            ["projection"] = MetricKind.Projection,
            ["acceleration_rms"] = MetricKind.AccelerationRms,
            ["angular_velocity_rms"] = MetricKind.AngularVelocityRms
        };

    private enum AxisBase { Up, Forward, Right, MajorDiag, MinorDiag, X, Y, Z }
    private enum AxisSide { A, B, Const }

    private readonly struct AxisSpec
    {
        public readonly AxisBase Base;
        public readonly AxisSide Side;
        public readonly int Sign; // +1 / -1
        public AxisSpec(AxisBase b, AxisSide s, int sign) { Base = b; Side = s; Sign = sign; }
    }

    public PrimitiveFactory()
    {
        var ta = Resources.Load<TextAsset>("JSON/Primitive/primitives");
        var text = ta ? ta.text : "[]";
        var list = JsonConvert.DeserializeObject<List<PrimitiveSpec>>(text) ?? new List<PrimitiveSpec>();
        foreach (var s in list)
            if (!string.IsNullOrWhiteSpace(s.id))
                _specsById[s.id] = s;

        if (VerboseLogs) Debug.Log($"[PrimitiveFactory] Loaded {_specsById.Count} specs");
    }

    // ------------------------------ Public API ------------------------------

    public void RegisterPrimitives(DevicePair pair, List<CondRef> condList)
    {
        if (pair == null) throw new ArgumentNullException(nameof(pair));
        foreach (var cond in condList)
        {
            if (!_specsById.TryGetValue(cond.Id, out var spec))
            {
                Warn($"Spec not found for id '{cond.Id}'");
                continue;
            }
            InflateFromBaseOrThrow(spec, cond.InlineSpec);
            EnsureRegistered(pair, spec);
        }
    }

    public IObservable<PrimitivePayload> Get(DevicePair pair, string id, InlinePrimitiveSpec inlineSpec)
    {
        if (pair == null) throw new ArgumentNullException(nameof(pair));
        if (id == null) throw new ArgumentNullException(nameof(id));

        var key = new PrimitiveKey(id, pair.Key);
        if (_cache.TryGetValue(key, out var lazy))
        {
            IncRef(key);
            return lazy.Value;
        }

        if (!_specsById.TryGetValue(id, out var spec))
            throw new ArgumentException($"Unknown primitive '{id}' (no spec found)");
        InflateFromBaseOrThrow(spec, inlineSpec);

        EnsureRegistered(pair, spec);
        IncRef(key);
        return _cache[key].Value;
    }
    

    public bool TryGet(DevicePair pair, string id, out IObservable<PrimitivePayload> stream)
    {
        if (pair == null) { stream = null; return false; }
        var key = new PrimitiveKey(id, pair.Key);
        if (_cache.TryGetValue(key, out var lazy))
        {
            IncRef(key);
            stream = lazy.Value;
            return true;
        }
        stream = null;
        return false;
    }

    public void Release(DevicePair pair, string id)
    {
        if (pair == null || id == null) return;
        var key = new PrimitiveKey(id, pair.Key);
        if (!_refCounts.TryGetValue(key, out var c)) return;

        if (--c <= 0)
        {
            _refCounts.Remove(key);
            _cache.Remove(key);
            if (VerboseLogs) Debug.Log($"[PrimitiveFactory] Evicted {key}");
        }
        else
        {
            _refCounts[key] = c;
        }
    }

    public void ClearAll()
    {
        _cache.Clear();
        _refCounts.Clear();
        if (VerboseLogs) Debug.Log("[PrimitiveFactory] Cleared cache");
    }

    // ------------------------------ Internals ------------------------------

    private void EnsureRegistered(DevicePair pair, PrimitiveSpec spec)
    {
        ValidateSpecOrThrow(spec, pair);

        var key = new PrimitiveKey(spec.id, pair.Key);
        if (_cache.ContainsKey(key)) return;

        _cache[key] = new Lazy<IObservable<PrimitivePayload>>(() =>
        {
            // Parse once
            var mk = ParseMetricKind(spec.metric, out var rawDir);
            var hasAxis = TryParseAxis(rawDir, out var axisSpec);

            // Reuse device axis streams
            var gA = RefStreamProvider.AxisStream(pair.A);
            var gB = pair.B != null ? RefStreamProvider.AxisStream(pair.B) : null;

            var ctx = new BuildCtx
            {
                A = pair.A,
                B = pair.B,
                Spec = spec,
                Metric = mk,
                Axis = axisSpec,
                HasAxis = hasAxis,
                RawDirection = rawDir,
                A_Axes = gA,
                B_Axes = gB
            };

            IObservable<object> raw = mk switch
            {
                MetricKind.Distance => DistanceBuilder(ctx).Select(x => (object)x),
                MetricKind.Angle => AngleBuilder(ctx).Select(x => (object)x),
                MetricKind.Velocity => VelocityBuilder(ctx).Select(x => (object)x),
                MetricKind.Acceleration => AccelerationBuilder(ctx).Select(x => (object)x),
                MetricKind.Projection => ProjectionBuilder(ctx).Select(x => (object)x),
                // NEW:
                MetricKind.AccelerationRms => AccelerationRmsBuilder(ctx).Select(x => (object)x),
                MetricKind.AngularVelocityRms => AngularVelocityRmsBuilder(ctx).Select(x => (object)x),
                _ => throw new NotSupportedException($"Unsupported metric '{spec.metric}'")
            };

            var stream = raw
                .Select(v => ToPayload(spec, v))
                .DistinctUntilChanged(PayloadComparer.Instance)
                .Publish()
                .RefCount();

            if (TraceValues)
            {
                stream.Subscribe(
                    p => Debug.Log($"[Prim {spec.id}@{pair.Key}] value={p.Value} valid={p.IsValid}"),
                    ex => Debug.LogError($"[Prim {spec.id}@{pair.Key}] {ex}")
                );
            }

            if (VerboseLogs)
                Debug.Log($"[PrimitiveFactory] Registered {key}");

            return stream;
        });
    }

    private void ValidateSpecOrThrow(PrimitiveSpec spec, DevicePair pair)
    {
        if (string.IsNullOrWhiteSpace(spec?.id))
            throw new ArgumentException("PrimitiveSpec.id must be non-empty");
        if (pair?.A == null)
            throw new InvalidOperationException("DevicePair is invalid (null A)");
        if (string.IsNullOrWhiteSpace(spec.metric))
            throw new ArgumentException($"Spec '{spec.id}' missing 'metric'");

        var mk = ParseMetricKind(spec.metric, out _);

        if ((mk == MetricKind.Distance || mk == MetricKind.Projection) &&
            (spec.refs == null || spec.refs.Count != 2))
            throw new NotSupportedException($"Spec '{spec.id}' expects exactly 2 refs for metric '{mk}'");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void IncRef(PrimitiveKey key)
    {
        _refCounts.TryGetValue(key, out var c);
        _refCounts[key] = c + 1;
    }

    // ------------------------------ Builders ------------------------------

    private static IObservable<float> DistanceBuilder(BuildCtx ctx)
    {
        var offset = BuildRawOffset(ctx).Publish().RefCount();

        if (!ctx.HasAxis)
            return offset.Select(v => v.magnitude);

        var axis = AxisStream(ctx).Select(SafeNorm).Publish().RefCount();
        return offset.CombineLatest(axis, (vec, ax) => Vector3.Dot(vec, ax));
    }

    private static IObservable<float> AngleBuilder(BuildCtx ctx)
    {
        var r = ctx.Spec.refs;

        // 1-ref (or not exactly 2 refs): pick Euler component from A
        if (r == null || r.Count != 2)
        {
            var axisName = ctx.RawDirection ?? "yaw";
            var e = RefStreamProvider.EulerAnglesStream(ctx.A).Publish().RefCount();
            return e.Select(v => axisName switch
            {
                "pitch" => v.x,
                "yaw" => v.y,
                "roll" => v.z,
                _ => throw new InvalidOperationException($"Unknown angle axis '{axisName}' in '{ctx.Spec.metric}'")
            });
        }

        // 2-refs: angle between direction vectors
        var u = RefStreamProvider.GetStream<Vector3>(ctx.A, r[0]);
        var v2 = RefStreamProvider.GetStream<Vector3>(ctx.B, r[1]);
        return u.CombineLatest(v2, (a, b) => Vector3.Angle(Norm(a), Norm(b))).Publish().RefCount();
    }

    private static IObservable<IProjectionResult?> ProjectionBuilder(BuildCtx ctx)
    {
        if (!ctx.HasAxis)
            throw new InvalidOperationException("projection requires a direction (e.g., projection:upA)");

        var axis = AxisStream(ctx).Select(SafeNorm).Publish().RefCount();

        var typeA = RefStreamProvider.GetStreamGeometryType(ctx.Spec.refs[0]);
        var typeB = RefStreamProvider.GetStreamGeometryType(ctx.Spec.refs[1]);

        // Polygon ⊓ Polygon
        if (typeA == GeometryType.Polygon && typeB == GeometryType.Polygon)
        {
            var cA = RefStreamProvider.CornersStream(ctx.A);
            var cB = RefStreamProvider.CornersStream(ctx.B);
            return cA.CombineLatest(cB, axis, (a, b, ax) =>
            {
                var polyA = new[] { a.tr, a.tl, a.bl, a.br };
                var polyB = new[] { b.tr, b.tl, b.bl, b.br };

                Vector3[] intersection;
                float areaA, projArea;
                SurfaceProjection.ProjectAndIntersect(polyA, polyB, out intersection, out areaA, out projArea);

                if (projArea <= 1e-6f) return null;
                DisplaySpatialData displayA = new DisplaySpatialData(ctx.A.displaySize.widthPixels, ctx.A.displaySize.heightPixels,new DeviceSpatialProvider.Corners(a.tr, a.tl, a.br, a.bl));
                DisplaySpatialData displayB = new DisplaySpatialData(ctx.B.displaySize.widthPixels, ctx.B.displaySize.heightPixels, new DeviceSpatialProvider.Corners(b.tr, b.tl, b.br, b.bl));
                DisplayPairSpatialData displayPairSpatialData = new DisplayPairSpatialData(displayA, displayB);
                
                return PolygonPolygonProjection.Create(polyA, polyB, intersection ?? Array.Empty<Vector3>(), areaA, projArea, ax, displayPairSpatialData);
            }).Publish().RefCount();
        }

        // Segment ⊓ Segment
        if (typeA == GeometryType.LineSegment && typeB == GeometryType.LineSegment)
        {
            var segA = RefStreamProvider.GetStream<(Vector3, Vector3)>(ctx.A, ctx.Spec.refs[0]);
            var segB = RefStreamProvider.GetStream<(Vector3, Vector3)>(ctx.B, ctx.Spec.refs[1]);
            return segA.CombineLatest(segB, axis, (a, b, ax) =>
            {
                var proj = SurfaceProjection.ProjectSegmentOntoSegment(a, b);
                if (proj == null) return null;
                return SegmentSegmentProjection.Create(a, b, proj.Value, ax);
            }).Publish().RefCount();
        }

        // Segment → Polygon
        if (typeA == GeometryType.LineSegment && typeB == GeometryType.Polygon)
        {
            var seg = RefStreamProvider.GetStream<(Vector3, Vector3)>(ctx.A, ctx.Spec.refs[0]);
            var cB = RefStreamProvider.CornersStream(ctx.B);
            return seg.CombineLatest(cB, axis, (s, b, ax) =>
            {
                var polyB = new[] { b.tr, b.tl, b.bl, b.br };
                var proj = SurfaceProjection.ProjectLineAlongAxisOntoPolygonSurface(s, polyB, ax);
                if (proj == null) return null;
                return SegmentPolygonProjection.Create(s, polyB, proj.Value, ax);
            }).Publish().RefCount();
        }

        // Point → Polygon
        if (typeA == GeometryType.Point && typeB == GeometryType.Polygon)
        {
            var pt = RefStreamProvider.GetStream<Vector3>(ctx.A, ctx.Spec.refs[0]);
            var cA = RefStreamProvider.CornersStream(ctx.A);
            var cB = RefStreamProvider.CornersStream(ctx.B);
            return pt.CombineLatest(cA, cB, axis, (p, a, b, ax) =>
            {
                var polyB = new[] { b.tr, b.tl, b.bl, b.br };
                var proj = SurfaceProjection.ProjectPointAlongAxisOntoPolygonSurface(p, polyB, ax);
                if (!proj.HasValue) return null;
                DisplaySpatialData displayA = new DisplaySpatialData(ctx.A.displaySize.widthPixels, ctx.A.displaySize.heightPixels, new DeviceSpatialProvider.Corners(a.tr, a.tl, a.br, a.bl));
                DisplaySpatialData displayB = new DisplaySpatialData(ctx.B.displaySize.widthPixels, ctx.B.displaySize.heightPixels, new DeviceSpatialProvider.Corners(b.tr, b.tl, b.br, b.bl));
                DisplayPairSpatialData displayPairSpatialData = new DisplayPairSpatialData(displayA, displayB);
                return new PointPolygonProjection(p, proj.Value, ax, polyB, displayPairSpatialData);
            }).Publish().RefCount();
        }

        // Point → Segment
        if (typeA == GeometryType.Point && typeB == GeometryType.LineSegment)
        {
            var pt = RefStreamProvider.GetStream<Vector3>(ctx.A, ctx.Spec.refs[0]);
            var seg = RefStreamProvider.GetStream<(Vector3, Vector3)>(ctx.B, ctx.Spec.refs[1]);
            var cA = RefStreamProvider.CornersStream(ctx.A);
            var cB = RefStreamProvider.CornersStream(ctx.B);

            return pt.CombineLatest(seg, axis, cA, cB, (p, s, ax, a, b) =>
            {
                var q = SurfaceProjection.ProjectPointOntoSegment(p, s);
                if (!q.HasValue) return null;

                DisplaySpatialData displayA = new DisplaySpatialData(ctx.A.displaySize.widthPixels, ctx.A.displaySize.heightPixels, new DeviceSpatialProvider.Corners(a.tr, a.tl, a.br, a.bl));
                DisplaySpatialData displayB = new DisplaySpatialData(ctx.B.displaySize.widthPixels, ctx.B.displaySize.heightPixels, new DeviceSpatialProvider.Corners(b.tr, b.tl, b.br, b.bl));
                DisplayPairSpatialData displayPairSpatialData = new DisplayPairSpatialData(displayA, displayB);
                return new PointSegmentProjection(p, q.Value, s, ax, displayPairSpatialData);
            }).Publish().RefCount();
        }

        throw new NotSupportedException($"projection not supported for {typeA} → {typeB}");
    }

    private static IObservable<float> VelocityBuilder(BuildCtx ctx)
    {
        IObservable<Vector3> v;

        if (ctx.Spec.refs != null && ctx.Spec.refs.Count == 2)
        {
            var off = BuildRawOffset(ctx).Publish().RefCount();
            var relVel = DifferentiateVector(off).Publish().RefCount();

            if (!ctx.HasAxis)
            {
                return off.CombineLatest(relVel, (o, dv) =>
                {
                    var m2 = o.sqrMagnitude;
                    if (m2 < 1e-12f) return 0f;
                    var dir = o / Mathf.Sqrt(m2);
                    return -Vector3.Dot(dv, dir);
                }).Publish().RefCount();
            }

            v = relVel;
        }
        else
        {
            v = RefStreamProvider.VelocityStream(ctx.A).Publish().RefCount();

            if (!ctx.HasAxis)
                return v.Select(x => x.magnitude).Publish().RefCount();
        }

        var axis = AxisStream(ctx).Select(SafeNorm).Publish().RefCount();
        return v.CombineLatest(axis, (vel, ax) => Vector3.Dot(vel, ax)).Publish().RefCount();
    }

    private static IObservable<float> AccelerationBuilder(BuildCtx ctx)
    {
        IObservable<Vector3> a;

        if (ctx.Spec.refs != null && ctx.Spec.refs.Count == 2)
        {
            var off = BuildRawOffset(ctx).Publish().RefCount();
            var relVel = DifferentiateVector(off).Publish().RefCount();
            var relAcc = DifferentiateVector(relVel).Publish().RefCount();

            if (!ctx.HasAxis)
            {
                return off.CombineLatest(relAcc, (o, da) =>
                {
                    var m2 = o.sqrMagnitude;
                    if (m2 < 1e-12f) return 0f;
                    var dir = o / Mathf.Sqrt(m2);
                    return -Vector3.Dot(da, dir);
                }).Publish().RefCount();
            }

            a = relAcc;
        }
        else
        {
            var vA = RefStreamProvider.VelocityStream(ctx.A).Publish().RefCount();
            a = DifferentiateVector(vA).Publish().RefCount();

            if (!ctx.HasAxis)
                return a.Select(x => x.magnitude).Publish().RefCount();
        }

        var axis = AxisStream(ctx).Select(SafeNorm).Publish().RefCount();
        return a.CombineLatest(axis, (acc, ax) => Vector3.Dot(acc, ax)).Publish().RefCount();
    }


    // === RMS helpers (EMA of squared value) ===
    private static IObservable<float> EmaRms(IObservable<float> x, float halfLife = 0.17f, float minDt = 1e-4f, float eps = 1e-3f)
    {
        float tauSec = (halfLife > 0f) ? (halfLife / Mathf.Log(2f)) : 0.25f;
        return x.Select(v => (t: Time.time, v2: v * v))
                .Scan((has: false, lastT: 0f, s2: 0f, rms: 0f), (s, cur) =>
                {
                    if (!s.has) return (true, cur.t, cur.v2, Mathf.Sqrt(cur.v2));
                    float dt = Mathf.Max(cur.t - s.lastT, minDt);
                    float tau = Mathf.Max(1e-4f, tauSec);
                    float a = 1f - Mathf.Exp(-dt / tau);           // time-constant EMA
                    float s2 = (1f - a) * s.s2 + a * cur.v2;
                    return (true, cur.t, s2, Mathf.Sqrt(s2));
                })
                .Select(s => s.rms)
                .Scan(float.PositiveInfinity, (prev, curr) => Mathf.Abs(curr - prev) > eps ? curr : prev)
                .DistinctUntilChanged();
    }

    // --- Acceleration RMS (default: norm; with axis: component RMS) ---
    private static IObservable<float> AccelerationRmsBuilder(BuildCtx ctx)
    {
        var aVec = RefStreamProvider.AccelerationStream(ctx.A).Publish().RefCount();

        // If no axis → use prebuilt norm-RMS for efficiency
        if (!ctx.HasAxis)
            return RefStreamProvider.AccelerationRmsStream(ctx.A);

        // With axis → RMS of projected component
        var axis = AxisStream(ctx).Select(SafeNorm).Publish().RefCount();
        var comp = aVec.CombineLatest(axis, (a, ax) => Vector3.Dot(a, ax));
        return EmaRms(comp, (float)(ctx.Spec.@params?.halfLifeSec), 1e-4f, 1e-3f).Publish().RefCount();
    }

    // --- Angular Velocity RMS (default: norm; with axis: component RMS) ---
    private static IObservable<float> AngularVelocityRmsBuilder(BuildCtx ctx)
    {
        var wVec = RefStreamProvider.AngularVelocityStream(ctx.A).Publish().RefCount();

        if (!ctx.HasAxis)
            return RefStreamProvider.AngularVelocityRmsStream(ctx.A);

        var axis = AxisStream(ctx).Select(SafeNorm).Publish().RefCount();
        var comp = wVec.CombineLatest(axis, (w, ax) => Vector3.Dot(w, ax)); // deg/s along axis
        return EmaRms(comp, (float)(ctx.Spec.@params?.halfLifeSec), 1e-4f, 0.1f).Publish().RefCount();
    }

    // ------------------------------ Geometry / Axis helpers ------------------------------

    private static IObservable<Vector3> BuildRawOffset(BuildCtx ctx)
    {
        var r = ctx.Spec.refs;
        var typeA = RefStreamProvider.GetStreamGeometryType(r[0]);
        var typeB = RefStreamProvider.GetStreamGeometryType(r[1]);

        if (typeA != GeometryType.LineSegment && typeB != GeometryType.LineSegment)
        {
            var a = RefStreamProvider.GetStream<Vector3>(ctx.A, r[0]);
            var b = RefStreamProvider.GetStream<Vector3>(ctx.B, r[1]);
            return a.CombineLatest(b, (pa, pb) => pb - pa);
        }

        if (typeA != GeometryType.LineSegment && typeB == GeometryType.LineSegment)
        {
            var a = RefStreamProvider.GetStream<Vector3>(ctx.A, r[0]);
            var b = RefStreamProvider.GetStream<(Vector3, Vector3)>(ctx.B, r[1]);
            return a.CombineLatest(b, (pt, seg) => SpatialHelper.PointToSegmentOffset(pt, seg.Item1, seg.Item2));
        }

        if (typeA == GeometryType.LineSegment && typeB != GeometryType.LineSegment)
        {
            var a = RefStreamProvider.GetStream<(Vector3, Vector3)>(ctx.A, r[0]);
            var b = RefStreamProvider.GetStream<Vector3>(ctx.B, r[1]);
            return a.CombineLatest(b, (seg, pt) => SpatialHelper.PointToSegmentOffset(pt, seg.Item1, seg.Item2));
        }

        var sa = RefStreamProvider.GetStream<(Vector3, Vector3)>(ctx.A, r[0]);
        var sb = RefStreamProvider.GetStream<(Vector3, Vector3)>(ctx.B, r[1]);
        return sa.CombineLatest(sb, (A, B) => SpatialHelper.NearestSegmentOffset(A.Item1, A.Item2, B.Item1, B.Item2));
    }

    private static IObservable<Vector3> AxisStream(BuildCtx ctx)
    {
        var a = ctx.A_Axes;
        var b = ctx.B_Axes;

        IObservable<Vector3> src;

        switch (ctx.Axis.Side)
        {
            case AxisSide.A:
                if (a == null) return Observable.Return(Vector3.zero);
                switch (ctx.Axis.Base)
                {
                    case AxisBase.Up: src = a.Select(v => v.up); break;
                    case AxisBase.Forward: src = a.Select(v => v.fwd); break;
                    case AxisBase.Right: src = a.Select(v => v.right); break;
                    case AxisBase.MajorDiag: src = a.Select(v => v.diag1); break;
                    case AxisBase.MinorDiag: src = a.Select(v => v.diag2); break;
                    default: src = Observable.Return(Vector3.zero); break;
                }
                break;

            case AxisSide.B:
                if (b == null) return Observable.Return(Vector3.zero);
                switch (ctx.Axis.Base)
                {
                    case AxisBase.Up: src = b.Select(v => v.up); break;
                    case AxisBase.Forward: src = b.Select(v => v.fwd); break;
                    case AxisBase.Right: src = b.Select(v => v.right); break;
                    case AxisBase.MajorDiag: src = b.Select(v => v.diag1); break;
                    case AxisBase.MinorDiag: src = b.Select(v => v.diag2); break;
                    default: src = Observable.Return(Vector3.zero); break;
                }
                break;

            case AxisSide.Const:
                return Observable.Return(ConstAxis(ctx.Axis.Base) * ctx.Axis.Sign);

            default:
                return Observable.Return(Vector3.zero);
        }

        return src.Select(v => v * ctx.Axis.Sign);
    }


    // ------------------------------ Differentiation ------------------------------

    private static IObservable<Vector3> DifferentiateVector(IObservable<Vector3> stream, float minDt = 1e-4f)
        => stream
            .Scan((has: false, prev: Vector3.zero, t: 0f, v: Vector3.zero),
                  (s, curr) =>
                  {
                      float now = Time.time;
                      if (!s.has) return (true, curr, now, Vector3.zero);
                      float dt = Mathf.Max(now - s.t, minDt);
                      return (true, curr, now, (curr - s.prev) / dt);
                  })
            .Select(s => s.v);

    // ------------------------------ Parsing / Utils ------------------------------

    private MetricKind ParseMetricKind(string metricSpec, out string rawDir)
    {
        if (string.IsNullOrWhiteSpace(metricSpec))
            throw new ArgumentException("metric is required");

        var idx = metricSpec.IndexOf(':');
        rawDir = idx < 0 ? null : metricSpec.Substring(idx + 1).Trim();
        var rawMetric = (idx < 0 ? metricSpec : metricSpec.Substring(0, idx)).Trim();

        if (!_metricMap.TryGetValue(rawMetric, out var mk))
            throw new NotSupportedException($"Unknown metric '{rawMetric}'");

        return mk;
    }

    private static bool TryParseAxis(string s, out AxisSpec spec)
    {
        spec = default;
        if (string.IsNullOrEmpty(s)) return false;

        int sign = 1;
        if (s[0] == '-') { sign = -1; s = s.Substring(1); }

        switch (s)
        {
            // A side
            case "upA": spec = new AxisSpec(AxisBase.Up, AxisSide.A, sign); return true;
            case "downA": spec = new AxisSpec(AxisBase.Up, AxisSide.A, -sign); return true;
            case "forwardA":
            case "forthA": spec = new AxisSpec(AxisBase.Forward, AxisSide.A, sign); return true;
            case "backA":
            case "backwardA": spec = new AxisSpec(AxisBase.Forward, AxisSide.A, -sign); return true;
            case "rightA": spec = new AxisSpec(AxisBase.Right, AxisSide.A, sign); return true;
            case "leftA": spec = new AxisSpec(AxisBase.Right, AxisSide.A, -sign); return true;
            case "majorDiagonalA": spec = new AxisSpec(AxisBase.MajorDiag, AxisSide.A, sign); return true;
            case "minorDiagonalA": spec = new AxisSpec(AxisBase.MinorDiag, AxisSide.A, sign); return true;

            // B side
            case "upB": spec = new AxisSpec(AxisBase.Up, AxisSide.B, sign); return true;
            case "downB": spec = new AxisSpec(AxisBase.Up, AxisSide.B, -sign); return true;
            case "forwardB":
            case "forthB": spec = new AxisSpec(AxisBase.Forward, AxisSide.B, sign); return true;
            case "backB":
            case "backwardB": spec = new AxisSpec(AxisBase.Forward, AxisSide.B, -sign); return true;
            case "rightB": spec = new AxisSpec(AxisBase.Right, AxisSide.B, sign); return true;
            case "leftB": spec = new AxisSpec(AxisBase.Right, AxisSide.B, -sign); return true;
            case "majorDiagonalB": spec = new AxisSpec(AxisBase.MajorDiag, AxisSide.B, sign); return true;
            case "minorDiagonalB": spec = new AxisSpec(AxisBase.MinorDiag, AxisSide.B, sign); return true;

            // World constants
            case "X": spec = new AxisSpec(AxisBase.X, AxisSide.Const, sign); return true;
            case "Y": spec = new AxisSpec(AxisBase.Y, AxisSide.Const, sign); return true;
            case "Z": spec = new AxisSpec(AxisBase.Z, AxisSide.Const, sign); return true;

            default: return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 ConstAxis(AxisBase b) =>
        b switch
        {
            AxisBase.X => Vector3.right,
            AxisBase.Y => Vector3.up,
            AxisBase.Z => Vector3.forward,
            _ => Vector3.zero
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 Norm(Vector3 v) => v.sqrMagnitude > 1e-12f ? v.normalized : Vector3.zero;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 SafeNorm(Vector3 v) => v.sqrMagnitude > 0f ? v.normalized : Vector3.zero;

    private PrimitivePayload ToPayload(PrimitiveSpec spec, object rawValue)
    {
        bool isProjection = spec.metric.IndexOf("projection", StringComparison.OrdinalIgnoreCase) >= 0;

        if (rawValue is float f32)
        {
            var valueInSpecUnit = UnitConverter.FromBaseUnit(f32, spec.unit);
            var ok = Evaluate(spec.condition, valueInSpecUnit, isProjection);
            return new PrimitivePayload { Id = spec.id, Value = f32, IsValid = ok };
        }

        if (isProjection)
        {
            var proj = rawValue as IProjectionResult;
            bool has = proj != null;
            var ratio = has ? proj.Ratio : 0f;
            var valueInSpecUnit = UnitConverter.FromBaseUnit(ratio, spec.unit);
            bool ok = has && Evaluate(spec.condition, valueInSpecUnit, true);

            return new PrimitivePayload { Id = spec.id, Value = rawValue, IsValid = ok };
        }

        return new PrimitivePayload { Id = spec.id, Value = 0f, IsValid = false };
    }

    private bool Evaluate(ComparatorSpec c, float x, bool isProjection)
    {
        if (c == null) return isProjection ? x > 0 : true;
        float tol = c?.tolerance != null ? (float)c.tolerance:0f;
        bool ok = true;
        if (c.lt is { } lt) ok &= x <= lt + tol;
        if (c.gt is { } gt) ok &= x >= gt - tol;
        if (c.eq is { } eq) ok &= x >= eq - tol && x <= eq + tol;
        return ok;
    }

    private void Warn(string msg) { if (VerboseLogs) Debug.LogWarning($"[PrimitiveFactory] {msg}"); }

    private sealed class BuildCtx
    {
        public Device A;
        public Device B;
        public PrimitiveSpec Spec;

        public MetricKind Metric;
        public AxisSpec Axis;
        public bool HasAxis;
        public string RawDirection;

        public IObservable<(Vector3 up, Vector3 fwd, Vector3 right, Vector3 diag1, Vector3 diag2)> A_Axes;
        public IObservable<(Vector3 up, Vector3 fwd, Vector3 right, Vector3 diag1, Vector3 diag2)> B_Axes;
    }

    private sealed class PayloadComparer : IEqualityComparer<PrimitivePayload>
    {
        public static readonly PayloadComparer Instance = new PayloadComparer();

        public bool Equals(PrimitivePayload x, PrimitivePayload y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;

            if (x.IsValid != y.IsValid) return false;
            if (x.Value == null && y.Value == null) return true;

            if (x.Value is float xf && y.Value is float yf)
                return Mathf.Abs(xf - yf) <= 1e-6f;

            return Equals(x.Value, y.Value);
        }

        public int GetHashCode(PrimitivePayload obj)
        {
            unchecked
            {
                int h = obj.IsValid ? 1 : 0;
                if (obj.Value is float f) h = (h * 397) ^ f.GetHashCode();
                else if (obj.Value != null) h = (h * 397) ^ obj.Value.GetHashCode();
                return h;
            }
        }
    }

    private void InflateFromBaseOrThrow(PrimitiveSpec basePrimitive, InlinePrimitiveSpec inline)
    {
        if (string.IsNullOrWhiteSpace(inline?.id))
            return;
        if (basePrimitive.id != inline.id)
            throw new ArgumentException($"InlinePrimitiveSpec.id '{inline.id}' does not match base primitive id '{basePrimitive.id}'");

        // Modify basePrimitive fields directly  
        if (!string.IsNullOrWhiteSpace(inline.description))
            basePrimitive.description = inline.description;
        if (!string.IsNullOrWhiteSpace(inline.unit))
            basePrimitive.unit = inline.unit;

        // Override fields inside condition  
        if (inline.condition != null)
        {
            if (basePrimitive.condition == null)
                basePrimitive.condition = inline.condition;
            else
            {
                if (inline.condition.lt is { } lt) basePrimitive.condition.lt = lt;
                if (inline.condition.gt is { } gt) basePrimitive.condition.gt = gt;
                if (inline.condition.eq is { } eq) basePrimitive.condition.eq = eq;
                if (inline.condition.tolerance is { } tol) basePrimitive.condition.tolerance = tol;
            }
        }
    }


}
