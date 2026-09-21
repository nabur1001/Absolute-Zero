using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbsoluteZero.Core.Cosmetic;
using AbsoluteZero.Core.Network;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

using LobbyPlayer = Unity.Services.Lobbies.Models.Player;

namespace AbsoluteZero.Core.Session
{
    public enum SessionState
    {
        Offline,
        Initializing,
        Ready,
        Connecting,
        LoadingGame,
        InGame,
        Disconnecting,
        Failed
    }

    public enum SessionOperation
    {
        None,
        CreatingLobby,
        JoiningLobby,
        WaitingRelayCode,
        AllocatingRelay,
        JoiningRelay,
        StartingHost,
        StartingClient,
        WaitingForPlayers
    }

    public class NetworkSessionCoordinator : MonoBehaviour
    {
        public static NetworkSessionCoordinator Instance { get; private set; }

        [SerializeField] float relayCodeTimeoutSeconds = 30f;
        [SerializeField] float gameEntryTimeoutSeconds = 60f;
        [SerializeField] int maxPlayers = 2;

        SessionState _state = SessionState.Offline;
        SessionOperation _operation = SessionOperation.None;
        uint _operationGeneration;
        string _lastError;
        bool _isHostRole;
        Lobby _currentLobby;

        GameMode _selectedMode = GameMode.OneVsOne;
        int _selectedPlayerCount = 2;

        SessionParticipantTable _participantTable;

        IUnityServicesGateway _services;
        ILobbyGateway _lobbyGateway;
        IRelayGateway _relayGateway;
        INetworkRuntime _networkRuntime;
        ISceneTransitionService _sceneTransition;

        public SessionState State => _state;
        public SessionOperation Operation => _operation;
        public string LastError => _lastError;
        public bool IsHostRole => _isHostRole;
        public Lobby CurrentLobby => _currentLobby;
        public GameMode SelectedMode => _selectedMode;
        public int SelectedPlayerCount => _selectedPlayerCount;

        public SessionParticipantTable ParticipantTable => _participantTable;

        public event Action<SessionState, SessionOperation> OnStateChanged;
        public event Action<string> OnError;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(this);
                return;
            }

            _services = new UnityServicesGateway();
            _lobbyGateway = new LobbyGateway();
            _relayGateway = new RelayGateway();
            _networkRuntime = new NgoNetworkRuntime();
            _sceneTransition = new SceneTransitionService("LobbyScene");
        }

        // InitializeAsync is called by AppBootstrapper — no self-init here

        void OnEnable()
        {
            TrySubscribeNgoCallbacks();
            TrySubscribeLobbyPoll();
        }

        void OnDisable()
        {
            if (_lobbyPollSubscribed)
            {
                var lobbyMgr = LobbyManager.Instance;
                if (lobbyMgr != null)
                    lobbyMgr.OnLobbyUpdated -= OnLobbyPolled;
                _lobbyPollSubscribed = false;
            }

            if (_ngoCallbacksSubscribed)
            {
                var nm = Unity.Netcode.NetworkManager.Singleton;
                if (nm != null)
                    nm.OnClientStopped -= OnNetworkStopped;
                _ngoCallbacksSubscribed = false;
            }
        }

        void OnDestroy()
        {
            _operationGeneration++;
            if (Instance == this) Instance = null;
        }

        bool _lobbyPollSubscribed;

        void TrySubscribeLobbyPoll()
        {
            if (_lobbyPollSubscribed) return;
            var lobbyMgr = LobbyManager.Instance;
            if (lobbyMgr == null) return;
            lobbyMgr.OnLobbyUpdated += OnLobbyPolled;
            _lobbyPollSubscribed = true;
        }

        void OnLobbyPolled(Lobby lobby)
        {
            if (_currentLobby == null || lobby == null) return;
            if (_currentLobby.Id != lobby.Id) return;
            _currentLobby = lobby;
        }

        bool _ngoCallbacksSubscribed;

        void TrySubscribeNgoCallbacks()
        {
            if (_ngoCallbacksSubscribed) return;
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnClientStopped += OnNetworkStopped;
            _ngoCallbacksSubscribed = true;
        }

        void OnNetworkStopped(bool wasHost)
        {
            if (_state == SessionState.Disconnecting) return;
            // The pending entry operation owns cleanup and reports its failure.
            if (_state == SessionState.LoadingGame) return;

            if (_state != SessionState.InGame && _state != SessionState.LoadingGame &&
                _state != SessionState.Connecting)
                return;

            Debug.Log("[SessionCoordinator] External network stop detected — resetting to Ready");
            _currentLobby = null;
            _isHostRole = false;
            _selectedMode = GameMode.OneVsOne;
            _selectedPlayerCount = 2;
            _participantTable?.Clear();
            _participantTable = null;
            _operationGeneration++;

            var lobbyMgr = LobbyManager.Instance;
            if (lobbyMgr != null)
            {
                lobbyMgr.SyncFromCoordinator(null, false);
                lobbyMgr.SetGameSessionActive(false);
            }

            if (_services.IsInitialized && _services.IsSignedIn)
                SetState(SessionState.Ready);
            else
                SetState(SessionState.Failed);
        }

        #region State Machine

        void SetState(SessionState newState, SessionOperation newOp = SessionOperation.None)
        {
            if (_state == newState && _operation == newOp) return;
            Debug.Log($"[SessionCoordinator] {_state}/{_operation} -> {newState}/{newOp}");
            _state = newState;
            _operation = newOp;
            OnStateChanged?.Invoke(_state, _operation);
        }

        void SetError(string message)
        {
            _lastError = message;
            OnError?.Invoke(message);
        }

        OperationScope BeginOperation()
        {
            _lastError = null;
            _operationGeneration++;
            return new OperationScope(() => _operationGeneration);
        }

        #endregion

        #region Public Commands

        public async Task InitializeAsync()
        {
            if (_state != SessionState.Offline && _state != SessionState.Failed) return;

            SetState(SessionState.Initializing);

            try
            {
                string profile = DetectParrelSyncProfile();
                var result = await _services.InitializeAndSignInAsync(profile);

                if (result.IsFailure)
                {
                    SetError(result.ErrorMessage);
                    SetState(SessionState.Failed);
                    return;
                }

                TrySubscribeNgoCallbacks();
                TrySubscribeLobbyPoll();
                SetState(SessionState.Ready);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SessionCoordinator] InitializeAsync exception: {e}");
                SetError(e.Message);
                SetState(SessionState.Failed);
            }
        }

        public async Task RetryInitializeAsync()
        {
            if (_state != SessionState.Failed) return;
            await InitializeAsync();
        }

        public void SetMatchParameters(GameMode mode, int playerCount)
        {
            _selectedMode = mode;
            _selectedPlayerCount = Mathf.Clamp(playerCount, 2, 4);
            Debug.Log($"[SessionCoordinator] Match params set — Mode={_selectedMode}, Players={_selectedPlayerCount}");
        }

        public async Task<Result<Unit>> CreateLobbyAsync(string lobbyName = null)
        {
            if (_state != SessionState.Ready)
                return Result<Unit>.Failure(OperationErrorCode.InvalidState, $"Cannot create lobby from state {_state}");

            _isHostRole = true;
            lobbyName ??= $"AZ_{UnityEngine.Random.Range(1000, 9999)}";

            SetState(SessionState.Connecting, SessionOperation.CreatingLobby);
            int lobbySlots = _selectedPlayerCount;
            var createResult = await _lobbyGateway.CreateAsync(lobbyName, lobbySlots, new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = CreatePlayerData(),
                Data = new Dictionary<string, DataObject>
                {
                    { "GameMode", new DataObject(DataObject.VisibilityOptions.Public, _selectedMode.ToString()) },
                    { "PlayerCount", new DataObject(DataObject.VisibilityOptions.Public, lobbySlots.ToString()) },
                    { "HostReady", new DataObject(DataObject.VisibilityOptions.Public, "false") },
                    { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, "") },
                    { "GameStarted", new DataObject(DataObject.VisibilityOptions.Member, "false") }
                }
            });

            if (createResult.IsFailure)
            {
                _isHostRole = false;
                return FailAndRecover(createResult.ErrorCode, createResult.ErrorMessage);
            }

            _currentLobby = createResult.Value;
            SyncLobbyManager(_currentLobby, true);
            LobbyManager.Instance?.FireCreatedEvent();

            SetState(SessionState.Ready);
            return Result<Unit>.Success(Unit.Value);
        }

        public async Task<Result<Unit>> StartMatchAsHostAsync()
        {
            if (_state != SessionState.Ready || _currentLobby == null || !_isHostRole)
                return Result<Unit>.Failure(OperationErrorCode.InvalidState, "No active lobby or not host");

            TrySubscribeNgoCallbacks();
            var scope = BeginOperation();
            scope.PushCompensation(() => CleanupLobby(_currentLobby.Id));

            // 1) Allocate relay — maxConnections = joining clients (playerCount - 1)
            SetState(SessionState.Connecting, SessionOperation.AllocatingRelay);
            int relayConnections = _selectedPlayerCount - 1;

            var relayResult = await _relayGateway.AllocateAsync(relayConnections);
            if (relayResult.IsFailure) { await scope.RunCompensations(); return FailAndRecover(relayResult.ErrorCode, relayResult.ErrorMessage); }
            if (scope.IsStale) return await scope.CancelWithCompensation();

            // 2) Start host — register ConnectionApproval first
            SetState(SessionState.Connecting, SessionOperation.StartingHost);
            RegisterConnectionApproval();
            scope.PushCompensation(() => { UnregisterConnectionApproval(); _networkRuntime.Shutdown(); return Task.CompletedTask; });

            var hostResult = _networkRuntime.StartHost(relayResult.Value.ServerData);
            if (hostResult.IsFailure) { await scope.RunCompensations(); return FailAndRecover(hostResult.ErrorCode, hostResult.ErrorMessage); }
            if (scope.IsStale) return await scope.CancelWithCompensation();

            // 3) Publish relay code to lobby
            var updateResult = await _lobbyGateway.UpdateAsync(_currentLobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, relayResult.Value.JoinCode) },
                    { "GameStarted", new DataObject(DataObject.VisibilityOptions.Member, "true") }
                }
            });

            if (updateResult.IsSuccess)
                _currentLobby = updateResult.Value;

            if (scope.IsStale) return await scope.CancelWithCompensation();

            // 4) Load game scene — branch by mode
            SetState(SessionState.LoadingGame);
            LobbyManager.Instance?.SetGameSessionActive(true);

            string sceneName = _selectedMode == GameMode.Multi ? "GameScene_Multi" : "GameScene";
            var sceneResult = _networkRuntime.LoadNetworkScene(sceneName);
            if (sceneResult.IsFailure)
            {
                LobbyManager.Instance?.SetGameSessionActive(false);
                await scope.RunCompensations();
                return FailAndRecover(sceneResult.ErrorCode, sceneResult.ErrorMessage);
            }

            return await CompleteGameEntryAsync(scope, sceneName);
        }

        public async Task<Result<Unit>> JoinGameAsync(string lobbyCode)
        {
            if (_state != SessionState.Ready)
                return Result<Unit>.Failure(OperationErrorCode.InvalidState, $"Cannot join from state {_state}");

            TrySubscribeNgoCallbacks();
            var scope = BeginOperation();
            _isHostRole = false;

            // 1) Join lobby by code
            SetState(SessionState.Connecting, SessionOperation.JoiningLobby);
            var joinResult = await _lobbyGateway.JoinByCodeAsync(lobbyCode, new JoinLobbyByCodeOptions
            {
                Player = CreatePlayerData()
            });

            if (joinResult.IsFailure) return FailAndRecover(joinResult.ErrorCode, joinResult.ErrorMessage);
            if (scope.IsStale) { scope.PushCompensation(() => LeaveLobbyCleanup(joinResult.Value.Id)); return await scope.CancelWithCompensation(); }

            _currentLobby = joinResult.Value;
            SyncLobbyManager(_currentLobby, false);
            LobbyManager.Instance?.FireJoinedEvent();

            scope.PushCompensation(() => LeaveLobbyCleanup(_currentLobby.Id));

            // 2) Wait for relay code
            SetState(SessionState.Connecting, SessionOperation.WaitingRelayCode);
            var relayCodeResult = await WaitForRelayCodeAsync(scope);
            if (relayCodeResult.IsFailure) { await scope.RunCompensations(); return FailAndRecover(relayCodeResult.ErrorCode, relayCodeResult.ErrorMessage); }
            if (scope.IsStale) return await scope.CancelWithCompensation();

            // 3) Join relay
            SetState(SessionState.Connecting, SessionOperation.JoiningRelay);
            var relayResult = await _relayGateway.JoinAsync(relayCodeResult.Value);
            if (relayResult.IsFailure) { await scope.RunCompensations(); return FailAndRecover(relayResult.ErrorCode, relayResult.ErrorMessage); }
            if (scope.IsStale) return await scope.CancelWithCompensation();

            // 4) Start client
            SetState(SessionState.Connecting, SessionOperation.StartingClient);
            scope.PushCompensation(() => { _networkRuntime.Shutdown(); return Task.CompletedTask; });

            var clientResult = _networkRuntime.StartClient(relayResult.Value.ServerData);
            if (clientResult.IsFailure) { await scope.RunCompensations(); return FailAndRecover(clientResult.ErrorCode, clientResult.ErrorMessage); }

            // 5) Scene will be loaded by host via NGO SceneManager
            SetState(SessionState.LoadingGame);
            LobbyManager.Instance?.SetGameSessionActive(true);

            string sceneName = _selectedMode == GameMode.Multi ? "GameScene_Multi" : "GameScene";
            return await CompleteGameEntryAsync(scope, sceneName);
        }

        async Task<Result<Unit>> CompleteGameEntryAsync(OperationScope scope, string sceneName)
        {
            float deadline = Time.realtimeSinceStartup + gameEntryTimeoutSeconds;
            var nm = Unity.Netcode.NetworkManager.Singleton;
            while (this != null && !scope.IsStale)
            {
                if (nm == null || !nm.IsListening)
                {
                    string reason = nm != null ? nm.DisconnectReason : null;
                    string message = string.IsNullOrEmpty(reason)
                        ? "Game connection closed before scene synchronization completed" : reason;
                    SetError(message);
                    await LeaveAsync();
                    return Result<Unit>.Failure(OperationErrorCode.NetworkStartFailed, message);
                }

                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
                // NGO sets IsConnectedClient after initial scene synchronization.
                if (nm.IsConnectedClient && scene.IsValid() && scene.isLoaded &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene() == scene)
                {
                    SetState(SessionState.InGame);
                    return Result<Unit>.Success(Unit.Value);
                }

                if (Time.realtimeSinceStartup >= deadline)
                {
                    const string message = "Timed out waiting for game scene synchronization";
                    SetError(message);
                    await LeaveAsync();
                    return Result<Unit>.Failure(OperationErrorCode.Timeout, message);
                }
                await Task.Delay(100);
            }
            // A newer leave/join owns the session; never shut it down here.
            return Result<Unit>.Failure(OperationErrorCode.Cancelled, "Game entry was cancelled");
        }

        public async Task LeaveAsync()
        {
            if (_state == SessionState.Offline || _state == SessionState.Disconnecting) return;

            SetState(SessionState.Disconnecting);
            _operationGeneration++;

            try
            {
                UnregisterConnectionApproval();
                _networkRuntime.Shutdown();

                if (RelayManager.Instance != null)
                    RelayManager.Instance.ClearState();

                if (_currentLobby != null)
                {
                    string lobbyId = _currentLobby.Id;
                    if (_isHostRole)
                        await _lobbyGateway.DeleteAsync(lobbyId);
                    else
                        await _lobbyGateway.RemovePlayerAsync(lobbyId, _services.PlayerId);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SessionCoordinator] Leave cleanup error: {e.Message}");
            }
            finally
            {
                bool wasInGame = _state == SessionState.Disconnecting &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "LobbyScene";

                _currentLobby = null;
                _isHostRole = false;
                _selectedMode = GameMode.OneVsOne;
                _selectedPlayerCount = 2;
                _participantTable?.Clear();
                _participantTable = null;

                var lobbyMgr = LobbyManager.Instance;
                if (lobbyMgr != null)
                {
                    lobbyMgr.SyncFromCoordinator(null, false);
                    lobbyMgr.SetGameSessionActive(false);
                    lobbyMgr.FireLeftEvent();
                }

                if (_services.IsInitialized && _services.IsSignedIn)
                    SetState(SessionState.Ready);
                else
                    SetState(SessionState.Failed);

                if (wasInGame)
                    _sceneTransition.LoadTitleScene();
            }
        }

        #endregion

        #region Internal Helpers

        void RegisterConnectionApproval()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null) return;
            nm.ConnectionApprovalCallback += ApproveConnection;
            nm.NetworkConfig.ConnectionApproval = true;
        }

        void UnregisterConnectionApproval()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null) return;
            nm.ConnectionApprovalCallback -= ApproveConnection;
        }

        void ApproveConnection(
            Unity.Netcode.NetworkManager.ConnectionApprovalRequest request,
            Unity.Netcode.NetworkManager.ConnectionApprovalResponse response)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            int connected = nm != null ? nm.ConnectedClientsIds.Count : 0;

            if (connected >= _selectedPlayerCount)
            {
                response.Approved = false;
                response.Reason = "Lobby full";
                Debug.Log($"[SessionCoordinator] Connection denied — {connected}/{_selectedPlayerCount}");
                return;
            }

            response.Approved = true;
            response.CreatePlayerObject = false;
        }

        Result<Unit> FailAndRecover(OperationErrorCode code, string message)
        {
            SetError(message);
            SetState(SessionState.Ready);
            return Result<Unit>.Failure(code, message);
        }

        public SessionParticipantTable BuildParticipantTable(IReadOnlyList<ulong> connectedClientIds, int requiredCount)
        {
            if (_participantTable != null && _participantTable.Count >= requiredCount)
            {
                Debug.Log($"[SessionCoordinator] ParticipantTable already populated ({_participantTable.Count} entries) — reusing");
                return _participantTable;
            }

            _participantTable = new SessionParticipantTable();
            var sorted = new List<ulong>(connectedClientIds);
            sorted.Sort();

            byte seat = 0;
            foreach (var clientId in sorted)
            {
                if (seat >= requiredCount) break;
                string pid = clientId.ToString();
                _participantTable.Register(pid, "match");
                _participantTable.TryValidateAndBind(pid, "match", clientId, out _);
                seat++;
            }

            Debug.Log($"[SessionCoordinator] ParticipantTable built — {seat} seats from {connectedClientIds.Count} clients");
            return _participantTable;
        }

        void SyncLobbyManager(Lobby lobby, bool isHostRole)
        {
            var lobbyMgr = LobbyManager.Instance;
            if (lobbyMgr == null) return;
            lobbyMgr.SyncFromCoordinator(lobby, isHostRole);
        }

        async Task CleanupLobby(string lobbyId)
        {
            try { await _lobbyGateway.DeleteAsync(lobbyId); }
            catch (Exception e) { Debug.LogWarning($"[SessionCoordinator] Lobby cleanup failed: {e.Message}"); }
            _currentLobby = null;
            _isHostRole = false;
            SyncLobbyManager(null, false);
        }

        async Task LeaveLobbyCleanup(string lobbyId)
        {
            try { await _lobbyGateway.RemovePlayerAsync(lobbyId, _services.PlayerId); }
            catch (Exception e) { Debug.LogWarning($"[SessionCoordinator] Lobby leave failed: {e.Message}"); }
            _currentLobby = null;
            SyncLobbyManager(null, false);
        }

        async Task<Result<string>> WaitForRelayCodeAsync(OperationScope scope)
        {
            var tcs = new TaskCompletionSource<string>();

            void OnLobbyUpdated(Lobby lobby)
            {
                if (lobby?.Data != null &&
                    lobby.Data.TryGetValue("RelayJoinCode", out var data) &&
                    !string.IsNullOrEmpty(data.Value))
                {
                    tcs.TrySetResult(data.Value);
                }
            }

            var lobbyMgr = LobbyManager.Instance;
            if (lobbyMgr == null)
                return Result<string>.Failure(OperationErrorCode.InvalidState, "LobbyManager not available");

            lobbyMgr.OnLobbyUpdated += OnLobbyUpdated;
            try
            {
                var current = lobbyMgr.CurrentLobby;
                if (current?.Data != null &&
                    current.Data.TryGetValue("RelayJoinCode", out var existing) &&
                    !string.IsNullOrEmpty(existing.Value))
                {
                    return Result<string>.Success(existing.Value);
                }

                int timeoutMs = (int)(relayCodeTimeoutSeconds * 1000);
                var timeoutTask = Task.Delay(timeoutMs);
                var completed = await Task.WhenAny(tcs.Task, timeoutTask);

                if (scope.IsStale)
                    return Result<string>.Failure(OperationErrorCode.Cancelled, "Superseded");

                if (completed == timeoutTask)
                    return Result<string>.Failure(OperationErrorCode.Timeout, "Relay code not received in time");

                return Result<string>.Success(tcs.Task.Result);
            }
            finally
            {
                lobbyMgr.OnLobbyUpdated -= OnLobbyUpdated;
            }
        }

        LobbyPlayer CreatePlayerData()
        {
            string playerId = _services.PlayerId;
            string shortId = playerId?.Length >= 6 ? playerId[..6] : (playerId ?? "Unknown");

            var profileService = CosmeticProfileService.Instance;
            string nickname = profileService != null ? profileService.Nickname : "";
            if (string.IsNullOrEmpty(nickname))
                nickname = $"Player_{shortId}";

            string cosmeticDto = profileService != null ? (profileService.GetCompactDto() ?? "") : "";

            return new LobbyPlayer
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, nickname) },
                    { "IsReady", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "false") },
                    { "LastAction", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, "") },
                    { "CosmeticData", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, cosmeticDto) }
                }
            };
        }

        static string DetectParrelSyncProfile()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var commandLine = Environment.GetCommandLineArgs();
            int profileIndex = Array.IndexOf(commandLine, "--az-services-profile");
            if (profileIndex >= 0 && profileIndex + 1 < commandLine.Length &&
                !string.IsNullOrWhiteSpace(commandLine[profileIndex + 1]))
            {
                string commandLineProfile = commandLine[profileIndex + 1].Trim();
                Debug.Log($"[SessionCoordinator] Services profile override: {commandLineProfile}");
                return commandLineProfile;
            }
#endif
#if UNITY_EDITOR
            try
            {
                var clonesManagerType = Type.GetType("ParrelSync.ClonesManager, ParrelSync");
                if (clonesManagerType != null)
                {
                    var isCloneMethod = clonesManagerType.GetMethod("IsClone",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var getArgumentMethod = clonesManagerType.GetMethod("GetArgument",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                    if (isCloneMethod != null && (bool)isCloneMethod.Invoke(null, null))
                    {
                        string customArgument = getArgumentMethod?.Invoke(null, null) as string ?? "";
                        string profile = string.IsNullOrEmpty(customArgument) ? "clone" : customArgument;
                        Debug.Log($"[SessionCoordinator] ParrelSync clone detected - Profile: {profile}");
                        return profile;
                    }
                }
            }
            catch (Exception) { }
#endif
            return null;
        }

        #endregion
    }
}
