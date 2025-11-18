using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;  // For UI Text

public class LogToUI : MonoBehaviour
{
    public TextMeshProUGUI logText;
    public TextMeshProUGUI registrationStatusText;
    [SerializeField] private int maxLines = 30;

    private string logs = "";

    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
        DeviceManager.OnDevicesChanged += UpdateDeviceRegistrationStatus; // 🔔 subscribe to event
    }

    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
        DeviceManager.OnDevicesChanged -= UpdateDeviceRegistrationStatus; // 🔔 subscribe to event
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

            if (logText != null)
                logText.text = logs;
        }

    }

    void UpdateDeviceRegistrationStatus()
    {
        var all = DeviceManager.GetAllDevices();
        if (registrationStatusText == null) return;

        if (all.Count == 0)
        {
            registrationStatusText.text = "<b>Registered Devices:</b>\n<color=gray>None connected</color>";
            return;
        }

        // Build formatted status text  
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>📱 Registered Devices</b>");
        sb.AppendLine($"<size=90%>Total: {all.Count}</color></size>");
        sb.AppendLine("------------------------------------");

        foreach (var kvp in all)
        {
            Device device = kvp.Value; // Fix: Use 'Device' type instead of 'DeviceInfo'  
            sb.AppendLine($"<b>ID:</b> {kvp.Key}");
            sb.AppendLine($"<b>Type:</b> {device.deviceType}");
            sb.AppendLine($"<b>Resolution:</b> {device.displaySize.widthPixels} × {device.displaySize.heightPixels}");
            sb.AppendLine($"<b>PPI:</b> {device.displaySize.ppi}");
            sb.AppendLine($"<b>Tracker:</b> {device?.transform?.name}");
            sb.AppendLine(); // blank line for spacing  
        }
        Debug.Log("[LogToUI] Updating device registration status UI." + all.Count);

        // Apply formatted text  
        registrationStatusText.text = sb.ToString().TrimEnd();
    }
}
