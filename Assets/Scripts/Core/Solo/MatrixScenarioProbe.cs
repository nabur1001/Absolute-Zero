#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using System.Text;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using AbsoluteZero.Core.Session;
using AbsoluteZero.Core.Turn;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AbsoluteZero.Core.Solo
{
    // Explicit opt-in local validation. Fixtures never run in a release player.
    public sealed partial class MatrixScenarioProbe : MonoBehaviour
    {
        string _case, _role, _token;
        int _count, _winner, _victim, _roundSeed, _specialTurn, _completedRematches, _turn = -1;
        uint _settled, _visible, _vote;
        bool _seeded, _done, _acted, _ready, _sawGame, _voted, _disconnectRecovered;
        float _phaseTime, _nextLog, _caseStart;
        NetworkManager _nm;
        static string Arg(string key, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            string scenario = Arg("--az-matrix", "");
            if (scenario.Length == 0) return;
            var go = new GameObject("MatrixScenarioProbe");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<MatrixScenarioProbe>();
            probe._case = scenario;
            probe._role = Arg("--az-role", "client");
            probe._token = Arg("--az-run", "matrix");
            probe._count = int.Parse(Arg("--az-count", "4"));
            probe._winner = int.Parse(Arg("--az-winner", "0"));
            probe._victim = scenario.StartsWith("disconnect") ? (Arg("--az-drop-seat", "3") == "2" ? 3 : 2)
                : (probe._winner + probe._count - 1) % probe._count;
        }

        void OnEnable()
        {
            CombatVFXManager.OnPresentationSettled += Settled;
            Application.logMessageReceived += Logged;
        }
        void OnDisable()
        {
            CombatVFXManager.OnPresentationSettled -= Settled;
            Application.logMessageReceived -= Logged;
            if (_nm != null)
            {
                _nm.OnClientDisconnectCallback -= Disconnected;
                _nm.OnClientStopped -= Stopped;
            }
        }
        void Disconnected(ulong client) => Debug.Log("[MATRIX] NET_DISCONNECT t=" + Time.realtimeSinceStartup.ToString("F1") + " client=" + client
            + " local=" + _nm.LocalClientId + " reason=" + _nm.DisconnectReason);
        void Stopped(bool host) => Debug.Log("[MATRIX] NET_STOP host=" + host + " scene=" + SceneManager.GetActiveScene().name);
        void Logged(string message, string stack, LogType type)
        {
            const string prefix = "[MatchResult] Visible seq=";
            if (message.StartsWith(prefix)) uint.TryParse(message.Substring(prefix.Length).Split(' ')[0], out _visible);
        }
        PlayerState[] Players() => FindObjectsByType<PlayerState>(FindObjectsSortMode.None)
            .Where(p => p.IsSpawned && p.PlayerIndex >= 0).OrderBy(p => p.PlayerIndex).ToArray();
        string StateText()
        {
            var state = MatchCompositionRoot.Instance?.NetworkState;
            return "kills=" + (state == null ? "" : string.Join(",", Enumerable.Range(0, state.KillScores.Count).Select(i => state.KillScores[i])))
                + " seats=" + string.Join(";", Players().Select(p => p.PlayerIndex + ":"
                + p.Temperature.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ":" + p.CurrentLifeState.Value
                + ":" + string.Join(",", Enumerable.Range(0, p.GetInventory().SlotStates.Count).Select(i =>
                    p.GetInventory().SlotStates[i].ItemId + "/" + p.GetInventory().SlotStates[i].RemainingUses))));
        }
        void Settled(uint sequence)
        {
            _settled = sequence;
            Debug.Log("[MATRIX] CHECK seq=" + sequence + " " + StateText());
        }
        IEnumerator Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 30;
            yield return null;
            _nm = NetworkManager.Singleton;
            if (_nm == null) { Fail("NetworkManager missing"); yield break; }
            _nm.OnClientDisconnectCallback += Disconnected;
            _nm.OnClientStopped += Stopped;
            NetworkSessionCoordinator.Instance.SetMatchParameters(_count == 2 ? Core.Network.GameMode.OneVsOne : Core.Network.GameMode.Multi, _count);
            _nm.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", ushort.Parse(Arg("--az-port", "17849")), "127.0.0.1");
            Debug.Log("[MATRIX] TRANSPORT heartbeat=" + _nm.GetComponent<UnityTransport>().HeartbeatTimeoutMS
                + " timeout=" + _nm.GetComponent<UnityTransport>().DisconnectTimeoutMS);
            _nm.NetworkConfig.ConnectionApproval = true;
            _nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(_token);
            if (_role == "host")
            {
                _nm.ConnectionApprovalCallback = (request, response) =>
                {
                    response.Approved = Encoding.UTF8.GetString(request.Payload) == _token && _nm.ConnectedClientsIds.Count < _count;
                    response.CreatePlayerObject = false;
                    response.Pending = false;
                };
                if (!_nm.StartHost()) { Fail("Host start"); yield break; }
                Debug.Log("[MATRIX] LISTENING");
                int expected = _case == "init-failure" ? _count - 1 : _count;
                while (_nm.ConnectedClientsIds.Count < expected) yield return null;
                _nm.SceneManager.LoadScene(_count == 2 ? "GameScene" : "GameScene_Multi", LoadSceneMode.Single);
            }
            else if (!_nm.StartClient()) Fail("Client start");
        }
        void Fail(string message)
        {
            if (_done) return;
            _done = true;
            Debug.LogError("[MATRIX] FAIL " + message);
            Application.Quit(2);
        }
        void Pass(string message)
        {
            if (_done) return;
            _done = true;
            Debug.Log("[MATRIX] PASS " + message + " " + StateText());
            StartCoroutine(ExitLater());
        }
        IEnumerator ExitLater()
        {
            yield return new WaitForSecondsRealtime(_role == "host" ? 5f : 2f);
            Application.Quit(0);
        }
        void Update()
        {
            if (_done) return;
            if (Time.realtimeSinceStartup > 300) { Fail("timeout"); return; }
            var root = MatchCompositionRoot.Instance;
            if (_case == "init-failure" && root?.InitializationFailure != null)
            {
                if (root.InitializationFailure.Contains("waiting for players"))
                {
                    if (GameObject.Find("MatchInitializationFailure") != null) Pass("EXPECTED_INIT_FAILURE_UI");
                }
                else Fail(root.InitializationFailure);
                return;
            }
            if (_case == "host-exit" && _role != "host" && _sawGame && (_nm == null || !_nm.IsConnectedClient)
                && SceneManager.GetActiveScene().name == "LobbyScene") { Pass("HOST_EXIT_RETURNED"); return; }
            if (_nm == null || !_nm.IsConnectedClient || root?.ActiveConfig == null) return;
            var tm = TurnManager.Instance;
            var state = root.NetworkState;
            if (tm == null || state == null || !state.IsSpawned) return;
            var players = Players();
            var local = players.FirstOrDefault(p => p.IsOwner);
            if (local == null) return;
            _sawGame = true;
            if (Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + 1;
                Debug.Log("[MATRIX] STATE local=" + local.PlayerIndex + " turn=" + tm.TurnNumber.Value + " phase=" + tm.CurrentPhase.Value
                    + " terminal=" + state.TerminalResult.Value.DecidingSequence + "/" + state.TerminalResult.Value.WinnerMask + "/" + state.TerminalResult.Value.Released);
            }
            if (_case == "init-failure" || _case == "host-exit") return;
            if (_case == "joint" || _case == "delayed")
            {
                // Preserve the existing scheduler's reverse pending-list order.
                byte expectedMask = (byte)(_case == "joint" ? 3 : 2);
                if (state.TerminalResult.Value.Released)
                {
                    if (state.TerminalResult.Value.WinnerMask != expectedMask) { Fail("wrong scheduled winner mask"); return; }
                    if (_case == "delayed" && (state.KillScores[0] != 4 || players[2].CurrentLifeState.Value != LifeState.Alive))
                    { Fail("later due effect ran after a terminal winner"); return; }
                    if (_settled == state.TerminalResult.Value.DecidingSequence && _visible == _settled)
                        Pass("SCHEDULED_WIN mask=" + expectedMask);
                    return;
                }
                if (tm.CurrentPhase.Value != TurnPhase.PrepPhase || players.Length != _count) return;
                if (_caseStart == 0) _caseStart = Time.unscaledTime;
                if (_nm.IsServer && !_seeded)
                {
                    _seeded = true;
                    for (int i = 0; i < 4; i++) { state.ServerAddKill(0); state.ServerAddKill(1); }
                    if (_case == "joint")
                    {
                        root.Roster.SetLifeState(0, LifeState.Ghost);
                        root.Roster.SetLifeState(1, LifeState.Ghost);
                    }
                    else
                    {
                        tm.GetBuffSystem().Schedule(2, EffectType.TempChange, -100, 1, 0);
                        tm.GetBuffSystem().Schedule(3, EffectType.TempChange, -100, 1, 1);
                    }
                    Debug.Log("[MATRIX] SCHEDULED_FIXTURE " + _case);
                }
                if (_case == "joint")
                {
                    if (!_acted && local.PlayerIndex < 2 && local.CurrentLifeState.Value == LifeState.Ghost
                        && Time.unscaledTime - _caseStart > 2)
                    {
                        _acted = true;
                        tm.UseGhostSkillRpc(1, (byte)(local.PlayerIndex + 2));
                    }
                    if (_nm.IsServer && !_ready && state.ServerGetCooldown(0, 1) > 0 && state.ServerGetCooldown(1, 1) > 0)
                    {
                        _ready = true;
                        players[2].Temperature.Value = players[3].Temperature.Value = 0.1f;
                        players[2].IsFanActive.Value = players[3].IsFanActive.Value = true;
                        Debug.Log("[MATRIX] JOINT_SAME_TICK_ARMED");
                    }
                }
                else if (!_ready && Time.unscaledTime - _caseStart > 2)
                {
                    _ready = true;
                    local.PressReadyServerRpc();
                }
                return;
            }
            if (_case == "idle" || _case == "disconnect-idle")
            {
                if (_caseStart == 0) _caseStart = Time.unscaledTime;
                int expected = _case == "disconnect-idle" ? _count - 1 : _count;
                if (Time.unscaledTime - _caseStart > 70 && players.Length == expected)
                    Pass("IDLE_CONNECTIONS_SURVIVE_70_SECONDS");
                return;
            }
            if (_case.StartsWith("minigame-"))
            {
                if (tm.CurrentPhase.Value != TurnPhase.PrepPhase || players.Length != _count) return;
                if (_caseStart == 0)
                {
                    _caseStart = Time.unscaledTime;
                    if (_nm.IsServer)
                    {
                        var inv = players.First(p => p.PlayerIndex == 1).GetInventory();
                        inv.SlotStates.Clear();
                        foreach (string name in new[] { "Fan", "Windbreaker", "Hug T-shirt" })
                            inv.GrantSpecificItem((short)Array.FindIndex(ItemManager.Instance.GetAllItems(), item => item.ItemName == name));
                    }
                    if (local.PlayerIndex == 1) StartCoroutine(CheckMiniGameInvalidation(local));
                }
                if (_nm.IsServer && !_seeded && Time.unscaledTime - _caseStart > 4)
                {
                    _seeded = true;
                    root.Roster.SetLifeState((byte)(_case == "minigame-actor" ? 1 : 3), LifeState.Ghost);
                    Debug.Log("[MATRIX] MINIGAME_LIFE_CHANGED");
                }
                if (local.PlayerIndex != 1 && Time.unscaledTime - _caseStart > 13) Pass("MINIGAME_OBSERVER");
                return;
            }
            if (_case == "multi-round")
            {
                if (_nm.IsServer && !_seeded && players.Length == _count && tm.CurrentPhase.Value == TurnPhase.PrepPhase)
                {
                    _seeded = true;
                    foreach (var player in players) if (player.PlayerIndex != 0) player.Temperature.Value = 0;
                    Debug.Log("[MATRIX] MULTI_ROUND_FIXTURE natural deaths on seats 1..3");
                }
                if (root.MatchManager.RoundNumber.Value >= 2 && tm.CurrentPhase.Value == TurnPhase.PrepPhase
                    && players.Length == _count && players.All(p => p.CurrentLifeState.Value == LifeState.Alive
                        && !p.HasSelectedItem.Value && !p.IsReady.Value && p.GetInventory().SlotStates.Count >= 4)
                    && state.GhostCooldowns.Count == 0 && Enumerable.Range(0, state.KillScores.Count).All(i => state.KillScores[i] == 0))
                    Pass("MULTI_ROUND_RESET local=" + local.PlayerIndex);
                return;
            }
            if (_case == "rpc-guards")
            {
                if (_caseStart == 0)
                {
                    _caseStart = Time.unscaledTime;
                    if (_nm.IsServer) root.Roster.SetLifeState(3, LifeState.Ghost);
                    if (local.PlayerIndex == 1 || local.PlayerIndex == 3) StartCoroutine(CheckRpcGuards(local));
                }
                if (local.PlayerIndex != 1 && local.PlayerIndex != 3 && Time.unscaledTime - _caseStart > 12) Pass("RPC_OBSERVER");
                return;
            }
            if (_case == "inventory" || _case == "services")
            {
                if (_nm.IsServer && !_seeded && players.Length == _count && tm.CurrentPhase.Value == TurnPhase.PrepPhase)
                {
                    _seeded = true;
                    try
                    {
                        if (_case == "inventory") CheckInventory(players); else CheckServices(players);
                        state.ServerAddKill(0);
                    }
                    catch (Exception error) { Fail(error.Message); }
                }
                if (state.KillScores[0] == 1) Pass(_case.ToUpperInvariant() + "_CHECKS local=" + local.PlayerIndex);
                return;
            }
            if (_count == 2 && HandleDuel(tm, players, local)) return;
            if (_nm.IsServer && !_seeded && tm.CurrentPhase.Value == TurnPhase.PrepPhase && players.Length == _count)
            {
                _seeded = true;
                if (_case == "special")
                {
                    var inv = players[0].GetInventory();
                    inv.SlotStates.Clear();
                    foreach (string name in new[] { "Cat", "Hug T-shirt", "Ice Cream", "Fan" })
                        inv.GrantSpecificItem((short)Array.FindIndex(ItemManager.Instance.GetAllItems(), item => item.ItemName == name));
                }
                else if (_count > 2)
                {
                    for (int i = 0; i < 4; i++) state.ServerAddKill(_winner);
                    players.First(p => p.PlayerIndex == _victim).Temperature.Value =
                        _case == "disconnect-prep" || _case == "disconnect-attack" ? 37 : 12;
                    if (_case == "ghost")
                    {
                        root.Roster.SetLifeState((byte)_winner, LifeState.Ghost);
                        players.First(p => p.PlayerIndex == _winner).GetInventory().SlotStates.Clear();
                    }
                }
                Debug.Log("[MATRIX] SEEDED case=" + _case + " winner=" + _winner + " victim=" + _victim);
            }
            if (_nm.IsServer && !_disconnectRecovered && (_case == "disconnect-prep" || _case == "disconnect-attack")
                && !root.Roster.IsConnected(byte.Parse(Arg("--az-drop-seat", "3"))))
            {
                _disconnectRecovered = true;
                var victim = players.FirstOrDefault(p => p.PlayerIndex == _victim);
                if (victim == null || victim.CurrentLifeState.Value != LifeState.Alive)
                { Fail("disconnect fixture victim died before recovery"); return; }
                // Start the finishing-attack fixture only after transport has detected the exit.
                victim.Temperature.Value = 12;
                Debug.Log("[MATRIX] DISCONNECT_RECOVERED_FINISHING_FIXTURE");
            }
            if (state.TerminalResult.Value.Released)
            {
                if (state.TerminalResult.Value.WinnerMask != (1 << _winner)) { Fail("wrong winner"); return; }
                if (_settled == state.TerminalResult.Value.DecidingSequence && _visible == _settled)
                    Pass("WIN local=" + local.PlayerIndex + " seq=" + _settled + " mask=" + state.TerminalResult.Value.WinnerMask);
                return;
            }
            if (_case == "special" && tm.TurnNumber.Value >= 4 && tm.CurrentPhase.Value == TurnPhase.PrepPhase)
            { Pass("SPECIAL_COMPLETED local=" + local.PlayerIndex); return; }
            if (tm.CurrentPhase.Value != TurnPhase.PrepPhase) { _turn = -1; return; }
            if (_case == "special" && _nm.IsServer && _specialTurn != tm.TurnNumber.Value)
            {
                _specialTurn = tm.TurnNumber.Value;
                string required = new[] { "Cat", "Hug T-shirt", "Ice Cream" }[Mathf.Min(_specialTurn - 1, 2)];
                var inv = players.First(p => p.PlayerIndex == 0).GetInventory();
                if (!Enumerable.Range(0, inv.SlotStates.Count).Any(i => inv.GetItemData(i)?.ItemName == required && inv.SlotStates[i].IsUsable))
                {
                    inv.GrantSpecificItem((short)Array.FindIndex(ItemManager.Instance.GetAllItems(), item => item.ItemName == required));
                    Debug.Log("[MATRIX] SPECIAL_FIXTURE_REPLENISH " + required);
                }
            }
            if (_turn != tm.TurnNumber.Value)
            {
                _turn = tm.TurnNumber.Value; _phaseTime = Time.unscaledTime; _acted = _ready = false;
                if (_nm.IsServer && ((_case == "win" && Arg("--az-network-stress", "0") == "1")
                    || (_disconnectRecovered && (_case == "disconnect-prep" || _case == "disconnect-attack"))))
                {
                    // Isolate delivery/replication from natural death and competing random winners.
                    var victim = players.FirstOrDefault(p => p.PlayerIndex == _victim);
                    if (victim != null && victim.CurrentLifeState.Value == LifeState.Alive)
                    {
                        victim.Temperature.Value = 1;
                        victim.IsFanActive.Value = false;
                        Debug.Log("[MATRIX] FINISHING_FIXTURE victim=1 fan=false");
                    }
                }
            }
            float elapsed = Time.unscaledTime - _phaseTime;
            if (_case == "ghost")
            {
                if (local.PlayerIndex == _winner && local.CurrentLifeState.Value == LifeState.Ghost && elapsed > 2 && !_acted)
                { _acted = true; tm.UseGhostSkillRpc(0, (byte)_victim); Debug.Log("[MATRIX] GHOST_INTENT"); }
                return;
            }
            if (local.CurrentLifeState.Value != LifeState.Alive) return;
            if (!_acted && elapsed > 2)
            {
                _acted = true;
                local.SelectItemServerRpc(254, 254);
                int defender = (_winner + 1) % _count;
                string item = local.PlayerIndex == defender && _turn == 1 ? "Windbreaker" : "Fan";
                if (_case == "win" && Arg("--az-network-stress", "0") == "1" && local.PlayerIndex != _winner)
                    item = "Windbreaker";
                byte target = (byte)(local.PlayerIndex == _winner ? _victim : _winner);
                if (_case == "special")
                {
                    item = local.PlayerIndex == 0 ? new[] { "Cat", "Hug T-shirt", "Ice Cream" }[Mathf.Min(_turn - 1, 2)] : "Fan";
                    target = (byte)(local.PlayerIndex == 0 ? 1 : (_count - 1));
                    if (local.PlayerIndex == _count - 1) target = 1;
                }
                Select(local, item, item == "Windbreaker" ? ActionIntent.NoTarget : target);
            }
            if (!_ready && elapsed > 4 + (local.PlayerIndex == _winner ? 0 : 0.3f))
            { _ready = true; local.PressReadyServerRpc(); }
        }
        void Select(PlayerState local, string item, byte target)
        {
            var inv = local.GetInventory();
            for (byte i = 0; i < inv.SlotStates.Count; i++)
                if (inv.GetItemData(i)?.ItemName == item && inv.SlotStates[i].IsUsable)
                {
                    local.SelectItemServerRpc(i, target);
                    if (inv.GetItemData(i).RequiresMiniGame) StartCoroutine(CompleteMiniGame(local, i));
                    Debug.Log("[MATRIX] INTENT " + item + " target=" + target); return;
                }
        }
        IEnumerator CompleteMiniGame(PlayerState local, byte slot)
        {
            // Simulated successful client result, not a test of the minigame's input UI.
            yield return new WaitForSecondsRealtime(0.75f);
            if (local != null && local.IsSpawned) local.SubmitMiniGameResultServerRpc(slot, true);
        }
        IEnumerator CheckRpcGuards(PlayerState local)
        {
            yield return new WaitForSecondsRealtime(2);
            if (local.PlayerIndex == 3)
            {
                local.SelectItemServerRpc(0, 0);
                yield return new WaitForSecondsRealtime(1);
                if (local.HasSelectedItem.Value) { Fail("ghost actor selected ordinary attack"); yield break; }
                local.SelectItemServerRpc(1);
                yield return new WaitForSecondsRealtime(1);
                if (local.HasSelectedItem.Value) { Fail("ghost actor selected self defense"); yield break; }
                yield return new WaitForSecondsRealtime(5);
                Pass("GHOST_ACTOR_REJECTED");
                yield break;
            }
            foreach (byte target in new byte[] { 254, 1, 3 })
            {
                local.SelectItemServerRpc(0, target);
                yield return new WaitForSecondsRealtime(1);
                if (local.HasSelectedItem.Value) { Fail("server accepted invalid target " + target); yield break; }
                Debug.Log("[MATRIX] ASSERT invalid target rejected " + target);
            }
            local.SelectItemServerRpc(254, 0);
            yield return new WaitForSecondsRealtime(1);
            if (local.HasSelectedItem.Value) { Fail("server accepted invalid slot"); yield break; }
            local.SelectItemServerRpc(0, 0);
            yield return new WaitForSecondsRealtime(1);
            if (!local.HasSelectedItem.Value) { Fail("valid target rejected"); yield break; }
            local.CancelSelectionServerRpc();
            yield return new WaitForSecondsRealtime(1);
            if (local.HasSelectedItem.Value) { Fail("valid selection did not cancel"); yield break; }
            Pass("RPC_GUARDS");
        }
        IEnumerator CheckMiniGameInvalidation(PlayerState local)
        {
            yield return new WaitForSecondsRealtime(2);
            var inv = local.GetInventory();
            if (inv.GetItemData(2)?.ItemName != "Hug T-shirt") { Fail("minigame fixture not ready"); yield break; }
            bool started = false;
            Action<byte, MiniGameType, float, int> onStart = (slot, kind, limit, goal) => started = true;
            local.OnMiniGameStart += onStart;
            local.SelectItemServerRpc(2, 3);
            yield return new WaitForSecondsRealtime(1);
            local.OnMiniGameStart -= onStart;
            if (!started) { Fail("minigame did not start"); yield break; }
            byte uses = inv.SlotStates[2].RemainingUses;
            yield return new WaitForSecondsRealtime(2.5f);
            local.SubmitMiniGameResultServerRpc(2, _case != "minigame-failure");
            yield return new WaitForSecondsRealtime(1);
            if (local.HasSelectedItem.Value || inv.SlotStates[2].RemainingUses != uses)
            { Fail("invalidated minigame queued or consumed item"); yield break; }
            local.SubmitMiniGameResultServerRpc(2, true);
            yield return new WaitForSecondsRealtime(1);
            if (local.HasSelectedItem.Value) { Fail("duplicate minigame result accepted"); yield break; }
            if (_case != "minigame-actor")
            {
                local.SelectItemServerRpc(2, 0);
                yield return new WaitForSecondsRealtime(1);
                local.SubmitMiniGameResultServerRpc(2, true);
                yield return new WaitForSecondsRealtime(1);
                if (!local.HasSelectedItem.Value) { Fail("valid minigame retry rejected"); yield break; }
            }
            // Keep the acting client connected until the observers have recorded their result.
            while (Time.unscaledTime - _caseStart < 14) yield return null;
            Pass("MINIGAME_INVALIDATION_AND_RETRY");
        }
        bool HandleDuel(TurnManager tm, PlayerState[] players, PlayerState local)
        {
            var match = MatchCompositionRoot.Instance.MatchManager;
            if (match.CurrentMatchState.Value == Core.Network.MatchState.RematchVote && _vote != match.RematchVoteEpoch.Value)
            {
                _vote = match.RematchVoteEpoch.Value; _voted = true;
                match.SubmitRematchDecisionRpc(true, _vote);
                Debug.Log("[MATRIX] REMATCH_ACCEPT local=" + local.PlayerIndex);
            }
            if (_voted && match.RoundNumber.Value == 1 && match.P1RoundWins.Value == 0 && match.P2RoundWins.Value == 0
                && tm.CurrentPhase.Value == TurnPhase.PrepPhase)
            {
                _voted = false;
                _completedRematches++;
                _roundSeed = 0;
                if (players.Length != 2 || FindObjectsByType<TurnManager>(FindObjectsSortMode.None).Length != 1
                    || FindObjectsByType<MatchCompositionRoot>(FindObjectsSortMode.None).Length != 1)
                { Fail("duplicate or missing match owner after rematch"); return true; }
                Debug.Log("[MATRIX] REMATCH_RESET cycle=" + _completedRematches + " local=" + local.PlayerIndex);
                if (_completedRematches >= (_case == "duel-repeat" ? 3 : 1))
                { Pass("DUEL_REMATCH_RESET local=" + local.PlayerIndex + " cycles=" + _completedRematches); return true; }
            }
            if (_nm.IsServer && tm.CurrentPhase.Value == TurnPhase.PrepPhase && _roundSeed != match.RoundNumber.Value)
            {
                _roundSeed = match.RoundNumber.Value;
                players.First(p => p.PlayerIndex == 1).Temperature.Value = 6;
                Debug.Log("[MATRIX] DUEL_ROUND_SEED round=" + _roundSeed);
            }
            return false;
        }
    }
}
#endif
