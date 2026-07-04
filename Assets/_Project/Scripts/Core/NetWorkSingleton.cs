using Unity.Netcode;
using UnityEngine;

namespace MyGame.Core
{
    /// <summary>
    /// Singleton sincronizzato sulla rete. 
    /// Eredita da NetworkBehaviour per usare NetworkVariables e RPC.
    /// </summary>
    public class NetworkSingleton<T> : NetworkBehaviour where T : NetworkBehaviour
    {
        protected static T _instance;
        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<T>();
                }
                return _instance;
            }
        }

        protected virtual void Awake()
        {
            if (_instance == null)
            {
                _instance = this as T;
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}