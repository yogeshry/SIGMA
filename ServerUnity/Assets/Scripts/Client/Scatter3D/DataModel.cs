// ===============================
// FILE: Scatter3DTypes.cs
// Shared types for the scatter system
// ===============================
using System;
using System.Collections.Generic;
using UnityEngine;


[Serializable]
public class ScatterPoint
{
    public string country;
    public string cluster;
    public int year;
    public float x; // x
    public float y; // z
    public float z; // z
    public float population;
    public Color color = Color.white;
    public float size = 0.02f; // radius (m)
}


public enum ScatterColorKey { Country, Cluster, Continent }
public enum ScatterPalette { Tableau10, HSVHash }