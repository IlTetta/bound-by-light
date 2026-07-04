using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

/// <summary>
/// Prisma rifrangente 3D per il puzzle di Cathedral.
/// Si muove su un binario in XZ e ruota attorno all'asse Y.
/// Deve avere un BoxCollider su layer "PuzzleLight" affinché il raggio lo colpisca.
///
/// Controlli (solo quando si ha il controllo, premi F vicino al prisma):
///   Freccia Sinistra / Destra : muove sul binario
///   Q / E                     : ruota
///
/// Sync rete: NetworkVariable per railT e rotY (no NetworkTransform,
/// per evitare conflitti con la nostra gestione manuale della posizione).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PrismReflector3D : NetworkBehaviour
{
    [Header("Rail")]
    [SerializeField] private Transform railStart;
    [SerializeField] private Transform railEnd;
    [SerializeField] private float moveSpeed = 3f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 90f;

    [Header("Interaction")]
    [SerializeField] public float interactionRadius = 2f;

    [Header("Visual Feedback")]
    [SerializeField] private Renderer prismRenderer;
    [SerializeField] private Color hitColor = new Color(1f, 0.95f, 0.4f);
    [SerializeField] private float hitFadeDuration = 0.15f;

    // ── NetworkVariables ──────────────────────────────────────────────────────
    private NetworkVariable<float> _railT = new NetworkVariable<float>(
        0.5f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<float> _rotationY = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<ulong> _controllingClient = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Stato locale ──────────────────────────────────────────────────────────
    private Color _idleColor;
    private float _hitFadeTimer;
    private bool _isHitThisFrame;
    private MaterialPropertyBlock _mpb;

    public bool IsLocallyControlling =>
        IsSpawned && NetworkManager.Singleton != null &&
        _controllingClient.Value == NetworkManager.Singleton.LocalClientId;

    public float InteractionRadius => interactionRadius;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
    }

    public override void OnNetworkSpawn()
    {
        if (prismRenderer != null)
        {
            _idleColor = prismRenderer.sharedMaterial != null
                ? prismRenderer.sharedMaterial.color
                : Color.white;
            if (_idleColor == Color.black || _idleColor.a < 0.01f)
                _idleColor = Color.white;
        }
        else
        {
            _idleColor = Color.white;
        }

        if (IsServer)
        {
            ApplyRailPosition(_railT.Value);
            ApplyRotation(_rotationY.Value);
        }

        _railT.OnValueChanged += (_, t) => ApplyRailPosition(t);
        _rotationY.OnValueChanged += (_, r) => ApplyRotation(r);
    }

    private void Update()
    {
        HandleHitFeedback();


        if (!IsLocallyControlling) return;
        HandleMovementInput();
        HandleRotationInput();
    }

    // ── API pubblica ──────────────────────────────────────────────────────────

    public void ToggleControl()
    {
        if (!IsSpawned) return;
        ulong myId = NetworkManager.Singleton.LocalClientId;
        bool iAmInControl = _controllingClient.Value == myId;
        RequestToggleControlServerRpc(myId, !iAmInControl);
    }

    public void OnHit() => _isHitThisFrame = true;

    // ── Input ─────────────────────────────────────────────────────────────────

    private void HandleMovementInput()
    {
        var kb = Keyboard.current;
        if (kb == null) { Debug.LogWarning("[Prism] Keyboard.current è null!"); return; }

        bool w = kb.wKey.isPressed;
        bool s = kb.sKey.isPressed;
        if (Time.frameCount % 30 == 0)
            Debug.Log($"[Prism] HandleMovementInput | W={w} S={s} railT={_railT.Value:F3} railLen={RailLength():F2}");

        float input = 0f;
        if (s) input = -1f;
        if (w) input =  1f;
        if (Mathf.Approximately(input, 0f)) return;

        float newT = Mathf.Clamp01(_railT.Value + input * moveSpeed * Time.deltaTime / RailLength());
        Debug.Log($"[Prism] RequestMove newT={newT:F3}");
        RequestMoveServerRpc(newT);
    }

    private void HandleRotationInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // A/D ruotano il prisma
        float rotInput = 0f;
        if (kb.aKey.isPressed) rotInput =  1f;
        if (kb.dKey.isPressed) rotInput = -1f;
        if (Mathf.Approximately(rotInput, 0f)) return;

        RequestRotateServerRpc(_rotationY.Value + rotInput * rotationSpeed * Time.deltaTime);
    }

    // ── Server RPCs ───────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void RequestToggleControlServerRpc(ulong clientId, bool take)
    {
        _controllingClient.Value = take ? clientId : ulong.MaxValue;
        Debug.Log($"[PrismReflector3D] {name}: controllo {(take ? "PRESO" : "RILASCIATO")} da client {clientId}. _controllingClient={_controllingClient.Value}");
    }

    [Rpc(SendTo.Server)]
    private void RequestMoveServerRpc(float newT)
    {
        Debug.Log($"[Prism RPC] Ricevuto! newT={newT:F3} IsServer={IsServer} IsSpawned={IsSpawned}");
        _railT.Value = Mathf.Clamp01(newT);
        ApplyRailPosition(_railT.Value);
        Debug.Log($"[Prism RPC] Dopo: railT={_railT.Value:F3} pos={transform.position}");
    }

    [Rpc(SendTo.Server)]
    private void RequestRotateServerRpc(float newRot)
    {
        _rotationY.Value = newRot;
        ApplyRotation(newRot);
    }

    // ── Trasformazioni ────────────────────────────────────────────────────────

    private void ApplyRailPosition(float t)
    {
        if (railStart == null || railEnd == null) return;
        transform.position = Vector3.Lerp(railStart.position, railEnd.position, t);
    }

    private void ApplyRotation(float rotY)
        => transform.rotation = Quaternion.Euler(0f, rotY, 0f);

    private float RailLength()
    {
        if (railStart == null || railEnd == null) return 1f;
        return Mathf.Max(0.1f, Vector3.Distance(railStart.position, railEnd.position));
    }

    // ── Feedback visivo ───────────────────────────────────────────────────────

    private void HandleHitFeedback()
    {
        if (prismRenderer == null) return;

        if (_isHitThisFrame)
        {
            SetRendererColor(hitColor);
            _hitFadeTimer = hitFadeDuration;
            _isHitThisFrame = false;
        }
        else if (_hitFadeTimer > 0f)
        {
            _hitFadeTimer -= Time.deltaTime;
            SetRendererColor(Color.Lerp(_idleColor, hitColor, _hitFadeTimer / hitFadeDuration));
        }
        else
        {
            SetRendererColor(_idleColor);
        }
    }

    private void SetRendererColor(Color c)
    {
        prismRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor("_BaseColor", c);
        _mpb.SetColor("_Color", c);
        prismRenderer.SetPropertyBlock(_mpb);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (railStart != null && railEnd != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(railStart.position, railEnd.position);
            Gizmos.DrawWireSphere(railStart.position, 0.15f);
            Gizmos.DrawWireSphere(railEnd.position, 0.15f);
        }
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}
