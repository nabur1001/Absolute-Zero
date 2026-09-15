using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Network
{
    public class DisconnectDispatcher : MonoBehaviour
    {
        public static DisconnectDispatcher Instance { get; private set; }

        IDisconnectHandler _currentHandler;
        IDisconnectHandler _defaultHandler;
        NetworkManager _subscribedNm;

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            TrySubscribe();
        }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (_subscribedNm != null && _subscribedNm != nm)
            {
                Unsubscribe();
            }
            if (_subscribedNm == null && nm != null)
                TrySubscribe();
        }

        void TrySubscribe()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || _subscribedNm != null) return;

            nm.OnClientDisconnectCallback += HandleDisconnect;
            _subscribedNm = nm;
            Debug.Log("[DisconnectDispatcher] Subscribed to OnClientDisconnectCallback");
        }

        void Unsubscribe()
        {
            if (_subscribedNm == null) return;
            _subscribedNm.OnClientDisconnectCallback -= HandleDisconnect;
            _subscribedNm = null;
        }

        public void SetDefaultHandler(IDisconnectHandler handler)
        {
            _defaultHandler = handler;
            if (_currentHandler == null)
                _currentHandler = handler;
        }

        public void SetHandler(IDisconnectHandler handler)
        {
            _currentHandler = handler;
            Debug.Log($"[DisconnectDispatcher] Handler set: {handler?.GetType().Name ?? "null"}");
        }

        public void RestoreDefaultIfCurrent(IDisconnectHandler handler)
        {
            if (_currentHandler == handler)
            {
                _currentHandler = _defaultHandler;
                Debug.Log("[DisconnectDispatcher] Restored default handler");
            }
        }

        void HandleDisconnect(ulong clientId)
        {
            if (_subscribedNm == null || !_subscribedNm.IsServer) return;

            Debug.Log($"[DisconnectDispatcher] Disconnect: ClientId={clientId}, Handler={_currentHandler?.GetType().Name ?? "none"}");
            _currentHandler?.OnPlayerDisconnected(clientId);
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this)
                Instance = null;
        }
    }
}
