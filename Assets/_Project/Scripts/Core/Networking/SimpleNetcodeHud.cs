using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// ═══════════════════════════════════════════════════════════════════════════════
// RELAY - decommentare i due blocchi using qui sotto quando i pacchetti
//         com.unity.services.relay e com.unity.services.authentication
//         sono installati e il progetto è collegato a UGS.
// ═══════════════════════════════════════════════════════════════════════════════
// using Unity.Services.Core;
// using Unity.Services.Authentication;
// using Unity.Services.Relay;
// using Unity.Services.Relay.Models;

public class SimpleNetcodeHud : MonoBehaviour
{
    // ── Stato LAN ─────────────────────────────────────────────────────────────
    private string _connectAddress = "127.0.0.1";
    private const ushort Port = 7777;

    // ═══════════════════════════════════════════════════════════════════════════
    // RELAY - stato interno
    // Decommentare insieme al blocco using e al blocco #region RELAY più in basso.
    // ═══════════════════════════════════════════════════════════════════════════
    // private string _joinCode      = "";
    // private string _relayStatus   = "";
    // private bool   _relayBusy     = false;   // true mentre le chiamate async sono in corso

    private void OnGUI()
    {
        const int w = 220, h = 40, pad = 10;
        int y = pad;

        bool isListening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        GUI.enabled = NetworkManager.Singleton != null && !isListening;

        // ── LAN: Host ─────────────────────────────────────────────────────────
        if (GUI.Button(new Rect(pad, y, w, h), "Start Host (LAN)"))
        {
            SetTransportAddressLAN("0.0.0.0", _connectAddress);
            NetworkManager.Singleton.StartHost();
        }

        y += h + pad;

        // ── LAN: Client ───────────────────────────────────────────────────────
        GUI.Label(new Rect(pad, y, 60, h), "Host IP:");
        _connectAddress = GUI.TextField(new Rect(pad + 65, y, w - 65, h), _connectAddress);

        y += h + pad;

        if (GUI.Button(new Rect(pad, y, w, h), "Start Client (LAN)"))
        {
            SetTransportAddressLAN(_connectAddress, _connectAddress);
            NetworkManager.Singleton.StartClient();
        }

        y += h + pad;

        // ═══════════════════════════════════════════════════════════════════════
        // RELAY UI - decommentare il blocco qui sotto quando Relay è configurato.
        // Mostra due bottoni (Host via Relay / Join via Relay) e i campi
        // per join code e status.
        // ═══════════════════════════════════════════════════════════════════════
        /*
        y += pad; // separatore visivo

        GUI.enabled = NetworkManager.Singleton != null && !isListening && !_relayBusy;

        if (GUI.Button(new Rect(pad, y, w, h), "Host via Relay"))
            _ = StartHostRelayAsync();

        y += h + pad;

        GUI.Label(new Rect(pad, y, 70, h), "Join Code:");
        _joinCode = GUI.TextField(new Rect(pad + 75, y, w - 75, h), _joinCode).ToUpper();

        y += h + pad;

        if (GUI.Button(new Rect(pad, y, w, h), "Join via Relay"))
            _ = StartClientRelayAsync();

        y += h + pad;

        GUI.enabled = true;

        if (!string.IsNullOrEmpty(_relayStatus))
            GUI.Label(new Rect(pad, y, 520, h), _relayStatus);

        y += h + pad;
        */
        // ═══════════════════════════════════════════════════════════════════════

        // ── Status connessione + Disconnect ──────────────────────────────────
        GUI.enabled = true;

        if (isListening)
        {
            string mode = NetworkManager.Singleton.IsServer ? "Host" : "Client";
            int count = NetworkManager.Singleton.ConnectedClients.Count;
            string myIp = GetLocalIP();
            var originalColor = GUI.color;
            GUI.color = Color.black;
            GUI.Label(new Rect(pad, y, 520, h * 2),
                $"Mode: {mode} | Clients: {count}\nLocal IP: {myIp} | Port: {Port}");
            GUI.color = originalColor;

            y += h * 2 + pad;

            if (GUI.Button(new Rect(pad, y, w, h), "Disconnect"))
                NetworkManager.Singleton.Shutdown();
        }
    }

    // ── LAN helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Configura UnityTransport per connessione diretta LAN/IP.
    /// bindAddress : su cosa il server ascolta ("0.0.0.0" = tutte le interfacce).
    /// connectAddress : IP a cui il client si connette.
    /// </summary>
    private static void SetTransportAddressLAN(string bindAddress, string connectAddress)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("[SimpleNetcodeHud] UnityTransport non trovato sul NetworkManager.");
            return;
        }
        transport.SetConnectionData(connectAddress, Port, bindAddress);
    }

    /// <summary>Restituisce il primo IP locale non-loopback (mostrato all'host per LAN).</summary>
    private static string GetLocalIP()
    {
        try
        {
            var hostEntry = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in hostEntry.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(ip))
                    return ip.ToString();
            }
        }
        catch { /* ignora */ }
        return "n/a";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RELAY - metodi async
    // Decommentare l'intero blocco #region quando Relay è configurato.
    // Richiede:
    //   - com.unity.services.relay
    //   - com.unity.services.authentication
    //   - Progetto collegato a UGS (Edit > Project Settings > Services)
    //   - Relay abilitato nella dashboard UGS (Multiplayer > Relay > Get started)
    // ═══════════════════════════════════════════════════════════════════════════

    /*
    #region RELAY

    /// <summary>
    /// Inizializza Unity Gaming Services e fa il login anonimo.
    /// Va chiamata prima di qualsiasi operazione Relay.
    /// </summary>
    private async Task InitUGSAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized) return;

        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    /// <summary>
    /// Crea una sessione Relay, ottiene il join code e avvia l'host.
    /// Mostra il join code nell'HUD così puoi copiarlo e mandarlo all'amico.
    /// </summary>
    private async Task StartHostRelayAsync()
    {
        _relayBusy  = true;
        _relayStatus = "Creazione sessione Relay...";

        try
        {
            await InitUGSAsync();

            // Crea un'allocazione per max 1 client (= 2 giocatori totali con host)
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections: 1);

            // Il join code è la stringa da condividere con il client (es. "AX3K-9F2B")
            _joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            _relayStatus = $"Join Code: {_joinCode}  ← mandalo all'amico";

            // Configura UnityTransport per usare Relay invece di UDP diretto
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            NetworkManager.Singleton.StartHost();
        }
        catch (System.Exception e)
        {
            _relayStatus = $"Errore Relay Host: {e.Message}";
            Debug.LogError($"[SimpleNetcodeHud] Relay host error: {e}");
        }
        finally
        {
            _relayBusy = false;
        }
    }

    /// <summary>
    /// Si unisce a una sessione Relay tramite join code e avvia il client.
    /// </summary>
    private async Task StartClientRelayAsync()
    {
        if (string.IsNullOrWhiteSpace(_joinCode))
        {
            _relayStatus = "Inserisci un join code prima di connetterti.";
            return;
        }

        _relayBusy   = true;
        _relayStatus = "Connessione a Relay...";

        try
        {
            await InitUGSAsync();

            JoinAllocation joinAllocation =
                await RelayService.Instance.JoinAllocationAsync(_joinCode.Trim());

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            NetworkManager.Singleton.StartClient();
            _relayStatus = "Connesso via Relay.";
        }
        catch (System.Exception e)
        {
            _relayStatus = $"Errore Relay Client: {e.Message}";
            Debug.LogError($"[SimpleNetcodeHud] Relay client error: {e}");
        }
        finally
        {
            _relayBusy = false;
        }
    }

    #endregion RELAY
    */
}