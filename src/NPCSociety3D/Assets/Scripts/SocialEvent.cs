using System;

[Serializable]
public class SocialEvent
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