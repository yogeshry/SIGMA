using TMPro;
using UnityEngine;
using UnityEngine.UI;  // For UI Text

public class LogToUI : MonoBehaviour
{
    public TextMeshProUGUI positionText;
    [SerializeField] private int maxLines = 30;

    private string logs = "";

    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (logString.StartsWith("[Rule"))
        {
            logs += logString + "\n";

            // Limit number of lines (optional, keeps UI fast)
            string[] lines = logs.Split('\n');
            if (lines.Length > maxLines)
            {
                logs = string.Join("\n", lines, lines.Length - maxLines, maxLines);
            }

            if (positionText != null)
                positionText.text = logs;
        }

    }
}
