using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NPCInteractionManager : MonoBehaviour
{
    private enum InteractionType
    {
        Talk,
        AskForHelp,
        Collaborate,
        PositiveRelation,
        Conflict,
        ShareMemory
    }

    private struct InteractionWeights
    {
        public int talk;
        public int askForHelp;
        public int collaborate;
        public int positiveRelation;
        public int conflict;
        public int shareMemory;
    }

    private struct RelationshipEffect
    {
        public int trustDelta;
        public int affinityDelta;
        public int tensionDelta;
    }

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

    [Header("NPCs da Sociedade")]
    public NPCAgent npcAfonso;
    public NPCAgent npcMarta;
    public NPCAgent npcDuarte;
    public NPCAgent npcLia;

    [Header("Configuração")]
    public float interactionInterval = 8f;
    public float interactionDistanceOffset = 1.4f;
    public float interactionApproachDelay = 2f;

    [Header("Decisão Social")]
    public bool useRelationshipAwareDecisions = true;
    public bool useRoleAwareDecisions = true;
    public bool enableDecisionDebugLogs = false;

    [Header("Equilíbrio Social")]
    public bool useSocialBalance = true;
    public int relationshipSaturationThreshold = 90;
    public int lowTensionThreshold = 5;
    public int longRelationshipInteractionThreshold = 20;
    public bool enableSocialBalanceDebugLogs = false;

    [Header("Eventos Sociais")]
    public bool persistSocialEvents = true;
    public bool enableSocialEventDebugLogs = false;

    [Header("UI")]
    public UILogManager uiLogManager;

    private float timer = 0f;
    private bool interactionInProgress = false;
    private NPCMemoryManager memoryManager;

    private void Start()
    {
        memoryManager = FindObjectOfType<NPCMemoryManager>();

        List<NPCAgent> validNpcs = GetValidNpcs();

        if (validNpcs.Count < 2)
        {
            Debug.LogWarning("NPCInteractionManager precisa de pelo menos 2 NPCs válidos.");
        }
    }

    private void Update()
    {
        if (interactionInProgress)
        {
            return;
        }

        timer += Time.deltaTime;

        if (timer >= interactionInterval)
        {
            StartCoroutine(SimulateRandomInteraction());
            timer = 0f;
        }
    }

    private IEnumerator SimulateRandomInteraction()
    {
        interactionInProgress = true;

        List<NPCAgent> validNpcs = GetValidNpcs();

        if (validNpcs.Count < 2)
        {
            interactionInProgress = false;
            yield break;
        }

        SelectRandomPair(validNpcs, out NPCAgent firstNpc, out NPCAgent secondNpc);

        InteractionType interactionType = ChooseInteractionType(firstNpc, secondNpc);
        RelationshipEffect relationshipEffect = GetRelationshipEffect(interactionType);
        SocialSnapshot snapshot = CaptureSocialSnapshot(firstNpc, secondNpc);

        MoveNpcNearTarget(firstNpc, secondNpc);

        yield return new WaitForSeconds(interactionApproachDelay);

        firstNpc.FaceTarget(secondNpc);
        secondNpc.FaceTarget(firstNpc);

        ApplyInteractionEffects(firstNpc, secondNpc, interactionType);

        string actionMessage = GenerateActionMessage(firstNpc, secondNpc, interactionType);

        ApplyRelationshipEffects(firstNpc, secondNpc, relationshipEffect, actionMessage);

        string timedMessage = AddTimePrefix(actionMessage);

        SocialEvent socialEvent = BuildSocialEvent(
            firstNpc,
            secondNpc,
            interactionType,
            timedMessage,
            relationshipEffect,
            snapshot
        );

        RegisterInteraction(firstNpc, secondNpc, timedMessage);
        RegisterSocialEvent(socialEvent);

        interactionInProgress = false;
    }

    private List<NPCAgent> GetValidNpcs()
    {
        List<NPCAgent> validNpcs = new List<NPCAgent>();

        if (npcAfonso != null)
        {
            validNpcs.Add(npcAfonso);
        }

        if (npcMarta != null)
        {
            validNpcs.Add(npcMarta);
        }

        if (npcDuarte != null)
        {
            validNpcs.Add(npcDuarte);
        }

        if (npcLia != null)
        {
            validNpcs.Add(npcLia);
        }

        return validNpcs;
    }

    private void SelectRandomPair(
        List<NPCAgent> validNpcs,
        out NPCAgent firstNpc,
        out NPCAgent secondNpc
    )
    {
        int firstIndex = UnityEngine.Random.Range(0, validNpcs.Count);
        int secondIndex = UnityEngine.Random.Range(0, validNpcs.Count - 1);

        if (secondIndex >= firstIndex)
        {
            secondIndex++;
        }

        firstNpc = validNpcs[firstIndex];
        secondNpc = validNpcs[secondIndex];
    }

    private InteractionType ChooseInteractionType(NPCAgent firstNpc, NPCAgent secondNpc)
    {
        InteractionWeights weights = CreateBaseWeights();

        RelationshipState relationship = firstNpc.GetRelationshipWith(secondNpc);

        if (useRelationshipAwareDecisions && relationship != null)
        {
            ApplyRelationshipWeights(ref weights, relationship);
        }

        ApplyMoodWeights(ref weights, firstNpc, secondNpc);

        if (useRoleAwareDecisions)
        {
            ApplyRoleWeights(ref weights, firstNpc, secondNpc, relationship);
            ApplyPersonalityWeights(ref weights, firstNpc, relationship);
            ApplyGoalWeights(ref weights, firstNpc, relationship);
        }

        if (useSocialBalance && relationship != null)
        {
            ApplySocialBalanceWeights(ref weights, relationship, firstNpc, secondNpc);
        }

        NormalizeWeights(ref weights);

        InteractionType selectedInteraction = RollInteraction(weights);

        if (enableDecisionDebugLogs)
        {
            Debug.Log(
                "Decision for " + firstNpc.npcName + " -> " + secondNpc.npcName +
                " | Talk: " + weights.talk +
                ", AskForHelp: " + weights.askForHelp +
                ", Collaborate: " + weights.collaborate +
                ", PositiveRelation: " + weights.positiveRelation +
                ", Conflict: " + weights.conflict +
                ", ShareMemory: " + weights.shareMemory +
                " | Selected: " + selectedInteraction
            );
        }

        return selectedInteraction;
    }

    private InteractionWeights CreateBaseWeights()
    {
        return new InteractionWeights
        {
            talk = 25,
            askForHelp = 15,
            collaborate = 15,
            positiveRelation = 15,
            conflict = 10,
            shareMemory = 15
        };
    }

    private void ApplyRelationshipWeights(ref InteractionWeights weights, RelationshipState relationship)
    {
        if (relationship.tension >= 70)
        {
            weights.conflict += 45;
            weights.talk += 15;
            weights.collaborate -= 15;
            weights.positiveRelation -= 10;
            weights.shareMemory -= 5;
        }
        else if (relationship.tension >= 40)
        {
            weights.conflict += 25;
            weights.talk += 10;
            weights.collaborate -= 5;
            weights.positiveRelation -= 5;
        }
        else if (relationship.tension <= 10)
        {
            weights.positiveRelation += 5;
            weights.collaborate += 5;
        }

        if (relationship.trust >= 75)
        {
            weights.collaborate += 30;
            weights.shareMemory += 20;
            weights.askForHelp += 10;
            weights.conflict -= 10;
        }
        else if (relationship.trust >= 60)
        {
            weights.collaborate += 15;
            weights.shareMemory += 10;
            weights.conflict -= 5;
        }
        else if (relationship.trust <= 30)
        {
            weights.conflict += 25;
            weights.talk += 15;
            weights.collaborate -= 15;
            weights.shareMemory -= 10;
        }

        if (relationship.affinity >= 75)
        {
            weights.talk += 20;
            weights.positiveRelation += 30;
            weights.shareMemory += 15;
            weights.conflict -= 10;
        }
        else if (relationship.affinity >= 60)
        {
            weights.talk += 10;
            weights.positiveRelation += 15;
            weights.shareMemory += 10;
        }
        else if (relationship.affinity <= 30)
        {
            weights.conflict += 15;
            weights.positiveRelation -= 10;
        }

        if (relationship.interactionCount == 0)
        {
            weights.talk += 25;
            weights.positiveRelation += 10;
            weights.conflict -= 10;
        }
        else if (relationship.interactionCount >= 8 && relationship.trust >= 60)
        {
            weights.shareMemory += 15;
            weights.collaborate += 10;
        }
    }

    private void ApplySocialBalanceWeights(
        ref InteractionWeights weights,
        RelationshipState relationship,
        NPCAgent firstNpc,
        NPCAgent secondNpc
    )
    {
        bool relationshipIsSaturated =
            relationship.trust >= relationshipSaturationThreshold &&
            relationship.affinity >= relationshipSaturationThreshold &&
            relationship.tension <= lowTensionThreshold;

        if (relationshipIsSaturated)
        {
            weights.conflict += 35;
            weights.talk += 20;
            weights.askForHelp += 5;
            weights.positiveRelation -= 25;
            weights.collaborate -= 10;
            weights.shareMemory -= 10;

            if (enableSocialBalanceDebugLogs)
            {
                Debug.Log(
                    "Social balance applied to saturated relationship: " +
                    firstNpc.npcName + " -> " + secondNpc.npcName +
                    " | Trust: " + relationship.trust +
                    ", Affinity: " + relationship.affinity +
                    ", Tension: " + relationship.tension
                );
            }
        }

        if (relationship.interactionCount >= longRelationshipInteractionThreshold)
        {
            weights.talk += 10;

            if (relationship.trust >= 80 && relationship.affinity >= 80)
            {
                weights.conflict += 15;
                weights.positiveRelation -= 10;
            }
        }

        if (relationship.tension > 0 && relationship.tension <= 20)
        {
            weights.talk += 15;
            weights.positiveRelation += 5;
            weights.conflict += 5;
        }
    }

    private void ApplyMoodWeights(ref InteractionWeights weights, NPCAgent firstNpc, NPCAgent secondNpc)
    {
        if (firstNpc.mood <= 25)
        {
            weights.conflict += 20;
            weights.askForHelp += 15;
            weights.positiveRelation -= 10;
            weights.collaborate -= 5;
        }
        else if (firstNpc.mood <= 40)
        {
            weights.askForHelp += 10;
            weights.talk += 10;
            weights.conflict += 5;
        }
        else if (firstNpc.mood >= 75)
        {
            weights.talk += 10;
            weights.collaborate += 15;
            weights.positiveRelation += 15;
            weights.conflict -= 10;
        }

        if (secondNpc != null && secondNpc.mood <= 30)
        {
            weights.talk += 10;
            weights.askForHelp += 10;
            weights.positiveRelation += 10;
        }
    }

    private void ApplyRoleWeights(
        ref InteractionWeights weights,
        NPCAgent firstNpc,
        NPCAgent secondNpc,
        RelationshipState relationship
    )
    {
        string role = NormalizeText(firstNpc.role);

        if (role.Contains("ferreiro"))
        {
            weights.collaborate += 25;
            weights.talk += 5;

            if (relationship != null && relationship.trust >= 60)
            {
                weights.collaborate += 15;
            }

            if (relationship != null && relationship.tension >= 50)
            {
                weights.conflict += 10;
                weights.talk += 10;
            }
        }

        if (role.Contains("agricultor") || role.Contains("agricultora"))
        {
            weights.collaborate += 20;
            weights.positiveRelation += 15;
            weights.askForHelp += 5;
            weights.conflict -= 10;

            if (relationship != null && relationship.affinity >= 60)
            {
                weights.positiveRelation += 15;
            }
        }

        if (role.Contains("guarda"))
        {
            weights.talk += 10;

            if (relationship != null && relationship.tension >= 50)
            {
                weights.conflict += 30;
                weights.talk += 20;
            }

            if (relationship != null && relationship.trust <= 35)
            {
                weights.conflict += 20;
            }

            if (secondNpc != null && secondNpc.mood <= 30)
            {
                weights.askForHelp += 10;
                weights.talk += 10;
            }
        }

        if (role.Contains("curandeiro") || role.Contains("curandeira"))
        {
            weights.askForHelp += 20;
            weights.positiveRelation += 25;
            weights.shareMemory += 15;
            weights.conflict -= 20;

            if (relationship != null && relationship.tension >= 30)
            {
                weights.talk += 20;
                weights.positiveRelation += 15;
                weights.conflict -= 10;
            }

            if (secondNpc != null && secondNpc.mood <= 40)
            {
                weights.askForHelp += 20;
                weights.positiveRelation += 10;
            }
        }
    }

    private void ApplyPersonalityWeights(
        ref InteractionWeights weights,
        NPCAgent firstNpc,
        RelationshipState relationship
    )
    {
        string personality = NormalizeText(firstNpc.personality);

        if (personality.Contains("cooperativ"))
        {
            weights.collaborate += 20;
            weights.positiveRelation += 15;
            weights.conflict -= 10;
        }

        if (personality.Contains("calm"))
        {
            weights.talk += 10;
            weights.conflict -= 15;
        }

        if (personality.Contains("pratic"))
        {
            weights.collaborate += 10;
            weights.askForHelp += 5;
        }

        if (personality.Contains("trabalhador"))
        {
            weights.collaborate += 20;
        }

        if (personality.Contains("direto") || personality.Contains("direta"))
        {
            weights.talk += 10;

            if (relationship != null && relationship.tension >= 40)
            {
                weights.conflict += 10;
            }
        }

        if (personality.Contains("competitiv"))
        {
            weights.collaborate += 10;

            if (relationship != null && relationship.tension >= 40)
            {
                weights.conflict += 15;
            }
        }

        if (personality.Contains("serio") || personality.Contains("sério"))
        {
            weights.talk += 10;
        }

        if (personality.Contains("protetor") || personality.Contains("protetora"))
        {
            weights.askForHelp += 10;
            weights.talk += 10;

            if (relationship != null && relationship.tension >= 50)
            {
                weights.conflict += 20;
            }
        }

        if (personality.Contains("desconfiad"))
        {
            if (relationship != null && relationship.trust <= 45)
            {
                weights.conflict += 20;
                weights.shareMemory -= 15;
                weights.collaborate -= 10;
            }
        }

        if (personality.Contains("empatic") || personality.Contains("empátic"))
        {
            weights.positiveRelation += 20;
            weights.askForHelp += 15;
            weights.conflict -= 15;
        }

        if (personality.Contains("paciente"))
        {
            weights.talk += 15;
            weights.conflict -= 15;
        }

        if (personality.Contains("prestavel") || personality.Contains("prestável"))
        {
            weights.askForHelp += 20;
            weights.positiveRelation += 10;
        }
    }

    private void ApplyGoalWeights(
        ref InteractionWeights weights,
        NPCAgent firstNpc,
        RelationshipState relationship
    )
    {
        string goal = NormalizeText(firstNpc.goal);

        if (goal.Contains("produzir") || goal.Contains("trocar recursos"))
        {
            weights.collaborate += 15;
            weights.talk += 5;
        }

        if (goal.Contains("alimentos") || goal.Contains("sustentar"))
        {
            weights.collaborate += 15;
            weights.positiveRelation += 10;
        }

        if (goal.Contains("proteger") || goal.Contains("evitar conflitos"))
        {
            weights.talk += 15;

            if (relationship != null && relationship.tension >= 40)
            {
                weights.conflict += 20;
            }
            else
            {
                weights.positiveRelation += 5;
            }
        }

        if (goal.Contains("ajudar") || goal.Contains("dificuldade"))
        {
            weights.askForHelp += 20;
            weights.positiveRelation += 15;
            weights.conflict -= 10;
        }
    }

    private string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        return text.ToLowerInvariant();
    }

    private void NormalizeWeights(ref InteractionWeights weights)
    {
        weights.talk = Mathf.Max(1, weights.talk);
        weights.askForHelp = Mathf.Max(1, weights.askForHelp);
        weights.collaborate = Mathf.Max(1, weights.collaborate);
        weights.positiveRelation = Mathf.Max(1, weights.positiveRelation);
        weights.conflict = Mathf.Max(1, weights.conflict);
        weights.shareMemory = Mathf.Max(1, weights.shareMemory);
    }

    private InteractionType RollInteraction(InteractionWeights weights)
    {
        int totalWeight =
            weights.talk +
            weights.askForHelp +
            weights.collaborate +
            weights.positiveRelation +
            weights.conflict +
            weights.shareMemory;

        int roll = UnityEngine.Random.Range(0, totalWeight);

        if (roll < weights.talk)
        {
            return InteractionType.Talk;
        }

        roll -= weights.talk;

        if (roll < weights.askForHelp)
        {
            return InteractionType.AskForHelp;
        }

        roll -= weights.askForHelp;

        if (roll < weights.collaborate)
        {
            return InteractionType.Collaborate;
        }

        roll -= weights.collaborate;

        if (roll < weights.positiveRelation)
        {
            return InteractionType.PositiveRelation;
        }

        roll -= weights.positiveRelation;

        if (roll < weights.conflict)
        {
            return InteractionType.Conflict;
        }

        return InteractionType.ShareMemory;
    }

    private void MoveNpcNearTarget(NPCAgent movingNpc, NPCAgent targetNpc)
    {
        Vector3 direction = movingNpc.transform.position - targetNpc.transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
        {
            Vector2 randomDirection = UnityEngine.Random.insideUnitCircle.normalized;
            direction = new Vector3(randomDirection.x, 0f, randomDirection.y);
        }

        Vector3 destination = targetNpc.transform.position + direction.normalized * interactionDistanceOffset;

        movingNpc.MoveTo(destination);
    }

    private void ApplyInteractionEffects(
        NPCAgent firstNpc,
        NPCAgent secondNpc,
        InteractionType interactionType
    )
    {
        switch (interactionType)
        {
            case InteractionType.Talk:
                firstNpc.ChangeMood(1);
                secondNpc.ChangeMood(1);
                firstNpc.ChangeTrust(0);
                secondNpc.ChangeTrust(0);
                break;

            case InteractionType.AskForHelp:
                firstNpc.ChangeMood(1);
                secondNpc.ChangeMood(2);
                firstNpc.ChangeTrust(1);
                secondNpc.ChangeTrust(1);
                break;

            case InteractionType.Collaborate:
                firstNpc.ChangeMood(2);
                secondNpc.ChangeMood(2);
                firstNpc.ChangeTrust(2);
                secondNpc.ChangeTrust(2);
                break;

            case InteractionType.PositiveRelation:
                firstNpc.ChangeMood(2);
                secondNpc.ChangeMood(2);
                firstNpc.ChangeTrust(2);
                secondNpc.ChangeTrust(2);
                break;

            case InteractionType.Conflict:
                firstNpc.ChangeMood(-5);
                secondNpc.ChangeMood(-5);
                firstNpc.ChangeTrust(-6);
                secondNpc.ChangeTrust(-6);
                break;

            case InteractionType.ShareMemory:
                firstNpc.ChangeMood(1);
                secondNpc.ChangeMood(1);
                firstNpc.ChangeTrust(1);
                secondNpc.ChangeTrust(1);
                break;
        }
    }

    private RelationshipEffect GetRelationshipEffect(InteractionType interactionType)
    {
        switch (interactionType)
        {
            case InteractionType.Talk:
                return new RelationshipEffect
                {
                    trustDelta = 0,
                    affinityDelta = 1,
                    tensionDelta = -1
                };

            case InteractionType.AskForHelp:
                return new RelationshipEffect
                {
                    trustDelta = 1,
                    affinityDelta = 1,
                    tensionDelta = 0
                };

            case InteractionType.Collaborate:
                return new RelationshipEffect
                {
                    trustDelta = 2,
                    affinityDelta = 2,
                    tensionDelta = -1
                };

            case InteractionType.PositiveRelation:
                return new RelationshipEffect
                {
                    trustDelta = 3,
                    affinityDelta = 3,
                    tensionDelta = -2
                };

            case InteractionType.Conflict:
                return new RelationshipEffect
                {
                    trustDelta = -8,
                    affinityDelta = -6,
                    tensionDelta = 12
                };

            case InteractionType.ShareMemory:
                return new RelationshipEffect
                {
                    trustDelta = 2,
                    affinityDelta = 1,
                    tensionDelta = -1
                };

            default:
                return new RelationshipEffect();
        }
    }

    private void ApplyRelationshipEffects(
        NPCAgent firstNpc,
        NPCAgent secondNpc,
        RelationshipEffect relationshipEffect,
        string actionMessage
    )
    {
        firstNpc.RegisterRelationshipInteraction(
            secondNpc,
            relationshipEffect.trustDelta,
            relationshipEffect.affinityDelta,
            relationshipEffect.tensionDelta,
            actionMessage
        );

        secondNpc.RegisterRelationshipInteraction(
            firstNpc,
            relationshipEffect.trustDelta,
            relationshipEffect.affinityDelta,
            relationshipEffect.tensionDelta,
            actionMessage
        );
    }

    private SocialSnapshot CaptureSocialSnapshot(NPCAgent firstNpc, NPCAgent secondNpc)
    {
        RelationshipState relationship = firstNpc.GetRelationshipWith(secondNpc);

        SocialSnapshot snapshot = new SocialSnapshot
        {
            actorMoodBefore = firstNpc.mood,
            actorTrustBefore = firstNpc.trust,
            targetMoodBefore = secondNpc.mood,
            targetTrustBefore = secondNpc.trust,
            relationshipTrustBefore = relationship != null ? relationship.trust : 50,
            relationshipAffinityBefore = relationship != null ? relationship.affinity : 50,
            relationshipTensionBefore = relationship != null ? relationship.tension : 0,
            relationshipInteractionCountBefore = relationship != null ? relationship.interactionCount : 0
        };

        return snapshot;
    }

    private SocialEvent BuildSocialEvent(
        NPCAgent firstNpc,
        NPCAgent secondNpc,
        InteractionType interactionType,
        string timedMessage,
        RelationshipEffect relationshipEffect,
        SocialSnapshot snapshot
    )
    {
        RelationshipState relationshipAfter = firstNpc.GetRelationshipWith(secondNpc);

        int relationshipTrustAfter = relationshipAfter != null
            ? relationshipAfter.trust
            : snapshot.relationshipTrustBefore;

        int relationshipAffinityAfter = relationshipAfter != null
            ? relationshipAfter.affinity
            : snapshot.relationshipAffinityBefore;

        int relationshipTensionAfter = relationshipAfter != null
            ? relationshipAfter.tension
            : snapshot.relationshipTensionBefore;

        int relationshipInteractionCountAfter = relationshipAfter != null
            ? relationshipAfter.interactionCount
            : snapshot.relationshipInteractionCountBefore;

        return new SocialEvent
        {
            eventId = Guid.NewGuid().ToString(),
            realTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            sceneTimestamp = GetSceneTimestamp(),

            actorName = firstNpc.npcName,
            actorRole = firstNpc.role,
            actorPersonality = firstNpc.personality,

            targetName = secondNpc.npcName,
            targetRole = secondNpc.role,
            targetPersonality = secondNpc.personality,

            interactionType = interactionType.ToString(),
            description = timedMessage,

            actorMoodBefore = snapshot.actorMoodBefore,
            actorMoodAfter = firstNpc.mood,
            actorTrustBefore = snapshot.actorTrustBefore,
            actorTrustAfter = firstNpc.trust,

            targetMoodBefore = snapshot.targetMoodBefore,
            targetMoodAfter = secondNpc.mood,
            targetTrustBefore = snapshot.targetTrustBefore,
            targetTrustAfter = secondNpc.trust,

            relationshipTrustBefore = snapshot.relationshipTrustBefore,
            relationshipTrustAfter = relationshipTrustAfter,
            relationshipAffinityBefore = snapshot.relationshipAffinityBefore,
            relationshipAffinityAfter = relationshipAffinityAfter,
            relationshipTensionBefore = snapshot.relationshipTensionBefore,
            relationshipTensionAfter = relationshipTensionAfter,
            relationshipInteractionCountBefore = snapshot.relationshipInteractionCountBefore,
            relationshipInteractionCountAfter = relationshipInteractionCountAfter,

            relationshipTrustDelta = relationshipTrustAfter - snapshot.relationshipTrustBefore,
            relationshipAffinityDelta = relationshipAffinityAfter - snapshot.relationshipAffinityBefore,
            relationshipTensionDelta = relationshipTensionAfter - snapshot.relationshipTensionBefore
        };
    }

    private void RegisterInteraction(
        NPCAgent firstNpc,
        NPCAgent secondNpc,
        string actionMessage
    )
    {
        firstNpc.AddMemory(actionMessage);
        secondNpc.AddMemory(actionMessage);

        firstNpc.PlayTalkingAnimation();
        secondNpc.PlayTalkingAnimation();

        Debug.Log(actionMessage);

        Debug.Log(
            firstNpc.npcName + " -> " + secondNpc.npcName + " | " +
            firstNpc.GetRelationshipSummaryWith(secondNpc)
        );

        Debug.Log(
            secondNpc.npcName + " -> " + firstNpc.npcName + " | " +
            secondNpc.GetRelationshipSummaryWith(firstNpc)
        );

        if (uiLogManager != null)
        {
            uiLogManager.AddLog(actionMessage);
        }
    }

    private void RegisterSocialEvent(SocialEvent socialEvent)
    {
        if (!persistSocialEvents || socialEvent == null)
        {
            return;
        }

        if (memoryManager == null)
        {
            memoryManager = FindObjectOfType<NPCMemoryManager>();
        }

        if (memoryManager == null)
        {
            return;
        }

        memoryManager.SaveSocialEvent(socialEvent);

        if (enableSocialEventDebugLogs)
        {
            Debug.Log(
                "SocialEvent saved | " +
                socialEvent.actorName + " -> " + socialEvent.targetName +
                " | Type: " + socialEvent.interactionType +
                " | Trust: " + socialEvent.relationshipTrustBefore + " -> " + socialEvent.relationshipTrustAfter +
                " | Affinity: " + socialEvent.relationshipAffinityBefore + " -> " + socialEvent.relationshipAffinityAfter +
                " | Tension: " + socialEvent.relationshipTensionBefore + " -> " + socialEvent.relationshipTensionAfter
            );
        }
    }

    private string GenerateActionMessage(
        NPCAgent firstNpc,
        NPCAgent secondNpc,
        InteractionType interactionType
    )
    {
        switch (interactionType)
        {
            case InteractionType.Talk:
                return $"{firstNpc.npcName} falou com {secondNpc.npcName} sobre os objetivos da vila.";

            case InteractionType.AskForHelp:
                return $"{firstNpc.npcName} pediu ajuda a {secondNpc.npcName}.";

            case InteractionType.Collaborate:
                return $"{firstNpc.npcName} colaborou com {secondNpc.npcName} numa tarefa da vila.";

            case InteractionType.PositiveRelation:
                return $"{firstNpc.npcName} criou uma relação positiva com {secondNpc.npcName}.";

            case InteractionType.Conflict:
                return $"{firstNpc.npcName} teve uma pequena discussão com {secondNpc.npcName}.";

            case InteractionType.ShareMemory:
                return $"{firstNpc.npcName} partilhou uma memória importante com {secondNpc.npcName}.";

            default:
                return $"{firstNpc.npcName} interagiu com {secondNpc.npcName}.";
        }
    }

    private string AddTimePrefix(string message)
    {
        return GetSceneTimestamp() + " " + message;
    }

    private string GetSceneTimestamp()
    {
        int totalSeconds = Mathf.FloorToInt(Time.timeSinceLevelLoad);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        return $"[{minutes:00}:{seconds:00}]";
    }

    private void OnValidate()
    {
        if (interactionInterval < 2f)
        {
            interactionInterval = 2f;
        }

        if (interactionDistanceOffset < 0.5f)
        {
            interactionDistanceOffset = 0.5f;
        }

        if (interactionApproachDelay < 0.5f)
        {
            interactionApproachDelay = 0.5f;
        }

        relationshipSaturationThreshold = Mathf.Clamp(relationshipSaturationThreshold, 60, 100);
        lowTensionThreshold = Mathf.Clamp(lowTensionThreshold, 0, 30);

        if (longRelationshipInteractionThreshold < 1)
        {
            longRelationshipInteractionThreshold = 1;
        }
    }
}