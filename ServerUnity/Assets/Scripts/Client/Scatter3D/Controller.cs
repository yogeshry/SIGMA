
// ===============================
// FILE: Scatter3DController.cs
// The orchestrator: wires up components and exposes a compact API
// ===============================
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using static Scatter3DAxes;

using System;




#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(Scatter3DDataLoader))]
[RequireComponent(typeof(Scatter3DSelection))]
[RequireComponent(typeof(Scatter3DPoints))]
[RequireComponent(typeof(Scatter3DTrails))]
public class Scatter3DController : MonoBehaviour
{
    [Header("Coloring")]
    public ScatterColorKey colorBy = ScatterColorKey.Cluster;
    public ScatterPalette palette = ScatterPalette.Tableau10;
    [Header("Population Scale")]
    public bool populationLog10 = true;

    [Header("Year")]
    public int year = 1998;

    // Axis frame
    [Header("Axes frame (world)")]
    public Transform origin; public Vector3 axisX = Vector3.right, axisZ = Vector3.forward, axisY = Vector3.up;
    public float xLen = 0.2f, zLen = 0.3f, yLen = 0.15f;

    Scatter3DDataLoader _data;
    Scatter3DSelection _sel;
    Scatter3DPoints _points;
    Scatter3DTrails _trails;
    Scatter3DAxes _axes;

#if UNITY_EDITOR
    bool _rebuildQueued;
    void QueueSafeRebuild()
    {
        if (_rebuildQueued) return; _rebuildQueued = true;
        //EditorApplication.delayCall += () => { _rebuildQueued = false; if (this) SafeRebuild(); };
    }
#endif

    void OnEnable()
    {
        EnsureComponents();
        Wire();
        SafeRebuild();
    }

    void OnDisable()
    {
        if (_data != null) _data.OnDataChanged -= SafeRebuild;
        if (_sel != null) _sel.OnSelectionChanged -= Redraw;
    }

    void EnsureComponents()
    {
        _data = GetComponent<Scatter3DDataLoader>() ?? gameObject.AddComponent<Scatter3DDataLoader>();
        _sel = GetComponent<Scatter3DSelection>() ?? gameObject.AddComponent<Scatter3DSelection>();
        _points = GetComponent<Scatter3DPoints>() ?? gameObject.AddComponent<Scatter3DPoints>();
        _trails = GetComponent<Scatter3DTrails>() ?? gameObject.AddComponent<Scatter3DTrails>();
        _axes = GetComponent<Scatter3DAxes>(); if (!_axes) _axes = gameObject.AddComponent<Scatter3DAxes>();
    }

    void Wire()
    {
        _data.OnDataChanged -= SafeRebuild; _data.OnDataChanged += SafeRebuild;
        // Selection changes only need a Redraw (not a full axis rebuild)
        _sel.OnSelectionChanged -= Redraw; _sel.OnSelectionChanged += Redraw;
        _trails.Map = _points.Map;
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        if (!gameObject.scene.IsValid()) return;
        QueueSafeRebuild();
#endif
    }

    void SafeRebuild()
    {
        if (!this || !gameObject || !gameObject.scene.IsValid()) return;
        RecolorData();
        BuildAxes(); // calls ApplyAxisConfig internally
        Redraw();
    }

    void ApplyAxisConfig()
    {
        if (!origin) origin = transform;
        // Push axis config to renderers
        _axes.SetAxisLengths(xLen, zLen, yLen);
        _axes.SetAxisDomains(_data.xDomain, _data.zDomain, _data.yDomain); 
        _axes.SetAxisLabels(_data.xField, _data.zField, _data.yField);
        _points.populationLog10 = populationLog10;
    }



    void RecolorData()
    {
        var list = _data.data; if (list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            string key = colorBy switch
            {
                ScatterColorKey.Cluster => list[i].cluster, // cluster/continent not carried: use country fallback or extend loader
                ScatterColorKey.Continent => list[i].cluster,
                _ => list[i].country
            };
            list[i].color = Scatter3DColor.ColorForKey(key, palette);
        }
    }

    void BuildAxes()
    {
        ApplyAxisConfig();
        _axes.RebuildAxes(populationLog10);
    }

    void Redraw()
    {
        _points.Redraw(_data.data, year);
        _trails.UpdateTrails(_data.data, year);
    }
    // ---- Enable/Disable (component logic on/off) ----

    public void SetChartEnabled(bool enabledState)
    {
        if (_axes) _axes.SetEnabled(enabledState);
        if (_points) _points.SetEnabled(enabledState);
        if (_trails) _trails.SetEnabled(enabledState);
        if (enabledState) Redraw(); // optional: refresh when turning back on
    }

    // ---- Show/Hide (rendering only) ----

    public void SetChartVisible(bool visible)
    {
        if (_axes) _axes.SetVisible(visible);
        if (_points) _points.SetVisible(visible);
        if (_trails) _trails.SetVisible(visible);
    }



    // ------- Public API (runtime) -------
    public void SetAxisLengths(float x, float z, float y, bool rebuildAxes = true, bool redraw = true)
    { xLen = x; zLen = z; yLen = y; if (rebuildAxes) BuildAxes(); if (redraw) Redraw(); }
    public void SetFade(float alpha, bool redraw = true) { _sel.SetFade(alpha); if (redraw) Redraw(); }
    public void SetYear(int newYear, bool redraw = true) { year = newYear; if (redraw) Redraw(); }
    public void SetSelectedCountries(IEnumerable<string> names, bool redraw = true) { _sel.SetSelected(names); if (redraw) Redraw(); }
    public void AddSelected(string name, bool redraw = true) { _sel.Toggle(name); if (redraw) Redraw(); }
    public void ClearSelected(bool redraw = true) { _sel.Clear(); if (redraw) Redraw(); }
    public void SetOrigin(Transform newOrigin, bool rebuildAxes = true, bool redraw = true)
    {
        if (!_axes) return;
        _axes.SetOrigin(newOrigin);
        if (rebuildAxes) _axes.RebuildAxes(_points ? _points.populationLog10 : true);
        if (redraw) Redraw();
    }
    public void EnableChart() => SetChartEnabled(true);
    public void DisableChart() => SetChartEnabled(false);

    public void ShowChart() => SetChartVisible(true);
    public void HideChart() => SetChartVisible(false);

    /// <summary>Set a single axis: axis = "x" | "y" | "z" | "size"; scale = "linear" | "log" </summary>
    //public void SetAxisConfig(string axis, string field, string scale = "linear", bool rebuild = true, bool redraw = true)
    //{
    //    _data.SetAxisConfig(axis, field, scale, recalcDomains: true);
    //    if (rebuild) BuildAxes();
    //    if (redraw) Redraw();
    //}

    public void SetAxisConfigJson(AxisConfigDef json, bool rebuild = true, bool redraw = true)
    {
        _data.SetAxisConfig(json);

        _axes.ApplyAxisConfig(json);
        //if (rebuild) BuildAxes();
        if (redraw) Redraw();
    }


}