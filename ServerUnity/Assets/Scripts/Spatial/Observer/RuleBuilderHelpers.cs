using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class RuleBuilderHelpers
{
    public static JToken GetMappingToken(object publishObj)
    {
        if (publishObj == null) return null;
        try
        {
            var jt = publishObj as JToken ?? JToken.FromObject(publishObj);
            return jt["mapping"];
        }
        catch { return null; }
    }

    public static Vector2? ReadRange(JToken tok)
    {
        if (tok == null || tok.Type != JTokenType.Array) return null;
        var arr = (JArray)tok;
        if (arr.Count < 2) return null;
        if (TryReadNumber(arr[0], out float a) && TryReadNumber(arr[1], out float b))
            return new Vector2(a, b);
        return null;
    }

    public static bool TryReadNumber(JToken tok, out float v)
    {
        v = 0f;
        if (tok == null) return false;
        if (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float)
        {
            v = tok.Value<float>();
            return true;
        }
        if (float.TryParse(tok.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out v))
            return true;
        return false;
    }

    public static bool TryReadNumber(object obj, out float v)
    {
        v = 0f;
        switch (obj)
        {
            case null: return false;
            case float f: v = f; return true;
            case double d: v = (float)d; return true;
            case int i: v = i; return true;
            case long l: v = l; return true;
            case JToken jt: return TryReadNumber(jt, out v);
            default:
                return float.TryParse(obj.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v);
        }
    }

    // --------------------------------------------------------
    // WORLD INPUTS
    // --------------------------------------------------------
    public static Vector3[] ResolveWorldInputs(JToken tok, Dictionary<string, object> snap)
    {
        var result = new List<Vector3>();

        if (tok.Type == JTokenType.Array)
        {
            foreach (var node in (JArray)tok)
            {
                if (node is JArray arr && arr.Count >= 3 &&
                    TryReadNumber(arr[0], out float x) &&
                    TryReadNumber(arr[1], out float y) &&
                    TryReadNumber(arr[2], out float z))
                {
                    result.Add(new Vector3(x, y, z));
                }
            }
            return result.ToArray();
        }

        if (tok.Type == JTokenType.String)
        {
            var path = tok.Value<string>();
            if (snap.TryGetValue(path, out var val))
            {
                if (val is Vector3 v3) { result.Add(v3); return result.ToArray(); }

                if (val is IEnumerable<object> en)
                {
                    foreach (var item in en)
                    {
                        if (item is Vector3 vv) result.Add(vv);
                        else if (item is JArray arr && arr.Count >= 3 &&
                                 TryReadNumber(arr[0], out float x) &&
                                 TryReadNumber(arr[1], out float y) &&
                                 TryReadNumber(arr[2], out float z))
                            result.Add(new Vector3(x, y, z));
                    }
                    return result.ToArray();
                }

                if (val is JArray ja)
                {
                    foreach (var arr in ja.OfType<JArray>())
                    {
                        if (arr.Count >= 3 &&
                            TryReadNumber(arr[0], out float x) &&
                            TryReadNumber(arr[1], out float y) &&
                            TryReadNumber(arr[2], out float z))
                            result.Add(new Vector3(x, y, z));
                    }
                    return result.ToArray();
                }
            }
        }

        return result.ToArray();
    }

    // --------------------------------------------------------
    // PIXEL INPUTS
    // --------------------------------------------------------
    public static Vector2[] ResolvePixelInputs(JToken tok)
    {
        var result = new List<Vector2>();
        if (tok == null) return result.ToArray();

        switch (tok.Type)
        {
            case JTokenType.Object:
                {
                    var obj = (JObject)tok;
                    var input = obj["input"];
                    if (input != null)
                        return ResolvePixelInputs(input);

                    if (TryReadNumber(obj["x"], out float x) &&
                        TryReadNumber(obj["y"], out float y))
                    {
                        result.Add(new Vector2(x, y));
                    }
                    return result.ToArray();
                }

            case JTokenType.Array:
                {
                    var arr = (JArray)tok;

                    // Case: [x, y]
                    if (arr.Count >= 2 &&
                        TryReadNumber(arr[0], out float x) &&
                        TryReadNumber(arr[1], out float y))
                    {
                        if (arr.Count == 2)
                        {
                            result.Add(new Vector2(x, y));
                            return result.ToArray();
                        }
                    }

                    // Case: [[x,y], [x,y], ...] OR [{x:…,y:…}, …]
                    foreach (var node in arr)
                    {
                        if (node is JArray pair && pair.Count >= 2 &&
                            TryReadNumber(pair[0], out float px) &&
                            TryReadNumber(pair[1], out float py))
                        {
                            result.Add(new Vector2(px, py));
                        }
                        else if (node is JObject pObj &&
                                 TryReadNumber(pObj["x"], out float ox) &&
                                 TryReadNumber(pObj["y"], out float oy))
                        {
                            result.Add(new Vector2(ox, oy));
                        }
                    }
                    return result.ToArray();
                }

            default:
                return result.ToArray();
        }
    }




    private static void AddArrayIfExists(JObject jo, string key, List<Vector3> target)
    {
        if (!jo.TryGetValue(key, out var token)) return;
        if (token is JArray arr)
        {
            foreach (var sub in arr.OfType<JArray>())
                if (sub.Count >= 3 &&
                    TryReadNumber(sub[0], out float x) &&
                    TryReadNumber(sub[1], out float y) &&
                    TryReadNumber(sub[2], out float z))
                    target.Add(new Vector3(x, y, z));
        }
    }

    private static bool TryParseProjectionString(string s, out List<Vector3> projected, out List<Vector3> source)
    {
        projected = new();
        source = new();

        try
        {
            int projStart = s.IndexOf("proj=(");
            if (projStart != -1)
            {
                int projEnd = s.IndexOf(")", projStart);
                if (projEnd > projStart)
                {
                    var v = ParseVector3(s.Substring(projStart + 6, projEnd - (projStart + 6)));
                    projected.Add(v);
                }
            }

            int segStart = s.IndexOf("seg=(");
            if (segStart != -1)
            {
                int arrow = s.IndexOf("→", segStart);
                int segEnd = s.IndexOf("))", segStart);
                if (arrow > segStart && segEnd > arrow)
                {
                    var left = ParseVector3(s.Substring(segStart + 5, arrow - (segStart + 5)));
                    var right = ParseVector3(s.Substring(arrow + 1, segEnd - (arrow + 1)));
                    source.Add(left);
                    source.Add(right);
                }
            }

            return projected.Count > 0 || source.Count > 0;
        }
        catch { return false; }
    }

    private static Vector3 ParseVector3(string s)
    {
        var parts = s.Split(',')
                     .Select(p => p.Trim('(', ')', ' '))
                     .Where(p => p.Length > 0)
                     .ToArray();
        float.TryParse(parts.ElementAtOrDefault(0), out var x);
        float.TryParse(parts.ElementAtOrDefault(1), out var y);
        float.TryParse(parts.ElementAtOrDefault(2), out var z);
        return new Vector3(x, y, z);
    }
}
