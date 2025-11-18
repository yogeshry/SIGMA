// GenericSchemaValidator.cs
// A static singleton class for validating JSON data against a JSON Schema with detailed error and warning reporting.

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;



/// <summary>
/// Static singleton class for JSON Schema validation.
/// Configure static settings and call Validate(...) from anywhere.
/// </summary>
public static class SchemaValidator
{
    // Configuration
    public static bool EnableDetailedLogging = true;
    public static bool EnablePerformanceMetrics = true;
    public static bool EnableWarnings = true;
    public static int MaxErrorsToReport = 50;
    public static bool SaveReportToFile = false;
    public static string ReportFilePath = "validation_report.json";

    // Events
    public static event Action<ValidationResult> OnValidationComplete;
    public static event Action<ValidationError> OnValidationError;
    public static event Action<ValidationWarning> OnValidationWarning;

    // Internal cache for parsed schemas
    private static readonly Dictionary<string, JSchema> SchemaCache = new Dictionary<string, JSchema>();

    /// <summary>
    /// Validates JSON string against schema string.
    /// </summary>
    public static ValidationResult Validate(string jsonData, string schemaData, string contextName = "Validation")
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

        if (EnablePerformanceMetrics)
            result.ValidationDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        if (EnableWarnings)
            GenerateWarnings(token, result);

        OnValidationComplete?.Invoke(result);
        if (EnableDetailedLogging)
            LogResults(result, contextName);
        if (SaveReportToFile)
            SaveReport(result, contextName, ReportFilePath);

        return result;
    }

    private static JSchema ParseSchema(string schemaData, ValidationResult result)
    {
        try
        {
            var key = schemaData.GetHashCode().ToString();
            if (SchemaCache.TryGetValue(key, out var cached))
                return cached;

            var schema = JSchema.Parse(schemaData);
            SchemaCache[key] = schema;

            if (schema.ExtensionData?.TryGetValue("$schema", out var version) == true)
                result.SchemaVersion = version.ToString();

            return schema;
        }
        catch (Exception ex)
        {
            AddError(result, $"Schema parsing error: {ex.Message}", "", "", ValidationErrorType.Unknown);
            if (EnableDetailedLogging) Debug.LogError(ex);
            return null;
        }
    }

    private static JToken ParseJsonData(string jsonData, ValidationResult result)
    {
        try { return JToken.Parse(jsonData); }
        catch (JsonReaderException ex)
        {
            AddError(result, ex.Message, ex.Path, "", ValidationErrorType.Unknown, null, null, ex.LineNumber, ex.LinePosition);
            if (EnableDetailedLogging) Debug.LogError(ex);
            return null;
        }
    }

    private static void PerformValidation(JToken token, JSchema schema, ValidationResult result)
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
                if (MaxErrorsToReport > 0 && errors.Count >= MaxErrorsToReport)
                    throw new Exception("Max error limit reached");
            };
            while (reader.Read()) { }
        }
        catch { }

        result.Errors = errors;
        result.IsValid = errors.Count == 0;
    }

    private static ValidationErrorType DetermineErrorType(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return ValidationErrorType.Unknown;
        msg = msg.ToLower();
        if (msg.Contains("required")) return ValidationErrorType.Required;
        if (msg.Contains("type")) return ValidationErrorType.Type;
        if (msg.Contains("enum")) return ValidationErrorType.Enum;
        return ValidationErrorType.Unknown;
    }

    private static void GenerateWarnings(JToken token, ValidationResult result)
    {
        if (GetDepth(token) > 10)
            result.Warnings.Add(new ValidationWarning { Message = "Deep nesting detected", Path = token.Path, WarningType = WarningType.Performance });
    }

    private static int GetDepth(JToken token, int depth = 0)
    {
        int max = depth;
        foreach (var child in token.Children())
            max = Math.Max(max, GetDepth(child, depth + 1));
        return max;
    }

    private static void AddError(ValidationResult res, string msg, string path, string schemaPath, ValidationErrorType type,
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

    private static void LogResults(ValidationResult result, string context)
    {
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
                Debug.LogError($"{prefix} ERROR [{e.ErrorType}] at '{e.Path}' (Schema: {e.SchemaPath}) Line {e.LineNumber}:{e.LinePosition} ? {e.Message}");
                if (e.ExpectedValue != null || e.ActualValue != null)
                    Debug.LogError($"{prefix}   Expected: {e.ExpectedValue}, Actual: {e.ActualValue}");
            }
        }
    }

    private static void SaveReport(ValidationResult result, string context, string reportFile)
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
            var path = Path.Combine(Application.persistentDataPath, reportFile);
            File.WriteAllText(path, reportJson);
            if (EnableDetailedLogging) Debug.Log($"[Validator:{context}] Report saved: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
        }
    }
}
