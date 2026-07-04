#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tool Editor che configura automaticamente i componenti Room, RoomEntryTrigger
/// e RoomWaveController su tutte le stanze generate da CathedralLevelBuilder.
///
/// Apribile da: Tools → Bound By Light → Setup Room Components
/// </summary>
public class RoomSetupTool : EditorWindow
{
    // ─── Dati stanze ──────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> DisplayNames = new()
    {
        { "garden_nartex",      "Garden + Nartex"        },
        { "nave",               "Nave"                   },
        { "porch",              "Porch"                  },
        { "crossing",           "Crossing"               },
        { "transept_left",      "Transept"               },
        { "transept_warehouse", "Transept + Warehouse"   },
        { "apse_choir",         "Apse + Choir (Boss)"    },
    };

    // Stanze con combattimento → ricevono RoomWaveController
    private static readonly HashSet<string> CombatRooms = new()
    {
        "nave", "crossing", "transept_left", "transept_warehouse", "apse_choir"
    };

    // ─── Config Inspector ─────────────────────────────────────────────────────

    private int  _playersRequired = 1; // 1 = comodo per test in solo
    private bool _overwriteExisting = false;

    // ─── Window ───────────────────────────────────────────────────────────────

    [MenuItem("Tools/Bound By Light/Setup Room Components")]
    public static void ShowWindow() =>
        GetWindow<RoomSetupTool>("Room Setup Tool");

    private void OnGUI()
    {
        GUILayout.Label("Room Component Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "Aggiunge Room, NetworkObject, RoomEntryTrigger e RoomWaveController\n" +
            "a tutte le Room_* trovate sotto 'Level_Cathedral' nella scena attiva.\n\n" +
            "Usa prima 'Build Cathedral Level' se la scena è vuota.",
            MessageType.Info);

        EditorGUILayout.Space();
        _playersRequired   = EditorGUILayout.IntSlider("Player per attivare stanza", _playersRequired, 1, 2);
        _overwriteExisting = EditorGUILayout.Toggle("Sovrascrivi componenti esistenti", _overwriteExisting);

        EditorGUILayout.Space();

        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("SETUP ROOM COMPONENTS", GUILayout.Height(42)))
            SetupAllRooms();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        GUI.backgroundColor = new Color(0.9f, 0.5f, 0.3f);
        if (GUILayout.Button("Rimuovi tutto (reset)", GUILayout.Height(28)))
            RemoveAllComponents();
        GUI.backgroundColor = Color.white;
    }

    // ─── Setup principale ─────────────────────────────────────────────────────

    private void SetupAllRooms()
    {
        GameObject root = GameObject.Find("Level_Cathedral");
        if (root == null)
        {
            EditorUtility.DisplayDialog("Errore",
                "Nessun GameObject 'Level_Cathedral' trovato.\n" +
                "Genera prima il livello con 'Build Cathedral Level'.", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Setup Room Components");
        int groupIndex = Undo.GetCurrentGroup();

        int processed = 0;

        foreach (Transform child in root.transform)
        {
            if (!child.name.StartsWith("Room_")) continue;

            string roomId = child.name.Substring(5); // rimuove "Room_"
            if (!DisplayNames.TryGetValue(roomId, out string displayName))
                displayName = roomId;

            SetupRoom(child, roomId, displayName);
            processed++;
        }

        Undo.CollapseUndoOperations(groupIndex);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        string risultato =
            $"✓ {processed} stanze configurate.\n\n" +
            "PROSSIMI STEP:\n" +
            "1. Salva la scena (Ctrl+S)\n" +
            "2. Assegna i prefab nemici al RoomWaveController di ogni stanza\n" +
            "3. Crea prefab porta (RoomDoor) e collegale alle stanze\n" +
            "4. Avvia il NetworkManager e testa il player";

        EditorUtility.DisplayDialog("Setup Completato", risultato, "OK");
        Debug.Log($"[RoomSetupTool] Setup completato: {processed} stanze.");
    }

    private void SetupRoom(Transform roomTr, string roomId, string displayName)
    {
        GameObject go = roomTr.gameObject;

        // ── 1. NetworkObject (obbligatorio per NetworkBehaviour) ──────────────
        if (go.GetComponent<NetworkObject>() == null)
            Undo.AddComponent<NetworkObject>(go);

        // ── 2. Room component ─────────────────────────────────────────────────
        Room room = go.GetComponent<Room>();
        bool roomWasNew = room == null;
        if (room == null)
            room = Undo.AddComponent<Room>(go);

        if (roomWasNew || _overwriteExisting)
        {
            SerializedObject roomSO = new SerializedObject(room);
            roomSO.FindProperty("roomId").stringValue          = roomId;
            roomSO.FindProperty("roomDisplayName").stringValue = displayName;
            roomSO.FindProperty("isStartRoom").boolValue       = roomId == "garden_nartex";

            // Collega PlayerSpawnPoint se esiste (solo garden_nartex)
            Transform spawnPt = roomTr.Find("PlayerSpawnPoint");
            if (spawnPt != null)
                roomSO.FindProperty("playerSpawnPoint").objectReferenceValue = spawnPt;

            roomSO.ApplyModifiedProperties();
        }

        // ── 3. RoomWaveController (solo stanze combat) ────────────────────────
        if (CombatRooms.Contains(roomId))
        {
            RoomWaveController waveCtrl = go.GetComponent<RoomWaveController>();
            bool waveWasNew = waveCtrl == null;
            if (waveCtrl == null)
                waveCtrl = Undo.AddComponent<RoomWaveController>(go);

            if (waveWasNew || _overwriteExisting)
            {
                // requiredPlayers sincronizzato con il setting del tool
                SerializedObject waveSO = new SerializedObject(waveCtrl);
                waveSO.FindProperty("requiredPlayers").intValue = _playersRequired;
                waveSO.ApplyModifiedProperties();
            }

            // Collega waveController alla Room
            SerializedObject roomSO2 = new SerializedObject(room);
            roomSO2.FindProperty("waveController").objectReferenceValue = waveCtrl;
            roomSO2.ApplyModifiedProperties();

            Debug.Log($"[RoomSetupTool] '{displayName}': RoomWaveController aggiunto (combat room).");
        }

        // ── 4. RoomEntryTrigger3D sul figlio EntryTrigger ────────────────────
        Transform entryTr = roomTr.Find("EntryTrigger");
        if (entryTr != null)
        {
            // Rimuovi il vecchio trigger 2D se presente
            RoomEntryTrigger oldTrigger = entryTr.GetComponent<RoomEntryTrigger>();
            if (oldTrigger != null)
                Undo.DestroyObjectImmediate(oldTrigger);

            RoomEntryTrigger3D trigger = entryTr.GetComponent<RoomEntryTrigger3D>();
            bool triggerWasNew = trigger == null;
            if (trigger == null)
                trigger = Undo.AddComponent<RoomEntryTrigger3D>(entryTr.gameObject);

            if (triggerWasNew || _overwriteExisting)
            {
                SerializedObject triggerSO = new SerializedObject(trigger);
                triggerSO.FindProperty("room").objectReferenceValue          = room;
                triggerSO.FindProperty("playersRequiredToActivate").intValue = _playersRequired;
                triggerSO.ApplyModifiedProperties();
            }
        }
        else
        {
            Debug.LogWarning($"[RoomSetupTool] '{displayName}': figlio 'EntryTrigger' non trovato. " +
                             "Esegui prima 'Build Cathedral Level 3D' con 'Aggiungi Entry Trigger' attivo.");
        }

        Debug.Log($"[RoomSetupTool] '{displayName}' ({roomId}) configurata.");
    }

    // ─── Reset ────────────────────────────────────────────────────────────────

    private static void RemoveAllComponents()
    {
        if (!EditorUtility.DisplayDialog("Conferma reset",
            "Rimuovere Room, NetworkObject, RoomWaveController e RoomEntryTrigger\n" +
            "da tutte le stanze? L'operazione è reversibile con Ctrl+Z.", "Sì, rimuovi", "Annulla"))
            return;

        GameObject root = GameObject.Find("Level_Cathedral");
        if (root == null) return;

        Undo.SetCurrentGroupName("Remove Room Components");
        int groupIndex = Undo.GetCurrentGroup();

        foreach (Transform child in root.transform)
        {
            if (!child.name.StartsWith("Room_")) continue;

            RemoveIfPresent<Room>(child.gameObject);
            RemoveIfPresent<RoomWaveController>(child.gameObject);
            RemoveIfPresent<NetworkObject>(child.gameObject);

            Transform entryTr = child.Find("EntryTrigger");
            if (entryTr != null)
            {
                RemoveIfPresent<RoomEntryTrigger>(entryTr.gameObject);
                RemoveIfPresent<RoomEntryTrigger3D>(entryTr.gameObject);
            }
        }

        Undo.CollapseUndoOperations(groupIndex);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[RoomSetupTool] Componenti rimossi da tutte le stanze.");
    }

    private static void RemoveIfPresent<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        if (comp != null)
            Undo.DestroyObjectImmediate(comp);
    }
}
#endif
