using System;
using System.Collections.Generic;

[Serializable]
public class OpenClawActorState
{
    public string name;
    public string role;
    public string goal;
    public string personality;
    public int mood;
    public int trust;
    public List<string> recentMemories = new List<string>();
}

[Serializable]
public class OpenClawNpcState
{
    public string name;
    public string role;
    public string goal;
    public string personality;
    public int mood;
    public int trust;
    public bool isBusy;
    public bool isAvailable = true;
    public List<string> recentMemories = new List<string>();
}

[Serializable]
public class OpenClawRelationshipState
{
    public string actorName;
    public string targetName;
    public int trust;
    public int affinity;
    public int tension;
    public int interactionCount;
    public string lastInteraction;
}

[Serializable]
public class OpenClawSocialEventState
{
    public string eventId;
    public string realTimestamp;
    public string sceneTimestamp;

    public string actorName;
    public string actorRole;
    public string actorPersonality;

    public string targetName;
    public string targetRole;
    public string targetPersonality;

    public string interactionType;
    public string description;

    public int actorMoodBefore;
    public int actorMoodAfter;
    public int actorTrustBefore;
    public int actorTrustAfter;

    public int targetMoodBefore;
    public int targetMoodAfter;
    public int targetTrustBefore;
    public int targetTrustAfter;

    public int relationshipTrustBefore;
    public int relationshipTrustAfter;
    public int relationshipAffinityBefore;
    public int relationshipAffinityAfter;
    public int relationshipTensionBefore;
    public int relationshipTensionAfter;
    public int relationshipInteractionCountBefore;
    public int relationshipInteractionCountAfter;

    public int relationshipTrustDelta;
    public int relationshipAffinityDelta;
    public int relationshipTensionDelta;
}

[Serializable]
public class OpenClawSocietyState
{
    public List<OpenClawNpcState> npcs =
        new List<OpenClawNpcState>();

    public List<OpenClawRelationshipState> relationships =
        new List<OpenClawRelationshipState>();

    public List<OpenClawSocialEventState> recentSocialEvents =
        new List<OpenClawSocialEventState>();
}

[Serializable]
public class OpenClawDecisionRequest
{
    public OpenClawActorState actor =
        new OpenClawActorState();

    public OpenClawSocietyState society =
        new OpenClawSocietyState();
}

[Serializable]
public class OpenClawRelationshipDelta
{
    public int trust;
    public int affinity;
    public int tension;
}

[Serializable]
public class OpenClawNpcDelta
{
    public int mood;
    public int trust;
}

[Serializable]
public class OpenClawDecisionResponse
{
    public string requestId;
    public string decisionSource;
    public long serverTimestamp;

    public string agentRoute;
    public string openclawResponseId;
    public string openclawModel;

    public bool llmRepairUsed;
    public bool technicalNormalizationApplied;
    public List<string> technicalNormalizations =
        new List<string>();

    public int rawModelTextLength;

    public string actorName;
    public string targetName;
    public string actionType;
    public string interactionType;
    public string message;
    public string animationTrigger;

    public OpenClawRelationshipDelta relationshipDelta =
        new OpenClawRelationshipDelta();

    public OpenClawNpcDelta actorDelta =
        new OpenClawNpcDelta();

    public OpenClawNpcDelta targetDelta =
        new OpenClawNpcDelta();

    public string reason;
    public string fallbackReason;
    public string firstAgentError;
}

[Serializable]
public class OpenClawErrorResponse
{
    public string error;
    public string details;
}