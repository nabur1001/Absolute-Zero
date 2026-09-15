using System.Collections.Generic;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Player.Identity;
using AbsoluteZero.Core.Session;
using UnityEngine;

namespace AbsoluteZero.Core.Match
{
    public class MatchCompositionRoot : MonoBehaviour
    {
        public static MatchCompositionRoot Instance { get; private set; }

        [SerializeField] GameModeRuleSO[] gameModeRules;

        PlayerRegistry _registry;
        public IReadOnlyPlayerRegistry Registry => _registry;
        public PlayerRegistry WritableRegistry => _registry;

        ItemManager _itemManager;
        MatchManager _matchManager;
        MatchNetworkState _networkState;
        MatchRoster _roster;
        public ItemManager ItemManager => _itemManager;
        public MatchManager MatchManager => _matchManager;
        public MatchNetworkState NetworkState => _networkState;
        public MatchRoster Roster => _roster;

        MatchConfig _activeConfig;
        public MatchConfig ActiveConfig => _activeConfig;
        public string InitializationFailure { get; private set; }
        public const float InitializationTimeout = 30f;

        public void FailInitialization(string reason)
        {
            if (InitializationFailure != null) return;
            InitializationFailure = reason;
            if (_networkState != null && _networkState.IsSpawned && _networkState.IsServer)
                _networkState.InitializationError.Value = new Unity.Collections.FixedString128Bytes(reason);
            Debug.LogError("[MatchInitialization] " + reason);
        }

        void Update()
        {
            if (InitializationFailure == null && _networkState != null && _networkState.IsSpawned
                && _networkState.InitializationError.Value.Length > 0)
                FailInitialization(_networkState.InitializationError.Value.ToString());
        }

        void Awake()
        {
            if (Instance != null)
            {
                Debug.LogError("[MatchCompositionRoot] Duplicate Root detected — destroying this instance");
                Destroy(this);
                return;
            }

            Instance = this;
            _registry = new PlayerRegistry();
            ValidateSceneReferences();
            SubscribeNetworkState();
            Debug.Log("[MatchCompositionRoot] Awake — Registry created, scene references validated");
        }

        void ValidateSceneReferences()
        {
            _itemManager = FindAnyObjectByType<ItemManager>();
            _matchManager = FindAnyObjectByType<MatchManager>();
            _networkState = FindAnyObjectByType<MatchNetworkState>();

            if (_itemManager == null)
                Debug.LogWarning("[MatchCompositionRoot] ItemManager not found in scene");
            if (_matchManager == null)
                Debug.LogWarning("[MatchCompositionRoot] MatchManager not found in scene");
            if (_networkState == null)
                Debug.LogWarning("[MatchCompositionRoot] MatchNetworkState not found in scene");
        }

        void SubscribeNetworkState()
        {
            if (_networkState != null)
                _networkState.OnConfigSynced += OnConfigSynced;
        }

        void UnsubscribeNetworkState()
        {
            if (_networkState != null)
                _networkState.OnConfigSynced -= OnConfigSynced;
        }

        void OnConfigSynced(MatchConfigNetData netData)
        {
            AssembleMatchConfig(netData);
        }

        public MatchConfig AssembleMatchConfig(MatchConfigNetData netData)
        {
            if (InitializationFailure != null) return null;
            var mode = (GameMode)netData.Mode;
            IGameModeRule rule = FindRuleForMode(mode);
            if (rule == null)
            {
                FailInitialization($"No GameModeRule found for mode {mode}");
                return null;
            }

            _activeConfig = new MatchConfig(rule, netData.RequiredPlayerCount, mode);
            Debug.Log($"[MatchCompositionRoot] MatchConfig assembled — Mode={mode}, Players={netData.RequiredPlayerCount}");
            return _activeConfig;
        }

        IGameModeRule FindRuleForMode(GameMode mode)
        {
            if (gameModeRules == null) return null;
            foreach (var rule in gameModeRules)
            {
                if (rule != null && rule.TargetMode == mode)
                    return rule;
            }
            return null;
        }

        public void InitializeRoster(MatchRoster roster)
        {
            _roster = roster;
        }

        public bool ServerBootstrapMatch()
        {
            if (InitializationFailure != null) return false;
            if (_activeConfig != null) return true;
            if (_networkState == null)
            {
                FailInitialization("MatchNetworkState is missing");
                return false;
            }
            if (!_networkState.IsSpawned || !_networkState.IsServer) return false;
            var nsc = NetworkSessionCoordinator.Instance;
            GameMode mode = nsc != null ? nsc.SelectedMode : GameMode.OneVsOne;
            int playerCount = nsc != null ? nsc.SelectedPlayerCount : 2;

            if (_networkState != null)
            {
                if (!_networkState.ServerInitialize(new MatchConfigNetData
                {
                    Mode = (byte)mode,
                    RequiredPlayerCount = (byte)playerCount
                }))
                {
                    FailInitialization("Conflicting match configuration");
                    return false;
                }
                if (_activeConfig == null) AssembleMatchConfig(_networkState.Config.Value);
            }

            Debug.Log($"[MatchCompositionRoot] ServerBootstrapMatch — Mode={mode}, Players={playerCount}");
            return _activeConfig != null;
        }

        public MatchRoster ServerCreateRoster(int requiredCount, IEnumerable<ulong> connectedClientIds)
        {
            var nsc = NetworkSessionCoordinator.Instance;
            SessionParticipantTable spt;

            if (nsc != null)
            {
                spt = nsc.BuildParticipantTable(
                    new List<ulong>(connectedClientIds), requiredCount);
            }
            else
            {
                // Test-only fallback: NSC should always exist in production (DDOL in LobbyScene).
                // This path allows unit/integration tests to run without full lobby flow.
                Debug.LogWarning("[MCR] NSC is null — using throwaway SPT (test-only path)");
                spt = new SessionParticipantTable();
                var sorted = new List<ulong>(connectedClientIds);
                sorted.Sort();
                byte s = 0;
                foreach (var cid in sorted)
                {
                    if (s >= requiredCount) break;
                    string pid = cid.ToString();
                    spt.Register(pid, "match");
                    spt.TryValidateAndBind(pid, "match", cid, out _);
                    s++;
                }
            }

            var roster = new MatchRoster(requiredCount);
            roster.SetRegistry(_registry);
            if (!roster.Hydrate(spt.AllEntries))
            {
                FailInitialization("Roster hydration failed");
                roster.Dispose();
                return null;
            }

            InitializeRoster(roster);

            if (_activeConfig != null && _activeConfig.Mode == GameMode.Multi)
            {
                var dispatcher = DisconnectDispatcher.Instance;
                if (dispatcher != null)
                    dispatcher.SetHandler(roster);
            }

            Debug.Log($"[MatchCompositionRoot] Roster created — {requiredCount} seats");
            return roster;
        }

        void OnDestroy()
        {
            if (Instance != this) return;

            UnsubscribeNetworkState();

            if (_roster != null)
            {
                var dispatcher = Network.DisconnectDispatcher.Instance;
                if (dispatcher != null)
                    dispatcher.RestoreDefaultIfCurrent(_roster);
                _roster.Dispose();
                _roster = null;
            }

            _registry?.Clear();
            _activeConfig = null;
            Instance = null;
            Debug.Log("[MatchCompositionRoot] Destroyed — Registry + Roster cleaned up");
        }
    }
}
