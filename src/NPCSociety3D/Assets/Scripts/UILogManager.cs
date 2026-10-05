using TMPro;
using UnityEngine;

public class UILogManager : MonoBehaviour
{
    public TextMeshProUGUI logText;

    private string currentLog = "";

    public void AddLog(string message)
    {
        currentLog = message + "\n" + currentLog;

        string[] lines = currentLog.Split('\n');

        if (lines.Length > 8)
        {
            currentLog = "";

            for (int i = 0; i < 8; i++)
            {
                currentLog += lines[i] + "\n";
            }
        }

        logText.text = currentLog;
    }
}