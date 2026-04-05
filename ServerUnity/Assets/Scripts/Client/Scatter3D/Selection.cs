using System;
using System.Collections.Generic;
using UnityEngine;


public class Scatter3DSelection : MonoBehaviour
{
    [Header("Selection / Fade")]
    public List<string> selectedCountries = new() { "India", "China", "United States" };
    [Range(0f, 1f)] public float fadeOthersAlpha = 0.25f;
    public bool fadeWhenAnySelected = true;

    // O(1) lookup set — rebuilt whenever the list changes
    readonly HashSet<string> _selectedSet = new(StringComparer.OrdinalIgnoreCase);

    public event Action OnSelectionChanged;

    void OnEnable() => RebuildSet();

    public bool AnySelected => _selectedSet.Count > 0;
    public bool IsSelected(string country) => _selectedSet.Contains(country);

    public void SetFade(float a) { fadeOthersAlpha = Mathf.Clamp01(a); OnSelectionChanged?.Invoke(); }
    public void SetSelected(IEnumerable<string> names)
    {
        selectedCountries = names != null ? new List<string>(names) : new List<string>();
        RebuildSet();
        OnSelectionChanged?.Invoke();
    }
    public void Toggle(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (!selectedCountries.Remove(name)) selectedCountries.Add(name);
        RebuildSet();
        OnSelectionChanged?.Invoke();
    }
    public void Clear() { selectedCountries.Clear(); _selectedSet.Clear(); OnSelectionChanged?.Invoke(); }

    void RebuildSet()
    {
        _selectedSet.Clear();
        if (selectedCountries == null) return;
        for (int i = 0; i < selectedCountries.Count; i++)
            _selectedSet.Add(selectedCountries[i]);
    }
}
