using AbsoluteZero.Core.Match;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace AbsoluteZero.Core.Network
{
    public class PlayerSpawnManager : MonoBehaviour, IDisconnectHandler
    {
        public static PlayerSpawnManager Instance { get; private set; }

        [Header("=== Player Prefab ===")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private bool spawnPlayerCharacter = true;

        [Header("=== Spawn Points ===")]
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private bool useSceneSpawnPointMarkers = true;
        [SerializeField] private bool includeInactiveSceneSpawnMarkers = false;
        [SerializeField] private float defaultSpawnRadius = 5f;
        [SerializeField] private float topDownFallbackY = 1f;

        private readonly Dictionary<ulong, NetworkObject> spawnedPlayers = new Dictionary<ulong, NetworkObject>();
        private readonly HashSet<ulong> pendingSpawnClients = new HashSet<ulong>();
        private readonly List<Transform> resolvedSpawnPoints = new List<Transform>();
        private bool networkCallbacksSubscribed;
        private bool sceneCallbacksSubscribed;
        private bool dispatcherRegistered;
        private bool directDisconnectSubscribed;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            RefreshResolvedSpawnPoints();
            TryRegisterCallbacks();
            QueueExistingClients();
            TrySpawnPendingClients();
        }

        private void Update()
        {
            if (!networkCallbacksSubscribed || !sceneCallbacksSubscribed)
                TryRegisterCallbacks();

            if (!dispatcherRegistered)
                TryRegisterDispatcher();

            if (pendingSpawnClients.Count > 0)
                TrySpawnPendingClients();
        }

        private void TryRegisterCallbacks()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            if (!networkCallbacksSubscribed)
            {
                networkManager.OnClientConnectedCallback += OnClientConnectedCallback;

                if (!dispatcherRegistered)
                {
                    networkManager.OnClientDisconnectCallback += OnPlayerDisconnected;
                    directDisconnectSubscribed = true;
                }

                networkCallbacksSubscribed = true;
            }

            if (!sceneCallbacksSubscribed && networkManager.SceneManager != null)
            {
                networkManager.SceneManager.OnLoadComplete += OnSceneLoadComplete;
                sceneCallbacksSubscribed = true;
            }
        }

        private void OnDestroy()
        {
            UnregisterCallbacks();
            if (Instance == this) Instance = null;
        }

        private void UnregisterCallbacks()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                networkCallbacksSubscribed = false;
                sceneCallbacksSubscribed = false;
                return;
            }

            if (networkCallbacksSubscribed)
            {
                networkManager.OnClientConnectedCallback -= OnClientConnectedCallback;
                if (directDisconnectSubscribed)
                {
                    networkManager.OnClientDisconnectCallback -= OnPlayerDisconnected;
                    directDisconnectSubscribed = false;
                }
                networkCallbacksSubscribed = false;
            }

            if (sceneCallbacksSubscribed && networkManager.SceneManager != null)
            {
                networkManager.SceneManager.OnLoadComplete -= OnSceneLoadComplete;
                sceneCallbacksSubscribed = false;
            }
        }

        private void TryRegisterDispatcher()
        {
            if (dispatcherRegistered) return;
            if (DisconnectDispatcher.Instance == null) return;

            DisconnectDispatcher.Instance.SetDefaultHandler(this);
            dispatcherRegistered = true;

            if (directDisconnectSubscribed)
            {
                var nm = NetworkManager.Singleton;
                if (nm != null)
                    nm.OnClientDisconnectCallback -= OnPlayerDisconnected;
                directDisconnectSubscribed = false;
            }
        }

        private bool IsGameplayScene()
        {
            return MatchCompositionRoot.Instance != null;
        }

        private void QueueExistingClients()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer) return;

            foreach (var client in networkManager.ConnectedClientsList)
            {
                if (!spawnedPlayers.ContainsKey(client.ClientId))
                    pendingSpawnClients.Add(client.ClientId);
            }
        }

        private void TrySpawnPendingClients()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer) return;
            if (!IsGameplayScene()) return;

            var mcr = MatchCompositionRoot.Instance;
            var roster = mcr?.Roster;

            if (roster != null && !roster.RosterReady)
                return;

            QueueExistingClients();
            if (pendingSpawnClients.Count == 0) return;

            List<ulong> spawnQueue = new List<ulong>(pendingSpawnClients);
            foreach (ulong clientId in spawnQueue)
            {
                if (!networkManager.ConnectedClients.ContainsKey(clientId))
                {
                    pendingSpawnClients.Remove(clientId);
                    continue;
                }

                if (spawnedPlayers.ContainsKey(clientId))
                {
                    pendingSpawnClients.Remove(clientId);
                    continue;
                }

                SpawnPlayerForClient(clientId, roster);
                pendingSpawnClients.Remove(clientId);
            }
        }

        private void OnClientConnectedCallback(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            pendingSpawnClients.Add(clientId);
            Debug.Log($"[PlayerSpawnManager] Client {clientId} connected, queued for spawn.");
            TrySpawnPendingClients();
        }

        public void OnPlayerDisconnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            pendingSpawnClients.Remove(clientId);
            Debug.Log($"[PlayerSpawnManager] Client {clientId} disconnected, despawning player...");
            DespawnPlayerForClient(clientId);
        }

        private void OnSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer) return;
            if (clientId != networkManager.LocalClientId) return;

            RefreshResolvedSpawnPoints();
            TrySpawnPendingClients();
        }

        private void SpawnPlayerForClient(ulong clientId, MatchRoster roster)
        {
            if (!spawnPlayerCharacter) return;

            if (playerPrefab == null)
            {
                Debug.LogError("[PlayerSpawnManager] Player prefab is not assigned.");
                return;
            }

            if (spawnedPlayers.ContainsKey(clientId))
            {
                Debug.LogWarning($"[PlayerSpawnManager] Player already spawned for client {clientId}");
                return;
            }

            byte seat = 0;
            bool usedSeat = roster != null && roster.TryGetSeatByClientId(clientId, out seat);
            Vector3 spawnPosition = usedSeat
                ? GetSpawnPositionBySeat(seat)
                : GetSpawnPositionLegacy(clientId);

            GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);

            NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError("[PlayerSpawnManager] Player prefab is missing NetworkObject.");
                Destroy(playerInstance);
                return;
            }

            networkObject.SpawnAsPlayerObject(clientId);
            spawnedPlayers[clientId] = networkObject;

            Debug.Log($"[PlayerSpawnManager] Player spawned for client {clientId} at {spawnPosition}" +
                      (usedSeat ? $" (seat={seat})" : " (legacy)"));
        }

        private void DespawnPlayerForClient(ulong clientId)
        {
            if (spawnedPlayers.TryGetValue(clientId, out NetworkObject networkObject))
            {
                if (networkObject != null && networkObject.IsSpawned)
                {
                    networkObject.Despawn();
                    Destroy(networkObject.gameObject);
                }

                spawnedPlayers.Remove(clientId);
                Debug.Log($"[PlayerSpawnManager] Player despawned for client {clientId}");
            }
        }

        public Vector3 GetSpawnPositionBySeat(byte seatIndex)
        {
            if (seatIndex < resolvedSpawnPoints.Count && resolvedSpawnPoints[seatIndex] != null)
                return resolvedSpawnPoints[seatIndex].position;

            return GetFallbackSpawn(seatIndex);
        }

        private Vector3 GetSpawnPositionLegacy(ulong clientId)
        {
            if (resolvedSpawnPoints.Count > 0)
            {
                int index = (int)(clientId % (ulong)resolvedSpawnPoints.Count);
                if (resolvedSpawnPoints[index] != null)
                    return resolvedSpawnPoints[index].position;

                resolvedSpawnPoints.RemoveAll(t => t == null);
                if (resolvedSpawnPoints.Count > 0)
                {
                    index = (int)(clientId % (ulong)resolvedSpawnPoints.Count);
                    return resolvedSpawnPoints[index].position;
                }
            }

            return GetFallbackSpawn(clientId);
        }


        public void RefreshResolvedSpawnPoints()
        {
            resolvedSpawnPoints.Clear();
            HashSet<Transform> unique = new HashSet<Transform>();

            if (spawnPoints != null)
            {
                foreach (Transform point in spawnPoints)
                {
                    if (point == null) continue;
                    if (unique.Add(point))
                        resolvedSpawnPoints.Add(point);
                }
            }

            if (useSceneSpawnPointMarkers)
            {
                PlayerSpawnPoint3D[] markers = FindObjectsByType<PlayerSpawnPoint3D>(
                    includeInactiveSceneSpawnMarkers ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None
                );
                if (markers != null && markers.Length > 0)
                {
                    System.Array.Sort(markers, (a, b) => a.Order.CompareTo(b.Order));
                    foreach (PlayerSpawnPoint3D marker in markers)
                    {
                        if (marker == null) continue;
                        Transform markerTransform = marker.transform;
                        if (unique.Add(markerTransform))
                            resolvedSpawnPoints.Add(markerTransform);
                    }
                }
            }

            Debug.Log($"[PlayerSpawnManager] Resolved spawn points: {resolvedSpawnPoints.Count}");
        }

        private Vector3 GetFallbackSpawn(ulong clientId)
        {
            Vector3 center = new Vector3(0f, topDownFallbackY, 0f);
            float angleDeg = (clientId + 1) * 137.5f;
            float angleRad = angleDeg * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(
                Mathf.Cos(angleRad) * defaultSpawnRadius,
                0f,
                Mathf.Sin(angleRad) * defaultSpawnRadius
            );
            return center + offset;
        }

        public NetworkObject GetPlayerForClient(ulong clientId)
        {
            spawnedPlayers.TryGetValue(clientId, out NetworkObject player);
            return player;
        }

        public IReadOnlyDictionary<ulong, NetworkObject> GetAllPlayers()
        {
            return spawnedPlayers;
        }

        public Vector3 GetRespawnPosition(ulong clientId)
        {
            var mcr = MatchCompositionRoot.Instance;
            var roster = mcr?.Roster;
            if (roster != null && roster.TryGetSeatByClientId(clientId, out byte seat))
                return GetSpawnPositionBySeat(seat);
            return GetSpawnPositionLegacy(clientId);
        }
    }
}
