using System.Collections.Generic;
using FMODUnity;
using MyGame.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Porta di transizione cooperativa: entrambi i player devono premere F
/// nei pressi della porta per essere teletrasportati alla destinazione.
///
/// Setup scena:
///   1. Aggiungi questo componente al GameObject della porta (o a un trigger figlio).
///   2. Assegna destination: un Transform posizionato davanti alla entry door della stanza successiva.
///   3. Assegna doorRenderer: il Renderer del modello della porta per il glow.
///   4. (Opzionale) interactPrompt: Canvas WorldSpace con testo "Premi F per entrare".
///   5. Per porte bloccate fino al clear: spunta lockedUntilRoomCleared e assegna roomToWatch.
///   6. Il Collider su questo GO deve coprire la zona davanti alla porta.
///   7. Aggiungi NetworkObject a questo GameObject.
/// </summary>
[RequireComponent(typeof(Collider), typeof(NetworkObject))]
public class CoopDoorTransition : NetworkBehaviour
{
    [Header("Destinazione")]
    [Tooltip("Transform posizionato davanti alla entry door della stanza successiva.")]
    [SerializeField] private Transform destination;

    [Header("Interazione")]
    [SerializeField] private KeyCode interactionKey = KeyCode.F;
    [SerializeField] private int requiredPlayers = 2;
    [Tooltip("GameObject (es. Canvas WorldSpace) con il prompt 'Premi F per entrare'. Opzionale.")]
    [SerializeField] private GameObject interactPrompt;

    [Header("Blocco fino al clear")]
    [Tooltip("Se true, la porta non è interagibile finché roomToWatch non è Cleared.")]
    [SerializeField] private bool lockedUntilRoomCleared = false;
    [Tooltip("La stanza il cui clear sblocca questa porta.")]
    [SerializeField] private Room roomToWatch;

    [Tooltip("Lista di stanze che devono essere TUTTE Cleared (es. boss door). " +
             "Se popolata, ha priorità su roomToWatch.")]
    [SerializeField] private List<Room> allRoomsRequired = new();

    [Header("Blocco fino all'attivazione del pozzo")]
    [Tooltip("Se assegnato, la porta rimane bloccata finché il pozzo non viene attivato.")]
    [SerializeField] private WellInteractable wellToWatch;

    [Header("Requisito chiavi")]
    [Tooltip("Numero di chiavi condivise necessarie per aprire questa porta. 0 = nessun requisito.")]
    [SerializeField] private int requiredKeys = 0;

    [Header("SFX")]
    [Tooltip("Emitter FMOD riprodotto quando la porta si sblocca (room cleared).")]
    [SerializeField] private StudioEventEmitter doorSfxEmitter;

    [Header("Visual porta aperta")]
    [Tooltip("Renderer del modello porta. Deve avere Emission abilitato nel materiale.")]
    [SerializeField] private Renderer doorRenderer;
    [SerializeField] private Color lockedEmission = new Color(0.5f, 0.05f, 0.05f) * 1.5f;  // rosso tenue = bloccata
    [SerializeField] private Color idleEmission   = new Color(0.05f, 0.55f, 0.15f) * 2f;   // verde tenue = aperta
    [SerializeField] private Color readyEmission  = new Color(0.15f, 1f,   0.4f)  * 6f;    // verde intenso = pronti

    // ─── NetworkVariables ─────────────────────────────────────────────────────

    /// <summary>Quanti player hanno già premuto F. Usato per il glow.</summary>
    public readonly NetworkVariable<int> ReadyCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── Privati ─────────────────────────────────────────────────────────────

    private readonly HashSet<ulong> _readyPlayers  = new();
    private MaterialPropertyBlock   _mpb;
    private bool                    _localInRange;
    private bool                    _teleported;
    private bool                    _isLocked;
    private int                     _clearedRoomsCount;

    // ─── NGO ─────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _mpb = new MaterialPropertyBlock();

        // Multi-room lock (boss door)
        if (allRoomsRequired.Count > 0)
        {
            _clearedRoomsCount = 0;
            foreach (var r in allRoomsRequired)
            {
                if (r == null) continue;
                if (r.State == RoomState.Cleared)
                    _clearedRoomsCount++;
                else
                    r.OnRoomCleared += HandleAllRoomCleared;   // named method — unsubscribable
            }
            _isLocked = _clearedRoomsCount < allRoomsRequired.Count;
        }
        else
        {
            _isLocked = lockedUntilRoomCleared || wellToWatch != null;

            if (lockedUntilRoomCleared && roomToWatch != null)
            {
                if (roomToWatch.State == RoomState.Cleared)
                    _isLocked = false;
                else
                    roomToWatch.OnRoomCleared += HandleRoomToWatchCleared;  // named method
            }

            if (wellToWatch != null)
            {
                if (wellToWatch.IsActivated.Value)
                    _isLocked = false;
                else
                    wellToWatch.IsActivated.OnValueChanged += HandleWellActivated;  // named method
            }
        }

        // Ascolta i cambiamenti delle chiavi per aggiornare il glow
        if (requiredKeys > 0 && GameManager.Instance != null)
            GameManager.Instance.SharedKeyCount.OnValueChanged += OnKeyCountChanged;

        ReadyCount.OnValueChanged += (_, _) => UpdateGlow();
        UpdateGlow();
    }

    public override void OnNetworkDespawn()
    {
        // Multi-room: rimuovi handler da ogni stanza
        foreach (var r in allRoomsRequired)
            if (r != null) r.OnRoomCleared -= HandleAllRoomCleared;

        // Single-room
        if (roomToWatch != null)
            roomToWatch.OnRoomCleared -= HandleRoomToWatchCleared;

        // Well
        if (wellToWatch != null)
            wellToWatch.IsActivated.OnValueChanged -= HandleWellActivated;

        if (requiredKeys > 0 && GameManager.Instance != null)
            GameManager.Instance.SharedKeyCount.OnValueChanged -= OnKeyCountChanged;
    }

    private void OnKeyCountChanged(int _, int newCount)
    {
        // Se ora abbiamo abbastanza chiavi e la porta era bloccata solo per chiavi, sblocca
        if (!_isLocked) return;
        if (HasEnoughKeys()) UpdateGlow();
    }

    private bool HasEnoughKeys()
    {
        if (requiredKeys <= 0) return true;
        if (GameManager.Instance == null) return false;
        return GameManager.Instance.SharedKeyCount.Value >= requiredKeys;
    }

    // ─── Named event handlers (no lambda — unsubscribable in OnNetworkDespawn) ──

    /// <summary>Handler per allRoomsRequired. Firma Action<Room> richiesta dall'evento.</summary>
    private void HandleAllRoomCleared(Room _)
    {
        _clearedRoomsCount++;
        if (_clearedRoomsCount >= allRoomsRequired.Count)
            Unlock();
    }

    /// <summary>Handler per roomToWatch.OnRoomCleared.</summary>
    private void HandleRoomToWatchCleared(Room _) => Unlock();

    /// <summary>Handler per wellToWatch.IsActivated.OnValueChanged.</summary>
    private void HandleWellActivated(bool _, bool activated) { if (activated) Unlock(); }

    public void Unlock()
    {
        _isLocked = false;
        PlayDoorSfxClientRpc();
        UpdateGlow();
    }

    [ClientRpc]
    private void PlayDoorSfxClientRpc()
    {
        doorSfxEmitter?.Play();
    }

    // ─── Trigger ─────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj == null || !netObj.CompareTag("Player")) return;

        if (netObj.IsOwner && !_isLocked && HasEnoughKeys())
        {
            _localInRange = true;
            if (interactPrompt != null) interactPrompt.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj == null || !netObj.CompareTag("Player")) return;

        // Se esce prima di confermare, rimuovi dal set dei pronti
        if (IsServer)
            CancelReady(netObj.NetworkObjectId);

        if (netObj.IsOwner)
        {
            _localInRange = false;
            if (interactPrompt != null) interactPrompt.SetActive(false);
        }
    }

    // ─── Input ───────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_localInRange || _teleported || _isLocked) return;
        if (!Input.GetKeyDown(interactionKey)) return;

        RegisterReadyServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    // ─── RPC ─────────────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void RegisterReadyServerRpc(ulong clientId)
    {
        if (_teleported) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client)) return;
        if (client.PlayerObject == null) return;

        ulong playerId = client.PlayerObject.NetworkObjectId;
        if (!_readyPlayers.Add(playerId)) return;   // già registrato

        ReadyCount.Value = _readyPlayers.Count;

        if (_readyPlayers.Count >= requiredPlayers)
        {
            _teleported = true;
            TeleportAllRpc(destination.position);
        }
    }

    private void CancelReady(ulong networkObjectId)
    {
        if (_readyPlayers.Remove(networkObjectId))
            ReadyCount.Value = _readyPlayers.Count;
    }

    [Rpc(SendTo.Everyone)]
    private void TeleportAllRpc(Vector3 targetPos)
    {
        var playerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (playerObj == null) return;

        var cc = playerObj.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            playerObj.transform.position = targetPos;
            cc.enabled = true;
        }
        else
        {
            playerObj.transform.position = targetPos;
        }

        _localInRange = false;
        if (interactPrompt != null) interactPrompt.SetActive(false);
    }

    // ─── Glow ─────────────────────────────────────────────────────────────────

    private void UpdateGlow()
    {
        if (doorRenderer == null || _mpb == null) return;

        Color emission;
        if (_isLocked || !HasEnoughKeys())
        {
            emission = lockedEmission;  // rosso = bloccata (stanza non cleared o chiavi insufficienti)
        }
        else
        {
            float t = requiredPlayers > 0 ? (float)ReadyCount.Value / requiredPlayers : 1f;
            emission = Color.Lerp(idleEmission, readyEmission, t);
        }

        doorRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_EmissionColor", emission);
        doorRenderer.SetPropertyBlock(_mpb);
    }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (destination == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(destination.position, 0.6f);
        Gizmos.DrawLine(transform.position, destination.position);
        UnityEditor.Handles.Label(destination.position + Vector3.up, "Destination");
    }
#endif
}
