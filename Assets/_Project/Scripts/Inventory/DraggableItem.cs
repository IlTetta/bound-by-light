using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum ItemType { Any, Weapon, Consumable, Ammo, Powerup }

public class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Image image;
    public ItemType itemType = ItemType.Any;
    [HideInInspector] public Transform parentAfterDrag;
    [HideInInspector] public int weaponIndex = -1; // indice arma reale (0=rifle, 1=shotgun)

    public void OnBeginDrag(PointerEventData eventData)
    {
        Debug.Log("Begin Drag");
        parentAfterDrag = transform.parent;
        // Risale al Canvas (non alla radice della scena) per restare nel contesto UI corretto
        Canvas canvas = GetComponentInParent<Canvas>();
        transform.SetParent(canvas != null ? canvas.transform : transform.root);
        transform.SetAsLastSibling();
        image.raycastTarget = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        Debug.Log("Dragging");
        transform.position = eventData.position;
    }
    public void OnEndDrag(PointerEventData eventData)
    {
        Debug.Log("End Drag");
        transform.SetParent(parentAfterDrag);
        image.raycastTarget = true;
    }

}
