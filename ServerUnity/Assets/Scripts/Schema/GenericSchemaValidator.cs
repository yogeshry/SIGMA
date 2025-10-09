// GenericSchemaValidator.cs
// A Unity MonoBehaviour for validating JSON data against a JSON Schema with detailed error and warning reporting.

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;


/// <summary>
/// MonoBehaviour that validates JSON data against a JSON Schema.
/// Attach to a GameObject and assign schemaAsset and jsonDataAsset in Inspector.
/// </summary>
public class GenericSchemaValidator : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("JSON Schema TextAsset (schemaAsset)")]
    public TextAsset schemaAsset;

    [Tooltip("JSON data TextAsset to validate (jsonDataAsset)")]
    public TextAsset jsonDataAsset;

    [Tooltip("Enable detailed logging to Console")]
    public bool enableDetailedLogging = true;

    [Tooltip("Enable performance timing metrics")]
    public bool enablePerformanceMetrics = true;

    [Tooltip("Enable warnings generation")]
    public bool enableWarnings = true;

    [Tooltip("Maximum number of errors to report (0 = unlimited)")]
    public int maxErrorsToReport = 50;

    [Header("Output Options")]
    [Tooltip("Save validation report to persistentDataPath")]
    public bool saveReportToFile = false;

    [Tooltip("Relative path under persistentDataPath for report file")]
    public string reportFilePath = "validation_report.json";

    public static event Action<ValidationResult> OnValidationComplete;
    public static event Action<ValidationError> OnValidationError;
    public static event Action<ValidationWarning> OnValidationWarning;

    private readonly Dictionary<string, JSchema> _schemaCache = new Dictionary<string, JSchema>();

    private void Awake()
    {
        if (schemaAsset == null || jsonDataAsset == null)
        {
            Debug.LogWarning("GenericSchemaValidator: Assign schemaAsset and jsonDataAsset in Inspector.");
            return;
        }
        ValidateJson();
    }

    /// <summary>
    /// Validates the assigned JSON data against the assigned schema.
    /// </summary>
    public ValidationResult ValidateJson()
    {
        return ValidateJson(jsonDataAsset.text, schemaAsset.text, jsonDataAsset.name);
    }

    private ValidationResult ValidateJson(string jsonData, string schemaData, string contextName)
    {
        var result = new ValidationResult { JsonPath = contextName, IsValid = true };
        var startTime = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(jsonData))
        {
            AddError(result, "JSON data is null or empty", "", "", ValidationErrorType.Required);
            return result;
        }
        if (string.IsNullOrWhiteSpace(schemaData))
        {
            AddError(result, "Schema data is null or empty", "", "", ValidationErrorType.Required);
            return result;
        }

        var schema = ParseSchema(schemaData, result);
        if (schema == null) return result;

        var token = ParseJsonData(jsonData, result);
        if (token == null) return result;

        PerformValidation(token, schema, result);

        if (enablePerformanceMetrics)
            result.ValidationDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        if (enableWarnings)
            GenerateWarnings(token, result);

        OnValidationComplete?.Invoke(result);
        LogResults(result, contextName);
        if (saveReportToFile)
            SaveReport(result, contextName);

        return result;
    }

    private JSchema ParseSchema(string schemaData, ValidationResult result)
    {
        try
        {
            var key = schemaData.GetHashCode().ToString();
            if (_schemaCache.TryGetValue(key, out var cached))
                return cached;

            var schema = JSchema.Parse(schemaData);
            _schemaCache[key] = schema;

            if (schema.ExtensionData?.TryGetValue("$schema", out var version) == true)
                result.SchemaVersion = version.ToString();

            return schema;
        }
        catch (Exception ex)
        {
            AddError(result, $"Schema parsing error: {ex.Message}", "", "", ValidationErrorType.Unknown);
            if (enableDetailedLogging) Debug.LogError(ex);
            return null;
        }
    }

    private JToken ParseJsonData(string jsonData, ValidationResult result)
    {
        try { return JToken.Parse(jsonData); }
        catch (JsonReaderException ex)
        {
            AddError(result, ex.Message, ex.Path, "", ValidationErrorType.Unknown, null, null, ex.LineNumber, ex.LinePosition);
            if (enableDetailedLogging) Debug.LogError(ex);
            return null;
        }
    }

    private void PerformValidation(JToken token, JSchema schema, ValidationResult result)
    {
        var errors = new List<ValidationError>();
        try
        {
            using var reader = new JSchemaValidatingReader(token.CreateReader()) { Schema = schema };
            reader.ValidationEventHandler += (s, e) =>
            {
                var err = new ValidationError
                {
                    Message = e.Message,
                    Path = e.Path,
                    SchemaPath = e.ValidationError?.SchemaId?.ToString() ?? "",
                    ErrorType = DetermineErrorType(e.Message),
                    ActualValue = e.ValidationError?.Value,
                    LineNumber = e.ValidationError?.LineNumber ?? 0,
                    LinePosition = e.ValidationError?.LinePosition ?? 0
                };
                errors.Add(err);
                OnValidationError?.Invoke(err);
                if (maxErrorsToReport > 0 && errors.Count >= maxErrorsToReport)
                    throw new Exception("Max error limit reached");
            };
            while (reader.Read()) { }
        }
        catch { }
        result.Errors = errors;
        result.IsValid = errors.Count == 0;
    }

    private ValidationErrorType DetermineErrorType(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return ValidationErrorType.Unknown;
        msg = msg.ToLower();
        if (msg.Contains("required")) return ValidationErrorType.Required;
        if (msg.Contains("type")) return ValidationErrorType.Type;
        if (msg.Contains("enum")) return ValidationErrorType.Enum;
        return ValidationErrorType.Unknown;
    }

    private void GenerateWarnings(JToken token, ValidationResult result)
    {
        if (GetDepth(token) > 10)
            result.Warnings.Add(new ValidationWarning { Message = "Deep nesting detected", Path = token.Path, WarningType = WarningType.Performance });
    }

    private int GetDepth(JToken token, int depth = 0)
    {
        int max = depth;
        foreach (var child in token.Children())
            max = Math.Max(max, GetDepth(child, depth + 1));
        return max;
    }

    private void AddError(ValidationResult res, string msg, string path, string schemaPath, ValidationErrorType type,
                          object expected = null, object actual = null, int line = 0, int pos = 0)
    {
        var err = new ValidationError
        {
            Message = msg,
            Path = path,
            SchemaPath = schemaPath,
            ErrorType = type,
            ActualValue = actual,
            LineNumber = line,
            LinePosition = pos
        };
        res.Errors.Add(err);
        res.IsValid = false;
        OnValidationError?.Invoke(err);
    }

    /// <summary>
    /// Logs validation results with detailed error information
    /// </summary>
    private void LogResults(ValidationResult result, string context)
    {
        if (!enableDetailedLogging) return;
        string prefix = $"[Validator:{context}]";
        if (result.IsValid)
        {
            Debug.Log($"{prefix} PASS in {result.ValidationDuration:F2} ms");
        }
        else
        {
            Debug.LogError($"{prefix} FAIL ({result.Errors.Count} errors):");
            foreach (var e in result.Errors)
            {
                Debug.LogError($"{prefix} ERROR [{e.ErrorType}] at '{e.Path}' (Schema: {e.SchemaPath}) Line {e.LineNumber}:{e.LinePosition} → {e.Message}");
                if (e.ExpectedValue != null || e.ActualValue != null)
                    Debug.LogError($"{prefix}   Expected: {e.ExpectedValue}, Actual: {e.ActualValue}");
            }
        }
    }

    private void SaveReport(ValidationResult result, string context)
    {
        try
        {
            var reportObj = new
            {
                Context = context,
                IsValid = result.IsValid,
                SchemaVersion = result.SchemaVersion,
                DurationMs = result.ValidationDuration,
                Errors = result.Errors,
                Warnings = result.Warnings
            };
            var reportJson = JsonConvert.SerializeObject(reportObj, Formatting.Indented);
            var path = Path.Combine(Application.persistentDataPath, reportFilePath);
            File.WriteAllText(path, reportJson);
            if (enableDetailedLogging) Debug.Log($" Report saved: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
        }
    }
}
