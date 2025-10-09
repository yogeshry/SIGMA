using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

/// <summary>  
/// Attach to any GameObject in your scene.  
/// Call RegisterRulesFromJson at runtime to load your declarative spatial rules.  
/// </summary>  
public class SpatialObserver : MonoBehaviour
{
    //[TextArea(5, 10)]
    //public string jsonRuleSpecs;      // paste your JSON array of RuleSpec here  

    private ObservableManager _manager;
    public static SpatialObserver Instance { get; private set; }


    void Awake()
    {
        // Enforce singleton
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        //_manager = new ObservableManager();
    }

    void Start()
    {
        
        //RegisterRulesFromJson();
    }

    /// <summary>  
    /// Parse the JSON in the inspector and register each rule.  
    /// </summary>  
    public void RegisterRulesFromJson()
    {
        _manager = new ObservableManager();
        Debug.Log("Registering rules from JSON...");
        //if (string.IsNullOrWhiteSpace(jsonRuleSpecs)) return;
        try
        {
            Debug.Log("Loading RuleSpecs from JSON...");
            var specsTextAsset = Resources.Load<TextAsset>("JSON/SpatialObservableExample");
            Debug.Log($"Loaded TextAsset: {specsTextAsset?.name}");
            if (specsTextAsset == null)
            {
                Debug.LogError("Failed to load TextAsset from Resources.");
                return;
            }

            var specs = JsonConvert.DeserializeObject<List<RuleSpec>>(specsTextAsset.text);
            Debug.Log($"Deserialized {specs?.Count} RuleSpecs from JSON.");
            if (specs == null)
            {
                Debug.LogError("Failed to deserialize JSON into RuleSpec list.");
                return;
            }

            foreach (var spec in specs)
            {
                Debug.Log($"Registering RuleSpec: {spec.id}");
                _manager.RegisterRule(spec);
            }

        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to parse RuleSpecs JSON: {e}");
        }
    }
}
