// ===============================
// FILE: SpatialObservablePoseApplier.cs
// Parses a "spatial_observable" message and updates chart origin pose
// ===============================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class Scatter3DPoseUpdate : MonoBehaviour
{
    [SerializeField] private Scatter3DController controller;
    [Tooltip("Log pose + parsed vectors")]
    public bool verboseLogs = true;

    static readonly Regex CornersRegex = new(@"\(\s*([-+]?[\d\.eE\-]+)\s*,\s*([-+]?[\d\.eE\-]+)\s*,\s*([-+]?[\d\.eE\-]+)\s*\)", RegexOptions.Compiled);

    // --- Jitter guard thresholds ---
    // Apply rotation only if it changes more than this many degrees
    private const float ROT_THRESHOLD_DEG = 5f;      // change to 10f if you want it stricter
    // Apply position only if it moves more than this many meters
    private const float POS_THRESHOLD_M = 0.015f;     // 2cm

    void Reset() { controller = FindObjectOfType<Scatter3DController>(); }

    /// <summary>
    /// Read mapping.fromPixelA[0] (position) and A.corners (rotation) to set chart origin.
    /// Returns true if a pose was applied.
    /// </summary>
    public bool TryApplyFromSpatial(JObject root)
    {
        if (controller == null) { Debug.LogWarning("[SpatialPose] No Scatter3DController set."); return false; }

        // streams may be embedded as a string or as an object
        var streamsTok = root?["streams"];
        if (streamsTok == null) return false;

        JObject streamsObj = null;
        if (streamsTok.Type == JTokenType.String)
        {
            try { streamsObj = JObject.Parse((string)streamsTok); }
            catch { Debug.LogWarning("[SpatialPose] streams string not valid JSON."); return false; }
        }
        else if (streamsTok.Type == JTokenType.Object)
        {
            streamsObj = (JObject)streamsTok;
        }
        if (streamsObj == null) return false;

        // --- Position from mapping.fromPixelA[0] ---
        var posOk = TryParseFromPixelA(streamsObj["mapping"], out var pos);

        // --- Rotation from A.corners (need >=3 points) ---
        var corners = TryParseCorners(streamsObj["A.corners"]);
        var rotOk = TryMakeRotationFromCorners(corners, out var rot);

        if (!posOk && !rotOk) return false; // nothing to do

        var t = controller.transform;

        // --- Jitter guard: only apply if change exceeds thresholds ---
        bool applyPos = false;
        bool applyRot = false;

        if (posOk)
        {
            float dist = Vector3.Distance(t.position, pos);
            applyPos = dist > POS_THRESHOLD_M;
        }

        if (rotOk)
        {
            float ang = Quaternion.Angle(t.rotation, rot);
            applyRot = ang > ROT_THRESHOLD_DEG;
        }

        // If neither change is significant, do nothing
        if (!applyPos && !applyRot) return false;

        if (applyRot) t.rotation = rot;
        if (applyPos) t.position = pos;

        controller.SetOrigin(t);

        if (verboseLogs)
        {
            var eul = t.rotation.eulerAngles;
            Debug.Log($"[SpatialPose] Applied origin pose. pos={t.position} rot={eul} " +
                      $"(right={t.rotation * Vector3.right}, up={t.rotation * Vector3.up}, fwd={t.rotation * Vector3.forward})");
        }

        return true;
    }

    // ----- helpers -----

    static bool TryParseFromPixelA(JToken mappingTok, out Vector3 pos)
    {
        pos = default;
        var mapping = mappingTok as JObject;
        var fpa = mapping?["fromPixelA"] as JArray;
        if (fpa == null || fpa.Count == 0) return false;

        var first = fpa[0];
        float x = (float?)first["x"] ?? 0f;
        float y = (float?)first["y"] ?? 0f;
        float z = (float?)first["z"] ?? 0f;
        pos = new Vector3(x, y, z);
        return true;
    }

    static List<Vector3> TryParseCorners(JToken token)
    {
        if (token == null) return null;

        // Case 1: JSON array: [[x,y,z], ...] or [{x:..,y:..,z:..}, ...]
        if (token.Type == JTokenType.Array)
        {
            var arr = (JArray)token;
            var list = new List<Vector3>(arr.Count);
            foreach (var t in arr)
            {
                if (t.Type == JTokenType.Array && t.Count() >= 3)
                    list.Add(new Vector3((float)t[0], (float)t[1], (float)t[2]));
                else if (t.Type == JTokenType.Object)
                    list.Add(new Vector3(
                        (float?)t["x"] ?? 0f,
                        (float?)t["y"] ?? 0f,
                        (float?)t["z"] ?? 0f));
            }
            return list;
        }

        // Case 2: string like "[(-0.318,-0.023,0.074), ...]"
        if (token.Type == JTokenType.String)
        {
            var s = (string)token;
            var m = CornersRegex.Matches(s);
            var list = new List<Vector3>(m.Count);
            foreach (Match mm in m)
            {
                if (!mm.Success || mm.Groups.Count < 4) continue;
                float x = float.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture);
                float y = float.Parse(mm.Groups[2].Value, CultureInfo.InvariantCulture);
                float z = float.Parse(mm.Groups[3].Value, CultureInfo.InvariantCulture);
                list.Add(new Vector3(x, y, z));
            }
            return list;
        }

        return null;
    }

    static bool TryMakeRotationFromCorners(List<Vector3> corners, out Quaternion rot)
    {
        rot = Quaternion.identity;
        if (corners == null || corners.Count < 3) return false;

        var p0 = corners[0];
        var p1 = corners[1];
        var p2 = corners[2];

        // right along edge (p0->p1), fallback to p0->p3 if needed
        var right = (p1 - p0);
        if (right.sqrMagnitude < 1e-8f && corners.Count > 3)
            right = (corners[3] - p0);
        if (right.sqrMagnitude < 1e-8f) right = Vector3.right;
        right.Normalize();

        // normal (up-ish) from cross of the two edges in the plane
        var up = Vector3.Cross((p2 - p0), right);
        if (up.sqrMagnitude < 1e-8f) up = Vector3.up;
        up.Normalize();
        // prefer pointing roughly world-up to avoid flips
        if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;

        var fwd = Vector3.Cross(up, right);
        if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.forward;
        fwd.Normalize();

        rot = Quaternion.LookRotation(fwd, up); // Z=fwd, Y=up, X=right
        return true;
    }
}
