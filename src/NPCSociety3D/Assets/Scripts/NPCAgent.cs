using System.Collections.Generic;
using UnityEngine;

public class NPCAgent : MonoBehaviour
{
    [Header("Identidade do Agente")]
    public string npcName;
    public string role;
    public string goal;
    public string personality;

    [Header("Memória do Agente")]
    public List<string> memory = new List<string>();

    [Header("Configuração da Memória")]
    public int maxMemoryEntries = 50;

    [Header("Relações Sociais")]
    public List<RelationshipState> relationships = new List<RelationshipState>();

    [Header("Estado Social")]
    public int trust = 50;
    public int mood = 50;
    public int completedActions = 0;

    [Header("Movimento")]
    public float moveSpeed = 2f;

    [Header("Animator")]
    public Animator animator;

    private Vector3 targetPosition;
    private bool isMoving = false;
    private NPCMemoryManager memoryManager;

    private void Start()
    {
        targetPosition = transform.position;

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        memoryManager = FindObjectOfType<NPCMemoryManager>();

        LoadPersistentMemory();
        LoadPersistentRelationships();
    }

    private void Update()
    {
        HandleMovement();
    }

    private void OnApplicationQuit()
    {
        SavePersistentRelationships();
    }

    private void HandleMovement()
    {
        if (!isMoving)
        {
            SetWalking(false);
            return;
        }

        SetWalking(true);

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            moveSpeed * Time.deltaTime
        );

        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        if (Vector3.Distance(transform.position, targetPosition) < 0.25f)
        {
            isMoving = false;
            SetWalking(false);
        }
    }

    public void MoveTo(Vector3 position)
    {
        targetPosition = position;
        isMoving = true;
        SetWalking(true);
    }

    public void FaceTarget(NPCAgent otherNpc)
    {
        if (otherNpc == null)
        {
            return;
        }

        Vector3 direction = otherNpc.transform.position - transform.position;
        direction.y = 0f;

        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    public void PlayTalkingAnimation()
    {
        if (animator != null)
        {
            animator.SetBool("isWalking", false);
            animator.SetBool("isTalking", true);
        }

        CancelInvoke(nameof(StopTalkingAnimation));
        Invoke(nameof(StopTalkingAnimation), 4f);
    }

    private void StopTalkingAnimation()
    {
        if (animator != null)
        {
            animator.SetBool("isTalking", false);
        }
    }

    private void SetWalking(bool value)
    {
        if (animator != null)
        {
            animator.SetBool("isWalking", value);
        }
    }

    public void AddMemory(string newMemory)
    {
        if (string.IsNullOrWhiteSpace(newMemory))
        {
            return;
        }

        memory.Add(newMemory);
        TrimMemoryToLimit();

        completedActions++;

        Debug.Log(npcName + " guardou memória: " + newMemory);

        if (memoryManager != null)
        {
            memoryManager.SaveMemory(npcName, memory);
        }
    }

    public RelationshipState GetRelationshipWith(NPCAgent targetNpc)
    {
        if (targetNpc == null || targetNpc == this || string.IsNullOrEmpty(targetNpc.npcName))
        {
            return null;
        }

        RelationshipState relationship = relationships.Find(
            existingRelationship => existingRelationship.targetNpcName == targetNpc.npcName
        );

        if (relationship == null)
        {
            relationship = new RelationshipState(targetNpc.npcName);
            relationships.Add(relationship);
        }

        return relationship;
    }

    public void RegisterRelationshipInteraction(
        NPCAgent targetNpc,
        int trustDelta,
        int affinityDelta,
        int tensionDelta,
        string interactionDescription
    )
    {
        RelationshipState relationship = GetRelationshipWith(targetNpc);

        if (relationship == null)
        {
            return;
        }

        relationship.ApplyInteraction(
            trustDelta,
            affinityDelta,
            tensionDelta,
            interactionDescription
        );

        SavePersistentRelationships();
    }

    public string GetRelationshipSummaryWith(NPCAgent targetNpc)
    {
        RelationshipState relationship = GetRelationshipWith(targetNpc);

        if (relationship == null)
        {
            return "No relationship data.";
        }

        return "Trust: " + relationship.trust +
               ", Affinity: " + relationship.affinity +
               ", Tension: " + relationship.tension +
               ", Interactions: " + relationship.interactionCount;
    }

    public void ChangeTrust(int amount)
    {
        trust += amount;
        trust = Mathf.Clamp(trust, 0, 100);
    }

    public void ChangeMood(int amount)
    {
        mood += amount;
        mood = Mathf.Clamp(mood, 0, 100);
    }

    private void LoadPersistentMemory()
    {
        if (memoryManager == null || string.IsNullOrWhiteSpace(npcName))
        {
            return;
        }

        memory = memoryManager.LoadNpcMemory(npcName);

        if (memory == null)
        {
            memory = new List<string>();
        }

        int originalMemoryCount = memory.Count;

        TrimMemoryToLimit();

        if (memory.Count != originalMemoryCount)
        {
            memoryManager.SaveMemory(npcName, memory);
            Debug.Log(npcName + " reduziu a memória persistente de " + originalMemoryCount + " para " + memory.Count + " entradas.");
        }

        Debug.Log(npcName + " carregou " + memory.Count + " memórias persistentes.");
    }

    private void LoadPersistentRelationships()
    {
        if (memoryManager == null || string.IsNullOrWhiteSpace(npcName))
        {
            return;
        }

        relationships = memoryManager.LoadNpcRelationships(npcName);

        if (relationships == null)
        {
            relationships = new List<RelationshipState>();
        }

        Debug.Log(npcName + " carregou " + relationships.Count + " relações sociais persistentes.");
    }

    private void SavePersistentRelationships()
    {
        if (memoryManager == null || string.IsNullOrWhiteSpace(npcName))
        {
            return;
        }

        memoryManager.SaveRelationships(npcName, relationships);
    }

    private void TrimMemoryToLimit()
    {
        if (maxMemoryEntries < 1)
        {
            maxMemoryEntries = 1;
        }

        while (memory.Count > maxMemoryEntries)
        {
            memory.RemoveAt(0);
        }
    }

    private void OnValidate()
    {
        if (maxMemoryEntries < 1)
        {
            maxMemoryEntries = 1;
        }

        trust = Mathf.Clamp(trust, 0, 100);
        mood = Mathf.Clamp(mood, 0, 100);

        if (moveSpeed < 0.1f)
        {
            moveSpeed = 0.1f;
        }
    }
}