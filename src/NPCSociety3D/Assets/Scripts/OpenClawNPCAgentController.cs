using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
[RequireComponent(typeof(NPCAgent))]
public class OpenClawNPCAgentController : MonoBehaviour
{
    [Header("Bridge")]
    [SerializeField]
    private string bridgeUrl =
        "http://127.0.0.1:5055/decide_next_action";

    [SerializeField]
    private int requestTimeoutSeconds = 300;

    [Header("Context Limits")]
    [SerializeField]
    private int recentMemoryLimit = 5;

    [SerializeField]
    private int recentSocialEventLimit = 5;

    [Header("Execution")]
    [SerializeField]
    private float interactionDistanceOffset = 1.4f;

    [SerializeField]
    private float movementTimeoutSeconds = 20f;

    [SerializeField]
    private float arrivalTolerance = 0.35f;

    [Header("Scene References")]
    [SerializeField]
    private NPCAgent actor;

    [SerializeField]
    private NPCMemoryManager memoryManager;

    [SerializeField]
    private UILogManager uiLogManager;

    [Header("Diagnostics")]
    [SerializeField]
    private bool enableDebugLogs = true;

    private bool requestInProgress;

    private struct SocialSnapshot
    {
        public int actorMoodBefore;
        public int actorTrustBefore;
        public int targetMoodBefore;
        public int targetTrustBefore;

        public int relationshipTrustBefore;
        public int relationshipAffinityBefore;
        public int relationshipTensionBefore;
        public int relationshipInteractionCountBefore;
    }

    public bool IsRequestInProgress
    {
        get
        {
            return requestInProgress;
        }
    }

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    [ContextMenu("Request OpenClaw Decision")]
    public void RequestNextDecision()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "OpenClaw decisions can only be requested while the scene is running.",
                this
            );

            return;
        }

        if (requestInProgress)
        {
            Debug.LogWarning(
                "An OpenClaw request or execution is already in progress for this NPC.",
                this
            );

            return;
        }

        StartCoroutine(RequestDecisionCoroutine());
    }

    private IEnumerator RequestDecisionCoroutine()
    {
        ResolveReferences();

        if (!ValidateConfiguration())
        {
            yield break;
        }

        requestInProgress = true;

        OpenClawDecisionRequest decisionRequest =
            BuildDecisionRequest();

        string requestJson =
            JsonUtility.ToJson(decisionRequest);

        if (enableDebugLogs)
        {
            Debug.Log(
                "OpenClaw request started for " +
                actor.npcName +
                ".",
                this
            );

            Debug.Log(
                "OpenClaw request JSON: " +
                requestJson,
                this
            );
        }

        byte[] requestBody =
            Encoding.UTF8.GetBytes(requestJson);

        using (
            UnityWebRequest webRequest =
                new UnityWebRequest(
                    bridgeUrl,
                    UnityWebRequest.kHttpVerbPOST
                )
        )
        {
            webRequest.uploadHandler =
                new UploadHandlerRaw(requestBody);

            webRequest.downloadHandler =
                new DownloadHandlerBuffer();

            webRequest.SetRequestHeader(
                "Content-Type",
                "application/json; charset=utf-8"
            );

            webRequest.SetRequestHeader(
                "Accept",
                "application/json"
            );

            webRequest.timeout =
                requestTimeoutSeconds;

            yield return webRequest.SendWebRequest();

            if (
                webRequest.result !=
                UnityWebRequest.Result.Success
            )
            {
                HandleHttpError(webRequest);
                requestInProgress = false;
                yield break;
            }

            string responseJson =
                webRequest.downloadHandler.text;

            OpenClawDecisionResponse decision;

            if (
                !TryParseDecision(
                    responseJson,
                    out decision
                )
            )
            {
                requestInProgress = false;
                yield break;
            }

            if (!ValidateDecision(decision))
            {
                requestInProgress = false;
                yield break;
            }

            LogReceivedDecision(
                decision,
                responseJson
            );

            yield return ExecuteDecisionCoroutine(
                decision
            );
        }

        requestInProgress = false;
    }

    private OpenClawDecisionRequest BuildDecisionRequest()
    {
        OpenClawDecisionRequest request =
            new OpenClawDecisionRequest();

        request.actor =
            BuildActorState(actor);

        request.society =
            BuildSocietyState();

        return request;
    }

    private OpenClawActorState BuildActorState(
        NPCAgent sourceNpc
    )
    {
        return new OpenClawActorState
        {
            name = sourceNpc.npcName,
            role = sourceNpc.role,
            goal = sourceNpc.goal,
            personality = sourceNpc.personality,
            mood = sourceNpc.mood,
            trust = sourceNpc.trust,
            recentMemories = GetRecentMemories(
                sourceNpc,
                recentMemoryLimit
            )
        };
    }

            private OpenClawSocietyState BuildSocietyState()
    {
        OpenClawSocietyState society =
            new OpenClawSocietyState();

        NPCAgent[] sceneNpcs =
            FindObjectsOfType<NPCAgent>();

        Array.Sort(
            sceneNpcs,
            CompareNpcsByName
        );

        foreach (NPCAgent npc in sceneNpcs)
        {
            if (
                npc == null ||
                npc == actor ||
                string.IsNullOrWhiteSpace(
                    npc.npcName
                )
            )
            {
                continue;
            }

            society.npcs.Add(
                BuildNpcState(npc)
            );
        }

        AddActorRelationships(society);
        AddRecentSocialEvents(society);

        return society;
    }

    private OpenClawNpcState BuildNpcState(
        NPCAgent sourceNpc
    )
    {
        return new OpenClawNpcState
        {
            name = sourceNpc.npcName,
            role = sourceNpc.role,
            goal = sourceNpc.goal,
            personality = sourceNpc.personality,
            mood = sourceNpc.mood,
            trust = sourceNpc.trust,
            isBusy = false,
            isAvailable = true,
            recentMemories = GetRecentMemories(
                sourceNpc,
                recentMemoryLimit
            )
        };
    }

            private void AddActorRelationships(
        OpenClawSocietyState society
    )
    {
        if (
            society == null ||
            actor == null ||
            actor.relationships == null ||
            string.IsNullOrWhiteSpace(actor.npcName)
        )
        {
            return;
        }

        foreach (
            RelationshipState relationship
            in actor.relationships
        )
        {
            if (
                relationship == null ||
                string.IsNullOrWhiteSpace(
                    relationship.targetNpcName
                )
            )
            {
                continue;
            }

            society.relationships.Add(
                new OpenClawRelationshipState
                {
                    actorName = actor.npcName,
                    targetName =
                        relationship.targetNpcName,
                    trust = relationship.trust,
                    affinity = relationship.affinity,
                    tension = relationship.tension,
                    interactionCount =
                        relationship.interactionCount,
                    lastInteraction =
                        relationship.lastInteraction
                }
            );
        }
    }

    private void AddRecentSocialEvents(
        OpenClawSocietyState society
    )
    {
        if (
            memoryManager == null ||
            recentSocialEventLimit <= 0
        )
        {
            return;
        }

        List<SocialEvent> storedEvents =
            memoryManager.LoadSocialEvents();

        if (
            storedEvents == null ||
            storedEvents.Count == 0
        )
        {
            return;
        }

        int firstEventIndex =
            Mathf.Max(
                0,
                storedEvents.Count -
                recentSocialEventLimit
            );

        for (
            int index = firstEventIndex;
            index < storedEvents.Count;
            index++
        )
        {
            SocialEvent sourceEvent =
                storedEvents[index];

            if (sourceEvent == null)
            {
                continue;
            }

            string eventJson =
                JsonUtility.ToJson(sourceEvent);

            OpenClawSocialEventState convertedEvent =
                JsonUtility.FromJson
                <OpenClawSocialEventState>(
                    eventJson
                );

            if (convertedEvent != null)
            {
                society.recentSocialEvents.Add(
                    convertedEvent
                );
            }
        }
    }

    private List<string> GetRecentMemories(
        NPCAgent sourceNpc,
        int maximumEntries
    )
    {
        List<string> recentMemories =
            new List<string>();

        if (
            sourceNpc == null ||
            sourceNpc.memory == null ||
            sourceNpc.memory.Count == 0
        )
        {
            return recentMemories;
        }

        int firstMemoryIndex =
            Mathf.Max(
                0,
                sourceNpc.memory.Count -
                maximumEntries
            );

        for (
            int index = firstMemoryIndex;
            index < sourceNpc.memory.Count;
            index++
        )
        {
            string memory =
                sourceNpc.memory[index];

            if (!string.IsNullOrWhiteSpace(memory))
            {
                recentMemories.Add(memory);
            }
        }

        return recentMemories;
    }

    private bool TryParseDecision(
        string responseJson,
        out OpenClawDecisionResponse decision
    )
    {
        decision = null;

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            Debug.LogError(
                "The OpenClaw Bridge returned an empty response.",
                this
            );

            return false;
        }

        try
        {
            decision =
                JsonUtility.FromJson
                <OpenClawDecisionResponse>(
                    responseJson
                );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "The OpenClaw Bridge response could not be parsed: " +
                exception.Message,
                this
            );

            return false;
        }

        if (decision == null)
        {
            Debug.LogError(
                "The OpenClaw Bridge response produced a null decision.",
                this
            );

            return false;
        }

        return true;
    }

    private bool ValidateDecision(
        OpenClawDecisionResponse decision
    )
    {
        if (
            !string.Equals(
                decision.actorName,
                actor.npcName,
                StringComparison.Ordinal
            )
        )
        {
            Debug.LogError(
                "The Bridge returned actorName '" +
                decision.actorName +
                "', but this controller belongs to '" +
                actor.npcName +
                "'.",
                this
            );

            return false;
        }

        if (
            decision.decisionSource !=
                "openclaw_agent" &&
            decision.decisionSource !=
                "local_idle_fallback"
        )
        {
            Debug.LogError(
                "Unsupported decisionSource returned by the Bridge: " +
                decision.decisionSource,
                this
            );

            return false;
        }

        if (!IsSupportedAction(decision.actionType))
        {
            Debug.LogError(
                "Unsupported actionType returned by the Bridge: " +
                decision.actionType,
                this
            );

            return false;
        }

        if (decision.actionType == "Idle")
        {
            if (
                !string.IsNullOrWhiteSpace(
                    decision.targetName
                )
            )
            {
                Debug.LogError(
                    "An Idle decision cannot contain a target.",
                    this
                );

                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(decision.message))
        {
            Debug.LogError(
                "A non-Idle OpenClaw decision requires a message.",
                this
            );

            return false;
        }

        NPCAgent target =
            FindNpcByName(decision.targetName);

        if (target == null)
        {
            Debug.LogError(
                "The target returned by OpenClaw does not exist in the scene: " +
                decision.targetName,
                this
            );

            return false;
        }

        if (target == actor)
        {
            Debug.LogError(
                "OpenClaw returned the actor as its own target.",
                this
            );

            return false;
        }

        if (
            decision.relationshipDelta == null ||
            decision.actorDelta == null ||
            decision.targetDelta == null
        )
        {
            Debug.LogError(
                "The OpenClaw decision is missing one or more delta objects.",
                this
            );

            return false;
        }

        return true;
    }

    private IEnumerator ExecuteDecisionCoroutine(
        OpenClawDecisionResponse decision
    )
    {
        if (decision.actionType == "Idle")
        {
            RegisterIdleDecision(decision);
            yield break;
        }

        NPCAgent target =
            FindNpcByName(decision.targetName);

        if (target == null)
        {
            Debug.LogError(
                "The target disappeared before the decision could be executed: " +
                decision.targetName,
                this
            );

            yield break;
        }

        SocialSnapshot snapshot =
            CaptureSocialSnapshot(
                actor,
                target
            );

        Vector3 destination =
            CalculateDestinationNearTarget(
                actor,
                target
            );

        actor.MoveTo(destination);

        float elapsedTime = 0f;

        while (
            Vector3.Distance(
                actor.transform.position,
                destination
            ) > arrivalTolerance &&
            elapsedTime < movementTimeoutSeconds
        )
        {
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        if (
            Vector3.Distance(
                actor.transform.position,
                destination
            ) > arrivalTolerance
        )
        {
            Debug.LogError(
                actor.npcName +
                " did not reach the interaction position before the movement timeout. " +
                "The social decision was not applied.",
                this
            );

            yield break;
        }

        actor.FaceTarget(target);
        target.FaceTarget(actor);

        string timedMessage =
            BuildTimedInteractionMessage(
                decision,
                target
            );

        ApplyNpcDelta(
            actor,
            decision.actorDelta
        );

        ApplyNpcDelta(
            target,
            decision.targetDelta
        );

        actor.RegisterRelationshipInteraction(
            target,
            decision.relationshipDelta.trust,
            decision.relationshipDelta.affinity,
            decision.relationshipDelta.tension,
            timedMessage
        );

        target.RegisterRelationshipInteraction(
            actor,
            decision.relationshipDelta.trust,
            decision.relationshipDelta.affinity,
            decision.relationshipDelta.tension,
            timedMessage
        );

        ApplyAnimation(
            actor,
            target,
            decision.animationTrigger
        );

        actor.AddMemory(timedMessage);
        target.AddMemory(timedMessage);

        uiLogManager.AddLog(timedMessage);

        SocialEvent socialEvent =
            BuildSocialEvent(
                decision,
                actor,
                target,
                timedMessage,
                snapshot
            );

        memoryManager.SaveSocialEvent(
            socialEvent
        );

        Debug.Log(timedMessage, this);

        Debug.Log(
            actor.npcName +
            " -> " +
            target.npcName +
            " | " +
            actor.GetRelationshipSummaryWith(target),
            this
        );

        Debug.Log(
            target.npcName +
            " -> " +
            actor.npcName +
            " | " +
            target.GetRelationshipSummaryWith(actor),
            this
        );

        Debug.Log(
            "OpenClaw decision executed successfully" +
            " | Actor: " +
            actor.npcName +
            " | Target: " +
            target.npcName +
            " | Action: " +
            decision.actionType,
            this
        );
    }

    private void RegisterIdleDecision(
        OpenClawDecisionResponse decision
    )
    {
        if (
            decision.decisionSource ==
            "local_idle_fallback"
        )
        {
            Debug.LogWarning(
                "The Bridge used the safe Idle fallback for " +
                actor.npcName +
                ". Reason: " +
                decision.fallbackReason,
                this
            );

            return;
        }

        Debug.Log(
            "OpenClaw selected Idle for " +
            actor.npcName +
            ". Reason: " +
            decision.reason,
            this
        );
    }

    private void ApplyNpcDelta(
        NPCAgent npc,
        OpenClawNpcDelta delta
    )
    {
        if (npc == null || delta == null)
        {
            return;
        }

        npc.ChangeMood(delta.mood);
        npc.ChangeTrust(delta.trust);
    }

    private void ApplyAnimation(
        NPCAgent sourceNpc,
        NPCAgent targetNpc,
        string animationTrigger
    )
    {
        if (animationTrigger == "Idle")
        {
            return;
        }

        if (
            animationTrigger == "Talk" ||
            animationTrigger == "Work"
        )
        {
            sourceNpc.PlayTalkingAnimation();
            targetNpc.PlayTalkingAnimation();
            return;
        }

        Debug.LogWarning(
            "Unsupported animationTrigger returned by the Bridge: " +
            animationTrigger,
            this
        );
    }

    private Vector3 CalculateDestinationNearTarget(
        NPCAgent movingNpc,
        NPCAgent targetNpc
    )
    {
        Vector3 direction =
            movingNpc.transform.position -
            targetNpc.transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
        {
            direction =
                targetNpc.transform.forward;

            direction.y = 0f;
        }

        if (direction.sqrMagnitude < 0.01f)
        {
            direction = Vector3.forward;
        }

        return
            targetNpc.transform.position +
            direction.normalized *
            interactionDistanceOffset;
    }

    private SocialSnapshot CaptureSocialSnapshot(
        NPCAgent sourceNpc,
        NPCAgent targetNpc
    )
    {
        RelationshipState relationship =
            FindExistingRelationship(
                sourceNpc,
                targetNpc.npcName
            );

        return new SocialSnapshot
        {
            actorMoodBefore = sourceNpc.mood,
            actorTrustBefore = sourceNpc.trust,
            targetMoodBefore = targetNpc.mood,
            targetTrustBefore = targetNpc.trust,
            relationshipTrustBefore =
                relationship != null
                    ? relationship.trust
                    : 50,
            relationshipAffinityBefore =
                relationship != null
                    ? relationship.affinity
                    : 50,
            relationshipTensionBefore =
                relationship != null
                    ? relationship.tension
                    : 0,
            relationshipInteractionCountBefore =
                relationship != null
                    ? relationship.interactionCount
                    : 0
        };
    }

    private SocialEvent BuildSocialEvent(
        OpenClawDecisionResponse decision,
        NPCAgent sourceNpc,
        NPCAgent targetNpc,
        string timedMessage,
        SocialSnapshot snapshot
    )
    {
        RelationshipState relationshipAfter =
            FindExistingRelationship(
                sourceNpc,
                targetNpc.npcName
            );

        int relationshipTrustAfter =
            relationshipAfter != null
                ? relationshipAfter.trust
                : snapshot.relationshipTrustBefore;

        int relationshipAffinityAfter =
            relationshipAfter != null
                ? relationshipAfter.affinity
                : snapshot.relationshipAffinityBefore;

        int relationshipTensionAfter =
            relationshipAfter != null
                ? relationshipAfter.tension
                : snapshot.relationshipTensionBefore;

        int relationshipInteractionCountAfter =
            relationshipAfter != null
                ? relationshipAfter.interactionCount
                : snapshot.relationshipInteractionCountBefore;

        return new SocialEvent
        {
            eventId = Guid.NewGuid().ToString(),
            realTimestamp = DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss"
            ),
            sceneTimestamp = GetSceneTimestamp(),

            actorName = sourceNpc.npcName,
            actorRole = sourceNpc.role,
            actorPersonality = sourceNpc.personality,

            targetName = targetNpc.npcName,
            targetRole = targetNpc.role,
            targetPersonality = targetNpc.personality,

            interactionType =
                !string.IsNullOrWhiteSpace(
                    decision.interactionType
                )
                    ? decision.interactionType
                    : decision.actionType,

            description = timedMessage,

            actorMoodBefore = snapshot.actorMoodBefore,
            actorMoodAfter = sourceNpc.mood,
            actorTrustBefore = snapshot.actorTrustBefore,
            actorTrustAfter = sourceNpc.trust,

            targetMoodBefore = snapshot.targetMoodBefore,
            targetMoodAfter = targetNpc.mood,
            targetTrustBefore = snapshot.targetTrustBefore,
            targetTrustAfter = targetNpc.trust,

            relationshipTrustBefore =
                snapshot.relationshipTrustBefore,
            relationshipTrustAfter =
                relationshipTrustAfter,
            relationshipAffinityBefore =
                snapshot.relationshipAffinityBefore,
            relationshipAffinityAfter =
                relationshipAffinityAfter,
            relationshipTensionBefore =
                snapshot.relationshipTensionBefore,
            relationshipTensionAfter =
                relationshipTensionAfter,
            relationshipInteractionCountBefore =
                snapshot.relationshipInteractionCountBefore,
            relationshipInteractionCountAfter =
                relationshipInteractionCountAfter,

            relationshipTrustDelta =
                relationshipTrustAfter -
                snapshot.relationshipTrustBefore,
            relationshipAffinityDelta =
                relationshipAffinityAfter -
                snapshot.relationshipAffinityBefore,
            relationshipTensionDelta =
                relationshipTensionAfter -
                snapshot.relationshipTensionBefore
        };
    }

    private RelationshipState FindExistingRelationship(
        NPCAgent sourceNpc,
        string targetNpcName
    )
    {
        if (
            sourceNpc == null ||
            sourceNpc.relationships == null ||
            string.IsNullOrWhiteSpace(targetNpcName)
        )
        {
            return null;
        }

        return sourceNpc.relationships.Find(
            relationship =>
                relationship != null &&
                relationship.targetNpcName ==
                targetNpcName
        );
    }

    private NPCAgent FindNpcByName(
        string npcName
    )
    {
        if (string.IsNullOrWhiteSpace(npcName))
        {
            return null;
        }

        NPCAgent[] sceneNpcs =
            FindObjectsOfType<NPCAgent>();

        foreach (NPCAgent npc in sceneNpcs)
        {
            if (
                npc != null &&
                string.Equals(
                    npc.npcName,
                    npcName,
                    StringComparison.Ordinal
                )
            )
            {
                return npc;
            }
        }

        return null;
    }

    private bool IsSupportedAction(
        string actionType
    )
    {
        return
            actionType == "Idle" ||
            actionType == "Talk" ||
            actionType == "AskForHelp" ||
            actionType == "Collaborate" ||
            actionType == "PositiveRelation" ||
            actionType == "Conflict" ||
            actionType == "ShareMemory";
    }

    private void HandleHttpError(
        UnityWebRequest webRequest
    )
    {
        string responseText =
            webRequest.downloadHandler != null
                ? webRequest.downloadHandler.text
                : "";

        OpenClawErrorResponse bridgeError = null;

        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                bridgeError =
                    JsonUtility.FromJson
                    <OpenClawErrorResponse>(
                        responseText
                    );
            }
            catch (Exception)
            {
                bridgeError = null;
            }
        }

        string bridgeMessage =
            bridgeError != null &&
            !string.IsNullOrWhiteSpace(
                bridgeError.details
            )
                ? bridgeError.details
                : responseText;

        Debug.LogError(
            "OpenClaw Bridge request failed." +
            " HTTP status: " +
            webRequest.responseCode +
            " | Unity error: " +
            webRequest.error +
            " | Bridge response: " +
            bridgeMessage,
            this
        );
    }

    private void LogReceivedDecision(
        OpenClawDecisionResponse decision,
        string responseJson
    )
    {
        Debug.Log(
            "OpenClaw decision received" +
            " | Source: " +
            decision.decisionSource +
            " | Route: " +
            decision.agentRoute +
            " | Actor: " +
            decision.actorName +
            " | Target: " +
            decision.targetName +
            " | Action: " +
            decision.actionType +
            " | Message: " +
            decision.message +
            " | Reason: " +
            decision.reason,
            this
        );

        if (enableDebugLogs)
        {
            Debug.Log(
                "OpenClaw response JSON: " +
                responseJson,
                this
            );
        }
    }

    private string BuildTimedInteractionMessage(
        OpenClawDecisionResponse decision,
        NPCAgent target
    )
    {
        return
            GetSceneTimestamp() +
            " " +
            actor.npcName +
            " -> " +
            target.npcName +
            ": " +
            decision.message;
    }

    private string GetSceneTimestamp()
    {
        int totalSeconds =
            Mathf.FloorToInt(
                Time.timeSinceLevelLoad
            );

        int minutes =
            totalSeconds / 60;

        int seconds =
            totalSeconds % 60;

        return
            $"[{minutes:00}:{seconds:00}]";
    }

    private int CompareNpcsByName(
        NPCAgent firstNpc,
        NPCAgent secondNpc
    )
    {
        string firstName =
            firstNpc != null
                ? firstNpc.npcName
                : "";

        string secondName =
            secondNpc != null
                ? secondNpc.npcName
                : "";

        return string.Compare(
            firstName,
            secondName,
            StringComparison.Ordinal
        );
    }

    private void ResolveReferences()
    {
        if (actor == null)
        {
            actor =
                GetComponent<NPCAgent>();
        }

        if (memoryManager == null)
        {
            memoryManager =
                FindObjectOfType
                <NPCMemoryManager>();
        }

        if (uiLogManager == null)
        {
            uiLogManager =
                FindObjectOfType
                <UILogManager>();
        }
    }

    private bool ValidateConfiguration()
    {
        if (actor == null)
        {
            Debug.LogError(
                "OpenClawNPCAgentController requires an NPCAgent component.",
                this
            );

            return false;
        }

        if (string.IsNullOrWhiteSpace(actor.npcName))
        {
            Debug.LogError(
                "The NPCAgent used by OpenClaw has no npcName.",
                this
            );

            return false;
        }

        if (memoryManager == null)
        {
            Debug.LogError(
                "OpenClawNPCAgentController could not find NPCMemoryManager.",
                this
            );

            return false;
        }

        if (uiLogManager == null)
        {
            Debug.LogError(
                "OpenClawNPCAgentController could not find UILogManager.",
                this
            );

            return false;
        }

        if (string.IsNullOrWhiteSpace(bridgeUrl))
        {
            Debug.LogError(
                "The OpenClaw Bridge URL is empty.",
                this
            );

            return false;
        }

        return true;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        requestInProgress = false;
    }

    private void OnValidate()
    {
        requestTimeoutSeconds =
            Mathf.Max(
                1,
                requestTimeoutSeconds
            );

        recentMemoryLimit =
            Mathf.Clamp(
                recentMemoryLimit,
                1,
                20
            );

        recentSocialEventLimit =
            Mathf.Clamp(
                recentSocialEventLimit,
                0,
                20
            );

        interactionDistanceOffset =
            Mathf.Max(
                0.5f,
                interactionDistanceOffset
            );

        movementTimeoutSeconds =
            Mathf.Max(
                1f,
                movementTimeoutSeconds
            );

        arrivalTolerance =
            Mathf.Clamp(
                arrivalTolerance,
                0.25f,
                1f
            );
    }
}
