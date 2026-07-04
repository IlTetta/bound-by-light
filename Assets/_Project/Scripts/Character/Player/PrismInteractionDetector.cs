using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Aggiunto al prefab del Player.
/// - Premi F vicino a un prisma per prenderne il controllo.
/// - Mentre controlli: A/D muovono sul binario, Q/E ruotano.
/// - Premi F di nuovo per rilasciare.
/// - Il motor del player viene bloccato tramite SetLocked (non disabled),
///   così il NetworkBehaviour resta attivo e la rete non si rompe.
/// </summary>
public class PrismInteractionDetector : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private bool disablePlayerMovement = true;

    private PlayerMovementMotor3D _motor3D;
    private PlayerMovementMotor2D _motor2D;
    private PlayerController _controller;
    private PlayerFaintHandler _faintHandler;
    private PrismReflector _controlledPrism;

    public override void OnNetworkSpawn()
    {
        // Solo il client owner gestisce l'input
        if (!IsOwner) { enabled = false; return; }

        _motor3D      = GetComponent<PlayerMovementMotor3D>();
        _motor2D      = GetComponent<PlayerMovementMotor2D>();
        _controller   = GetComponent<PlayerController>();
        _faintHandler = GetComponent<PlayerFaintHandler>();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F)) return;

        if (_controlledPrism != null)
            ReleasePrism();
        else
        {
            var nearest = FindNearestPrism();
            if (nearest != null) GrabPrism(nearest);
        }
    }

    // ── Grab / Release ────────────────────────────────────────────────────────

    private void GrabPrism(PrismReflector prism)
    {
        _controlledPrism = prism;
        _controlledPrism.ToggleControl();

        if (disablePlayerMovement)
        {
            // SetLocked blocca il movimento ma NON disabilita il NetworkBehaviour
            if (_motor3D != null) _motor3D.SetLocked(true);
            else if (_motor2D != null) _motor2D.SetLocked(true);
            if (_controller != null) _controller.enabled = false;
        }
    }

    private void ReleasePrism()
    {
        _controlledPrism.ToggleControl();
        _controlledPrism = null;

        if (disablePlayerMovement)
        {
            if (_motor3D != null) _motor3D.SetLocked(false);
            else if (_motor2D != null) _motor2D.SetLocked(false);
            // Re-abilita il controller solo se il player non è fainted
            bool isFainted = _faintHandler != null && _faintHandler.IsFainted;
            if (_controller != null && !isFainted)
                _controller.enabled = true;
        }
    }

    // ── Ricerca prisma ────────────────────────────────────────────────────────

    private PrismReflector FindNearestPrism()
    {
        var allPrisms = FindObjectsByType<PrismReflector>(FindObjectsSortMode.None);
        PrismReflector nearest = null;
        float minDist = float.MaxValue;

        foreach (var prism in allPrisms)
        {
            Vector3 a = transform.position; a.y = 0f;
            Vector3 b = prism.transform.position; b.y = 0f;
            float dist = Vector3.Distance(a, b);
            if (dist < prism.InteractionRadius && dist < minDist)
            {
                minDist = dist;
                nearest = prism;
            }
        }
        return nearest;
    }
}