// ===============================
// FILE: Scatter3DSelection.cs
// Holds selected countries + fading config
// ===============================
using System;
using System.Collections.Generic;
using UnityEngine;


public class Scatter3DSelection : MonoBehaviour
{
    [Header("Selection / Fade")]
    public List<string> selectedCountries = new() { "India", "China", "United States" };
    [Range(0f, 1f)] public float fadeOthersAlpha = 0.25f;
    public bool fadeWhenAnySelected = true;


    public event Action OnSelectionChanged;


    public bool AnySelected => selectedCountries != null && selectedCountries.Count > 0;
    public bool IsSelected(string country) => selectedCountries != null && selectedCountries.Contains(country);


    public void SetFade(float a) { fadeOthersAlpha = Mathf.Clamp01(a); OnSelectionChanged?.Invoke(); }
    public void SetSelected(IEnumerable<string> names)
    {
        selectedCountries = names != null ? new List<string>(new HashSet<string>(names)) : new List<string>();
        OnSelectionChanged?.Invoke();
    }
    public void Toggle(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!selectedCountries.Remove(name)) selectedCountries.Add(name);
        OnSelectionChanged?.Invoke();
    }
    public void Clear() { selectedCountries.Clear(); OnSelectionChanged?.Invoke(); }
}