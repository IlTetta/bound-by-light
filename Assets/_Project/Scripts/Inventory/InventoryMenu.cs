using UnityEngine;
using System.Collections;
using System;
using UnityEngine.UI;


public class InventoryMenu : MonoBehaviour
{ // Assign in inspector
    public Canvas CanvasObject;
    public PlayerController player;

    void Awake() //Awake e On destroy servono per applicare lo script della pausa al player controller
    {
        PauseController.OnGameStateChanged += OnGameStateChanged;
    }

    void OnDestroy()
    {
        PauseController.OnGameStateChanged -= OnGameStateChanged;
    }


    void Update()
    {
        if (PauseController.CurrentGameState == GameState.Paused)
        {
            return;
        }

        if (Input.GetKeyUp(KeyCode.E))
        {
            toggleInventory();
        }

    }

    public void toggleInventory()
    {
        setInventory(!CanvasObject.enabled);
    }

    public void setInventory(bool active)
    {
        CanvasObject.enabled = active;
        if (player != null) player.enabled = !active;
        // Il cursore è sempre visibile (mira col mouse) — PauseController gestisce il cursore
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnGameStateChanged(GameState newGameState) // Serve per implementare allo script l'effetto del pause
    {
        setInventory(false);
    }


}

