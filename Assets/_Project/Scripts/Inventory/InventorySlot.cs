using System;
using UnityEngine;
using UnityEngine.EventSystems;

public enum SlotType { Any, Weapon, Consumable, Ammo, Powerup }

public class InventorySlot : MonoBehaviour, IDropHandler
{
    public SlotType slotType = SlotType.Any;

    /// <summary>Fired quando due slot arma si scambiano. Parametro = indice slot destinazione (0 o 1).</summary>
    public static event Action<int> OnWeaponSwapped;

    /// <summary>Indice arma (0 o 1) per slot di tipo Weapon; -1 altrimenti.</summary>
    public int weaponSlotIndex = -1;

    public void OnDrop(PointerEventData eventData)
    {
        GameObject dropped = eventData.pointerDrag;
        if (dropped == null) return;

        DraggableItem draggable = dropped.GetComponent<DraggableItem>();
        if (draggable == null) return;

        // Controlla compatibilità tipo slot/item
        if (slotType != SlotType.Any && draggable.itemType != ItemType.Any && draggable.itemType != (ItemType)slotType)
            return;

        if (transform.childCount == 0)
        {
            draggable.parentAfterDrag = transform;
        }
        else
        {
            // Swap: l'item nello slot va dove stava quello trascinato
            Transform sourceTransform = draggable.parentAfterDrag;
            DraggableItem current = transform.GetChild(0).GetComponent<DraggableItem>();
            if (current != null)
                current.transform.SetParent(sourceTransform);
            draggable.parentAfterDrag = transform;

            // Equipaggia l'arma trascinata usando l'indice memorizzato sull'icona stessa
            InventorySlot sourceSlot = sourceTransform.GetComponent<InventorySlot>();
            if (slotType == SlotType.Weapon && sourceSlot != null && sourceSlot.slotType == SlotType.Weapon
                && draggable.weaponIndex >= 0)
            {
                OnWeaponSwapped?.Invoke(draggable.weaponIndex);
            }
        }
    }
}
