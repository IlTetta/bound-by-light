using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quando il player raccoglie un'arma, istanzia automaticamente il suo icon
/// nello slot arma corrispondente nell'inventario.
///
/// Setup prefab:
///   - Assegna WeaponData[0] = WeaponData del Rifle, WeaponData[1] = WeaponData del Shotgun
///   - Assegna WeaponSlots[0] = Slot 0 del Weapon Grid, WeaponSlots[1] = Slot 1
/// </summary>
public class WeaponInventoryDisplay : MonoBehaviour
{
    [Header("Weapon Data (stesso ordine degli slot: 0=Rifle, 1=Shotgun)")]
    [SerializeField] private WeaponData[] weaponData;

    [Header("Slot arma nell'inventario (Weapon Grid > Slot, Slot(1))")]
    [SerializeField] private Transform[] weaponSlots;

    private PlayerController _controller;

    private void Start()
    {
        _controller = GetComponent<PlayerController>();
        if (_controller != null)
            _controller.OnWeaponAcquired += OnWeaponAcquired;
        InventorySlot.OnWeaponSwapped += OnInventoryWeaponSwapped;
    }

    private void OnDestroy()
    {
        if (_controller != null)
            _controller.OnWeaponAcquired -= OnWeaponAcquired;
        InventorySlot.OnWeaponSwapped -= OnInventoryWeaponSwapped;
    }

    private void OnInventoryWeaponSwapped(int destinationSlotIndex)
    {
        _controller?.SwitchToWeapon(destinationSlotIndex);
    }

    private void OnWeaponAcquired(int slot)
    {
        if (slot < 0 || slot >= weaponSlots.Length) return;
        if (slot >= weaponData.Length || weaponData[slot] == null) return;

        Transform slotTransform = weaponSlots[slot];
        if (slotTransform == null) return;

        // Rimuovi eventuale item già presente nello slot
        foreach (Transform child in slotTransform)
            Destroy(child.gameObject);

        WeaponData data = weaponData[slot];
        if (data.icon == null) return;

        // Crea il GO dell'item nell'inventario
        GameObject item = new GameObject(data.weaponName);
        item.transform.SetParent(slotTransform, false);

        RectTransform rt = item.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = item.AddComponent<Image>();
        img.sprite = data.icon;
        img.preserveAspect = false;

        DraggableItem draggable = item.AddComponent<DraggableItem>();
        draggable.image = img;
        draggable.itemType = ItemType.Weapon;
        draggable.weaponIndex = slot;
    }
}
