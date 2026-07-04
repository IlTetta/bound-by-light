using Unity.Netcode;
using UnityEngine;
using System;

namespace MyGame.Core
{
    /// <summary>
    /// Manager centrale per Bound by Light. 
    /// Coordina stati di gioco, timer, energia condivisa e progressione delle stanze.
    /// </summary>
    public class GameManager : NetworkSingleton<GameManager>
    {
        // Stati formali del gioco definiti nel concept [cite: 45, 150]
        public enum GameState { Hub, InRoom, Puzzle, BossBattle, GameOver, GameEnding }

        [Header("Match Settings")]
        public GameState InitialState = GameState.Hub;

        [Header("Shared Resources (Netcode)")]
        // Energia del legame condivisa dai gemelli [cite: 56, 207]
        public NetworkVariable<float> SharedBondEnergy = new NetworkVariable<float>(0f);
        // Valuta condivisa ottenuta dai nemici [cite: 184, 206]
        public NetworkVariable<int> SharedCurrency = new NetworkVariable<int>(0);
        // Stato attuale del gioco sincronizzato [cite: 191]
        public NetworkVariable<GameState> CurrentState = new NetworkVariable<GameState>();

        [Header("Room Progression")]
        public NetworkVariable<int> CurrentRoomIndex = new NetworkVariable<int>(0);

        [Header("Keys")]
        /// <summary>Numero di chiavi raccolte dai player (condiviso, non consumabile).</summary>
        public NetworkVariable<int> SharedKeyCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [Header("Ultimate")]
        // True dopo che i player raccolgono l'UltimatePickup nella stanza sotterranea.
        public NetworkVariable<bool> BondAbilityUnlocked = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [Header("Bond Energy Settings")]
        [Tooltip("Moltiplicatore sull'energia ricevuta dai nemici. " +
                 "1 = default, 2 = doppia velocit  di carica. Modifica qui per bilanciare.")]
        [SerializeField] private float energyChargeMultiplier = 2f;

        [Header("Tether Settings")]
        [SerializeField] private GameObject tetherPrefab;

        // Eventi per la UI locale e feedback [cite: 53]
        // Keyword 'event' impedisce a codice esterno di assegnare con = invece di +=
        public event Action OnStateChanged;
        public event Action OnBondEnergyFull;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                CurrentState.Value = InitialState;
                SharedBondEnergy.Value = 0f;
            }

            // Sottoscrizione ai cambiamenti per aggiornare UI e logica locale su Host e Client [cite: 132]
            CurrentState.OnValueChanged += (oldState, newState) => OnStateChanged?.Invoke();
            SharedBondEnergy.OnValueChanged += CheckBondEnergy;
        }

        private void CheckBondEnergy(float oldVal, float newVal)
        {
            if (newVal >= 100f) // Assumendo 100 come carica massima 
            {
                OnBondEnergyFull?.Invoke();
            }
        }

        #region Server Side Logic (Authority)

        /// <summary>
        /// Chiamato quando un nemico viene sconfitto per aggiornare risorse condivise [cite: 184]
        /// </summary>
        public void AddResources(int currencyAmount, float energyAmount)
        {
            if (!IsServer) return;

            SharedCurrency.Value += currencyAmount;

            // L'energia si carica sconfiggendo nemici [cite: 174]
            float nextEnergy = SharedBondEnergy.Value + energyAmount * energyChargeMultiplier;
            SharedBondEnergy.Value = Mathf.Clamp(nextEnergy, 0f, 100f);
        }

        /// <summary>
        /// Gestisce la transizione tra le modular combat rooms [cite: 45, 66]
        /// </summary>
        public void CompleteRoom()
        {
            if (!IsServer) return;

            CurrentRoomIndex.Value++;
            // Dopo un set di stanze, il loop porta al Boss [cite: 70, 188]
            if (CurrentRoomIndex.Value >= 5)
            {
                ChangeState(GameState.BossBattle);
            }
        }

        public void ConsumeEnergy(float amount)
        {
            if (!IsServer) return;
            SharedBondEnergy.Value = Mathf.Clamp(SharedBondEnergy.Value - amount, 0f, 100f);
        }

        [Rpc(SendTo.Server)]
        public void RequestConsumeEnergyServerRpc(float amount)
        {
            ConsumeEnergy(amount);
        }

        /// <summary>Aggiunge una chiave al pool condiviso. Chiamato da KeyPickup lato server.</summary>
        public void AddKey()
        {
            if (!IsServer) return;
            SharedKeyCount.Value++;
            Debug.Log($"[GameManager] Chiave raccolta. Totale: {SharedKeyCount.Value}");
        }

        public void UnlockBondAbility()
        {
            if (!IsServer) return;
            BondAbilityUnlocked.Value = true;
            Debug.Log("[GameManager] Bond Ability sbloccata.");
        }

        public void ChangeState(GameState newState)
        {
            if (!IsServer) return;
            CurrentState.Value = newState;
        }

        /// <summary>
        /// Attende <paramref name="delay"/> secondi, poi despawna il boss e setta GameEnding.
        /// Va chiamato sul server. La coroutine gira su GameManager così non viene
        /// annullata quando il NetworkObject del boss viene distrutto.
        /// </summary>
        public void StartGameEndingSequence(NetworkObject bossNetObj, float delay)
        {
            if (!IsServer) return;
            StartCoroutine(GameEndingRoutine(bossNetObj, delay));
        }

        private System.Collections.IEnumerator GameEndingRoutine(NetworkObject bossNetObj, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (bossNetObj != null && bossNetObj.IsSpawned)
                bossNetObj.Despawn(destroy: true);

            ChangeState(GameState.GameEnding);
        }
        #endregion

        #region Multiplayer Handling

        public void SpawnTether(NetworkObject player1, NetworkObject player2)
        {
            if (!IsServer) return;

            GameObject tetherInst = Instantiate(tetherPrefab);
            NetworkObject netObj = tetherInst.GetComponent<NetworkObject>();
            netObj.Spawn(); // Fa apparire la corda su tutti i client

            TetherManager manager = tetherInst.GetComponent<TetherManager>();
            manager.PlayerARef.Value = player1;
            manager.PlayerBRef.Value = player2;
        }

        #endregion
    }
}