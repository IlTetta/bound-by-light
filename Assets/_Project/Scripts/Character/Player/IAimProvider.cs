/// <summary>
/// Interfaccia che espone la direzione di mira normalizzata di un player.
///
/// Lore devi implementarla sul componente
/// che gestisce l'aim (mouse o analogico), e assegnarlo al CoopCameraController
/// tramite il metodo RegisterAimProvider().
///
/// Esempio di implementazione minima:
///
///     public class PlayerAimController : MonoBehaviour, IAimProvider
///     {
///         private Vector2 _aimDir;
///
///         // ... logica mouse/analogico ...
///
///         public Vector2 AimDirection => _aimDir;  // normalizzato
///         public bool    IsAiming     => _aimDir.sqrMagnitude > 0.01f;
///     }
///
/// Devi poi chiamare in OnNetworkSpawn() (solo IsOwner):
///     CoopCameraController.Instance?.RegisterAimProvider(this);
/// E in OnNetworkDespawn():
///     CoopCameraController.Instance?.UnregisterAimProvider(this);
/// </summary>
public interface IAimProvider
{
    /// <summary>Direzione di mira normalizzata nel world space (o screen space per mouse).</summary>
    UnityEngine.Vector2 AimDirection { get; }

    /// <summary>True se il player sta attivamente mirando (evita look-ahead con aim a zero).</summary>
    bool IsAiming { get; }
}