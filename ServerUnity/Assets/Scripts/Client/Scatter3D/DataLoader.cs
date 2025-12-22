using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using static Scatter3DAxes;

public class Scatter3DDataLoader : MonoBehaviour
{
    // === Public config (current mapping + scale) ===
    public string xField = "fertility_rate", yField = "gdp_per_capita", zField = "life_expectancy", sizeField = "population";
    public ScaleType xScale = ScaleType.Linear, yScale = ScaleType.Linear, zScale = ScaleType.Linear, sizeScale = ScaleType.Sqrt;

    [Header("Domains (post-transform)")]
    public Vector2 xDomain = new(0, 1), yDomain = new(0, 1), zDomain = new(0, 1), sizeDomain = new(1, 1);
    public Vector2 populationDomain = new(1e5f, 1.4e9f); // legacy name kept for size to avoid breaking other code

    public bool inferDomainsFromData = true;
    public List<ScatterPoint> data = new();
    public event Action OnDataChanged;

    [Header("CSV Source")]
    public TextAsset csvFile;              // drag & drop in Inspector
    public bool autoLoadOnPlay = true;

    // === Raw row (all numeric columns captured) ===
    class RawRow
    {
        public string country, cluster;
        public int year;
        public Dictionary<string, float> num = new(StringComparer.OrdinalIgnoreCase);
    }

    readonly List<RawRow> _raw = new();


    void Start()
    {
        if (autoLoadOnPlay)
        {
            if (csvFile != null) LoadFromCsvText(csvFile.text);
        }
    }

    public enum ScaleType { Linear, Log10, Sqrt }

    // ---------- CSV load (keep your existing source; this is a helper) ----------
    public void LoadFromCsvText(string csvText)
    {
        _raw.Clear(); data.Clear();
        if (string.IsNullOrWhiteSpace(csvText)) { Debug.LogWarning("[Scatter3D] Empty CSV"); return; }

        var lines = csvText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return;

        // headers
        var headers = lines[0].Split(',');
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++) idx[headers[i].Trim()] = i;

        float ParseF(string[] cols, string name)
        {
            if (!idx.TryGetValue(name, out var k) || k >= cols.Length) return float.NaN;
            var s = cols[k].Trim();
            return float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v) ? v : float.NaN;
        }
        string ParseS(string[] cols, string name)
        {
            if (!idx.TryGetValue(name, out var k) || k >= cols.Length) return "";
            return cols[k].Trim();
        }

        for (int r = 1; r < lines.Length; r++)
        {
            var row = lines[r].Trim(); if (string.IsNullOrEmpty(row)) continue;
            var cols = SplitCsv(row);   // instead of row.Split(',')

            var rr = new RawRow
            {
                country = ParseS(cols, "country"),
                cluster = ParseS(cols, "continent"),
            };
            int.TryParse(ParseS(cols, "year"), out rr.year);

            // capture ALL numeric columns for remapping later
            for (int i = 0; i < headers.Length; i++)
            {
                var h = headers[i].Trim();
                if (string.Equals(h, "country", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(h, "continent", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(h, "region", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(h, "year", StringComparison.OrdinalIgnoreCase)) continue;

                var s = i < cols.Length ? cols[i].Trim() : "";
                if (float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v))
                    rr.num[h] = v;
            }
            // common aliases
            if (!rr.num.ContainsKey("population") && rr.num.TryGetValue("pop", out var pop)) rr.num["population"] = pop;
            if (!rr.num.ContainsKey("life_expect") && rr.num.TryGetValue("life_exp", out var le)) rr.num["life_expect"] = le;

            _raw.Add(rr);
        }

        // build first time with current mapping
        RebuildDataFromMapping(recalcDomains: true);
        OnDataChanged?.Invoke();
    }

    // ---------- Rebuild (mapping + transforms + domains + size) ----------
    public void ConfigureAxes(Dictionary<string, (string field, string scale)> cfg, bool recalcDomains = true)
    {
        if (cfg != null)
        {
            if (cfg.TryGetValue("x", out var X)) { xField = X.field ?? xField; xScale = ParseScale(X.scale, xScale); }
            if (cfg.TryGetValue("y", out var Y)) { yField = Y.field ?? yField; yScale = ParseScale(Y.scale, yScale); }
            if (cfg.TryGetValue("z", out var Z)) { zField = Z.field ?? zField; zScale = ParseScale(Z.scale, zScale); }
            if (cfg.TryGetValue("size", out var S)) { sizeField = S.field ?? sizeField; sizeScale = ParseScale(S.scale, sizeScale); }
        }
        RebuildDataFromMapping(recalcDomains);
        OnDataChanged?.Invoke();
    }

    public void SetAxisConfig(AxisConfigDef cfg, bool recalcDomains = false)
    {
        
        cfg.axis = (cfg.axis ?? "x").ToLowerInvariant();
        var t = ParseScale(cfg.scaleType, ScaleType.Linear);
        switch (cfg.axis)
        {
            case "x":
                xField = cfg.field ?? xField;
                xScale = t;
                xDomain = cfg.domain != null && cfg.domain.Length == 2
                    ? new Vector2(cfg.domain[0], cfg.domain[1])
                    : xDomain;
                break;
            case "y":
                yField = cfg.field ?? yField;
                yScale = t;
                yDomain = cfg.domain != null && cfg.domain.Length == 2
                    ? new Vector2(cfg.domain[0], cfg.domain[1])
                    : yDomain;
                break;
            case "z":
                zField = cfg.field ?? zField;
                zScale = t;
                zDomain = cfg.domain != null && cfg.domain.Length == 2
                    ? new Vector2(cfg.domain[0], cfg.domain[1])
                    : zDomain;
                break;
            case "size":
                sizeField = cfg.field ?? sizeField;
                sizeScale = t;
                sizeDomain = cfg.domain != null && cfg.domain.Length == 2
                    ? new Vector2(cfg.domain[0], cfg.domain[1])
                    : sizeDomain;
                break;
            default: xField = cfg.field ?? xField; xScale = t; break;
        }
        RebuildDataFromMapping(recalcDomains);
        OnDataChanged?.Invoke();
    }

    ScaleType ParseScale(string s, ScaleType def)
    {
        if (string.IsNullOrEmpty(s)) return def;
        var k = s.Trim().ToLowerInvariant();
        if (k is "log" or "log10") return ScaleType.Log10;
        return ScaleType.Linear;
    }

    static float Transform(float v, ScaleType s)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) return float.NaN;
        if (s == ScaleType.Sqrt) return Mathf.Sqrt(v); // epsilon floor
        return v;
    }

    void RebuildDataFromMapping(bool recalcDomains)
    {
        data.Clear();
        if (_raw.Count == 0) return;

        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        float minS = float.PositiveInfinity, maxS = float.NegativeInfinity;

        foreach (var rr in _raw)
        {
            rr.num.TryGetValue(xField, out var vx);
            rr.num.TryGetValue(yField, out var vy);
            rr.num.TryGetValue(zField, out var vz);
            rr.num.TryGetValue(sizeField, out var vs);

            //if (true) {
            //    vx = Transform(vx, xScale);
            //    vy = Transform(vy, yScale);
            //    vz = Transform(vz, zScale);
            //    vs = Transform(vs, sizeScale);
            //}

            var ok = !(float.IsNaN(vx) || float.IsNaN(vy) || float.IsNaN(vz) || float.IsNaN(vs));
            if (!ok) continue;

            if (vx < minX) minX = vx; if (vx > maxX) maxX = vx;
            if (vy < minY) minY = vy; if (vy > maxY) maxY = vy;
            if (vz < minZ) minZ = vz; if (vz > maxZ) maxZ = vz;
            if (vs < minS) minS = vs; if (vs > maxS) maxS = vs;

            data.Add(new ScatterPoint
            {
                country = rr.country,
                cluster = rr.cluster,
                year = rr.year,
                x = vx,
                y = vy,
                z = vz,
                population = vs, // carry raw-for-size; Map/Points can treat as generic "size"
                color = Color.white,
                size = 0f // filled after domains
            });
        }

        if (recalcDomains && data.Count > 0)
        {

            // With this corrected line:
            xDomain = Scatter3DUtil.DomainFromMinMax(xScale, minX, maxX);
            yDomain = Scatter3DUtil.DomainFromMinMax(yScale, minY, maxY);
            zDomain = Scatter3DUtil.DomainFromMinMax(zScale, minZ, maxZ);
            sizeDomain = Scatter3DUtil.DomainFromMinMax(ScaleType.Sqrt, minS, maxS);

            populationDomain = sizeDomain; // keep legacy pipe
        }

        // Fill visual size from sizeDomain
        // p.size is the radius (already mapped from [d0, d1] via sqrt scaling)

        // Precompute sqrt-domain for sqrt scale
        float sqrtMin = Mathf.Sqrt(sizeDomain.x);
        float sqrtMax = Mathf.Sqrt(sizeDomain.y);

        // Guard against bad / degenerate domain
        if (!float.IsFinite(sqrtMin) || !float.IsFinite(sqrtMax) || Mathf.Approximately(sqrtMin, sqrtMax))
        {
            sqrtMin = 0f;
            sqrtMax = 1f;
        }

        // Radius range in world units (tweak to taste)
         float rMin = Mathf.Max(0.002f, 0.11f / 200f);
         float rMax = Mathf.Max(0.015f, 0.11f / 18f);

        for (int i = 0; i < data.Count; i++)
        {
            float v = data[i].population;

            // D3-style: ignore non-positive / non-finite => radius 0
            if (!float.IsFinite(v) || v <= 0f)
            {
                data[i].size = 0f;   // radius
                continue;
            }

            float t = Scatter3DUtil.SafeInvLerp(sqrtMin, sqrtMax, (float)Math.Sqrt(v));  // 0..1
            data[i].size = Mathf.Lerp(rMin, rMax, t);             // radius
        }

    }

    //// Add inside Scatter3DDataLoader (class scope)
    //public static int ContinentToCode(string continent)
    //{
    //    if (string.IsNullOrWhiteSpace(continent)) return -1;
    //    var c = continent.Trim().ToLowerInvariant();

    //    // normalize a few common variants
    //    if (c is "eu" or "european") c = "europe";
    //    if (c is "na" or "north america" or "south america" or "latam" or "america")
    //        c = "americas";
    //    if (c is "oceania" or "australasia") c = "australia";

    //    // mapping requested by you:
    //    // europe -> 0, americas -> 1, australia(oceania) -> 2, africa -> 3, asia -> 4
    //    return c switch
    //    {
    //        "europe" => 0,
    //        "americas" => 1,
    //        "australia" => 2,    // treat Oceania/Australasia as "australia"
    //        "africa" => 3,
    //        "asia" => 4,
    //        _ => -1    // unknown / other
    //    };
    //}



    static string[] SplitCsv(string line)
    {
        var res = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQ = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQ && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else inQ = !inQ;
            }
            else if (c == ',' && !inQ)
            {
                res.Add(cur.ToString()); cur.Length = 0;
            }
            else cur.Append(c);
        }
        res.Add(cur.ToString());
        return res.ToArray();
    }

}
