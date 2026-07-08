using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Da aggiungere al root di PlayerPrefab_Twin1 e Twin2.
/// In OnNetworkSpawn disabilita tutto l'HUD per i player remoti e
/// collega i riferimenti runtime per il player locale.
/// </summary>
public class LocalPlayerHUD : NetworkBehaviour
{
    [Header("HUD Root Objects")]
    [SerializeField] private GameObject ui;
    [SerializeField] private GameObject minimapCamera;
    [SerializeField] private GameObject canvasMark;
    [SerializeField] private GameObject bars;
    [SerializeField] private GameObject canvasMinimap;
    [SerializeField] private GameObject inventario;
    [SerializeField] private GameObject pauseMenu;


    [Header("Item HUD Camera (overlay medikit)")]
    [Tooltip("Il GO HandItem con la Camera Overlay per il medikit. Lascia vuoto se non usato.")]
    [SerializeField] private Camera itemHudCamera;

    [Header("Waiting for Reconnect Panel (solo Host)")]
    [Tooltip("Pannello 'Waiting for Player 2' nel prefab del Twin.")]
    [SerializeField] private GameObject waitingPanel;
    [Tooltip("TextMeshProUGUI con il countdown nel pannello di attesa.")]
    [SerializeField] private TextMeshProUGUI waitingTimerText;
    [Tooltip("Bottone 'Return to Main Menu' nel pannello di attesa.")]
    [SerializeField] private Button waitingGiveUpButton;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
            SetupLocalPlayer();
        else
            DisableRemoteHUD();
    }

    private void SetupLocalPlayer()
    {
        if (ui != null)            ui.SetActive(true);
        if (minimapCamera != null) minimapCamera.SetActive(true);
        if (canvasMark != null)    canvasMark.SetActive(true);
        if (bars != null)          bars.SetActive(true);
        if (canvasMinimap != null) canvasMinimap.SetActive(true);
        if (inventario != null)    inventario.SetActive(true);
        if (pauseMenu != null)     pauseMenu.SetActive(false);

        CoopCameraController.Instance?.RegisterPlayer(transform);

        // Aggiunge la camera overlay del medikit allo stack della Main Camera
        if (itemHudCamera != null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                var cameraData = mainCam.GetUniversalAdditionalCameraData();
                if (!cameraData.cameraStack.Contains(itemHudCamera))
                    cameraData.cameraStack.Add(itemHudCamera);
            }
        }

        // Collega Pause Menu al PauseController nella scena
        PauseController pc = FindFirstObjectByType<PauseController>();
        if (pc != null && pauseMenu != null)
            pc.PauseUI = pauseMenu;

        // Collega questo GameObject alla MinimapCamera
        if (minimapCamera != null)
        {
            MinimapCamerNoRotation minimap = minimapCamera.GetComponent<MinimapCamerNoRotation>();
            if (minimap != null)
                minimap.Player = gameObject;
        }

        // Collega PlayerController all'InventoryMenu
        if (inventario != null)
        {
            InventoryMenu invMenu = inventario.GetComponentInChildren<InventoryMenu>(true);
            if (invMenu != null)
                invMenu.player = GetComponent<PlayerController>();
        }

        // Collega il pannello di attesa reconnect a DisconnectionHandler (solo sull'host)
        if (IsServer && waitingPanel != null)
        {
            DisconnectionHandler dh = FindFirstObjectByType<DisconnectionHandler>();
            if (dh != null)
            {
                dh.WaitingPanel = waitingPanel;
                dh.TimerText    = waitingTimerText;

                // Collega il bottone "Return to Main Menu" a runtime
                // (non è possibile farlo nell'inspector perché DisconnectionHandler è nella scena)
                if (waitingGiveUpButton != null)
                    waitingGiveUpButton.onClick.AddListener(dh.OnGiveUpClicked);

                waitingPanel.SetActive(false);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        CoopCameraController.Instance?.UnregisterPlayer(transform);

        // Rimuove la camera overlay dallo stack
        if (itemHudCamera != null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
                mainCam.GetUniversalAdditionalCameraData().cameraStack.Remove(itemHudCamera);
        }
    }

    private void DisableRemoteHUD()
    {
        if (ui != null) ui.SetActive(false);
        if (minimapCamera != null) minimapCamera.SetActive(false);
        if (canvasMark != null) canvasMark.SetActive(false);
        if (bars != null) bars.SetActive(false);
        if (canvasMinimap != null) canvasMinimap.SetActive(false);
        if (inventario != null) inventario.SetActive(false);
        if (pauseMenu != null) pauseMenu.SetActive(false);
        if (itemHudCamera != null) itemHudCamera.gameObject.SetActive(false);
    }
}
