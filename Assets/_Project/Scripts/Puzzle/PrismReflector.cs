using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

/// <summary>
/// NetworkTransform è usato SOLO per la rotazione (sync rotation ON, sync position OFF).
/// La posizione è gestita interamente da _railT NetworkVariable → ApplyRailPosition().
/// Questo evita il conflitto tra NetworkTransform che sovrascrive transform.position
/// e il nostro sistema a binario.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
public class PrismReflector : NetworkBehaviour
{
    [Header("Rail Settings")]
    [SerializeField] private Transform railStart;
    [SerializeField] private Transform railEnd;
    [Tooltip("Velocità in unità-mondo/secondo.")]
    [SerializeField] private float moveSpeed = 3f;

    [Header("Rotation Settings")]
    [SerializeField] private float rotationSpeed = 90f;

    [Header("Interaction")]
    [SerializeField] public float interactionRadius = 1.5f;

    [Header("Visual Feedback")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Color hitColor = new Color(1f, 0.95f, 0.4f);
    [SerializeField] private float hitFadeDuration = 0.15f;

    // ── NetworkVariables ──────────────────────────────────────────────────────
    private NetworkVariable<float> _railT = new NetworkVariable<float>(
        0.5f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<float> _rotationZ = new NetworkVariable<float>(
        45f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<ulong> _controllingClient = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Stato locale ──────────────────────────────────────────────────────────
    private Color _idleColor;
    private float _hitFadeTimer = 0f;
    private bool _isHitThisFrame = false;

    private bool IsLocallyControlling =>
        IsSpawned && NetworkManager.Singleton != null &&
        _controllingClient.Value == NetworkManager.Singleton.LocalClientId;

    public float InteractionRadius => interactionRadius;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Disabilita sync posizione su NetworkTransform: la gestiamo noi via _railT.
        // Lascia sync rotazione ON così la rotazione è già propagata da NetworkTransform.
        var nt = GetComponent<NetworkTransform>();
        if (nt != null)
        {
            nt.SyncPositionX = false;
            nt.SyncPositionY = false;
            nt.SyncPositionZ = false;
            // SyncRotAngleZ rimane true (default)
        }

        // Colore idle: se il SpriteRenderer ha colore nero/trasparente in editor,
        // usiamo bianco come fallback sicuro
        if (spriteRenderer != null)
        {
            _idleColor = spriteRenderer.color;
            // Se il colore è nero (impostazione di default errata in editor) → forza bianco
            if (_idleColor == Color.black || _idleColor.a < 0.01f)
            {
                _idleColor = Color.white;
                spriteRenderer.color = _idleColor;
            }
        }
        else
        {
            _idleColor = Color.white;
        }

        if (IsServer)
        {
            ApplyRailPosition(_railT.Value);
            ApplyRotation(_rotationZ.Value);
        }

        _railT.OnValueChanged += (_, t) => ApplyRailPosition(t);
        _rotationZ.OnValueChanged += (_, r) => ApplyRotation(r);
    }

    private void Update()
    {
        HandleHitFeedback();
        if (!IsLocallyControlling) return;
        HandleMovementInput();
        HandleRotationInput();
    }

    // ── API ───────────────────────────────────────────────────────────────────

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
        float input = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) input = -1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) input = 1f;
        if (Mathf.Approximately(input, 0f)) return;

        float newT = Mathf.Clamp01(_railT.Value + input * moveSpeed * Time.deltaTime / RailLength());
        RequestMoveServerRpc(newT);
    }

    private void HandleRotationInput()
    {
        float rotInput = 0f;
        if (Input.GetKey(KeyCode.Q)) rotInput = 1f;
        if (Input.GetKey(KeyCode.E)) rotInput = -1f;
        if (Mathf.Approximately(rotInput, 0f)) return;

        RequestRotateServerRpc(_rotationZ.Value + rotInput * rotationSpeed * Time.deltaTime);
    }

    // ── Server RPCs ───────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void RequestToggleControlServerRpc(ulong clientId, bool take)
    {
        _controllingClient.Value = take ? clientId : ulong.MaxValue;
    }

    [Rpc(SendTo.Server)]
    private void RequestMoveServerRpc(float newT)
    {
        _railT.Value = Mathf.Clamp01(newT);
        ApplyRailPosition(_railT.Value);
    }

    [Rpc(SendTo.Server)]
    private void RequestRotateServerRpc(float newRot)
    {
        _rotationZ.Value = newRot;
        ApplyRotation(newRot);
    }

    // ── Trasformazioni ────────────────────────────────────────────────────────

    private void ApplyRailPosition(float t)
    {
        if (railStart == null || railEnd == null) return;
        transform.position = Vector3.Lerp(railStart.position, railEnd.position, t);
    }

    private void ApplyRotation(float rotZ)
        => transform.rotation = Quaternion.Euler(0f, 0f, rotZ);

    private float RailLength()
    {
        if (railStart == null || railEnd == null) return 1f;
        return Mathf.Max(0.1f, Vector3.Distance(railStart.position, railEnd.position));
    }

    // ── Feedback visivo ───────────────────────────────────────────────────────

    private void HandleHitFeedback()
    {
        if (spriteRenderer == null) return;

        if (_isHitThisFrame)
        {
            spriteRenderer.color = hitColor;
            _hitFadeTimer = hitFadeDuration;
            _isHitThisFrame = false;
        }
        else if (_hitFadeTimer > 0f)
        {
            _hitFadeTimer -= Time.deltaTime;
            spriteRenderer.color = Color.Lerp(_idleColor, hitColor, _hitFadeTimer / hitFadeDuration);
        }
        else
        {
            spriteRenderer.color = _idleColor;
        }
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (railStart != null && railEnd != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(railStart.position, railEnd.position);
            Gizmos.DrawWireSphere(railStart.position, 0.1f);
            Gizmos.DrawWireSphere(railEnd.position, 0.1f);
        }
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}