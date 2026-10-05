using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class OpenClawSocietyAutonomyManager : MonoBehaviour
{
    [Header("Autonomy")]
    [SerializeField]
    private bool enableAutonomy = false;

    private enum ActorSelectionMode
    {
        RoundRobin = 0,
        OpenClawSocietyScheduler = 1
    }

    [Header("Actor Selection")]
    [SerializeField]
    private ActorSelectionMode actorSelectionMode =
        ActorSelectionMode.RoundRobin;

    [SerializeField]
    private string societySchedulerUrl =
        "http://127.0.0.1:5055/choose_next_actor";

    [SerializeField]
    private int societySchedulerTimeoutSeconds = 60;

    [SerializeField]
    private bool fallbackToRoundRobinOnSchedulerFailure = true;

    [SerializeField]
    private bool autoDiscoverAgents = true;

    [SerializeField]
    private List<OpenClawNPCAgentController> agentControllers =
        new List<OpenClawNPCAgentController>();

    [SerializeField]
    private bool preserveManualAgentOrder = true;

    [SerializeField]
    private int startAgentIndex = 0;

    [SerializeField]
    private int maxAutonomousAgents = 1;

    [Header("Decision Limits")]
    [SerializeField]
    private int maxDecisionsPerPlaySession = 1;

    [SerializeField]
    private bool disableAutonomyAfterDecisionLimit = true;

    [Header("Timing")]
    [SerializeField]
    private float initialDelaySeconds = 10f;

    [SerializeField]
    private float globalCooldownSeconds = 180f;

    [SerializeField]
    private float npcCooldownSeconds = 300f;

    [SerializeField]
    private int maxDecisionsPerMinute = 1;

    [Header("Diagnostics")]
    [SerializeField]
    private bool enableDebugLogs = true;

    private readonly Dictionary<OpenClawNPCAgentController, float>
        nextAllowedDecisionTimeByAgent =
            new Dictionary<OpenClawNPCAgentController, float>();

    private readonly Queue<float> decisionStartTimes =
        new Queue<float>();

    private Coroutine autonomyRoutine;
    private bool managerRequestInProgress;
    private float nextGlobalDecisionTime;
    private int nextCandidateIndex;
    private int decisionsStartedThisPlaySession;

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        RefreshAgents();

        nextCandidateIndex =
            GetSafeStartAgentIndex();

        autonomyRoutine =
            StartCoroutine(AutonomyLoop());
    }

    private void OnDisable()
    {
        if (autonomyRoutine != null)
        {
            StopCoroutine(autonomyRoutine);
            autonomyRoutine = null;
        }

        managerRequestInProgress = false;
    }

    private void OnValidate()
    {
        maxAutonomousAgents =
            Mathf.Max(1, maxAutonomousAgents);

        startAgentIndex =
            Mathf.Max(0, startAgentIndex);

        maxDecisionsPerPlaySession =
            Mathf.Max(0, maxDecisionsPerPlaySession);

        initialDelaySeconds =
            Mathf.Max(0f, initialDelaySeconds);

        globalCooldownSeconds =
            Mathf.Max(1f, globalCooldownSeconds);

        npcCooldownSeconds =
            Mathf.Max(1f, npcCooldownSeconds);

        maxDecisionsPerMinute =
            Mathf.Max(1, maxDecisionsPerMinute);
    }

    [ContextMenu("Refresh OpenClaw Agents")]
    public void RefreshAgents()
    {
        if (agentControllers == null)
        {
            agentControllers =
                new List<OpenClawNPCAgentController>();
        }

        if (autoDiscoverAgents)
        {
            OpenClawNPCAgentController[] discoveredAgents =
                FindObjectsOfType<OpenClawNPCAgentController>();

            agentControllers.Clear();

            foreach (
                OpenClawNPCAgentController discoveredAgent
                in discoveredAgents
            )
            {
                if (discoveredAgent != null)
                {
                    agentControllers.Add(discoveredAgent);
                }
            }
        }

        RemoveNullAgentReferences();

        if (
            autoDiscoverAgents ||
            !preserveManualAgentOrder
        )
        {
            SortAgentControllers();
        }

        if (enableDebugLogs)
        {
            Debug.Log(
                "OpenClaw autonomy manager found " +
                agentControllers.Count +
                " agent controller(s).",
                this
            );
        }
    }

    [ContextMenu("Enable Autonomy")]
    private void EnableAutonomyFromContextMenu()
    {
        enableAutonomy = true;

        if (enableDebugLogs)
        {
            Debug.Log(
                "OpenClaw society autonomy enabled.",
                this
            );
        }
    }

    [ContextMenu("Disable Autonomy")]
    private void DisableAutonomyFromContextMenu()
    {
        enableAutonomy = false;

        if (enableDebugLogs)
        {
            Debug.Log(
                "OpenClaw society autonomy disabled.",
                this
            );
        }
    }

    public void SetAutonomyEnabled(bool isEnabled)
    {
        enableAutonomy = isEnabled;
    }

    private IEnumerator AutonomyLoop()
    {
        if (initialDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(initialDelaySeconds);
        }

        while (true)
        {
            if (!enableAutonomy)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            if (
                autoDiscoverAgents &&
                (
                    agentControllers == null ||
                    agentControllers.Count == 0
                )
            )
            {
                RefreshAgents();
            }

            RemoveNullAgentReferences();

            if (
                managerRequestInProgress ||
                HasAnyActiveRequest()
            )
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            TrimDecisionStartTimes();

            if (!CanStartAnotherDecision())
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            if (HasReachedPlaySessionDecisionLimit())
            {
                if (disableAutonomyAfterDecisionLimit)
                {
                    enableAutonomy = false;

                    if (enableDebugLogs)
                    {
                        Debug.Log(
                            "OpenClaw society autonomy disabled after reaching the play session decision limit.",
                            this
                        );
                    }
                }

                yield return new WaitForSeconds(1f);
                continue;
            }

            OpenClawNPCAgentController selectedController = null;
            yield return SelectNextEligibleController(
                controller =>
                {
                    selectedController = controller;
                });

            if (selectedController == null)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            yield return RunAutonomousDecision(
                selectedController
            );

            yield return null;
        }
    }

    private IEnumerator RunAutonomousDecision(
        OpenClawNPCAgentController controller
    )
    {
        if (controller == null)
        {
            yield break;
        }

        managerRequestInProgress = true;

        RegisterDecisionStart();

        if (enableDebugLogs)
        {
            Debug.Log(
                "Autonomous OpenClaw decision requested for " +
                GetAgentDisplayName(controller) +
                ".",
                this
            );
        }

        controller.RequestNextDecision();

        yield return null;

        while (
            controller != null &&
            controller.IsRequestInProgress
        )
        {
            yield return null;
        }

        if (controller != null)
        {
            nextAllowedDecisionTimeByAgent[controller] =
                Time.time + npcCooldownSeconds;
        }

        nextGlobalDecisionTime =
            Time.time + globalCooldownSeconds;

        managerRequestInProgress = false;

        if (enableDebugLogs)
        {
            Debug.Log(
                "Autonomous OpenClaw decision finished for " +
                GetAgentDisplayName(controller) +
                ".",
                this
            );
        }

        if (
            HasReachedPlaySessionDecisionLimit() &&
            disableAutonomyAfterDecisionLimit
        )
        {
            enableAutonomy = false;

            if (enableDebugLogs)
            {
                Debug.Log(
                    "OpenClaw society autonomy disabled after reaching the play session decision limit.",
                    this
                );
            }
        }
    }

    private void RegisterDecisionStart()
    {
        decisionStartTimes.Enqueue(Time.time);
        decisionsStartedThisPlaySession++;
    }

    private bool CanStartAnotherDecision()
    {
        if (Time.time < nextGlobalDecisionTime)
        {
            return false;
        }

        return decisionStartTimes.Count <
            maxDecisionsPerMinute;
    }

    private bool HasReachedPlaySessionDecisionLimit()
    {
        if (maxDecisionsPerPlaySession <= 0)
        {
            return false;
        }

        return decisionsStartedThisPlaySession >=
            maxDecisionsPerPlaySession;
    }

    private void TrimDecisionStartTimes()
    {
        float oldestAllowedTime =
            Time.time - 60f;

        while (
            decisionStartTimes.Count > 0 &&
            decisionStartTimes.Peek() < oldestAllowedTime
        )
        {
            decisionStartTimes.Dequeue();
        }
    }

    private bool HasAnyActiveRequest()
    {
        if (agentControllers == null)
        {
            return false;
        }

        foreach (
            OpenClawNPCAgentController controller
            in agentControllers
        )
        {
            if (
                controller != null &&
                controller.IsRequestInProgress
            )
            {
                return true;
            }
        }

        return false;
    }

    private int GetSafeStartAgentIndex()
    {
        if (
            agentControllers == null ||
            agentControllers.Count == 0
        )
        {
            return 0;
        }

        return Mathf.Clamp(
            startAgentIndex,
            0,
            agentControllers.Count - 1
        );
    }


    private IEnumerator SelectNextEligibleController(
        Action<OpenClawNPCAgentController> onSelected)
    {
        if (actorSelectionMode != ActorSelectionMode.OpenClawSocietyScheduler)
        {
            onSelected?.Invoke(GetNextEligibleController());
            yield break;
        }

        yield return TrySelectControllerWithSocietyScheduler(onSelected);
    }

    private IEnumerator TrySelectControllerWithSocietyScheduler(
        Action<OpenClawNPCAgentController> onSelected)
    {
        List<OpenClawNPCAgentController> candidates =
            GetSchedulerEligibleControllers();

        if (candidates.Count == 0)
        {
            onSelected?.Invoke(null);
            yield break;
        }

        SocietySchedulerRequest schedulerRequest =
            BuildSocietySchedulerRequest(candidates);

        string requestJson = JsonUtility.ToJson(schedulerRequest);
        byte[] requestBody = Encoding.UTF8.GetBytes(requestJson);

        using (UnityWebRequest webRequest =
            new UnityWebRequest(
                societySchedulerUrl,
                UnityWebRequest.kHttpVerbPOST))
        {
            webRequest.uploadHandler =
                new UploadHandlerRaw(requestBody);
            webRequest.downloadHandler =
                new DownloadHandlerBuffer();
            webRequest.SetRequestHeader(
                "Content-Type",
                "application/json");
            webRequest.timeout =
                Mathf.Max(1, societySchedulerTimeoutSeconds);

            yield return webRequest.SendWebRequest();

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "Society scheduler request failed: " +
                    webRequest.error);

                SelectRoundRobinFallback(onSelected);
                yield break;
            }

            string responseJson =
                webRequest.downloadHandler != null
                    ? webRequest.downloadHandler.text
                    : string.Empty;

            SocietySchedulerResponse response = null;

            try
            {
                response =
                    JsonUtility.FromJson<SocietySchedulerResponse>(
                        responseJson);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Society scheduler response could not be parsed: " +
                    exception.Message);
            }

            if (!IsValidSocietySchedulerResponse(response))
            {
                Debug.LogWarning(
                    "Society scheduler returned an invalid response: " +
                    responseJson);

                SelectRoundRobinFallback(onSelected);
                yield break;
            }

            if (response.decisionSource == "local_round_robin_fallback")
            {
                Debug.LogWarning(
                    "Society scheduler backend used fallback: " +
                    response.fallbackReason);

                SelectRoundRobinFallback(onSelected);
                yield break;
            }

            OpenClawNPCAgentController selectedController =
                FindControllerByNpcName(response.actorName);

            if (!IsControllerEligibleForScheduler(selectedController))
            {
                Debug.LogWarning(
                    "Society scheduler selected an invalid or unavailable actor: " +
                    response.actorName);

                SelectRoundRobinFallback(onSelected);
                yield break;
            }

            Debug.Log(
                "Society scheduler selected actor " +
                response.actorName +
                " | Source: " +
                response.decisionSource +
                " | Priority: " +
                response.priority +
                " | Reason: " +
                response.reason);

            onSelected?.Invoke(selectedController);
        }
    }

    private void SelectRoundRobinFallback(
        Action<OpenClawNPCAgentController> onSelected)
    {
        if (!fallbackToRoundRobinOnSchedulerFailure)
        {
            onSelected?.Invoke(null);
            return;
        }

        onSelected?.Invoke(GetNextEligibleController());
    }

    private bool IsValidSocietySchedulerResponse(
        SocietySchedulerResponse response)
    {
        return response != null &&
            !string.IsNullOrWhiteSpace(response.actorName) &&
            !string.IsNullOrWhiteSpace(response.decisionSource);
    }

    private List<OpenClawNPCAgentController> GetSchedulerEligibleControllers()
    {
        List<OpenClawNPCAgentController> candidates =
            new List<OpenClawNPCAgentController>();

        if (agentControllers == null || agentControllers.Count == 0)
        {
            return candidates;
        }

        int safeStartIndex = GetSafeStartAgentIndex();
        int activeCount =
            Mathf.Min(
                Mathf.Max(1, maxAutonomousAgents),
                agentControllers.Count);

        for (int offset = 0; offset < activeCount; offset++)
        {
            int candidateIndex =
                (safeStartIndex + offset) % agentControllers.Count;

            OpenClawNPCAgentController controller =
                agentControllers[candidateIndex];

            if (IsControllerEligibleForScheduler(controller))
            {
                candidates.Add(controller);
            }
        }

        return candidates;
    }

    private bool IsControllerEligibleForScheduler(
        OpenClawNPCAgentController controller)
    {
        if (controller == null || controller.IsRequestInProgress)
        {
            return false;
        }

        if (nextAllowedDecisionTimeByAgent.TryGetValue(
            controller,
            out float nextAllowedTime) &&
            Time.time < nextAllowedTime)
        {
            return false;
        }

        return true;
    }

    private SocietySchedulerRequest BuildSocietySchedulerRequest(
        List<OpenClawNPCAgentController> candidates)
    {
        SocietySchedulerRequest request =
            new SocietySchedulerRequest();

        List<SocietySchedulerActorState> actors =
            new List<SocietySchedulerActorState>();

        foreach (OpenClawNPCAgentController controller in candidates)
        {
            actors.Add(BuildSocietySchedulerActorState(controller));
        }

        request.eligibleActors = actors.ToArray();
        request.society = new SocietySchedulerContext();

        return request;
    }

    private SocietySchedulerActorState BuildSocietySchedulerActorState(
        OpenClawNPCAgentController controller)
    {
        NPCAgent npc =
            controller != null
                ? controller.GetComponent<NPCAgent>()
                : null;

        string npcName =
            ReadStringMember(npc, "npcName");

        if (string.IsNullOrWhiteSpace(npcName) && controller != null)
        {
            npcName = controller.gameObject.name;
        }

        return new SocietySchedulerActorState
        {
            name = npcName,
            role = ReadStringMember(npc, "role"),
            goal = ReadStringMember(npc, "goal"),
            personality = ReadStringMember(npc, "personality"),
            mood = ReadIntMember(npc, "mood", 50),
            trust = ReadIntMember(npc, "trust", 50),
            recentMemories = ReadRecentMemories(npc, 5)
        };
    }

    private string[] ReadRecentMemories(NPCAgent npc, int limit)
    {
        List<string> memories = new List<string>();

        object memoryObject =
            ReadMemberValue(npc, "memory");

        System.Collections.IEnumerable enumerable =
            memoryObject as System.Collections.IEnumerable;

        if (enumerable != null && !(memoryObject is string))
        {
            foreach (object item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }

                string memory = item.ToString();

                if (!string.IsNullOrWhiteSpace(memory))
                {
                    memories.Add(memory);
                }
            }
        }

        int safeLimit = Mathf.Max(0, limit);

        if (safeLimit > 0 && memories.Count > safeLimit)
        {
            memories =
                memories.GetRange(
                    memories.Count - safeLimit,
                    safeLimit);
        }

        return memories.ToArray();
    }

    private object ReadMemberValue(object source, string memberName)
    {
        if (source == null || string.IsNullOrWhiteSpace(memberName))
        {
            return null;
        }

        Type sourceType = source.GetType();

        System.Reflection.FieldInfo field =
            sourceType.GetField(memberName);

        if (field != null)
        {
            return field.GetValue(source);
        }

        System.Reflection.PropertyInfo property =
            sourceType.GetProperty(memberName);

        if (property != null)
        {
            return property.GetValue(source, null);
        }

        return null;
    }

    private string ReadStringMember(object source, string memberName)
    {
        object value = ReadMemberValue(source, memberName);
        return value != null ? value.ToString() : string.Empty;
    }

    private int ReadIntMember(
        object source,
        string memberName,
        int fallbackValue)
    {
        object value = ReadMemberValue(source, memberName);

        if (value is int)
        {
            return (int)value;
        }

        if (value != null &&
            int.TryParse(value.ToString(), out int parsedValue))
        {
            return parsedValue;
        }

        return fallbackValue;
    }

    private OpenClawNPCAgentController FindControllerByNpcName(
        string npcName)
    {
        if (agentControllers == null ||
            string.IsNullOrWhiteSpace(npcName))
        {
            return null;
        }

        foreach (OpenClawNPCAgentController controller in agentControllers)
        {
            if (controller == null)
            {
                continue;
            }

            NPCAgent npc = controller.GetComponent<NPCAgent>();
            string candidateName =
                ReadStringMember(npc, "npcName");

            if (string.Equals(
                candidateName,
                npcName,
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    controller.gameObject.name,
                    npcName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return controller;
            }
        }

        return null;
    }

private OpenClawNPCAgentController GetNextEligibleController()
    {
        if (
            agentControllers == null ||
            agentControllers.Count == 0
        )
        {
            return null;
        }

        int activeAgentLimit =
            Mathf.Min(
                maxAutonomousAgents,
                agentControllers.Count
            );

        if (activeAgentLimit <= 0)
        {
            return null;
        }

        if (
            nextCandidateIndex < 0 ||
            nextCandidateIndex >= activeAgentLimit
        )
        {
            nextCandidateIndex = 0;
        }

        for (
            int offset = 0;
            offset < activeAgentLimit;
            offset++
        )
        {
            int candidateIndex =
                (nextCandidateIndex + offset) %
                activeAgentLimit;

            OpenClawNPCAgentController candidate =
                agentControllers[candidateIndex];

            if (IsEligibleController(candidate))
            {
                nextCandidateIndex =
                    (candidateIndex + 1) %
                    activeAgentLimit;

                return candidate;
            }
        }

        return null;
    }

    private bool IsEligibleController(
        OpenClawNPCAgentController controller
    )
    {
        if (controller == null)
        {
            return false;
        }

        if (!controller.isActiveAndEnabled)
        {
            return false;
        }

        if (controller.IsRequestInProgress)
        {
            return false;
        }

        float nextAllowedTime;

        if (
            nextAllowedDecisionTimeByAgent.TryGetValue(
                controller,
                out nextAllowedTime
            ) &&
            Time.time < nextAllowedTime
        )
        {
            return false;
        }

        return true;
    }

    private void RemoveNullAgentReferences()
    {
        if (agentControllers == null)
        {
            return;
        }

        agentControllers.RemoveAll(
            controller => controller == null
        );
    }

    private void SortAgentControllers()
    {
        if (agentControllers == null)
        {
            return;
        }

        agentControllers.Sort(CompareControllersByName);
    }

    private int CompareControllersByName(
        OpenClawNPCAgentController left,
        OpenClawNPCAgentController right
    )
    {
        string leftName =
            GetAgentDisplayName(left);

        string rightName =
            GetAgentDisplayName(right);

        return string.Compare(
            leftName,
            rightName,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private string GetAgentDisplayName(
        OpenClawNPCAgentController controller
    )
    {
        if (controller == null)
        {
            return "Unknown";
        }

        NPCAgent npcAgent =
            controller.GetComponent<NPCAgent>();

        if (
            npcAgent != null &&
            !string.IsNullOrWhiteSpace(npcAgent.npcName)
        )
        {
            return npcAgent.npcName;
        }

        return controller.gameObject.name;
    }

    [Serializable]
    private class SocietySchedulerRequest
    {
        public SocietySchedulerActorState[] eligibleActors =
            new SocietySchedulerActorState[0];

        public SocietySchedulerContext society =
            new SocietySchedulerContext();
    }

    [Serializable]
    private class SocietySchedulerActorState
    {
        public string name = string.Empty;
        public string role = string.Empty;
        public string goal = string.Empty;
        public string personality = string.Empty;
        public int mood = 50;
        public int trust = 50;
        public string[] recentMemories =
            new string[0];
    }

    [Serializable]
    private class SocietySchedulerContext
    {
        public string[] relationships =
            new string[0];

        public string[] recentSocialEvents =
            new string[0];
    }

    [Serializable]
    private class SocietySchedulerResponse
    {
        public string actorName = string.Empty;
        public string decisionSource = string.Empty;
        public string openclawModel = string.Empty;
        public string openclawResponseId = string.Empty;
        public int priority = 0;
        public int rawModelTextLength = 0;
        public string reason = string.Empty;
        public string requestId = string.Empty;
        public string schedulerRoute = string.Empty;
        public int serverTimestamp = 0;
        public string fallbackReason = string.Empty;
    }

}
