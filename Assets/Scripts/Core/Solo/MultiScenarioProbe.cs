#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using System.Text;
using AbsoluteZero.Core.Common;
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
    // Opt-in development-only local integration probe. No production startup changes.
    public sealed class MultiScenarioProbe : MonoBehaviour
    {
        string _role;
        string _run;
        bool _fixture;
        bool _finished;
        float _nextSnapshot;
        float _phaseStart;
        int _observedTurn = -1;
        bool _acted;
        bool _ready;
        NetworkManager _nm;
        uint _lastSettled;
        uint _visibleResult;
        float _nextBootLog;

        void OnEnable()
        {
            Core.Combat.CombatVFXManager.OnPresentationSettled += OnSettled;
            Application.logMessageReceived += OnLog;
        }
        void OnDisable()
        {
            Core.Combat.CombatVFXManager.OnPresentationSettled -= OnSettled;
            Application.logMessageReceived -= OnLog;
        }
        void OnSettled(uint sequence)
        {
            _lastSettled = sequence;
            Debug.Log("[SCENARIO] SETTLED seq=" + sequence);
            ScreenCapture.CaptureScreenshot(System.IO.Path.ChangeExtension(Application.consoleLogPath,
                ".seq" + sequence + ".png"));
            var state = MatchCompositionRoot.Instance?.NetworkState;
            if (state != null)
            {
                var players = FindObjectsByType<PlayerState>(FindObjectsSortMode.None)
                    .Where(p => p.IsSpawned && p.PlayerIndex >= 0).OrderBy(p => p.PlayerIndex);
                Debug.Log("[SCENARIO] CHECKPOINT seq=" + sequence + " kills="
                    + string.Join(",", Enumerable.Range(0, state.KillScores.Count).Select(i => state.KillScores[i]))
                    + " seats=" + string.Join(";", players.Select(p => p.PlayerIndex + ":"
                    + p.Temperature.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + ":" + p.CurrentLifeState.Value + ":" + InventoryText(p))));
            }
        }
        void OnLog(string message, string stack, LogType type)
        {
            const string prefix = "[MatchResult] Visible seq=";
            if (message.StartsWith(prefix))
                uint.TryParse(message.Substring(prefix.Length).Split(' ')[0], out _visibleResult);
        }

        static string Argument(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            string role = Argument("--az-scenario");
            if (role != "host" && role != "client") return;
            var go = new GameObject("MultiScenarioProbe");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<MultiScenarioProbe>();
            probe._role = role;
            probe._run = Argument("--az-run") ?? "local-scenario";
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 30;
            yield return null;
            _nm = NetworkManager.Singleton;
            if (_nm == null) { Debug.LogError("[SCENARIO] No NetworkManager"); yield break; }
            NetworkSessionCoordinator.Instance?.SetMatchParameters(Core.Network.GameMode.Multi, 4);
            var transport = _nm.GetComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17849, "127.0.0.1");
            _nm.NetworkConfig.ConnectionApproval = true;
            _nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(_run);
            if (_role == "host")
            {
                _nm.ConnectionApprovalCallback = Approve;
                if (!_nm.StartHost()) { Debug.LogError("[SCENARIO] Host failed"); yield break; }
                Debug.Log("[SCENARIO] HOST_LISTENING");
                while (_nm.ConnectedClientsIds.Count < 4) yield return null;
                Debug.Log("[SCENARIO] FOUR_CONNECTED");
                _nm.SceneManager.LoadScene("GameScene_Multi", LoadSceneMode.Single);
            }
            else
            {
                if (!_nm.StartClient()) Debug.LogError("[SCENARIO] Client failed");
            }
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = Encoding.UTF8.GetString(request.Payload) == _run
                && _nm.ConnectedClientsIds.Count < 4;
            response.CreatePlayerObject = false;
            response.Pending = false;
        }

        void Update()
        {
            if (Time.realtimeSinceStartup > 240f && !_finished)
            {
                _finished = true;
                Debug.LogError("[SCENARIO] TIMEOUT");
                Application.Quit(2);
            }
            if (_nm == null || !_nm.IsConnectedClient) return;
            var tm = TurnManager.Instance;
            var state = MatchCompositionRoot.Instance?.NetworkState;
            if (Time.unscaledTime >= _nextBootLog)
            {
                _nextBootLog = Time.unscaledTime + 5f;
                Debug.Log("[SCENARIO] BOOT role=" + _role + " scene=" + SceneManager.GetActiveScene().name
                    + " networkStateSpawned=" + (state != null && state.IsSpawned)
                    + " configReady=" + (MatchCompositionRoot.Instance?.ActiveConfig != null)
                    + " failure=" + MatchCompositionRoot.Instance?.InitializationFailure);
            }
            if (tm == null || state == null || !state.IsSpawned) return;
            var players = FindObjectsByType<PlayerState>(FindObjectsSortMode.None)
                .Where(p => p.IsSpawned && p.PlayerIndex >= 0).OrderBy(p => p.PlayerIndex).ToArray();
            var local = players.FirstOrDefault(p => p.IsOwner);
            if (local == null) return;
            if (_nm.IsServer && !_fixture && players.Length == 4 && tm.CurrentPhase.Value == TurnPhase.PrepPhase)
            {
                // Explicit late-match fixture, applied once before any scripted intent.
                for (int i = 0; i < 4; i++) state.ServerAddKill(0);
                players.First(p => p.PlayerIndex == 3).Temperature.Value = 12f;
                _fixture = true;
                Debug.Log("[SCENARIO] FIXTURE seat0Kills=4 seat3Temperature=12");
                if (!state.ServerInitialize(state.Config.Value) || state.KillScores[0] != 4)
                    Debug.LogError("[SCENARIO] Bootstrap retry changed active scores");
                else Debug.Log("[SCENARIO] BOOTSTRAP_IDEMPOTENT");
            }
            if (Time.unscaledTime >= _nextSnapshot)
            {
                _nextSnapshot = Time.unscaledTime + 1f;
                var seats = string.Join(";", players.Select(p => p.PlayerIndex + ":" + p.Temperature.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + ":" + p.CurrentLifeState.Value + ":" + InventoryText(p)));
                Debug.Log("[SCENARIO] SNAP local=" + local.PlayerIndex + " turn=" + tm.TurnNumber.Value + " phase=" + tm.CurrentPhase.Value
                    + " kills=" + string.Join(",", Enumerable.Range(0, state.KillScores.Count).Select(i => state.KillScores[i])) + " terminal=" + state.TerminalResult.Value.DecidingSequence
                    + "/" + state.TerminalResult.Value.WinnerMask + "/" + state.TerminalResult.Value.Released + " seats=" + seats);
            }
            if (state.TerminalResult.Value.Released && !_finished)
            {
                if (_nm.IsServer)
                {
                    var terminal = state.TerminalResult.Value;
                    if (!state.ServerInitialize(state.Config.Value)
                        || state.TerminalResult.Value.DecidingSequence != terminal.DecidingSequence
                        || state.TerminalResult.Value.WinnerMask != terminal.WinnerMask
                        || !state.TerminalResult.Value.Released || state.KillScores[0] < 5)
                        Debug.LogError("[SCENARIO] Bootstrap retry changed terminal state");
                    else Debug.Log("[SCENARIO] TERMINAL_IDEMPOTENT");
                }
                _finished = true;
                StartCoroutine(Finish(local.PlayerIndex, state.TerminalResult.Value.WinnerMask,
                    state.TerminalResult.Value.DecidingSequence));
            }
            if (_finished || local.CurrentLifeState.Value != LifeState.Alive) return;
            if (tm.CurrentPhase.Value != TurnPhase.PrepPhase) { _observedTurn = -1; return; }
            if (_observedTurn != tm.TurnNumber.Value)
            {
                _observedTurn = tm.TurnNumber.Value;
                _phaseStart = Time.unscaledTime;
                _acted = false;
                _ready = false;
            }
            float elapsed = Time.unscaledTime - _phaseStart;
            if (!_acted && elapsed > 2f + local.PlayerIndex * 0.15f)
            {
                _acted = true;
                if (tm.TurnNumber.Value == 1) local.SelectItemServerRpc(254, 254);
                string item = local.PlayerIndex == 1 ? (tm.TurnNumber.Value == 1 ? "Windbreaker" : "Cat") : "Fan";
                byte target = local.PlayerIndex == 0 ? (byte)3 : (byte)1;
                var inv = local.GetInventory();
                for (byte s = 0; s < inv.SlotStates.Count; s++)
                    if (inv.GetItemData(s)?.ItemName == item && inv.SlotStates[s].IsUsable)
                    {
                        if (item == "Windbreaker") target = ActionIntent.NoTarget;
                        else if (item == "Cat") target = 2;
                        local.SelectItemServerRpc(s, target);
                        Debug.Log("[SCENARIO] INTENT local=" + local.PlayerIndex + " turn=" + tm.TurnNumber.Value + " item=" + item + " target=" + target);
                        break;
                    }
            }
            if (!_ready && elapsed > 3.5f + local.PlayerIndex * 0.2f)
            {
                _ready = true;
                local.PressReadyServerRpc();
                Debug.Log("[SCENARIO] READY local=" + local.PlayerIndex + " acceptedSelection=" + local.HasSelectedItem.Value);
            }
        }

        IEnumerator Finish(int seat, byte mask, uint sequence)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (_lastSettled != sequence || _visibleResult != sequence)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogError("[SCENARIO] Result presentation timeout");
                    Application.Quit(4);
                    yield break;
                }
                yield return null;
            }
            ScreenCapture.CaptureScreenshot(System.IO.Path.ChangeExtension(Application.consoleLogPath, ".png"));
            yield return new WaitForSecondsRealtime(_role == "host" ? 5f : 2f);
            Debug.Log("[SCENARIO] FINISHED local=" + seat + " winnerMask=" + mask);
            Application.Quit(mask == 1 ? 0 : 3);
        }

        static string InventoryText(PlayerState player)
        {
            var slots = player.GetInventory().SlotStates;
            return string.Join(",", Enumerable.Range(0, slots.Count)
                .Select(i => slots[i].ItemId + "/" + slots[i].RemainingUses));
        }
    }
}
#endif
