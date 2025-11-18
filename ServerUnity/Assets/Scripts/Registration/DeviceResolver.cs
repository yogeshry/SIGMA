using UnityEngine;
using UnityEngine.Rendering.VirtualTexturing;

public static class DeviceResolver
{
    /// <summary>
    /// Call this once, e.g. from your game’s bootstrapping logic.
    /// Loads schema/config from Resources, validates, and resolves devices.
    /// </summary>
    public static void ResolveDevices(out Device resolvedA, out Device resolvedB)
    {
        // Initialize out parameters to null to ensure they are assigned before method exits
        resolvedA = null;
        resolvedB = null;

        // Load JSON from Resources/JSON/DeviceResolverSchema.json
        var schemaAsset = Resources.Load<TextAsset>("JSON/Device/DeviceResolverSchema");
        // Load JSON from Resources/JSON/BothConstraint.json
        var configAsset = Resources.Load<TextAsset>("JSON/Device/BothConstraint");
        if (schemaAsset == null || configAsset == null)
        {
            Debug.LogError("[DeviceResolver] Missing JSON assets under Resources/JSON/");
            return;
        }

        // Validate against schema
        var result = SchemaValidator.Validate(configAsset.text, schemaAsset.text);
        if (!result.IsValid)
        {
            foreach (var err in result.Errors)
                Debug.LogError($"[DeviceResolver] Schema validation failed: {err.Message} (at {err.Path})");
            return;
        }

        // Resolve device pair
        Device tempResolvedA, tempResolvedB;
        DeviceManager.GetDevicePair(configAsset.text, out tempResolvedA, out tempResolvedB);
        if (tempResolvedA == null || tempResolvedB == null)
        {
            //Debug.LogError("[DeviceResolver] DeviceManager failed to resolve a valid pair.");
            return;
        }

        // Assign resolved devices to out parameters
        resolvedA = tempResolvedA;
        resolvedB = tempResolvedB;

        Debug.Log($"[DeviceResolver] Successfully resolved devices: {resolvedA} & {resolvedB}");
    }

    public static void ResolveDevices(out Device resolvedA, out Device resolvedB, string configAsset)
    {
        // Initialize out parameters to null to ensure they are assigned before method exits
        resolvedA = null;
        resolvedB = null;

        // Load JSON from Resources/JSON/DeviceResolverSchema.json
        var schemaAsset = Resources.Load<TextAsset>("JSON/Device/DeviceResolverSchema");

        if (schemaAsset == null || configAsset == null)
        {
            Debug.LogError("[DeviceResolver] Missing JSON assets under Resources/JSON/");
            return;
        }

        // Validate against schema
        var result = SchemaValidator.Validate(configAsset, schemaAsset.text);
        if (!result.IsValid)
        {
            foreach (var err in result.Errors)
                Debug.LogError($"[DeviceResolver] Schema validation failed: {err.Message} (at {err.Path})");
            return;
        }

        // Resolve device pair
        Device tempResolvedA, tempResolvedB;
        DeviceManager.GetDevicePair(configAsset, out tempResolvedA, out tempResolvedB);
        if (tempResolvedA == null)
        {
            //Debug.LogError("[DeviceResolver] DeviceManager failed to resolve a valid pair.");
            return;
        }

        // Assign resolved devices to out parameters
        resolvedA = tempResolvedA;
        resolvedB = tempResolvedB;

        Debug.Log($"[DeviceResolver] Successfully resolved devices: {resolvedA} & {resolvedB}");
    }
}
