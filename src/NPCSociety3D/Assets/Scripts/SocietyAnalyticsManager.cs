using System;
using System.Collections.Generic;
using UnityEngine;

public class SocietyAnalyticsManager : MonoBehaviour
{
    [Header("Analytics Configuration")]
    public bool enableAnalyticsLogs = true;
    public bool logOnStart = true;
    public float analyticsInterval = 30f;
    public bool includePersistentSocialEvents = true;
    public bool includeReportDeltas = true;

    private float timer = 0f;
    private int reportCounter = 0;

    private float lastAverageTrust = 0f;
    private float lastAverageAffinity = 0f;
    private float lastAverageTension = 0f;
    private int lastStoredSocialEventCount = -1;

    private NPCMemoryManager memoryManager;
    private readonly List<NPCAgent> npcAgents = new List<NPCAgent>();

    private void Start()
    {
        RefreshReferences();

        if (logOnStart && enableAnalyticsLogs)
        {
            Invoke(nameof(LogSocietyAnalytics), 1f);
        }
    }

    private void Update()
    {
        if (!enableAnalyticsLogs)
        {
            return;
        }

        timer += Time.deltaTime;

        if (timer >= analyticsInterval)
        {
            timer = 0f;
            LogSocietyAnalytics();
        }
    }

    public void LogSocietyAnalytics()
    {
        string report = BuildAnalyticsReport();
        Debug.Log(report);
    }

    private string BuildAnalyticsReport()
    {
        RefreshReferences();

        reportCounter++;

        int npcCount = npcAgents.Count;
        int relationshipCount = 0;

        int trustSum = 0;
        int affinitySum = 0;
        int tensionSum = 0;

        string strongestRelationship = "N/A";
        string mostTenseRelationship = "No active tension";
        string mostSocialNpc = "N/A";

        int strongestRelationshipScore = int.MinValue;
        int highestTension = 0;
        int highestNpcInteractionCount = int.MinValue;

        Dictionary<string, int> interactionsByNpc = new Dictionary<string, int>();

        foreach (NPCAgent npcAgent in npcAgents)
        {
            if (npcAgent == null)
            {
                continue;
            }

            string npcName = GetNpcName(npcAgent);

            if (!interactionsByNpc.ContainsKey(npcName))
            {
                interactionsByNpc[npcName] = 0;
            }

            if (npcAgent.relationships == null)
            {
                continue;
            }

            foreach (RelationshipState relationship in npcAgent.relationships)
            {
                if (relationship == null || string.IsNullOrWhiteSpace(relationship.targetNpcName))
                {
                    continue;
                }

                relationshipCount++;

                trustSum += relationship.trust;
                affinitySum += relationship.affinity;
                tensionSum += relationship.tension;

                interactionsByNpc[npcName] += relationship.interactionCount;

                string relationshipLabel = npcName + " -> " + relationship.targetNpcName;
                int relationshipScore = relationship.trust + relationship.affinity - relationship.tension;

                if (relationshipScore > strongestRelationshipScore)
                {
                    strongestRelationshipScore = relationshipScore;
                    strongestRelationship = relationshipLabel +
                        " (Trust: " + relationship.trust +
                        ", Affinity: " + relationship.affinity +
                        ", Tension: " + relationship.tension + ")";
                }

                if (relationship.tension > highestTension)
                {
                    highestTension = relationship.tension;
                    mostTenseRelationship = relationshipLabel +
                        " (Tension: " + relationship.tension +
                        ", Trust: " + relationship.trust +
                        ", Affinity: " + relationship.affinity + ")";
                }
            }
        }

        foreach (KeyValuePair<string, int> npcInteractionData in interactionsByNpc)
        {
            if (npcInteractionData.Value > highestNpcInteractionCount)
            {
                highestNpcInteractionCount = npcInteractionData.Value;
                mostSocialNpc = npcInteractionData.Key + " (" + npcInteractionData.Value + " relationship interactions)";
            }
        }

        float averageTrust = relationshipCount > 0 ? (float)trustSum / relationshipCount : 0f;
        float averageAffinity = relationshipCount > 0 ? (float)affinitySum / relationshipCount : 0f;
        float averageTension = relationshipCount > 0 ? (float)tensionSum / relationshipCount : 0f;

        int storedSocialEventCount = 0;
        string mostFrequentEventType = "N/A";

        if (includePersistentSocialEvents)
        {
            CalculatePersistentEventMetrics(out storedSocialEventCount, out mostFrequentEventType);
        }

        bool hasPreviousReport = reportCounter > 1;

        List<string> reportLines = new List<string>
        {
            "Society Analytics",
            "Report #: " + reportCounter,
            "Generated At: " + GetSceneTimestamp(),
            "NPCs: " + npcCount,
            "Directional relationships: " + relationshipCount,
            "Average Trust: " + averageTrust.ToString("0.0"),
            "Average Affinity: " + averageAffinity.ToString("0.0"),
            "Average Tension: " + averageTension.ToString("0.0"),
            "Strongest Relationship: " + strongestRelationship,
            "Most Tense Relationship: " + mostTenseRelationship,
            "Most Social NPC: " + mostSocialNpc
        };

        if (includeReportDeltas)
        {
            reportLines.Add("Average Trust Delta: " + FormatFloatDelta(hasPreviousReport, averageTrust - lastAverageTrust));
            reportLines.Add("Average Affinity Delta: " + FormatFloatDelta(hasPreviousReport, averageAffinity - lastAverageAffinity));
            reportLines.Add("Average Tension Delta: " + FormatFloatDelta(hasPreviousReport, averageTension - lastAverageTension));
        }

        if (includePersistentSocialEvents)
        {
            reportLines.Add("Stored Social Events: " + storedSocialEventCount);
            reportLines.Add("Most Frequent Event Type: " + mostFrequentEventType);

            if (includeReportDeltas)
            {
                int eventDelta = lastStoredSocialEventCount >= 0
                    ? storedSocialEventCount - lastStoredSocialEventCount
                    : 0;

                reportLines.Add("Stored Social Events Delta: " + FormatIntegerDelta(hasPreviousReport, eventDelta));
            }
        }

        lastAverageTrust = averageTrust;
        lastAverageAffinity = averageAffinity;
        lastAverageTension = averageTension;
        lastStoredSocialEventCount = storedSocialEventCount;

        return string.Join("\n", reportLines.ToArray());
    }

    private void CalculatePersistentEventMetrics(
        out int storedSocialEventCount,
        out string mostFrequentEventType
    )
    {
        storedSocialEventCount = 0;
        mostFrequentEventType = "N/A";

        if (memoryManager == null)
        {
            memoryManager = FindObjectOfType<NPCMemoryManager>();
        }

        if (memoryManager == null)
        {
            return;
        }

        List<SocialEvent> socialEvents = memoryManager.LoadSocialEvents();

        if (socialEvents == null || socialEvents.Count == 0)
        {
            return;
        }

        storedSocialEventCount = socialEvents.Count;

        Dictionary<string, int> eventTypeCounts = new Dictionary<string, int>();

        foreach (SocialEvent socialEvent in socialEvents)
        {
            if (socialEvent == null || string.IsNullOrWhiteSpace(socialEvent.interactionType))
            {
                continue;
            }

            if (!eventTypeCounts.ContainsKey(socialEvent.interactionType))
            {
                eventTypeCounts[socialEvent.interactionType] = 0;
            }

            eventTypeCounts[socialEvent.interactionType]++;
        }

        int highestCount = 0;

        foreach (KeyValuePair<string, int> eventTypeData in eventTypeCounts)
        {
            if (eventTypeData.Value > highestCount)
            {
                highestCount = eventTypeData.Value;
                mostFrequentEventType = eventTypeData.Key + " (" + eventTypeData.Value + ")";
            }
        }
    }

    private void RefreshReferences()
    {
        npcAgents.Clear();

        NPCAgent[] foundNpcAgents = FindObjectsOfType<NPCAgent>();

        foreach (NPCAgent npcAgent in foundNpcAgents)
        {
            if (npcAgent == null || string.IsNullOrWhiteSpace(npcAgent.npcName))
            {
                continue;
            }

            npcAgents.Add(npcAgent);
        }

        npcAgents.Sort(CompareNpcAgentsByName);

        if (memoryManager == null)
        {
            memoryManager = FindObjectOfType<NPCMemoryManager>();
        }
    }

    private int CompareNpcAgentsByName(NPCAgent firstNpc, NPCAgent secondNpc)
    {
        string firstName = firstNpc != null ? firstNpc.npcName : "";
        string secondName = secondNpc != null ? secondNpc.npcName : "";

        return string.Compare(firstName, secondName, StringComparison.OrdinalIgnoreCase);
    }

    private string GetNpcName(NPCAgent npcAgent)
    {
        if (npcAgent == null || string.IsNullOrWhiteSpace(npcAgent.npcName))
        {
            return "Unknown NPC";
        }

        return npcAgent.npcName;
    }

    private string GetSceneTimestamp()
    {
        int totalSeconds = Mathf.FloorToInt(Time.timeSinceLevelLoad);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        return $"[{minutes:00}:{seconds:00}]";
    }

    private string FormatFloatDelta(bool hasPreviousReport, float delta)
    {
        if (!hasPreviousReport)
        {
            return "N/A";
        }

        if (Mathf.Abs(delta) < 0.05f)
        {
            return "0.0";
        }

        return delta > 0f
            ? "+" + delta.ToString("0.0")
            : delta.ToString("0.0");
    }

    private string FormatIntegerDelta(bool hasPreviousReport, int delta)
    {
        if (!hasPreviousReport)
        {
            return "N/A";
        }

        if (delta == 0)
        {
            return "0";
        }

        return delta > 0
            ? "+" + delta
            : delta.ToString();
    }

    private void OnValidate()
    {
        if (analyticsInterval < 5f)
        {
            analyticsInterval = 5f;
        }
    }
}