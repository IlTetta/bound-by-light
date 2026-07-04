using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class MainMenu : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string gameSceneName = "Cathedral";

    [Header("Join")]
    [SerializeField] private TMPro.TMP_InputField ipInputField;
    [Tooltip("IP mostrato di default se non è ancora stato salvato nessun IP precedente")]
    [SerializeField] private string defaultIp = "192.168.1.8";

    [Header("Host Info (opzionale)")]
    [Tooltip("TMP Text nel pannello Host dove mostrare l'IP locale da condividere col secondo player")]
    [SerializeField] private TMPro.TMP_Text localIpText;

    private const ushort Port = 7777;
    private const string LastIpKey = "LastHostIP";

    private void Start()
    {
        // Pre-compila il campo IP con l'ultimo indirizzo usato
        if (ipInputField != null)
        {
            string lastIp = PlayerPrefs.GetString(LastIpKey, defaultIp);
            if (!string.IsNullOrEmpty(lastIp))
                ipInputField.text = lastIp;
        }
    }

    // ── Bottone HOST ─────────────────────────────────────────────────────────
    // I pannelli vengono già gestiti dal bottone con SetActive.
    // Questa funzione aggiunge solo la logica di rete.
    public void OnHostClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[MainMenu] NetworkManager.Singleton è null. Assicurati che sia in scena.");
            return;
        }

        SetTransportLAN("0.0.0.0", "127.0.0.1");
        NetworkManager.Singleton.StartHost();
        NetworkManager.Singleton.SceneManager.LoadScene(
            gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);

        if (localIpText != null)
            localIpText.text = $"Il tuo IP: {GetLocalIP()}\nCondividilo col secondo player";
    }

    // ── Bottone ENTER (pannello Join) ────────────────────────────────────────
    public void OnEnterClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[MainMenu] NetworkManager.Singleton è null. Assicurati che sia in scena.");
            return;
        }

        string ip = ipInputField.text.Trim();
        if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";

        // Salva l'IP per il prossimo avvio (utile dopo una disconnessione)
        PlayerPrefs.SetString(LastIpKey, ip);
        PlayerPrefs.Save();

        SetTransportLAN(ip, ip);
        NetworkManager.Singleton.StartClient();
    }

    // ── Bottone BACK (pannello Join → menu principale) ───────────────────────
    /// <summary>
    /// Chiama questo sul bottone Back del pannello Join.
    /// Fa lo Shutdown del NetworkManager prima di tornare al menu,
    /// altrimenti StartHost() fallisce silenziosamente se StartClient()
    /// era già stato chiamato in precedenza.
    /// </summary>
    public void OnBackClicked()
    {
        if (NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsClient ||
             NetworkManager.Singleton.IsHost   ||
             NetworkManager.Singleton.IsServer))
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    // ── Bottone QUIT ─────────────────────────────────────────────────────────
    public void OnQuitClicked()
    {
        Application.Quit();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SetTransportLAN(string bindAddress, string connectAddress)
    {
        if (NetworkManager.Singleton == null) return;
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null) return;
        transport.SetConnectionData(connectAddress, Port, bindAddress);
    }

    private static string GetLocalIP()
    {
        try
        {
            foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(ip))
                    return ip.ToString();
        }
        catch { }
        return "n/a";
    }
}
