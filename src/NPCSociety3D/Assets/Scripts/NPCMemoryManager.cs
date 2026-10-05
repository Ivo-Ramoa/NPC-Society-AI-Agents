using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class NPCMemoryManager : MonoBehaviour
{
    private const string MemoryFileName = "npc_memory.json";
    private const string RelationshipFileName = "npc_relationships.json";
    private const string SocialEventFileName = "npc_social_events.json";

    [Header("Persistência")]
    public int maxStoredSocialEvents = 300;

    private string MemoryFilePath => Path.Combine(Application.persistentDataPath, MemoryFileName);
    private string RelationshipFilePath => Path.Combine(Application.persistentDataPath, RelationshipFileName);
    private string SocialEventFilePath => Path.Combine(Application.persistentDataPath, SocialEventFileName);

    public List<string> LoadNpcMemory(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName))
        {
            return new List<string>();
        }

        NPCMemoryDatabase database = LoadMemoryDatabase();
        NPCMemoryEntry entry = database.npcMemories.Find(memoryEntry => memoryEntry.npcName == npcName);

        if (entry == null || entry.memories == null)
        {
            return new List<string>();
        }

        return new List<string>(entry.memories);
    }

    public void SaveMemory(string npcName, List<string> memories)
    {
        if (string.IsNullOrWhiteSpace(npcName))
        {
            return;
        }

        NPCMemoryDatabase database = LoadMemoryDatabase();
        NPCMemoryEntry entry = database.npcMemories.Find(memoryEntry => memoryEntry.npcName == npcName);

        if (entry == null)
        {
            entry = new NPCMemoryEntry
            {
                npcName = npcName,
                memories = new List<string>()
            };

            database.npcMemories.Add(entry);
        }

        entry.memories = memories != null
            ? new List<string>(memories)
            : new List<string>();

        SaveMemoryDatabase(database);

        Debug.Log("Memória guardada em: " + MemoryFilePath);
    }

    public List<RelationshipState> LoadNpcRelationships(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName))
        {
            return new List<RelationshipState>();
        }

        NPCRelationshipDatabase database = LoadRelationshipDatabase();
        NPCRelationshipEntry entry = database.npcRelationships.Find(
            relationshipEntry => relationshipEntry.npcName == npcName
        );

        if (entry == null || entry.relationships == null)
        {
            return new List<RelationshipState>();
        }

        return CloneRelationships(entry.relationships);
    }

    public void SaveRelationships(string npcName, List<RelationshipState> relationships)
    {
        if (string.IsNullOrWhiteSpace(npcName))
        {
            return;
        }

        NPCRelationshipDatabase database = LoadRelationshipDatabase();
        NPCRelationshipEntry entry = database.npcRelationships.Find(
            relationshipEntry => relationshipEntry.npcName == npcName
        );

        if (entry == null)
        {
            entry = new NPCRelationshipEntry
            {
                npcName = npcName,
                relationships = new List<RelationshipState>()
            };

            database.npcRelationships.Add(entry);
        }

        entry.relationships = relationships != null
            ? CloneRelationships(relationships)
            : new List<RelationshipState>();

        SaveRelationshipDatabase(database);

        Debug.Log("Relações sociais guardadas em: " + RelationshipFilePath);
    }

    public List<SocialEvent> LoadSocialEvents()
    {
        NPCSocialEventDatabase database = LoadSocialEventDatabase();

        if (database.socialEvents == null)
        {
            return new List<SocialEvent>();
        }

        return new List<SocialEvent>(database.socialEvents);
    }

    public void SaveSocialEvent(SocialEvent socialEvent)
    {
        if (socialEvent == null)
        {
            return;
        }

        NPCSocialEventDatabase database = LoadSocialEventDatabase();

        if (database.socialEvents == null)
        {
            database.socialEvents = new List<SocialEvent>();
        }

        database.socialEvents.Add(socialEvent);
        TrimSocialEvents(database.socialEvents);

        SaveSocialEventDatabase(database);
    }

    private NPCMemoryDatabase LoadMemoryDatabase()
    {
        if (!File.Exists(MemoryFilePath))
        {
            return new NPCMemoryDatabase();
        }

        try
        {
            string json = File.ReadAllText(MemoryFilePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new NPCMemoryDatabase();
            }

            NPCMemoryDatabase database = JsonUtility.FromJson<NPCMemoryDatabase>(json);

            if (database == null)
            {
                return new NPCMemoryDatabase();
            }

            if (database.npcMemories == null)
            {
                database.npcMemories = new List<NPCMemoryEntry>();
            }

            return database;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Não foi possível carregar a memória dos NPCs: " + exception.Message);
            return new NPCMemoryDatabase();
        }
    }

    private void SaveMemoryDatabase(NPCMemoryDatabase database)
    {
        try
        {
            string json = JsonUtility.ToJson(database, true);
            File.WriteAllText(MemoryFilePath, json);
        }
        catch (Exception exception)
        {
            Debug.LogError("Erro ao guardar memória dos NPCs: " + exception.Message);
        }
    }

    private NPCRelationshipDatabase LoadRelationshipDatabase()
    {
        if (!File.Exists(RelationshipFilePath))
        {
            return new NPCRelationshipDatabase();
        }

        try
        {
            string json = File.ReadAllText(RelationshipFilePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new NPCRelationshipDatabase();
            }

            NPCRelationshipDatabase database = JsonUtility.FromJson<NPCRelationshipDatabase>(json);

            if (database == null)
            {
                return new NPCRelationshipDatabase();
            }

            if (database.npcRelationships == null)
            {
                database.npcRelationships = new List<NPCRelationshipEntry>();
            }

            return database;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Não foi possível carregar relações sociais dos NPCs: " + exception.Message);
            return new NPCRelationshipDatabase();
        }
    }

    private void SaveRelationshipDatabase(NPCRelationshipDatabase database)
    {
        try
        {
            string json = JsonUtility.ToJson(database, true);
            File.WriteAllText(RelationshipFilePath, json);
        }
        catch (Exception exception)
        {
            Debug.LogError("Erro ao guardar relações sociais dos NPCs: " + exception.Message);
        }
    }

    private NPCSocialEventDatabase LoadSocialEventDatabase()
    {
        if (!File.Exists(SocialEventFilePath))
        {
            return new NPCSocialEventDatabase();
        }

        try
        {
            string json = File.ReadAllText(SocialEventFilePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new NPCSocialEventDatabase();
            }

            NPCSocialEventDatabase database = JsonUtility.FromJson<NPCSocialEventDatabase>(json);

            if (database == null)
            {
                return new NPCSocialEventDatabase();
            }

            if (database.socialEvents == null)
            {
                database.socialEvents = new List<SocialEvent>();
            }

            return database;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Não foi possível carregar eventos sociais dos NPCs: " + exception.Message);
            return new NPCSocialEventDatabase();
        }
    }

    private void SaveSocialEventDatabase(NPCSocialEventDatabase database)
    {
        try
        {
            string json = JsonUtility.ToJson(database, true);
            File.WriteAllText(SocialEventFilePath, json);
        }
        catch (Exception exception)
        {
            Debug.LogError("Erro ao guardar eventos sociais dos NPCs: " + exception.Message);
        }
    }

    private List<RelationshipState> CloneRelationships(List<RelationshipState> sourceRelationships)
    {
        List<RelationshipState> clonedRelationships = new List<RelationshipState>();

        foreach (RelationshipState relationship in sourceRelationships)
        {
            if (relationship == null || string.IsNullOrWhiteSpace(relationship.targetNpcName))
            {
                continue;
            }

            clonedRelationships.Add(new RelationshipState
            {
                targetNpcName = relationship.targetNpcName,
                trust = relationship.trust,
                affinity = relationship.affinity,
                tension = relationship.tension,
                interactionCount = relationship.interactionCount,
                lastInteraction = relationship.lastInteraction
            });
        }

        return clonedRelationships;
    }

    private void TrimSocialEvents(List<SocialEvent> socialEvents)
    {
        if (socialEvents == null)
        {
            return;
        }

        if (maxStoredSocialEvents < 1)
        {
            maxStoredSocialEvents = 1;
        }

        while (socialEvents.Count > maxStoredSocialEvents)
        {
            socialEvents.RemoveAt(0);
        }
    }

    private void OnValidate()
    {
        if (maxStoredSocialEvents < 1)
        {
            maxStoredSocialEvents = 1;
        }
    }
}

[Serializable]
public class NPCMemoryDatabase
{
    public List<NPCMemoryEntry> npcMemories = new List<NPCMemoryEntry>();
}

[Serializable]
public class NPCMemoryEntry
{
    public string npcName;
    public List<string> memories = new List<string>();
}

[Serializable]
public class NPCRelationshipDatabase
{
    public List<NPCRelationshipEntry> npcRelationships = new List<NPCRelationshipEntry>();
}

[Serializable]
public class NPCRelationshipEntry
{
    public string npcName;
    public List<RelationshipState> relationships = new List<RelationshipState>();
}

[Serializable]
public class NPCSocialEventDatabase
{
    public List<SocialEvent> socialEvents = new List<SocialEvent>();
}