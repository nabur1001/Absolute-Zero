using System;
using AbsoluteZero.Core.Network;
using UnityEngine;

namespace AbsoluteZero.Core.Session
{
    public class AppBootstrapper : MonoBehaviour
    {
        public static AppBootstrapper Instance { get; private set; }

        public bool IsReady { get; private set; }
        public event Action OnReady;

        void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
            {
                Destroy(this);
                return;
            }
        }

        async void Start()
        {
            try
            {
                await InitializeSequenceAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AppBootstrapper] Fatal init error: {e}");
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        async System.Threading.Tasks.Task InitializeSequenceAsync()
        {
            Debug.Log("[AppBootstrapper] Initialization sequence starting...");

            var coordinator = NetworkSessionCoordinator.Instance;
            if (coordinator == null)
            {
                Debug.LogError("[AppBootstrapper] NetworkSessionCoordinator not found on Managers");
                return;
            }

            await coordinator.InitializeAsync();

            if (coordinator.State == SessionState.Failed)
            {
                Debug.LogError($"[AppBootstrapper] Coordinator init failed: {coordinator.LastError}");
                return;
            }

            IsReady = true;
            Debug.Log("[AppBootstrapper] Initialization complete — all systems ready");
            OnReady?.Invoke();
        }
    }
}
