using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Fallback aim provider basato sul mouse.
/// Da usare finch� Lore non ha implementato il sistema di mira.
/// 
/// Da mettere sul prefab player insieme agli altri componenti.
/// Si autoregistra al CoopCameraController in OnNetworkSpawn (solo owner).
///
/// Quando Lore avr� il suo AimController, questo script pu� essere
/// rimosso dal prefab e sostituito con la sua implementazione di IAimProvider.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public sealed class MouseAimProvider : NetworkBehaviour, IAimProvider
{
    private Camera _cam;
    private Vector2 _aimDir;
    private Plane   _gamePlane;
    private float   _lastPlaneY = float.MinValue;

    public Vector2 AimDirection => _aimDir;
    public bool IsAiming => _aimDir.sqrMagnitude > 0.01f;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        _cam = Camera.main;
        CoopCameraController.Instance?.RegisterAimProvider(this);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        CoopCameraController.Instance?.UnregisterAimProvider(this);
    }

    private void Update()
    {
        if (!IsOwner || _cam == null) return;

        // In 3D isometrico il gioco avviene sul piano XZ (Y = quota del player).
        // Proiettiamo il ray del mouse sul piano XZ passante per il player.
        // Il piano viene ricalcolato solo quando la Y cambia (evita ricreazione ogni frame).
        float currentY = transform.position.y;
        if (!Mathf.Approximately(currentY, _lastPlaneY))
        {
            _gamePlane  = new Plane(Vector3.up, transform.position);
            _lastPlaneY = currentY;
        }

        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);

        if (_gamePlane.Raycast(ray, out float dist))
        {
            Vector3 mouseWorld = ray.GetPoint(dist);
            // Direzione sul piano XZ → restituita come Vector2 (X, Z)
            Vector3 dir3 = mouseWorld - transform.position;
            dir3.y = 0f;
            Vector2 dir = new Vector2(dir3.x, dir3.z);
            _aimDir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.zero;
        }
    }
}