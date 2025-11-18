using System;
using System.Collections.Generic;
using UniRx;
using UnityEngine;
using Newtonsoft.Json.Linq;

/// <summary>
/// Resolves and applies world/pixel coordinate mappings between paired devices.
/// Keeps ApplyMappingsIfAny() minimal and delegates to private helpers.
/// </summary>
public static class MappingResolver
{
    /// <summary>
    /// Applies all defined coordinate mappings (toPixelA/B, fromPixelA/B) between devices.
    /// </summary>
    public static IObservable<Dictionary<string, object>> ApplyMappingsIfAny(
        IObservable<Dictionary<string, object>> baseSnapshot,
        DevicePair pair,
        JToken mappingToken)
    {
        if (mappingToken == null || mappingToken.Type != JTokenType.Object)
            return baseSnapshot;

        var defCorners = default((Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR));

        // Observable streams for corners of both devices
        var cornersAObs = RefStreamProvider.CornersStream(pair.A)
            .Select(c => (TL: c.tl, TR: c.tr, BL: c.bl, BR: c.br))
            .Publish().RefCount();

        var cornersBObs = (pair.B != null)
            ? RefStreamProvider.CornersStream(pair.B)
                .Select(c => (TL: c.tl, TR: c.tr, BL: c.bl, BR: c.br))
                .Publish().RefCount()
            : Observable.Return(defCorners);

        var cornersAB = Observable.CombineLatest(
            cornersAObs.StartWith(defCorners),
            cornersBObs.StartWith(defCorners),
            (a, b) => (a, b)
        );

        return baseSnapshot
            .WithLatestFrom(cornersAB, (snap, ab) => (snap, ab.a, ab.b))
            .Select(tuple => ApplyMappings(tuple.snap, pair, tuple.a, tuple.b, mappingToken));
    }



    // ----------------------------------------------------------------------
    // PRIVATE HELPERS
    // ----------------------------------------------------------------------

    private static Dictionary<string, object> ApplyMappings(
        Dictionary<string, object> snapshot,
        DevicePair pair,
        (Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR) ca,
        (Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR) cb,
        JToken mappingToken)
    {
        var outSnap = new Dictionary<string, object>(snapshot);
        var map = new Dictionary<string, object>();

        try
        {
            ApplyToPixel("A", map, mappingToken["toPixelA"], snapshot, pair.A, ca);
            ApplyToPixel("B", map, mappingToken["toPixelB"], snapshot, pair.B, cb);

            ApplyFromPixel("A", map, mappingToken["fromPixelA"], pair.A, ca);
            ApplyFromPixel("B", map, mappingToken["fromPixelB"], pair.B, cb);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MappingResolver] Mapping error: {ex.Message}");
        }

        if (map.Count > 0)
            outSnap["mapping"] = map;

        return outSnap;
    }

    private static void ApplyToPixel(
        string label,
        Dictionary<string, object> map,
        JToken mappingDef,
        Dictionary<string, object> snapshot,
        Device device,
        (Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR) corners)
    {
        if (mappingDef == null || device == null) return;

        var (w, h) = GetResolution(device);
        if (w <= 0 || h <= 0 || !HasCorners(corners)) return;

        var world = RuleBuilderHelpers.ResolveWorldInputs(mappingDef, snapshot);
        if (world == null || world.Length == 0) return;

        map[$"toPixel{label}"] = CoordinateMapping.WorldToPixelFromCornersList(
            world, w, h, corners.TL, corners.TR, corners.BL, corners.BR, true);
    }

    private static void ApplyFromPixel(
        string label,
        Dictionary<string, object> map,
        JToken mappingDef,
        Device device,
        (Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR) corners)
    {
        if (mappingDef == null || device == null) return;

        var (w, h) = GetResolution(device);
        if (w <= 0 || h <= 0 || !HasCorners(corners)) return;

        var pix = RuleBuilderHelpers.ResolvePixelInputs(mappingDef);
        if (pix == null || pix.Length == 0) return;

        map[$"fromPixel{label}"] = CoordinateMapping.PixelToWorldFromCornersList(
            pix, w, h, corners.TL, corners.TR, corners.BL, corners.BR, true);
    }

    private static bool HasCorners((Vector3 TL, Vector3 TR, Vector3 BL, Vector3 BR) c)
    {
        return !(c.TL == default && c.TR == default && c.BL == default && c.BR == default);
    }

    private static (int w, int h) GetResolution(Device d)
    {
        return (d?.displaySize.widthPixels ?? 0, d?.displaySize.heightPixels ?? 0);
    }




}
