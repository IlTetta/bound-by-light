using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Menu debug per la demo: salta direttamente a una stanza, marcando come
/// Cleared tutte quelle precedenti nell'ordine di progressione configurato.
///
/// Setup:
///   1. Aggiungi questo componente a un GameObject vuoto con NetworkObject
///      nella scena (in-scene, non spawnato a runtime).
///   2. Compila la lista "rooms" nell'Inspector con le stanze in ordine.
///   3. Per ogni stanza puoi configurare un tasto rapido (es. Keypad1).
///   4. Premi F1 in play mode per aprire/chiudere il menu.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class DebugRoomJumper : NetworkBehaviour
{
    [Serializable]
    public class RoomEntry
    {
        [Tooltip("Nome visualizzato nel menu.")]
        public string label;
        [Tooltip("Riferimento al componente Room della stanza.")]
        public Room room;
        [Tooltip("Punto spawn custom. Se null usa room.PlayerSpawnPoint, altrimenti il centro della stanza.")]
        public Transform overrideSpawnPoint;
        [Tooltip("Tasto rapido (es. Keypad1). None = nessun tasto.")]
        public KeyCode hotkey = KeyCode.None;
    }

    [Header("Stanze in ordine di progressione")]
    [SerializeField] private List<RoomEntry> rooms = new();

    [Header("UI")]
    [SerializeField] private KeyCode toggleUIKey = KeyCode.F1;
    [Tooltip("Separazione laterale in metri tra i player spawnati nella stessa stanza.")]
    [SerializeField] private float playerSpawnSpacing = 1.5f;

    private bool _showUI;
    private Vector2 _scrollPos;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;

        if (Input.GetKeyDown(toggleUIKey))
            _showUI = !_showUI;

        if (!_showUI) return;

        for (int i = 0; i < rooms.Count; i++)
        {
            var e = rooms[i];
            if (e.hotkey != KeyCode.None && Input.GetKeyDown(e.hotkey))
                RequestJumpServerRpc(i);
        }
    }

    private void OnGUI()
    {
        if (!IsSpawned || !IsServer || !_showUI) return;

        var areaStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 8, 8) };
        var boldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

        GUILayout.BeginArea(new Rect(16, 16, 290, 520), areaStyle);

        GUILayout.Label($"DEBUG — Room Jumper  [{toggleUIKey} per chiudere]", boldLabel);
        GUILayout.Space(4);

        _scrollPos = GUILayout.BeginScrollView(_scrollPos);

        for (int i = 0; i < rooms.Count; i++)
        {
            var e = rooms[i];
            string stateStr = e.room != null ? e.room.State.ToString() : "—";
            string hint     = e.hotkey != KeyCode.None ? $"  [{e.hotkey}]" : "";

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{i + 1}. {e.label}  ({stateStr}){hint}");
            if (GUILayout.Button("Vai", GUILayout.Width(50)))
                RequestJumpServerRpc(i);
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // ── RPC ───────────────────────────────────────────────────────────────────

    /// <summary>Chiunque può richiedere il salto — viene eseguito solo sul server.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestJumpServerRpc(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= rooms.Count) return;

        // 1. Marca tutte le stanze precedenti come Cleared
        for (int i = 0; i < targetIndex; i++)
            rooms[i].room?.SetCleared();

        // 2. Calcola posizione spawn della stanza target
        var entry = rooms[targetIndex];
        Transform spawnT = entry.overrideSpawnPoint
            ?? entry.room?.PlayerSpawnPoint
            ?? entry.room?.transform;
        Vector3 basePos = spawnT != null ? spawnT.position : Vector3.zero;

        // 3. Per ogni client: muovi lato server E invia RPC mirato con la posizione esatta
        int idx = 0;
        foreach (var kvp in NetworkManager.ConnectedClients)
        {
            ulong  clientId  = kvp.Key;
            var    playerObj = kvp.Value.PlayerObject;
            if (playerObj == null) { idx++; continue; }

            Vector3 targetPos = basePos + Vector3.right * (idx * playerSpawnSpacing);

            // Aggiorna Transform E Rigidbody server-side + forza sync Physics→Transform
            MovePlayer(playerObj, targetPos);

            // Invia al singolo client la sua posizione: gestirà Rigidbody/CC localmente
            TeleportPlayerRpc(targetPos, RpcTarget.Single(clientId, RpcTargetUse.Temp));

            idx++;
        }

        Debug.Log($"[DebugRoomJumper] Jump → '{entry.label}'. " +
                  $"Stanze precedenti segnate Cleared: {targetIndex}.");
    }

    /// <summary>
    /// Inviato al singolo client con la sua posizione target.
    /// Il client muove il proprio player localmente per evitare che
    /// Rigidbody o CharacterController resistano alla sync del NetworkTransform.
    /// </summary>
    [Rpc(SendTo.SpecifiedInParams)]
    private void TeleportPlayerRpc(Vector3 targetPos, RpcParams _ = default)
    {
        var playerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (playerObj == null) return;

        MovePlayer(playerObj, targetPos);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sposta un player object gestendo sia Rigidbody che CharacterController.
    /// Funziona sia server-side che client-side.
    /// </summary>
    private void MovePlayer(NetworkObject playerObj, Vector3 targetPos)
    {
        // CharacterController deve essere disabilitato per permettere lo spostamento diretto
        var cc = playerObj.GetComponentInChildren<CharacterController>();
        if (cc != null) cc.enabled = false;

        // Imposta il Transform (quello che NetworkTransform replica)
        playerObj.transform.position = targetPos;

        // Imposta anche la fisica e azzera le velocità
        var rb = playerObj.GetComponent<Rigidbody>()
               ?? playerObj.GetComponentInChildren<Rigidbody>();
        if (rb != null)
        {
            rb.position        = targetPos;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Forza il sync immediato Physics ↔ Transform, evita che il motore fisico
        // pensi che il player sia ancora nella posizione vecchia
        Physics.SyncTransforms();

        if (cc != null) cc.enabled = true;
    }
}
