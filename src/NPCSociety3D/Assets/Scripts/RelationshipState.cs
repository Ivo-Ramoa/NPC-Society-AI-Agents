using System;
using UnityEngine;

[Serializable]
public class RelationshipState
{
    public string targetNpcName;

    [Range(0, 100)]
    public int trust = 50;

    [Range(0, 100)]
    public int affinity = 50;

    [Range(0, 100)]
    public int tension = 0;

    public int interactionCount = 0;
    public string lastInteraction = "";

    public RelationshipState()
    {
    }

    public RelationshipState(string targetNpcName)
    {
        this.targetNpcName = targetNpcName;
    }

    public void ApplyInteraction(
        int trustDelta,
        int affinityDelta,
        int tensionDelta,
        string interactionDescription
    )
    {
        int effectiveTrustDelta = CalculateSocialValueDelta(trust, trustDelta);
        int effectiveAffinityDelta = CalculateSocialValueDelta(affinity, affinityDelta);
        int effectiveTensionDelta = CalculateTensionDelta(tension, tensionDelta);

        trust = Mathf.Clamp(trust + effectiveTrustDelta, 0, 100);
        affinity = Mathf.Clamp(affinity + effectiveAffinityDelta, 0, 100);
        tension = Mathf.Clamp(tension + effectiveTensionDelta, 0, 100);

        ApplyAntiSaturationPressure(trustDelta, affinityDelta, tensionDelta);

        interactionCount++;
        lastInteraction = interactionDescription;
    }

    private int CalculateSocialValueDelta(int currentValue, int requestedDelta)
    {
        if (requestedDelta == 0)
        {
            return 0;
        }

        if (requestedDelta > 0)
        {
            if (currentValue >= 95)
            {
                return 1;
            }

            if (currentValue >= 85)
            {
                return Mathf.Max(1, Mathf.CeilToInt(requestedDelta * 0.5f));
            }

            if (currentValue >= 75)
            {
                return Mathf.Max(1, Mathf.CeilToInt(requestedDelta * 0.75f));
            }

            return requestedDelta;
        }

        if (currentValue >= 90)
        {
            return Mathf.FloorToInt(requestedDelta * 1.25f);
        }

        return requestedDelta;
    }

    private int CalculateTensionDelta(int currentTension, int requestedDelta)
    {
        if (requestedDelta == 0)
        {
            return 0;
        }

        if (requestedDelta < 0)
        {
            if (currentTension <= 5)
            {
                return 0;
            }

            if (currentTension <= 15)
            {
                return Mathf.Max(-1, requestedDelta);
            }

            return requestedDelta;
        }

        if (currentTension >= 85)
        {
            return Mathf.Max(1, Mathf.CeilToInt(requestedDelta * 0.5f));
        }

        return requestedDelta;
    }

    private void ApplyAntiSaturationPressure(
        int requestedTrustDelta,
        int requestedAffinityDelta,
        int requestedTensionDelta
    )
    {
        bool isLongRelationship = interactionCount >= 20;
        bool isSaturatedRelationship = trust >= 98 && affinity >= 98 && tension <= 5;
        bool wasPositiveOrNeutralInteraction =
            requestedTrustDelta >= 0 &&
            requestedAffinityDelta >= 0 &&
            requestedTensionDelta <= 0;

        if (!isLongRelationship || !isSaturatedRelationship || !wasPositiveOrNeutralInteraction)
        {
            return;
        }

        trust = Mathf.Max(90, trust - 2);
        affinity = Mathf.Max(90, affinity - 2);
        tension = Mathf.Clamp(tension + 2, 0, 100);
    }
}