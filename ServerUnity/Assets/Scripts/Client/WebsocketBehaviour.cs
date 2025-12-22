using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using WebSocketSharp;
using WebSocketSharp.Server;
using static Scatter3DAxes;

/// <summary>
/// WebSocket behavior for live-updating the Scatter3DGapminder chart.
/// Mount on a WebSocketServer as: wssv.AddWebSocketService<ScatterUpdateBehavior>("/scatter");
/// </summary>
public class ScatterUpdateBehavior : WebSocketBehavior
{
    private static bool VerboseLogs = true;

    // -------- Message DTOs --------
    [Serializable]
    private class ScatterMsg
    {
        public string commandType;             // e.g., "scatter:setYear", "scatter:setFade", ...
        public int year;
        public float fade;
        public float xLen, yLen, zLen;
        public List<string> selected;          // complete selection set
        public string? add;                 // single add/remove
        public bool? showTrails;
        public float? trailWidth, trailAlpha, trailNodeScale;
        public int? trailNodeEveryNYears;
        public bool? clear;                    // used by :setSelected (clear then set)
        public bool? hide;                    // used by :setSelected (clear then set)
        public AxisConfigDef axisConfig; // generic config map
    }

    //[Serializable]
    //public class axisConfigMsg
    //{
    //    public string axis;            // "x", "y", "z"
    //    public string field;           // data field name
    //    public string scaleType;       // "linear", "log", etc.

    //    public float[] domain;             // optional [lo, hi]
    //    public float axisLength = 0; // optional, overrides xLen/yLen/zLen if >0
    //    public int tickCount = 0;    // informational
    //    public List<AxisTickDef> ticks;
    //}

    //// ===== JSON axis-config support =====
    //[Serializable]
    //public class AxisTickDef { public float value; public string label; public float t; }


    [Serializable]
    private class ScatterAck
    {
        public string type = "scatterAck";
        public string deviceSessionId;
        public string command;
        public string status;
        public string error;
        public object state;                   // snapshot after command
        public string timestamp;
    }

    // -------- Chart access --------
    private static Scatter3DController _chartCache;
    private static Scatter3DController Chart
    {
        get
        {
            if (_chartCache == null)
                _chartCache = GameObject.FindObjectOfType<Scatter3DController>();
            return _chartCache;
        }
    }

    // -------- Lifecycle --------
    protected override void OnOpen()
    {
        if (VerboseLogs) Debug.Log($"[WS][scatter] Client connected: {ID}");
    }

    protected override void OnClose(CloseEventArgs e)
    {
        if (VerboseLogs) Debug.Log($"[WS][scatter] Client disconnected: {ID}");
    }

    protected override void OnMessage(MessageEventArgs e)
    {
        if (VerboseLogs) Debug.Log($"[WS][scatter] {ID} → {e.Data}");

        ScatterMsg msg = null;
        try { msg = JsonConvert.DeserializeObject<ScatterMsg>(e.Data); }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WS][scatter] Invalid message format from {ID}: {ex.Message}");
            return;
        }

        if (msg == null || string.IsNullOrEmpty(msg.commandType))
        {
            Debug.LogWarning($"[WS][scatter] Missing commandType in message from {ID}");
            return;
        }

        Debug.Log($"[WS][scatter] Enqueuing command {msg.commandType} from {ID}");

        // All Unity object changes must run on main thread.
        MainThreadDispatcher.Enqueue(() =>
        {
            try
            {
                var ok = Handle(msg);
            }
            catch (Exception ex)
            {
                Debug.Log(ex);
            }
        });
    }

    // -------- Command handling --------
    private bool Handle(ScatterMsg m)
    {
        var chart = Chart;
        Debug.Log($"[WS][scatter] Handling command: {Chart}");
        if (!chart) return false;

        switch (m.commandType)
        {
            case "scatter:getState":
                // just reply; no mutation
                break;

            case "scatter:setYear":
                int year = (m.year != 0 ? m.year : chart.year);
                chart.SetYear(year);
                Debug.Log($"[WS][scatter] Set year to {year}");
                break;

            case "scatter:setFade":
                float fadeOthersAlpha = Mathf.Clamp01(m.fade);
                chart.SetFade(fadeOthersAlpha);
                break;

            case "scatter:setAxis":


                if (m.axisConfig != null && m.axisConfig.axis != null)
                {
                    chart.SetAxisConfigJson(m.axisConfig);

                }
                else
                {
                    if (m.xLen > 0f) chart.xLen = m.xLen;
                    if (m.zLen > 0f) chart.zLen = m.zLen;
                    if (m.yLen > 0f) chart.yLen = m.yLen;
                    chart.SetAxisLengths(chart.xLen, chart.zLen, chart.yLen);
                }
                break;            

            case "scatter:setSelected":
                if (m.clear == true) chart.ClearSelected();
                
                else if (m.selected != null)
                {
                    // replace entire selection with unique set
                    //chart.SelectOnly(m.selected);
                    chart.SetSelectedCountries(m.selected);
                }
                else if (m.add != null)
                {
                   chart.AddSelected(m.add);

                }
                break;
            case "scatter:setVisible":
                if (m.hide == true)
                    chart.SetChartVisible(false);
                else
                    chart.SetChartVisible(true);
                break;


            case "scatter:echo":
                // no-op, just ack with state
                break;

            default:
                Debug.LogWarning($"[WS][scatter] Unknown command: {m.commandType}");
                break;
        }



        return true;
    }

    // -------- Public toggles --------
    public static void SetVerboseLogs(bool active)
    {
        VerboseLogs = active;
        Debug.Log($"[WS][scatter] Logger {(active ? "enabled" : "disabled")}");
    }
}
