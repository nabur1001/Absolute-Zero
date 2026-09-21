#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Player;
using AbsoluteZero.Core.Session;
using AbsoluteZero.Core.Turn;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AbsoluteZero.Core.Solo
{
    /// <summary>
    /// Opt-in development player probe for a visible four-player smoke match.
    /// It drives only the owning player's public RPCs and never runs in release builds.
    /// </summary>
    public sealed class VisualFourPlayerProbe : MonoBehaviour
    {
        static readonly string[] Profiles = { "Aggressor", "Counter", "Support", "Opportunist" };

        string _role;
        string _run;
        int _seed;
        int _turnLimit;
        ushort _port;
        bool _relayFlow;
        string _coordinationFile;
        NetworkManager _network;
        System.Random _random;
        int _localSeat = -1;
        int _observedTurn = -1;
        int _settledTurns;
        float _phaseStart;
        float _nextStateLog;
        float _nextBootLog;
        bool _acted;
        bool _ready;
        bool _finishing;
        bool _hoverCaptureStarted;
        bool _hoverCaptureDone;
        bool _ghostShowcase;
        bool _ghostForced;
        bool _ghostCaptureStarted;
        bool _frostRequested;
        bool _chillRequested;
        bool _showcaseFinishStarted;
        float _ghostObservedAt;
        float _nextGhostRequestAt;
        int _ghostSkillsObserved;
        string _lastChoice = "waiting";
        string _lastIssue = "none";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            if (Arg("--az-visual-flow", "0") != "1") return;
            var go = new GameObject(nameof(VisualFourPlayerProbe));
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<VisualFourPlayerProbe>();
            probe._role = Arg("--az-role", "client");
            probe._run = Arg("--az-run", "visual-local");
            probe._seed = ParseInt("--az-seed", 29031);
            probe._turnLimit = Mathf.Clamp(ParseInt("--az-turns", 4), 1, 20);
            probe._port = (ushort)Mathf.Clamp(ParseInt("--az-port", 17859), 1024, ushort.MaxValue);
            probe._relayFlow = Arg("--az-relay-flow", "0") == "1";
            probe._coordinationFile = Arg("--az-coordination-file", "");
            probe._ghostShowcase = Arg("--az-ghost-showcase", "0") == "1";
        }

        static string Arg(string key, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        static int ParseInt(string key, int fallback)
        {
            return int.TryParse(Arg(key, fallback.ToString(CultureInfo.InvariantCulture)), out int value)
                ? value : fallback;
        }

        void OnEnable()
        {
            Core.Combat.CombatVFXManager.OnPresentationSettled += OnPresentationSettled;
            TurnManager.OnGhostSkillUsed += OnGhostSkillUsedVisual;
            Application.logMessageReceived += OnUnityLog;
        }

        void OnDisable()
        {
            Core.Combat.CombatVFXManager.OnPresentationSettled -= OnPresentationSettled;
            TurnManager.OnGhostSkillUsed -= OnGhostSkillUsedVisual;
            Application.logMessageReceived -= OnUnityLog;
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 30;
            yield return null;

            _network = NetworkManager.Singleton;
            if (_network == null)
            {
                Fail("No NetworkManager");
                yield break;
            }

            NetworkSessionCoordinator.Instance?.SetMatchParameters(GameMode.Multi, 4);
            if (_relayFlow)
            {
                yield return StartRelayLobbyFlow();
                yield break;
            }

            var transport = _network.GetComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", _port, "127.0.0.1");
            _network.NetworkConfig.ConnectionApproval = true;
            _network.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(_run);

            if (_role == "host")
            {
                UnityEngine.Random.InitState(_seed);
                _network.ConnectionApprovalCallback = Approve;
                if (!_network.StartHost())
                {
                    Fail("Host failed to start");
                    yield break;
                }
                Debug.Log($"[VISUAL] HOST_LISTENING seed={_seed} port={_port}");
                while (_network.ConnectedClientsIds.Count < 4) yield return null;
                Debug.Log("[VISUAL] FOUR_CONNECTED");
                _network.SceneManager.LoadScene("GameScene_Multi", LoadSceneMode.Single);
            }
            else if (!_network.StartClient())
            {
                Fail("Client failed to start");
            }
        }

        IEnumerator StartRelayLobbyFlow()
        {
            if (string.IsNullOrWhiteSpace(_coordinationFile))
            {
                Fail("Relay coordination file was not provided");
                yield break;
            }

            var coordinator = NetworkSessionCoordinator.Instance;
            if (coordinator == null)
            {
                Fail("NetworkSessionCoordinator not found");
                yield break;
            }

            float initializeDeadline = Time.realtimeSinceStartup + 45f;
            while (coordinator.State == SessionState.Offline || coordinator.State == SessionState.Initializing)
            {
                if (Time.realtimeSinceStartup >= initializeDeadline)
                {
                    Fail("Unity Services initialization timed out");
                    yield break;
                }
                yield return null;
            }
            if (coordinator.State != SessionState.Ready)
            {
                Fail($"Unity Services initialization failed: {coordinator.LastError}");
                yield break;
            }

            coordinator.SetMatchParameters(GameMode.Multi, 4);
            Debug.Log($"[VISUAL] RELAY_AUTH_READY role={_role}");

            if (_role == "host")
            {
                Task<Result<Unit>> createTask = coordinator.CreateLobbyAsync($"AZ_Relay_{_run[..Mathf.Min(8, _run.Length)]}");
                yield return WaitForTask(createTask, 45f, "Create lobby");
                if (_finishing) yield break;
                if (!TaskSucceeded(createTask))
                {
                    Fail($"Create lobby failed: {TaskError(createTask)}");
                    yield break;
                }

                string lobbyCode = coordinator.CurrentLobby?.LobbyCode;
                if (string.IsNullOrEmpty(lobbyCode))
                {
                    Fail("Created lobby did not provide a lobby code");
                    yield break;
                }
                WriteCoordinationFile(lobbyCode);
                Debug.Log("[VISUAL] RELAY_LOBBY_READY");

                float lobbyDeadline = Time.realtimeSinceStartup + 75f;
                while ((coordinator.CurrentLobby?.Players?.Count ?? 0) < 4)
                {
                    if (Time.realtimeSinceStartup >= lobbyDeadline)
                    {
                        Fail($"Lobby population timed out at {coordinator.CurrentLobby?.Players?.Count ?? 0}/4");
                        yield break;
                    }
                    yield return null;
                }
                Debug.Log("[VISUAL] RELAY_LOBBY_FOUR_JOINED");

                UnityEngine.Random.InitState(_seed);
                Task<Result<Unit>> startTask = coordinator.StartMatchAsHostAsync();
                yield return WaitForTask(startTask, 90f, "Relay host start");
                if (_finishing) yield break;
                if (!TaskSucceeded(startTask))
                {
                    Fail($"Relay host start failed: {TaskError(startTask)}");
                    yield break;
                }
                Debug.Log("[VISUAL] RELAY_HOST_LISTENING");
            }
            else
            {
                string lobbyCode = null;
                float codeDeadline = Time.realtimeSinceStartup + 45f;
                while (string.IsNullOrWhiteSpace(lobbyCode))
                {
                    if (File.Exists(_coordinationFile))
                        lobbyCode = File.ReadAllText(_coordinationFile).Trim();
                    if (Time.realtimeSinceStartup >= codeDeadline)
                    {
                        Fail("Lobby code coordination timed out");
                        yield break;
                    }
                    yield return null;
                }

                Task<Result<Unit>> joinTask = coordinator.JoinGameAsync(lobbyCode);
                yield return WaitForTask(joinTask, 90f, "Relay client join");
                if (_finishing) yield break;
                if (!TaskSucceeded(joinTask))
                {
                    Fail($"Relay client join failed: {TaskError(joinTask)}");
                    yield break;
                }
                Debug.Log("[VISUAL] RELAY_CLIENT_CONNECTED");
            }
        }

        IEnumerator WaitForTask(Task task, float timeoutSeconds, string operation)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Fail($"{operation} timed out after {timeoutSeconds:0} seconds");
                    yield break;
                }
                yield return null;
            }
        }

        static string TaskError(Task<Result<Unit>> task)
        {
            if (task.IsCanceled) return "task cancelled";
            if (task.IsFaulted) return task.Exception?.GetBaseException().Message ?? "task faulted";
            return task.Result.ErrorMessage ?? task.Result.ErrorCode.ToString();
        }

        static bool TaskSucceeded(Task<Result<Unit>> task)
        {
            return task.IsCompletedSuccessfully && task.Result.IsSuccess;
        }

        void WriteCoordinationFile(string lobbyCode)
        {
            string fullPath = Path.GetFullPath(_coordinationFile);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
            string temporaryPath = fullPath + ".tmp";
            File.WriteAllText(temporaryPath, lobbyCode, Encoding.UTF8);
            File.Move(temporaryPath, fullPath);
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = Encoding.UTF8.GetString(request.Payload) == _run
                && _network.ConnectedClientsIds.Count < 4;
            response.CreatePlayerObject = false;
            response.Pending = false;
        }

        void Update()
        {
            if (_finishing) return;
            if (Time.realtimeSinceStartup > 300f)
            {
                Fail("Scenario timeout after 300 seconds");
                return;
            }
            if (_network == null || !_network.IsConnectedClient) return;

            var root = MatchCompositionRoot.Instance;
            var state = root?.NetworkState;
            var turn = TurnManager.Instance;
            if (Time.unscaledTime >= _nextBootLog)
            {
                _nextBootLog = Time.unscaledTime + 5f;
                Debug.Log($"[VISUAL] BOOT role={_role} scene={SceneManager.GetActiveScene().name} "
                    + $"state={state != null && state.IsSpawned} config={root?.ActiveConfig != null} failure={root?.InitializationFailure}");
            }
            if (turn == null || state == null || !state.IsSpawned) return;

            var players = CurrentPlayers();
            var local = players.FirstOrDefault(player => player.IsOwner);
            if (local == null) return;
            if (_localSeat < 0)
            {
                _localSeat = local.PlayerIndex;
                _random = new System.Random(unchecked(_seed * 397 ^ (_localSeat + 1) * 7919));
                Debug.Log($"[VISUAL] ASSIGNED local={_localSeat} profile={Profiles[Mathf.Clamp(_localSeat, 0, 3)]}");
            }

            if (Time.unscaledTime >= _nextStateLog)
            {
                _nextStateLog = Time.unscaledTime + 1f;
                LogState("STATE", turn, state, players);
            }

            if (state.TerminalResult.Value.Released)
            {
                StartCoroutine(Finish("terminal-result"));
                return;
            }
            if (_ghostShowcase)
            {
                UpdateGhostShowcase(local, players, turn);
                return;
            }
            if (local.CurrentLifeState.Value != LifeState.Alive) return;
            if (turn.CurrentPhase.Value != TurnPhase.PrepPhase)
            {
                _observedTurn = -1;
                return;
            }

            if (_observedTurn != turn.TurnNumber.Value)
            {
                _observedTurn = turn.TurnNumber.Value;
                _phaseStart = Time.unscaledTime;
                _acted = false;
                _ready = false;
                Capture($"turn-{_observedTurn:00}-prep");
                if (_observedTurn == 1 && !_hoverCaptureStarted)
                {
                    _hoverCaptureStarted = true;
                    StartCoroutine(CaptureTargetHover(local, players));
                }
            }

            float elapsed = Time.unscaledTime - _phaseStart;
            if (_observedTurn == 1 && !_hoverCaptureDone) return;
            if (!_acted && elapsed > 1.5f + _localSeat * 0.2f)
            {
                _acted = true;
                TryChooseAction(local, players, turn.TurnNumber.Value);
            }
            if (!_ready && elapsed > 3.5f + _localSeat * 0.2f)
            {
                _ready = true;
                local.PressReadyServerRpc();
                Debug.Log($"[VISUAL] READY turn={turn.TurnNumber.Value} local={_localSeat} selected={local.HasSelectedItem.Value}");
            }
        }

        void UpdateGhostShowcase(PlayerState local, PlayerState[] players, TurnManager turn)
        {
            if (_role == "host" && !_ghostForced && players.Length == 4
                && turn.CurrentPhase.Value == TurnPhase.PrepPhase
                && FindObjectsByType<PlayerSeatMarker>(FindObjectsSortMode.None).Length >= 3)
            {
                _ghostForced = turn.DebugForceGhostForVisual(1);
            }

            var ghost = players.FirstOrDefault(player => player.PlayerIndex == 1);
            if (ghost == null || ghost.CurrentLifeState.Value != LifeState.Ghost) return;
            if (!_ghostCaptureStarted)
            {
                _ghostCaptureStarted = true;
                _ghostObservedAt = Time.unscaledTime;
                StartCoroutine(CaptureGhostTransition());
            }

            if (_localSeat != 1) return;
            float elapsed = Time.unscaledTime - _ghostObservedAt;
            if (_ghostSkillsObserved == 0 && elapsed >= 4f && Time.unscaledTime >= _nextGhostRequestAt)
            {
                bool firstRequest = !_frostRequested;
                _frostRequested = true;
                _nextGhostRequestAt = Time.unscaledTime + 0.75f;
                turn.UseGhostSkillRpc(GhostSkillService.SKILL_FROST_STRIKE, 0);
                Debug.Log($"[VISUAL] GHOST_REQUEST skill=FrostStrike actor=1 target=0 retry={!firstRequest}");
            }
            if (_ghostSkillsObserved == 1 && elapsed >= 6.2f && Time.unscaledTime >= _nextGhostRequestAt)
            {
                bool firstRequest = !_chillRequested;
                _chillRequested = true;
                _nextGhostRequestAt = Time.unscaledTime + 0.75f;
                turn.UseGhostSkillRpc(GhostSkillService.SKILL_CHILL_AURA, 2);
                Debug.Log($"[VISUAL] GHOST_REQUEST skill=ChillAura actor=1 target=2 retry={!firstRequest}");
            }
        }

        IEnumerator CaptureGhostTransition()
        {
            yield return new WaitForSecondsRealtime(0.45f);
            Capture("ghost-freeze");
            yield return new WaitForSecondsRealtime(1.75f);
            Capture("ghost-placeholder");
        }

        void OnGhostSkillUsedVisual(byte ghostSeat, byte skillIndex, byte targetSeat)
        {
            _ghostSkillsObserved++;
            Debug.Log($"[VISUAL] GHOST_VFX_START actor={ghostSeat} skill={skillIndex} target={targetSeat}");
            StartCoroutine(CaptureGhostSkillFrames(ghostSeat, skillIndex, targetSeat));
        }

        IEnumerator CaptureGhostSkillFrames(byte ghostSeat, byte skillIndex, byte targetSeat)
        {
            yield return new WaitForSecondsRealtime(0.28f);
            string skill = skillIndex == GhostSkillService.SKILL_FROST_STRIKE ? "frost" : "chill";
            Capture($"ghost-{skill}-motion");
            yield return new WaitForSecondsRealtime(0.52f);
            Debug.Log($"[VISUAL] GHOST_VFX_IMPACT actor={ghostSeat} skill={skillIndex} target={targetSeat}");
            Capture($"ghost-{skill}-impact");
            if (_ghostShowcase && _ghostSkillsObserved >= 2 && !_showcaseFinishStarted)
            {
                _showcaseFinishStarted = true;
                StartCoroutine(FinishGhostShowcase());
            }
        }

        IEnumerator FinishGhostShowcase()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            StartCoroutine(Finish("ghost-showcase"));
        }

        void TryChooseAction(PlayerState local, PlayerState[] players, int turnNumber)
        {
            var inventory = local.GetInventory();
            var candidates = new List<(byte Slot, ItemDataSO Item)>();
            for (byte slot = 0; slot < inventory.SlotStates.Count; slot++)
            {
                var item = inventory.GetItemData(slot);
                if (item != null && inventory.SlotStates[slot].IsUsable)
                    candidates.Add((slot, item));
            }
            if (candidates.Count == 0)
            {
                _lastChoice = "no-usable-item";
                Debug.LogWarning($"[VISUAL] NO_ACTION turn={turnNumber} local={_localSeat}");
                return;
            }

            // Mini-game input has its own focused matrix. Keep this visual match on the
            // ordinary selection/presentation path when any ordinary item is available.
            var ordinary = candidates.Where(entry => !entry.Item.RequiresMiniGame).ToList();
            if (ordinary.Count > 0) candidates = ordinary;

            var choice = ChooseByProfile(candidates);
            byte target = choice.Item.GetTargetMode() == TargetMode.Self
                ? ActionIntent.NoTarget : ChooseTarget(players);
            if (choice.Item.GetTargetMode() == TargetMode.SingleTarget && target == ActionIntent.NoTarget)
            {
                _lastChoice = choice.Item.ItemName + " rejected:no-target";
                Debug.LogWarning($"[VISUAL] NO_TARGET turn={turnNumber} local={_localSeat} item={choice.Item.ItemName}");
                return;
            }

            _lastChoice = $"{choice.Item.ItemName} -> {(target == ActionIntent.NoTarget ? "self" : target.ToString())}";
            Debug.Log($"[VISUAL] PLAN turn={turnNumber} local={_localSeat} profile={Profiles[_localSeat]} "
                + $"roll={_random.Next(0, 100)} slot={choice.Slot} item={choice.Item.ItemName} category={choice.Item.Category} "
                + $"target={(target == ActionIntent.NoTarget ? "self" : target.ToString())}");
            local.SelectItemServerRpc(choice.Slot, target);
            if (choice.Item.RequiresMiniGame)
                StartCoroutine(CompleteMiniGame(local, choice.Slot, turnNumber));
        }

        IEnumerator CaptureTargetHover(PlayerState local, PlayerState[] players)
        {
            yield return new WaitForSecondsRealtime(0.75f);

            var uiType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("AbsoluteZero.UI.Game.GameUIManager", false))
                .FirstOrDefault(type => type != null);
            var ui = uiType != null ? FindFirstObjectByType(uiType) as MonoBehaviour : null;
            var inventory = local != null ? local.GetInventory() : null;
            var targets = players
                .Where(player => player != null && player.PlayerIndex != _localSeat
                    && player.CurrentLifeState.Value == LifeState.Alive)
                .OrderBy(player => AZPlayerVisual.GetRemoteVisualSlot(player.PlayerIndex, _localSeat))
                .ToArray();
            byte targetableSlot = byte.MaxValue;
            if (inventory != null)
            {
                for (byte slot = 0; slot < inventory.SlotStates.Count; slot++)
                {
                    var item = inventory.GetItemData(slot);
                    if (item != null && inventory.SlotStates[slot].IsUsable
                        && item.GetTargetMode() == TargetMode.SingleTarget)
                    {
                        targetableSlot = slot;
                        break;
                    }
                }
            }

            var beginMethod = uiType?.GetMethod("DebugBeginTargetHover");
            var visibleProperty = uiType?.GetProperty("DebugTargetHoverVisible");
            var cancelMethod = uiType?.GetMethod("DebugCancelTargetHover");
            if (ui == null || beginMethod == null || targetableSlot == byte.MaxValue || targets.Length == 0)
            {
                Debug.LogWarning($"[VISUAL] TARGET_HOVER_SKIPPED local={_localSeat} ui={ui != null} slot={targetableSlot}");
                _hoverCaptureDone = true;
                yield break;
            }

            string[] slotNames = { "west", "north", "east" };
            foreach (var targetPlayer in targets)
            {
                byte target = (byte)targetPlayer.PlayerIndex;
                int visualSlot = AZPlayerVisual.GetRemoteVisualSlot(targetPlayer.PlayerIndex, _localSeat);
                bool began = (bool)beginMethod.Invoke(ui, new object[] { targetableSlot, target });
                if (!began) continue;

                float deadline = Time.unscaledTime + 2f;
                while (!(bool)(visibleProperty?.GetValue(ui) ?? false) && Time.unscaledTime < deadline)
                    yield return null;

                yield return new WaitForEndOfFrame();
                string direction = visualSlot >= 0 && visualSlot < slotNames.Length
                    ? slotNames[visualSlot] : "unknown";
                Capture($"turn-01-target-{direction}-P{target}");
                bool visible = (bool)(visibleProperty?.GetValue(ui) ?? false);
                Debug.Log($"[VISUAL] TARGET_HOVER_CAPTURE local={_localSeat} slot={targetableSlot} target={target} direction={direction} visible={visible}");
                yield return null;
                yield return new WaitForEndOfFrame();
                cancelMethod?.Invoke(ui, null);
                yield return null;
            }
            _hoverCaptureDone = true;
        }

        (byte Slot, ItemDataSO Item) ChooseByProfile(List<(byte Slot, ItemDataSO Item)> candidates)
        {
            IEnumerable<(byte Slot, ItemDataSO Item)> preferred = candidates;
            int roll = _random.Next(0, 100);
            if (_localSeat == 0 && roll < 65)
                preferred = candidates.Where(entry => entry.Item.Category == ItemCategory.Attack || entry.Item.Category == ItemCategory.Debuff);
            else if (_localSeat == 1 && roll < 60)
                preferred = candidates.Where(entry => entry.Item.Category == ItemCategory.Defense);
            else if (_localSeat == 2 && roll < 60)
                preferred = candidates.Where(entry => entry.Item.Category == ItemCategory.Recovery || entry.Item.Category == ItemCategory.Buff);
            else if (_localSeat == 3 && roll < 65)
                preferred = candidates.Where(entry => entry.Item.Category == ItemCategory.Sabotage || entry.Item.Category == ItemCategory.Special);

            var pool = preferred.ToArray();
            if (pool.Length == 0) pool = candidates.ToArray();
            return pool[_random.Next(pool.Length)];
        }

        byte ChooseTarget(PlayerState[] players)
        {
            var eligible = players.Where(player => player.PlayerIndex != _localSeat
                    && player.CurrentLifeState.Value == LifeState.Alive)
                .OrderBy(player => player.PlayerIndex).ToArray();
            if (eligible.Length == 0) return ActionIntent.NoTarget;

            if (_localSeat == 0)
                return (byte)eligible.OrderBy(player => player.Temperature.Value).First().PlayerIndex;
            if (_localSeat == 1)
            {
                var seat0 = eligible.FirstOrDefault(player => player.PlayerIndex == 0);
                if (seat0 != null) return 0;
            }
            if (_localSeat == 3)
                return (byte)eligible.OrderBy(player => player.Temperature.Value).First().PlayerIndex;
            return (byte)eligible[_random.Next(eligible.Length)].PlayerIndex;
        }

        IEnumerator CompleteMiniGame(PlayerState local, byte slot, int turnNumber)
        {
            yield return new WaitForSecondsRealtime(0.75f);
            if (local != null && local.IsSpawned)
            {
                local.SubmitMiniGameResultServerRpc(slot, true);
                Debug.Log($"[VISUAL] MINIGAME_SIMULATED turn={turnNumber} local={_localSeat} slot={slot} success=true");
            }
        }

        void OnPresentationSettled(uint sequence)
        {
            if (_finishing || _localSeat < 0) return;
            _settledTurns++;
            var root = MatchCompositionRoot.Instance;
            var turn = TurnManager.Instance;
            if (root?.NetworkState != null && turn != null)
                LogState("CHECKPOINT seq=" + sequence, turn, root.NetworkState, CurrentPlayers());
            Capture($"turn-{_settledTurns:00}-settled-seq-{sequence}");
            if (_ghostShowcase) return;
            if (_settledTurns >= _turnLimit)
                StartCoroutine(Finish("turn-limit"));
        }

        void OnUnityLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            if (message.StartsWith("[VISUAL]")) return;
            _lastIssue = message.Replace('\n', ' ').Replace('\r', ' ');
        }

        void LogState(string label, TurnManager turn, MatchNetworkState state, PlayerState[] players)
        {
            string seats = string.Join(";", players.Select(player => player.PlayerIndex + ":"
                + player.Temperature.Value.ToString("F2", CultureInfo.InvariantCulture) + ":"
                + player.CurrentLifeState.Value + ":ready=" + player.IsReady.Value + ":selected=" + player.HasSelectedItem.Value));
            string kills = string.Join(",", Enumerable.Range(0, state.KillScores.Count).Select(i => state.KillScores[i]));
            Debug.Log($"[VISUAL] {label} local={_localSeat} turn={turn.TurnNumber.Value} phase={turn.CurrentPhase.Value} "
                + $"settled={_settledTurns} kills={kills} terminal={state.TerminalResult.Value.DecidingSequence}/"
                + $"{state.TerminalResult.Value.WinnerMask}/{state.TerminalResult.Value.Released} seats={seats}");
        }

        PlayerState[] CurrentPlayers()
        {
            return FindObjectsByType<PlayerState>(FindObjectsSortMode.None)
                .Where(player => player.IsSpawned && player.PlayerIndex >= 0)
                .OrderBy(player => player.PlayerIndex).ToArray();
        }

        IEnumerator Finish(string reason)
        {
            if (_finishing) yield break;
            _finishing = true;
            // A settled checkpoint requested a screenshot earlier in the same frame.
            // Give it a complete rendered frame before requesting the final capture.
            yield return null;
            yield return new WaitForEndOfFrame();
            Capture("final-" + reason);
            Debug.Log($"[VISUAL] PASS local={_localSeat} reason={reason} settled={_settledTurns} lastChoice={_lastChoice}");
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit(0);
        }

        void Fail(string reason)
        {
            if (_finishing) return;
            _finishing = true;
            _lastIssue = reason;
            Capture("failure");
            Debug.LogError("[VISUAL] FAIL " + reason);
            Application.Quit(2);
        }

        void Capture(string label)
        {
            string logPath = Application.consoleLogPath;
            string folder = Path.GetDirectoryName(logPath) ?? Application.persistentDataPath;
            string role = _role == "host" ? "host" : "seat" + Mathf.Max(0, _localSeat);
            string path = Path.Combine(folder, $"{role}.{Sanitize(label)}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[VISUAL] CAPTURE " + path);
        }

        static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '-');
            return value;
        }

        void OnGUI()
        {
            if (Arg("--az-visual-flow", "0") != "1") return;
            var boxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                normal = { textColor = Color.white }
            };
            boxStyle.normal.background = Texture2D.whiteTexture;
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                normal = { textColor = Color.white }
            };
            Color previous = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.09f, 0.88f);
            GUI.Box(new Rect(12, 12, 390, 142), GUIContent.none, boxStyle);
            GUI.color = previous;
            string profile = _localSeat >= 0 && _localSeat < Profiles.Length ? Profiles[_localSeat] : "Connecting";
            string turnText = TurnManager.Instance == null ? "-" : TurnManager.Instance.TurnNumber.Value.ToString();
            string phaseText = TurnManager.Instance == null ? "-" : TurnManager.Instance.CurrentPhase.Value.ToString();
            GUI.Label(new Rect(24, 22, 365, 125),
                $"4P VISUAL DEBUG | {(_role == "host" ? "HOST" : "CLIENT")} | Seat {_localSeat}\n"
                + $"Profile: {profile}  Seed: {_seed}\nTurn: {turnText}  Phase: {phaseText}  Settled: {_settledTurns}/{_turnLimit}\n"
                + $"Choice: {_lastChoice}\nIssue: {_lastIssue}", labelStyle);
        }
    }
}
#endif
