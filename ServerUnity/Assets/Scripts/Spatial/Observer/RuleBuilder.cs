using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UniRx;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class RuleEvent
{
    public bool State;                               // current combined boolean state
    public Dictionary<string, object> Streams;       // path -> measurement object
}
// Add at top-level of your codebase (same file or a small new file).

public sealed class CompositionSpec
{
    public string id;
    public string description;          // optional
    public string @operator;            // "AND" | "OR" | "NOT" (defaults to AND)
    public object primitives;           // same shapes as rule.condition.primitives (strings or inline overrides)
}

public sealed class RuleBuilder
{
    private readonly PrimitiveFactory _factory;

    public bool VerboseLogs = true;   // lifecycle logs
    public bool TraceValues = false; // per-primitive log taps

    public RuleBuilder(PrimitiveFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        LoadCompositionsFromResources("JSON/compositions");
    }

    /// <summary>
    /// Emits RuleEvent snapshots per 'on':
    /// enter  → on state false→true
    /// exit   → on state true→false
    /// change → on any state change
    /// always → on publish value changes
    /// true   → on publish value changes while state==true
    /// false  → on publish value changes while state==false
    /// </summary>
    public IObservable<RuleEvent> Build(RuleSpec spec)
    {
        try
        {
            // ---------- Validate ----------
            if (spec == null) throw new ArgumentNullException(nameof(spec), "RuleSpec cannot be null");
            //if (spec.condition == null) throw new ArgumentException("RuleSpec.condition is required", nameof(spec));

            //var rawIds = spec?.condition?.primitives?
            //             .Where(s => !string.IsNullOrWhiteSpace(s))
            //             .ToArray() ?? Array.Empty<string>();
            List<CondRef> condList = ParseConditionPrimitives(spec?.condition?.primitives);
            var rawIds = condList.Select(c => c.Id).ToArray();
            //if (rawIds.Length == 0)
            //    throw new ArgumentException("RuleSpec.condition.primitives must have at least one id", nameof(spec));


            var op = NormalizeOperator(spec?.condition?.@operator);
            var onMode = NormalizeOn(spec.on);

            // ---------- Resolve devices ----------
            var entitiesJson = ExtractEntitiesJson(spec.entities);
            if (string.IsNullOrWhiteSpace(entitiesJson))
                throw new ArgumentException("RuleSpec.entities is null/empty; cannot resolve devices.", nameof(spec));

            Device A, B;
            try { DeviceResolver.ResolveDevices(out A, out B, entitiesJson); }
            catch (Exception ex) { throw new InvalidOperationException($"Device resolution failed: {ex.Message}", ex); }

            if (A == null)
            {
                if (VerboseLogs) Debug.LogWarning($"[RuleBuilder] Devices unresolved for '{spec.id}'. Emitting default snapshot (false, empty).");
                return Observable.Return(new RuleEvent { State = false, Streams = new Dictionary<string, object>() });
            }
            var pair = new DevicePair(A, B);
            if (VerboseLogs) Debug.Log($"[RuleBuilder] Devices resolved for '{spec.id}': {pair}");
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IObservable<bool> stateBase;
            // Fix for the line causing CS0119 and CS0443 errors  
            // Original line: var allIds = String[];  
            // Corrected line:  
            var allIds = Array.Empty<string>();
            var pubs = ParsePublishStreams(spec.publish?.streams);

            if (rawIds.Length == 0)
            {
                stateBase = Observable.Return(true); // no condition => always true
            }
            else
            {
                var condStreams = new List<IObservable<bool>>(condList.Count);
                var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cr in condList)
                    condStreams.Add(BuildPredicateStream(pair, cr, stack, usedIds, spec));

                stateBase = CombineBooleanStreams(condStreams, op).Publish().RefCount();
            }
           
            var latestState = stateBase.StartWith(false).Publish().RefCount();
            var stateChanges = stateBase.DistinctUntilChanged().Publish().RefCount();

            // ---------- Publish snapshot (measurement objects) ----------
            var publishSnapshot = BuildPublishSnapshot(pair, pubs, spec, usedIds, RuleBuilderHelpers.GetMappingToken(spec?.publish)).Publish().RefCount();
            var latestSnapshot = publishSnapshot.StartWith(new Dictionary<string, object>()).Publish().RefCount();


            // ---------- Triggers ----------
            // ---------- Triggers ----------
            // stateBase:    evolving combined bool (AND/OR/NOT)
            // latestState:  stateBase with StartWith(false) so we always have a current state
            // stateChanges: only edges of stateBase (false->true or true->false)
            // publishSnapshot: emits only when any requested publish stream (measurement) changes
            // latestSnapshot:  last known publish values (so we can pair them with state-only triggers)

            IObservable<RuleEvent> output;

            switch (onMode)
            {
                case "enter":
                    // Emit ONCE when combined state goes false -> true
                    // Snapshot is whatever the latest publish values are at the edge.
                    output = stateChanges
                        .Where(v => v)
                        .WithLatestFrom(latestSnapshot, (st, snap) => new RuleEvent { State = true, Streams = snap });
                    break;

                case "exit":
                    // Emit ONCE when combined state goes true -> false
                    output = stateChanges
                        .Where(v => !v)
                        .WithLatestFrom(latestSnapshot, (st, snap) => new RuleEvent { State = false, Streams = snap });
                    break;

                case "change":
                    // Emit on ANY state edge (true->false or false->true), with the current snapshot
                    output = stateChanges
                        .WithLatestFrom(latestSnapshot, (st, snap) => new RuleEvent { State = st, Streams = snap });
                    break;

                case "true":
                    // Emit ONLY when a publish stream changes AND current combined state is true
                    output = publishSnapshot
                        .WithLatestFrom(latestState, (snap, st) => (snap, st))
                        .Where(t => t.st)
                        .Select(t => new RuleEvent { State = true, Streams = t.snap });
                    break;

                case "false":
                    // Emit ONLY when a publish stream changes AND current combined state is false
                    output = publishSnapshot
                        .WithLatestFrom(latestState, (snap, st) => (snap, st))
                        .Where(t => !t.st)
                        .Select(t => new RuleEvent { State = false, Streams = t.snap });
                    break;

                case "always":
                default:
                    // Emit on ANY publish-stream change (no regard to state edges).
                    // We still include the current state so listeners see both.
                    output = publishSnapshot
                        .WithLatestFrom(latestState, (snap, st) => new RuleEvent { State = st, Streams = snap });
                    break;
            }


            var shared = output.Publish().RefCount();

            return Observable.Create<RuleEvent>(observer =>
            {
                var sub = shared.Subscribe(observer);
                return Disposable.Create(() =>
                {
                    try { sub.Dispose(); }
                    finally
                    {
                        //foreach (var id in usedIds) _factory.Release(pair, id);
                        if (VerboseLogs) Debug.Log($"[RuleBuilder] Released primitives for '{spec.id}'");
                    }
                });
            });

        }
        catch (Exception ex)
        {
            Debug.LogError($"[RuleBuilder] Build failed for '{spec?.id}': {ex}");
            return Observable.Throw<RuleEvent>(ex);
        }
    }
    // Inside RuleBuilder
    private IObservable<bool> BuildPredicateStream(
        DevicePair pair,
        CondRef entry,
        HashSet<string> recursionStack,
        HashSet<string> usedPrimitiveIds,
        RuleSpec spec)
    {
        // Is this entry a COMPOSITION id?
        if (_compositionsById.TryGetValue(entry.Id, out var comp))
        {
            // cycle guard
            if (!recursionStack.Add(comp.id))
                throw new InvalidOperationException($"Composition cycle detected at '{comp.id}'.");

            var childConds = ParseConditionPrimitives(comp.primitives); // re-use your existing parser
            var childStreams = new List<IObservable<bool>>(childConds.Count);
            foreach (var ch in childConds)
                childStreams.Add(BuildPredicateStream(pair, ch, recursionStack, usedPrimitiveIds, spec));

            recursionStack.Remove(comp.id);

            var op = NormalizeOperator(comp.@operator);     // default AND
            var combined = CombineBooleanStreams(childStreams, op).Publish().RefCount();

            // Allow negation on a composition reference (if someone used "!coplanar")
            if (entry.Negate) combined = combined.Select(v => !v);

            if (TraceValues)
                combined = combined.Do(v => Debug.Log($"[Rule {spec.id}|comp:{comp.id}] => {v}"));

            return combined;
        }

        // LEAF primitive path (with optional inline override)
        // Ensure the primitive stream exists (this inc-refs inside the factory)
        var payload = _factory.Get(pair, entry.Id, entry.InlineSpec);
        usedPrimitiveIds.Add(entry.Id);

        var s = payload.Select(pl => pl.IsValid);
        if (entry.Negate) s = s.Select(v => !v);

        if (TraceValues)
            s = s.Do(v => Debug.Log($"[Rule {spec.id}|{(entry.Negate ? "!" : "")}{entry.Id}] IsValid={v}"));

        return s.Publish().RefCount();
    }

    // ===== Publish snapshot (OBJECT values; emits on value changes using deep-ish equality) =====
    private IObservable<Dictionary<string, object>> BuildPublishSnapshot(
        DevicePair pair,
        List<(string path, string primitiveId)> pubs,
        RuleSpec spec,
         HashSet<string> usedIds,
             JToken mappingToken // <-- NEW
)
    {
        if (pubs == null || pubs.Count == 0)
            return Observable.Return(new Dictionary<string, object>());

        var valueStreams = new List<IObservable<PublishSample>>(pubs.Count);
        var missing = new List<string>();

        foreach (var p in pubs)
        {
            if (!string.IsNullOrEmpty(p.primitiveId))
            {
                IObservable<PrimitivePayload> payloadStream;

                if (_factory.TryGet(pair, p.primitiveId, out var existing))
                {
                    payloadStream = existing;
                    // TryGet already inc-ref’d; track
                    usedIds.Add(p.primitiveId);
                }
                else
                {
                    // Force create (inc-ref) so publish works even when id only exists via composition
                    payloadStream = _factory.Get(pair, p.primitiveId, null);
                    usedIds.Add(p.primitiveId);
                }

                var vs = payloadStream
                    .Select(pl => pl.Value)
                    .DistinctUntilChanged(ObjectEquality.Instance)
                    .Select(val => new PublishSample(p.path, val));

                if (TraceValues)
                    vs = vs.Do(s => Debug.Log($"[Rule {spec.id}|{p.primitiveId}] Value={FormatForLog(s.Value)} ({s.Path})"));

                valueStreams.Add(vs);
            }
            else
            {
                // ----- feature path: A.<ref> / B.<ref> (e.g., A.corners)
                // Map to device + feature name
                var path = p.path ?? string.Empty;
                Device dev = null;
                string feature = null;

                if (path.StartsWith("A.", StringComparison.OrdinalIgnoreCase))
                {
                    dev = pair.A;
                    feature = path.Substring(2); // after "A."
                }
                else if (path.StartsWith("B.", StringComparison.OrdinalIgnoreCase))
            {
                    dev = pair.B;
                    feature = path.Substring(2); // after "B."
                }

                if (dev != null && !string.IsNullOrWhiteSpace(feature))
                {
                    var vs = ResolveFeatureStream(dev, feature)
                        .DistinctUntilChanged(ObjectEquality.Instance)
                        .Select(val => new PublishSample(path, val));

                    if (TraceValues)
                        vs = vs.Do(s => Debug.Log($"[Rule {spec.id}|feat {path}] Value={FormatForLog(s.Value)}"));

                    valueStreams.Add(vs);
                }
            }

            }


        if (missing.Count > 0)
            throw new ArgumentException($"Primitives not found for publish: {string.Join(", ", missing)}");

        IObservable<Dictionary<string, object>> baseSnapshot = valueStreams
            .Merge()
            .Scan(new Dictionary<string, object>(), (acc, sample) =>
            {
                var next = new Dictionary<string, object>(acc);
                next[sample.Path] = sample.Value;
                return next;
            })
            .DistinctUntilChanged(new DictObjectComparer()); // snapshot de-dupe
                                                             // 2.2) Apply mappings (no-op if mappingToken is null)
        return MappingResolver.ApplyMappingsIfAny(baseSnapshot, pair, mappingToken);
    }

    //private IObservable<Dictionary<string, object>> ApplyMappingsIfAny(
    //  IObservable<Dictionary<string, object>> baseSnapshot,
    //  DevicePair pair,
    //  JToken mappingToken)
    //{
    //    if (mappingToken == null || mappingToken.Type != JTokenType.Object)
    //        return baseSnapshot;

    //    var defCorners = default((Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR));

    //    var cornersAObs = RefStreamProvider.CornersStream(pair.A)
    //        .Select(c => (TL: c.tl, TR: c.tr, BL: c.bl, BR: c.br))
    //        .Publish().RefCount();

    //    var cornersBObs = (pair.B != null
    //        ? RefStreamProvider.CornersStream(pair.B)
    //            .Select(c => (TL: c.tl, TR: c.tr, BL: c.bl, BR: c.br))
    //            .Publish().RefCount()
    //        : Observable.Return(defCorners));

    //    var cornersAB = Observable.CombineLatest(
    //        cornersAObs.StartWith(defCorners),
    //        cornersBObs.StartWith(defCorners),
    //        (a, b) => (a, b)
    //    );

    //    (int w, int h) GetRes(Device d)
    //    {
    //        return (d.displaySize.widthPixels, d.displaySize.heightPixels);
    //    }

    //    return baseSnapshot
    //        .WithLatestFrom(cornersAB, (snap, ab) => (snap, ab.a, ab.b))
    //        .Select(tuple =>
    //        {
    //            var (snap, ca, cb) = tuple;
    //            var haveA = !(ca.TL == default && ca.TR == default);
    //            var haveB = !(cb.TL == default && cb.TR == default);

    //            var (WA, HA) = GetRes(pair.A);
    //            var (WB, HB) = (pair.B != null) ? GetRes(pair.B) : (0, 0);

    //            var outSnap = new Dictionary<string, object>(snap);
    //            var map = new Dictionary<string, object>();

    //            try
    //            {
    //                // ---- toPixelA ----
    //                var toA = mappingToken["toPixelA"];
    //                if (toA != null && haveA)
    //                {
    //                    var world = RuleBuilderHelpers.ResolveWorldInputs(toA, snap);
    //                    if (world != null && world.Length > 0)
    //                    {
 
    //                        map["toPixelA"] = CoordinateMapping.WorldToPixelFromCornersList(
    //                                world, WA, HA, ca.TL, ca.TR, ca.BL, ca.BR, true); ;
    //                    }
    //                }

    //                // ---- toPixelB ----
    //                var toB = mappingToken["toPixelB"];
    //                if (toB != null && haveB && WB > 0 && HB > 0)
    //                {
    //                    var world = RuleBuilderHelpers.ResolveWorldInputs(toB, snap);
    //                    if (world!=null && world.Length > 0)
    //                    {

    //                        map["toPixelB"] = CoordinateMapping.WorldToPixelFromCornersList(
    //                                world, WB, HB, cb.TL, cb.TR, cb.BL, cb.BR, true);

    //                    }
    //                }

    //                // ---- fromPixelA (absolute pixel points) ----
    //                var fromA = mappingToken["fromPixelA"];
    //                if (fromA != null && haveA)
    //                {
    //                    var pix = RuleBuilderHelpers.ResolvePixelInputs(fromA); // absolute pixels
    //                    if (pix.Length > 0)
    //                    {
    //                        map["fromPixelA"] = CoordinateMapping.PixelToWorldFromCornersList(
    //                                pix, WA, HA, ca.TL, ca.TR, ca.BL, ca.BR, true); ;
    //                    }
    //                }

    //                // ---- fromPixelB (absolute pixel points) ----
    //                var fromB = mappingToken["fromPixelB"];
    //                if (fromB != null && haveB && WB > 0 && HB > 0)
    //                {
    //                    var pix = RuleBuilderHelpers.ResolvePixelInputs(fromB); // absolute pixels
    //                    if (pix.Length > 0)
    //                    {
    //                        map["fromPixelB"] = CoordinateMapping.PixelToWorldFromCornersList(
    //                                pix, WB, HB, cb.TL, cb.TR, cb.BL, cb.BR, true); ;
    //                    }
    //                }

    //                //// ---- semantic remap ----
    //                //var sem = mappingToken["semantic"];
    //                //if (sem?.Type == JTokenType.Object)
    //                //{
    //                //    var primId = (string)sem["primitive"];
    //                //    var outRange = ReadRange(sem["range"]) ?? new Vector2(0, 1);
    //                //    var inDomain = ReadRange(sem["domain"]) ?? new Vector2(0, 1);
    //                //    var path = (string)sem["path"];
    //                //    if (!string.IsNullOrEmpty(primId) && string.IsNullOrEmpty(path))
    //                //        path = $"primitives.{primId}.measurement";

    //                //    if (!string.IsNullOrEmpty(path) && snap.TryGetValue(path, out var raw))
    //                //    {
    //                //        if (TryReadNumber(raw, out float v))
    //                //        {
    //                //            map["semantic"] = RangeMap.Remap(v, inDomain.x, inDomain.y, outRange.x, outRange.y, true);
    //                //        }
    //                //        else if (raw is JObject jo &&
    //                //                 TryReadNumber(jo["value"], out float vv) &&
    //                //                 TryReadNumber(jo["min"], out float vmin) &&
    //                //                 TryReadNumber(jo["max"], out float vmax))
    //                //        {
    //                //            map["semantic"] = RangeMap.Remap(vv, vmin, vmax, outRange.x, outRange.y, true);
    //                //        }
    //                //    }
    //                //}
    //            }
    //            catch (Exception ex)
    //            {
    //                Debug.LogWarning($"[RuleBuilder] mapping apply error: {ex.Message}");
    //            }

    //            if (map.Count > 0)
    //                outSnap["mapping"] = map;

    //            return outSnap;
    //        })
    //        .DistinctUntilChanged(new DictObjectComparer());
    //}


    // ===== Boolean combiner (AND / OR / NOT) =====
    private static IObservable<bool> CombineBooleanStreams(IReadOnlyList<IObservable<bool>> streams, string op)
    {
        if (streams == null || streams.Count == 0)
        {
            return op == "AND" ? Observable.Return(true)
                 : op == "OR" ? Observable.Return(false)
                 : /* NOT */     Observable.Return(false);
        }

        if (op == "NOT")
        {
            if (streams.Count == 1)
                return streams[0].StartWith(false).Select(v => !v).DistinctUntilChanged();

            var andCombined = MergeScan(streams, seed: true, reducerAll: true);
            return andCombined.Select(v => !v).DistinctUntilChanged();
        }

        if (op == "AND")
            return MergeScan(streams, seed: false, reducerAll: true);

        // OR
        return MergeScan(streams, seed: false, reducerAll: false);
    }

    private static IObservable<bool> MergeScan(IReadOnlyList<IObservable<bool>> streams, bool seed, bool reducerAll)
    {
        if (streams.Count == 1)
            return streams[0].StartWith(seed).DistinctUntilChanged();

        var indexed = streams.Select((s, i) => s.StartWith(seed).Select(v => (index: i, value: v)));

        return indexed
            .Merge()
            .Scan(Enumerable.Repeat(seed, streams.Count).ToArray(),
                (latest, evt) =>
                {
                    var next = (bool[])latest.Clone();
                    next[evt.index] = evt.value;
                    return next;
                })
            .Select(vals => reducerAll ? vals.All(v => v) : vals.Any(v => v))
            .DistinctUntilChanged();
    }

    // ===== Helpers =====

    private static (string id, bool negate) ParsePrimitiveRef(string raw)
    {
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) return (s, false);
        if (s[0] == '!' || s[0] == '~') return (s.Substring(1), true);
        return (s, false);
    }

    private static string NormalizeOperator(string op)
    {
        var o = (op ?? "AND").Trim().ToUpperInvariant();
        return (o == "AND" || o == "OR" || o == "NOT") ? o : "AND";
    }

    private static string NormalizeOn(string on)
    {
        var o = (on ?? "always").Trim().ToLowerInvariant();
        switch (o)
        {
            case "enter":
            case "exit":
            case "change":
            case "always":
            case "true":
            case "false":
                return o;
            default:
                return "always";
        }
    }

    private static List<(string path, string primitiveId)> ParsePublishStreams(IEnumerable<string> entries)
    {
        var list = new List<(string, string)>();
        foreach (var raw in entries ?? Enumerable.Empty<string>())
        {
            var s = (raw ?? "").Trim();
            if (s.Length == 0) continue;

            // Primitive path: primitives.<id>.(measurement|value)
            const string prefix = "primitives.";
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var rest = s.Substring(prefix.Length);
                var parts = rest.Split('.');
                if (parts.Length == 0) continue;

                var id = parts[0];
                if (string.IsNullOrWhiteSpace(id)) continue;

                var suffix = (parts.Length >= 2 ? parts[1] : "measurement").ToLowerInvariant();
                if (suffix != "measurement" && suffix != "value") suffix = "measurement";

                list.Add(($"primitives.{id}.{suffix}", id));
                continue;
            }

            // Feature path: A.<ref> or B.<ref>  (mark primitiveId = null)
            if ((s.StartsWith("A.", StringComparison.OrdinalIgnoreCase) ||
                 s.StartsWith("B.", StringComparison.OrdinalIgnoreCase)) && s.Length > 2)
            {
                list.Add((s, null));
            }
            // else: silently ignore unknown entries
        }
        return list;
    }


    private static string ExtractEntitiesJson(object entities)
    {
        if (entities == null) return null;
        return entities switch
        {
            string s => s.TrimStart().StartsWith("{") || s.TrimStart().StartsWith("[") ? s : JsonConvert.SerializeObject(s),
            JToken t => t.ToString(Formatting.None),
            _ => JsonConvert.SerializeObject(entities, Formatting.None)
        };
    }

    private static string FormatForLog(object v)
    {
        if (v == null) return "null";
        if (v is Vector3 vv) return $"({vv.x:F3},{vv.y:F3},{vv.z:F3})";
        if (v is Vector2 v2) return $"({v2.x:F3},{v2.y:F3})";
        if (v is Quaternion q) return $"({q.x:F3},{q.y:F3},{q.z:F3},{q.w:F3})";
        return v.ToString();
    }

    // ===== Internal types & equality =====

    private readonly struct PublishSample
    {
        public readonly string Path;
        public readonly object Value;
        public PublishSample(string path, object value) { Path = path; Value = value; }
    }

    /// Deep-ish equality for measurement objects
    private sealed class ObjectEquality : IEqualityComparer<object>
    {
        public static readonly ObjectEquality Instance = new ObjectEquality();

        private const float Eps = 1e-4f;

        public new bool Equals(object x, object y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;

            // JToken deep compare
            if (x is JToken jtX || y is JToken jtY)
            {
                var a = x as JToken ?? JToken.FromObject(x);
                var b = y as JToken ?? JToken.FromObject(y);
                return JToken.DeepEquals(a, b);
            }

            // Numeric → compare as double
            if (TryAsDouble(x, out var dx) && TryAsDouble(y, out var dy))
                return Math.Abs(dx - dy) <= Eps;

            // Vector types
            if (x is Vector3 vx && y is Vector3 vy)
                return (vx - vy).sqrMagnitude <= Eps * Eps;
            if (x is Vector2 v2x && y is Vector2 v2y)
                return (v2x - v2y).sqrMagnitude <= Eps * Eps;
            if (x is Vector4 v4x && y is Vector4 v4y)
                return (v4x - v4y).sqrMagnitude <= Eps * Eps;
            if (x is Quaternion qx && y is Quaternion qy)
            {
                // Handle q and -q equivalence
                var dot = Mathf.Abs(Quaternion.Dot(qx, qy));
                return 1f - dot <= Eps;
            }

            // IEnumerable (arrays/lists)
            if (x is IEnumerable ex && y is IEnumerable ey)
            // Fallback
                return SequenceEqual(ex, ey, this);

            return x.Equals(y);
        }

        public int GetHashCode(object obj)
        {
            if (obj == null) return 0;

            // JToken stable hash via string (avoid huge cost on large tokens as needed)
            if (obj is JToken jt)
                return jt.ToString(Formatting.None).GetHashCode();

            if (TryAsDouble(obj, out var d))
                return Quantize(d, Eps).GetHashCode();

            if (obj is Vector3 v)
                return Quantize(v.x, Eps).GetHashCode() ^ Quantize(v.y, Eps).GetHashCode() ^ Quantize(v.z, Eps).GetHashCode();
            if (obj is Vector2 v2)
                return Quantize(v2.x, Eps).GetHashCode() ^ Quantize(v2.y, Eps).GetHashCode();
            if (obj is Vector4 v4)
                return Quantize(v4.x, Eps).GetHashCode() ^ Quantize(v4.y, Eps).GetHashCode() ^ Quantize(v4.z, Eps).GetHashCode() ^ Quantize(v4.w, Eps).GetHashCode();
            if (obj is Quaternion q)
                return Quantize(q.x, Eps).GetHashCode() ^ Quantize(q.y, Eps).GetHashCode() ^ Quantize(q.z, Eps).GetHashCode() ^ Quantize(q.w, Eps).GetHashCode();

            if (obj is IEnumerable e)
            {
                unchecked
                {
                    int h = 17;
                    foreach (var item in e)
                        h = h * 31 + GetHashCode(item);
                    return h;
                }
            }

            return obj.GetHashCode();
        }

        private static bool TryAsDouble(object o, out double d)
        {
            switch (o)
            {
                case double dd: d = dd; return true;
                case float f: d = f; return true;
                case decimal m: d = (double)m; return true;
                case byte b: d = b; return true;
                case sbyte sb: d = sb; return true;
                case short s: d = s; return true;
                case ushort us: d = us; return true;
                case int i: d = i; return true;
                case uint ui: d = ui; return true;
                case long l: d = l; return true;
                case ulong ul: d = ul; return true;
                default: d = 0; return false;
            }
        }

        private static bool SequenceEqual(IEnumerable a, IEnumerable b, IEqualityComparer<object> cmp)
        {
            var ea = a.GetEnumerator();
            var eb = b.GetEnumerator();
            while (true)
            {
                var ma = ea.MoveNext();
                var mb = eb.MoveNext();
                if (ma != mb) return false;
                if (!ma) return true;
                if (!cmp.Equals(ea.Current, eb.Current)) return false;
            }
        }

        private static int Quantize(double v, double eps) => Mathf.RoundToInt((float)(v / eps));
        private static int Quantize(float v, float eps) => Mathf.RoundToInt(v / eps);
    }
    private static IObservable<object> ResolveFeatureStream(Device device, string refName)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        if (string.IsNullOrWhiteSpace(refName)) throw new ArgumentException("feature ref must be non-empty", nameof(refName));

        // helpers
        static string P(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";
        static string CornersStr((Vector3 tr, Vector3 tl, Vector3 bl, Vector3 br) c)
            => $"[{P(c.tr)}, {P(c.tl)}, {P(c.bl)}, {P(c.br)}]";

        var rn = refName.Trim();

        // --- Full corners as one formatted string ---
        if (string.Equals(rn, "corners", StringComparison.OrdinalIgnoreCase))
        {
            return RefStreamProvider.CornersStream(device)
                .Select(c => (object)CornersStr(c));
        }

        // (Optional) Named corners as "(x,y,z)" strings
        if (rn.Equals("topLeft", StringComparison.OrdinalIgnoreCase) || rn.Equals("tl", StringComparison.OrdinalIgnoreCase))
            return RefStreamProvider.CornersStream(device).Select(c => (object)P(c.tl));
        if (rn.Equals("topRight", StringComparison.OrdinalIgnoreCase) || rn.Equals("tr", StringComparison.OrdinalIgnoreCase))
            return RefStreamProvider.CornersStream(device).Select(c => (object)P(c.tr));
        if (rn.Equals("bottomLeft", StringComparison.OrdinalIgnoreCase) || rn.Equals("bl", StringComparison.OrdinalIgnoreCase))
            return RefStreamProvider.CornersStream(device).Select(c => (object)P(c.bl));
        if (rn.Equals("bottomRight", StringComparison.OrdinalIgnoreCase) || rn.Equals("br", StringComparison.OrdinalIgnoreCase))
            return RefStreamProvider.CornersStream(device).Select(c => (object)P(c.br));

        // (Optional) Edges as "A.rightEdge=[(x,y,z), (x,y,z)]"
        if (rn.Equals("topEdge", StringComparison.OrdinalIgnoreCase))
            return RefStreamProvider.CornersStream(device).Select(c => (object)$"[{P(c.tl)}, {P(c.tr)}]");
        if (rn.Equals("bottomEdge", StringComparison.OrdinalIgnoreCase))
            return RefStreamProvider.CornersStream(device).Select(c => (object)$"[{P(c.bl)}, {P(c.br)}]");
        if (rn.Equals("leftEdge", StringComparison.OrdinalIgnoreCase))
            return RefStreamProvider.CornersStream(device).Select(c => (object)$"[{P(c.tl)}, {P(c.bl)}]");
        if (rn.Equals("rightEdge", StringComparison.OrdinalIgnoreCase))
            return RefStreamProvider.CornersStream(device).Select(c => (object)$"[{P(c.tr)}, {P(c.br)}]");


        // Use geometry classification for other refs
        var g = RefStreamProvider.GetStreamGeometryType(rn);
        switch (g)
        {
            case GeometryType.Point:
                return RefStreamProvider.GetStream<Vector3>(device, rn).Select(v => (object)v);

            case GeometryType.LineSegment:
                return RefStreamProvider.GetStream<(Vector3, Vector3)>(device, rn).Select(seg => (object)seg);

            case GeometryType.Polygon:
                if (string.Equals(rn, "surface", StringComparison.OrdinalIgnoreCase))
                    return RefStreamProvider.CornersStream(device)
                        .Select(c => (object)new Vector3[] { c.tr, c.tl, c.bl, c.br });
                throw new NotSupportedException($"Polygon ref '{rn}' requires a concrete provider (e.g., CornersStream).");

            default:
                throw new NotSupportedException($"Unknown/unsupported feature ref '{rn}' (geometry type: {g}).");
        }
    }

    /// Snapshot equality for Dictionary<string, object>
    private sealed class DictObjectComparer : IEqualityComparer<Dictionary<string, object>>
    {
        public bool Equals(Dictionary<string, object> x, Dictionary<string, object> y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null || x.Count != y.Count) return false;

            var cmp = ObjectEquality.Instance;
            foreach (var kv in x)
            {
                if (!y.TryGetValue(kv.Key, out var v)) return false;
                if (!cmp.Equals(kv.Value, v)) return false;
            }
            return true;
        }

        public int GetHashCode(Dictionary<string, object> obj)
        {
            if (obj == null) return 0;
            unchecked
            {
                int h = 17;
                foreach (var kv in obj.OrderBy(k => k.Key))
                {
                    h = h * 31 + kv.Key.GetHashCode();
                    h = h * 31 + ObjectEquality.Instance.GetHashCode(kv.Value);
                }
                return h;
            }
        }

    }

    private static List<CondRef> ParseConditionPrimitives(object primitivesField)
    {
        List<CondRef> list = new List<CondRef>();
        if (primitivesField == null) return list;

        var token = primitivesField as JToken ?? JToken.FromObject(primitivesField);
        if (token is JArray arr)
        {
            foreach (var it in arr)
            {
                if (it.Type == JTokenType.String)
                {
                    var raw = it.Value<string>() ?? "";
                    var neg = raw.Length > 0 && (raw[0] == '!' || raw[0] == '~');
                    var id = neg ? raw.Substring(1) : raw;
                    if (!string.IsNullOrWhiteSpace(id))
                        list.Add(new CondRef { Id = id, Negate = neg });
                }
                else if (it.Type == JTokenType.Object)
                {
                    var inline = it.ToObject<InlinePrimitiveSpec>();
                    if (inline == null || string.IsNullOrWhiteSpace(inline.id)) continue;



                    list.Add(new CondRef
                    {
                        Id = inline.id,
                        InlineSpec = inline,
                        Negate = false // (negation not supported for object form)
                    });
                }
            }
        }
        else if (token.Type == JTokenType.String)
        {
            var raw = token.Value<string>() ?? "";
            var neg = raw.Length > 0 && (raw[0] == '!' || raw[0] == '~');
            var id = neg ? raw.Substring(1) : raw;
            if (!string.IsNullOrWhiteSpace(id))
                list.Add(new CondRef { Id = id, Negate = neg });
        }

        return list;
    }
    // Inside RuleBuilder
    private readonly Dictionary<string, CompositionSpec> _compositionsById =
        new Dictionary<string, CompositionSpec>(StringComparer.OrdinalIgnoreCase);

    public void SetCompositions(IEnumerable<CompositionSpec> list)
    {
        _compositionsById.Clear();
        if (list == null) return;
        foreach (var c in list)
        {
            if (!string.IsNullOrWhiteSpace(c?.id))
                _compositionsById[c.id] = c;
        }
    }
    public void LoadCompositionsFromResources(string resourcePath)
    {
        var ta = Resources.Load<TextAsset>(resourcePath);
        var json = ta ? ta.text : "[]";
        var list = JsonConvert.DeserializeObject<List<CompositionSpec>>(json) ?? new List<CompositionSpec>();
        SetCompositions(list);
        if (VerboseLogs) Debug.Log($"[RuleBuilder] Loaded {list.Count} compositions from Resources '{resourcePath}'.");
    }
}
