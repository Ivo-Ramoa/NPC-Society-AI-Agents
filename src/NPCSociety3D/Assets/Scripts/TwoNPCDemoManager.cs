using UnityEngine;

public class TwoNPCDemoManager : MonoBehaviour
{
    [Header("NPCs da conversa")]
    public NPCAgent npcLia;
    public NPCAgent npcDuarte;

    [Header("UI")]
    public UILogManager uiLogManager;

    private bool started = false;

    void Start()
    {
        Invoke(nameof(StartConversation), 2f);
    }

    void StartConversation()
    {
        if (started) return;
        started = true;

        if (npcLia == null || npcDuarte == null)
        {
            Debug.LogWarning("Faltam NPCs no TwoNPCDemoManager.");
            return;
        }

        Vector3 destination = npcDuarte.transform.position + new Vector3(1.5f, 0, 0);

        npcLia.MoveTo(destination);

        Invoke(nameof(StartTalking), 4f);
    }

    void StartTalking()
    {
        FaceEachOther();

        string message = "Lia aproximou-se de Duarte e começou uma conversa para o ajudar na proteção da vila.";

        npcLia.AddMemory(message);
        npcDuarte.AddMemory(message);

        npcLia.ChangeMood(5);
        npcDuarte.ChangeMood(5);

        npcLia.ChangeTrust(5);
        npcDuarte.ChangeTrust(10);

        npcLia.PlayTalkingAnimation();
        npcDuarte.PlayTalkingAnimation();

        AddLog(message);

        Invoke(nameof(EndConversation), 5f);
    }

    void EndConversation()
    {
        string resultMessage = "A confiança entre Lia e Duarte aumentou. Duarte sente-se mais seguro e Lia completou uma ação de ajuda.";

        npcLia.AddMemory(resultMessage);
        npcDuarte.AddMemory(resultMessage);

        npcLia.ChangeMood(5);
        npcDuarte.ChangeTrust(5);

        AddLog(resultMessage);
    }

    void FaceEachOther()
    {
        Vector3 directionToDuarte = npcDuarte.transform.position - npcLia.transform.position;
        directionToDuarte.y = 0;

        if (directionToDuarte != Vector3.zero)
        {
            npcLia.transform.rotation = Quaternion.LookRotation(directionToDuarte);
        }

        Vector3 directionToLia = npcLia.transform.position - npcDuarte.transform.position;
        directionToLia.y = 0;

        if (directionToLia != Vector3.zero)
        {
            npcDuarte.transform.rotation = Quaternion.LookRotation(directionToLia);
        }
    }

    void AddLog(string message)
    {
        Debug.Log(message);

        if (uiLogManager != null)
        {
            uiLogManager.AddLog(message);
        }
    }
}