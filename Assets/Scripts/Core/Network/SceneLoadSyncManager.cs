using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.Core.Network
{
    public class SceneLoadSyncManager : NetworkBehaviour
    {
        public static SceneLoadSyncManager Instance { get; private set; }

        [Header("=== Settings ===")]
        [SerializeField] private float loadTimeout = 30f;
        [SerializeField] private float fadeOutDuration = 0.5f;

        [Header("=== UI References ===")]
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private CanvasGroup overlayCanvasGroup;
        [SerializeField] private Image progressBarFill;

        public bool AllPlayersLoaded => networkAllPlayersLoaded.Value;

        private readonly NetworkVariable<bool> networkAllPlayersLoaded = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> networkLoadedCount = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> networkTotalCount = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private HashSet<ulong> loadedClients;
        private float timeoutTimer;

        private bool isFadingOut;
        private float fadeTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            networkLoadedCount.OnValueChanged += OnLoadedCountChanged;
            networkTotalCount.OnValueChanged += OnTotalCountChanged;
            networkAllPlayersLoaded.OnValueChanged += OnAllPlayersLoadedChanged;

            if (IsServer)
            {
                loadedClients = new HashSet<ulong>();
                timeoutTimer = loadTimeout;

                int total = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);

                networkTotalCount.Value = total;
                networkLoadedCount.Value = 0;
                networkAllPlayersLoaded.Value = false;

                Debug.Log($"[SceneLoadSyncManager] Server initialized. Waiting for {total} clients to load.");
            }

            if (networkAllPlayersLoaded.Value)
            {
                Debug.Log("[SceneLoadSyncManager] Already loaded. Skipping overlay.");
                return;
            }

            ShowOverlay();
            UpdateUI();
            ReportLoadedServerRpc();
        }

        public override void OnNetworkDespawn()
        {
            networkLoadedCount.OnValueChanged -= OnLoadedCountChanged;
            networkTotalCount.OnValueChanged -= OnTotalCountChanged;
            networkAllPlayersLoaded.OnValueChanged -= OnAllPlayersLoadedChanged;

            if (Instance == this) Instance = null;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (IsServer && !networkAllPlayersLoaded.Value)
            {
                timeoutTimer -= Time.unscaledDeltaTime;
                if (timeoutTimer <= 0f)
                {
                    Debug.LogWarning($"[SceneLoadSyncManager] Timeout reached. " +
                                     $"Loaded {networkLoadedCount.Value}/{networkTotalCount.Value}. Forcing start.");
                    networkAllPlayersLoaded.Value = true;
                }
            }

            if (isFadingOut && overlayCanvasGroup != null)
            {
                fadeTimer += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(fadeTimer / fadeOutDuration);
                overlayCanvasGroup.alpha = 1f - t;

                if (t >= 1f)
                {
                    isFadingOut = false;
                    HideOverlay();
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReportLoadedServerRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;

            if (!loadedClients.Add(clientId)) return;

            networkLoadedCount.Value = loadedClients.Count;
            Debug.Log($"[SceneLoadSyncManager] Client {clientId} loaded. ({loadedClients.Count}/{networkTotalCount.Value})");

            if (loadedClients.Count >= networkTotalCount.Value)
            {
                Debug.Log("[SceneLoadSyncManager] All players loaded!");
                networkAllPlayersLoaded.Value = true;
            }
        }

        #region Overlay Control

        private void ShowOverlay()
        {
            if (overlayRoot == null)
            {
                Debug.LogWarning("[SceneLoadSyncManager] overlayRoot is not assigned.");
                return;
            }

            overlayRoot.SetActive(true);

            if (overlayCanvasGroup == null)
                overlayCanvasGroup = overlayRoot.GetComponent<CanvasGroup>();

            if (overlayCanvasGroup != null)
            {
                overlayCanvasGroup.alpha = 1f;
                overlayCanvasGroup.blocksRaycasts = true;
            }
        }

        private void HideOverlay()
        {
            if (overlayRoot != null)
                overlayRoot.SetActive(false);
        }

        private void UpdateUI()
        {
            if (progressBarFill == null) return;

            int loaded = networkLoadedCount.Value;
            int total = networkTotalCount.Value;
            float progress = total > 0 ? (float)loaded / total : 0f;

            progressBarFill.fillAmount = progress;
        }

        private void OnLoadedCountChanged(int oldValue, int newValue) => UpdateUI();
        private void OnTotalCountChanged(int oldValue, int newValue) => UpdateUI();

        private void OnAllPlayersLoadedChanged(bool oldValue, bool newValue)
        {
            if (newValue)
            {
                Debug.Log("[SceneLoadSyncManager] All players loaded. Fading out overlay.");

                if (overlayCanvasGroup != null)
                {
                    overlayCanvasGroup.blocksRaycasts = false;
                    isFadingOut = true;
                    fadeTimer = 0f;
                }
                else
                {
                    HideOverlay();
                }
            }
        }

        #endregion
    }
}
