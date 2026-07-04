using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Gestisce l'interazione del player con i PrismReflector3D.
///
/// F vicino a un prisma  → prendi controllo, player bloccato
///                          WASD controllano il prisma (W/S = binario, A/D = rotazione)
/// F di nuovo            → rilascia controllo, player sbloccato
/// </summary>
[RequireComponent(typeof(PlayerMovementMotor3D))]
public class PlayerPuzzleInteractor : NetworkBehaviour
{
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    private PlayerMovementMotor3D _motor;
    private PrismReflector3D _currentPrism;

    private void Awake()
    {
        _motor = GetComponent<PlayerMovementMotor3D>();
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Verifica esterna: se il prisma ha perso il controllo (es. disconnessione), sblocca
        if (_currentPrism != null && !_currentPrism.IsLocallyControlling)
            ReleaseControl();

        if (!Input.GetKeyDown(interactKey)) return;

        if (_currentPrism != null)
        {
            // Rilascia il prisma corrente
            _currentPrism.ToggleControl();
            ReleaseControl();
        }
        else
        {
            // Cerca il prisma più vicino e prendi controllo
            PrismReflector3D nearest = FindNearestPrism();
            if (nearest == null) return;

            nearest.ToggleControl();
            _currentPrism = nearest;
            _motor.SetLocked(true);
        }
    }

    private void ReleaseControl()
    {
        _currentPrism = null;
        _motor.SetLocked(false);
    }

    private PrismReflector3D FindNearestPrism()
    {
        var prisms = FindObjectsOfType<PrismReflector3D>();
        PrismReflector3D best = null;
        float minDist = float.MaxValue;

        foreach (var p in prisms)
        {
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < p.InteractionRadius && d < minDist)
            {
                minDist = d;
                best = p;
            }
        }

        return best;
    }
}
