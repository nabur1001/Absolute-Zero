using System.Collections;
using System.Collections.Generic;
using AbsoluteZero.Core.Buff;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using AbsoluteZero.Core.Player.Identity;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Turn
{
    public class TurnManager : NetworkBehaviour, ITurnContext
    {
        public static TurnManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] float prepDuration = 20f;

        public readonly NetworkVariable<TurnPhase> CurrentPhase = new(
            TurnPhase.WaitingForPlayers, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> TurnNumber = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<double> PrepStartServerTime = new(
            0.0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<float> PrepDuration = new(
            20f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> RemainingTime = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> LastRoundWinner = new(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<EnvironmentType> ActiveEnvironment = new(
            EnvironmentType.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<byte> FirstReadySeat = new(
            byte.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public static event System.Action<CombatResultData> OnCombatResult;
        public static event System.Action<EnvironmentType> OnEnvironmentAnnounced;

        PlayerState[] _players = new PlayerState[2];
        PlayerModifiers[] _modifiers = new PlayerModifiers[2];
        TemperatureSystem _tempSystem;
        CombatEngine _combatEngine;
        BuffDebuffSystem _buffSystem;
        ItemManager _itemManager;
        MatchManager _matchManager;
        PresentationBarrier _barrier;
        EnvironmentRuleService _envRules;
        RoundLifecycleService _roundLifecycle;

        [SerializeField, Min(1f)] float presentationTimeoutSeconds = 10f;

        float[] _tempsAtTurnStart = new float[2];
        uint _resultSequence;

        const float EMOTE_DISPLAY_SEC = 1.0f;
        bool _emoteWindowClosed;
        public bool AcceptEmotes => IsSpawned && CurrentPhase.Value == TurnPhase.PrepPhase && !_emoteWindowClosed;

        static readonly WaitForSeconds _waitHalf = new(0.5f);
        static readonly WaitForSeconds _waitOne = new(1f);
        static readonly WaitForSeconds _waitTwo = new(2f);
        static readonly WaitForSeconds _waitThree = new(3f);
        static readonly WaitForSeconds _waitFour = new(4f);
        static readonly WaitForSeconds _waitFive = new(5f);
        static readonly WaitForSeconds _waitSix = new(6f);
        static readonly WaitForSeconds _waitKidsSteal = new(EnvironmentRuleService.KIDS_STEAL_STAGING_SECONDS);
        static readonly WaitForSeconds _waitAmbulanceBlanket = new(EnvironmentRuleService.AMBULANCE_BLANKET_STAGING_SECONDS);

        public PlayerState GetPlayer(int index) => _players[index];
        public PlayerModifiers[] GetModifiers() => _modifiers;
        public TemperatureSystem GetTempSystem() => _tempSystem;
        public BuffDebuffSystem GetBuffSystem() => _buffSystem;
        public ItemDropTable GetDropTable() => _itemManager != null ? _itemManager.GetDropTable() : null;

        TurnPhase ITurnContext.Phase => CurrentPhase.Value;
        bool ITurnContext.CanAcceptEmotes => AcceptEmotes;
        double ITurnContext.PrepStartTime => PrepStartServerTime.Value;
        float ITurnContext.PrepDurationSeconds => PrepDuration.Value;

        void ITurnContext.PublishItemUsed(byte playerIdx, byte slotIdx, byte category, bool isSub)
            => OnItemUsedClientRpc(playerIdx, slotIdx, category, isSub);

        void ITurnContext.PublishOpponentRevealed(byte playerIdx, short itemId)
            => RevealOpponentItemClientRpc(playerIdx, itemId);

        // ─── Publication Methods ─────────────────────────────

        void PublishPhaseChanged(TurnPhase phase, int turnNumber)
            => OnPhaseChangedClientRpc(phase, turnNumber);

        void PublishCombatResult(CombatResultData data)
            => OnCombatResultClientRpc(data);

        void PublishEnvironment(EnvironmentType env)
            => AnnounceEnvironmentClientRpc(env);

        void PublishDeathSequence(int loserIndex, bool endsMatch)
            => TriggerDeathSequenceRpc(loserIndex, endsMatch);

        void PublishKidsStealStaging()
            => KidsStealStagingClientRpc();

        void PublishAmbulanceBlanketStaging(bool p1IsLower)
            => AmbulanceBlanketStagingClientRpc(p1IsLower);

        void PublishDebugLog(string message)
            => CombatDebugLogRpc(message);

        void PublishReviveVisuals()
            => ReviveVisualsClientRpc();

        // ─── Lifecycle ───────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            Instance = this;

            if (IsServer)
            {
                _tempSystem = new TemperatureSystem();
                _combatEngine = new CombatEngine();
                _buffSystem = new BuffDebuffSystem();
                _barrier = new PresentationBarrier();
                _envRules = new EnvironmentRuleService();
                _roundLifecycle = new RoundLifecycleService();

                var mcr = MatchCompositionRoot.Instance;
                if (mcr != null)
                {
                    _itemManager = mcr.ItemManager;
                    _matchManager = mcr.MatchManager;
                }

                if (_itemManager == null)
                    _itemManager = FindAnyObjectByType<ItemManager>();
                if (_matchManager == null)
                    _matchManager = FindAnyObjectByType<MatchManager>();

                NetworkManager.OnClientDisconnectCallback += OnClientDisconnectForBarrier;
                StartCoroutine(WaitForPlayersRoutine());
            }
        }

        public override void OnNetworkDespawn()
        {
            StopAllCoroutines();
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnectForBarrier;
            _barrier?.Reset();
            OnCombatResult = null;
            OnEnvironmentAnnounced = null;
            if (Instance == this) Instance = null;
            base.OnNetworkDespawn();
        }

        void OnClientDisconnectForBarrier(ulong clientId)
        {
            _barrier?.HandleDisconnect(clientId);
        }

        public void ReceivePresentationAck(uint sequence, ulong senderClientId)
        {
            _barrier?.ReceiveAck(sequence, senderClientId);
        }

        // ─── Player Discovery ────────────────────────────────

        IEnumerator WaitForPlayersRoutine()
        {
            CurrentPhase.Value = TurnPhase.WaitingForPlayers;

            var mcr = MatchCompositionRoot.Instance;
            if (mcr == null)
            {
                Debug.LogError("[TurnManager] MatchCompositionRoot not found — cannot discover players");
                yield break;
            }

            var registry = mcr.WritableRegistry;
            while (registry.TotalCount < 2)
                yield return _waitHalf;

            var pending = new List<PlayerBinding>(registry.EnumeratePending());
            pending.Sort((a, b) =>
                a.NetworkObject.OwnerClientId.CompareTo(b.NetworkObject.OwnerClientId));

            for (int i = 0; i < _players.Length && i < pending.Count; i++)
            {
                _players[i] = pending[i].State;
                _players[i].Initialize(i, _players[i].GetComponent<PlayerInventory>());
                _players[i].BindTurnContext(this);
            }

            if (_itemManager != null)
                for (int i = 0; i < _players.Length; i++)
                    _itemManager.InitializePlayerInventory(_players[i].GetInventory());

            if (_matchManager != null)
                _matchManager.StartRound();

            Debug.Log($"[TurnManager] Players found via Registry: P0={_players[0].OwnerClientId}, P1={_players[1].OwnerClientId}");

            yield return StartCoroutine(PrepPhaseRoutine());
        }

        // ─── Prep Phase ──────────────────────────────────────

        IEnumerator PrepPhaseRoutine()
        {
            if (!IsSpawned) yield break;
            TurnNumber.Value++;
            LastRoundWinner.Value = -1;
            FirstReadySeat.Value = byte.MaxValue;

            _roundLifecycle.ResetForNewTurn(_players[0], _players[1], _modifiers);

            _envRules.LogActiveEnvironment(ActiveEnvironment.Value, TurnNumber.Value);

            float currentPrepDuration = _envRules.GetPrepDuration(ActiveEnvironment.Value, prepDuration);

            if (_envRules.ShouldApplyKidsEffect(ActiveEnvironment.Value, TurnNumber.Value))
            {
                Debug.Log("[ENV] Kids: steal staging + removing 1 random item from each player");
                PublishKidsStealStaging();
                yield return _waitKidsSteal;
                for (int i = 0; i < _players.Length; i++)
                    _envRules.RemoveRandomUnusedItem(_players[i].GetInventory());
            }

            if (_envRules.ShouldApplyAmbulanceEffect(ActiveEnvironment.Value, TurnNumber.Value))
            {
                Debug.Log($"[ENV] Ambulance: Turn 3 triggered — P0={_players[0].Temperature.Value:F1}° P1={_players[1].Temperature.Value:F1}°");
                int healTarget = _envRules.DetermineAmbulanceTarget(
                    _players[0].Temperature.Value, _players[1].Temperature.Value);

                if (healTarget >= 0)
                {
                    PublishAmbulanceBlanketStaging(healTarget == 0);
                    yield return _waitAmbulanceBlanket;
                }

                if (healTarget >= 0 && healTarget < _players.Length)
                {
                    _tempSystem.ApplyHeal(_players[healTarget], 10f);
                    Debug.Log($"[ENV] Ambulance: P{healTarget} healed +10° → {_players[healTarget].Temperature.Value:F1}° (lower temp)");
                }
                else if (healTarget < 0)
                {
                    Debug.Log("[ENV] Ambulance: same temp — no heal applied");
                }
            }

            PrepStartServerTime.Value = NetworkManager.ServerTime.Time;
            PrepDuration.Value = currentPrepDuration;

            _emoteWindowClosed = false;
            CurrentPhase.Value = TurnPhase.PrepPhase;
            PublishPhaseChanged(TurnPhase.PrepPhase, TurnNumber.Value);

            for (int i = 0; i < _players.Length; i++)
                _tempsAtTurnStart[i] = _players[i].Temperature.Value;

            _tempSystem.ResetTimer();
            float elapsed = 0f;
            bool skipFirstFanTick = true;

            RemainingTime.Value = Mathf.CeilToInt(currentPrepDuration);

            while (elapsed < currentPrepDuration)
            {
                float dt = Time.deltaTime;
                elapsed += dt;
                _tempSystem.Accumulate(dt);

                int newRemaining = Mathf.CeilToInt(currentPrepDuration - elapsed);
                if (newRemaining != RemainingTime.Value)
                    RemainingTime.Value = Mathf.Max(0, newRemaining);

                float recoveryRate = _envRules.GetRecoveryRate(ActiveEnvironment.Value);
                var dropTable = GetDropTable();

                while (_tempSystem.ConsumeTick())
                {
                    for (int i = 0; i < _players.Length; i++)
                    {
                        if (!skipFirstFanTick)
                            _tempSystem.ApplyFanTick(_players[i]);
                        _tempSystem.ApplyRecoveryTick(_players[i], recoveryRate);
                        _tempSystem.CheckThresholds(_players[i], _players[i].GetInventory(),
                            _players[i].GetInventory().GetThresholdGranted(), dropTable);
                    }
                    skipFirstFanTick = false;
                }

                var deathWinner = _roundLifecycle.DetermineDeathWinner(_tempSystem, _players[0], _players[1]);
                if (deathWinner.HasValue)
                {
                    yield return StartCoroutine(HandleRoundEnd(deathWinner.Value));
                    yield break;
                }

                bool allReady = true;
                for (int i = 0; i < _players.Length; i++)
                    if (!_players[i].IsReady.Value) { allReady = false; break; }
                if (allReady) break;

                yield return null;
            }

            RemainingTime.Value = 0;

            for (int i = 0; i < _players.Length; i++)
            {
                if (!_players[i].IsReady.Value) _roundLifecycle.ForceReady(_players[i]);
                _roundLifecycle.RevertFanUpgrade(_players[i]);
            }

            byte firstSeat = byte.MaxValue;
            float earliestTimestamp = float.MaxValue;
            for (int i = 0; i < _players.Length; i++)
            {
                var q = _players[i].GetActionQueue();
                if (q.readyTimestamp > 0f && q.readyTimestamp < earliestTimestamp)
                {
                    earliestTimestamp = q.readyTimestamp;
                    firstSeat = (byte)i;
                }
            }
            FirstReadySeat.Value = firstSeat;

            _emoteWindowClosed = true;
            double lastEmote = 0;
            for (int i = 0; i < _players.Length; i++)
                lastEmote = System.Math.Max(lastEmote, _players[i].LastEmoteServerTime);

            float emoteRemain = (float)(EMOTE_DISPLAY_SEC - (NetworkManager.ServerTime.Time - lastEmote));
            for (float t = 0f; t < emoteRemain; t += Time.deltaTime)
                yield return null;

            yield return StartCoroutine(AttackPhaseRoutine());
        }

        // ─── Attack Phase ────────────────────────────────────

        IEnumerator AttackPhaseRoutine()
        {
            if (!IsSpawned) yield break;
            CurrentPhase.Value = TurnPhase.AttackPhase;
            PublishPhaseChanged(TurnPhase.AttackPhase, TurnNumber.Value);

            Debug.Log($"[COMBAT] ========== TURN {TurnNumber.Value} ATTACK PHASE START ==========");
            Debug.Log($"[COMBAT] P0 temp={_players[0].Temperature.Value:F1}° | P1 temp={_players[1].Temperature.Value:F1}°");

            for (int i = 0; i < _players.Length; i++)
                _players[i].IsBasicBlocked.Value = false;

            Debug.Log($"[COMBAT] --- Processing delayed buffs/debuffs ---");
            _buffSystem.ProcessTurnStart(_players[0], _players[1]);
            Debug.Log($"[COMBAT] After buffs: P0={_players[0].Temperature.Value:F1}° | P1={_players[1].Temperature.Value:F1}°");

            var mainNames = new string[_players.Length];
            var subNames = new string[_players.Length];
            for (int i = 0; i < _players.Length; i++)
            {
                var q = _players[i].GetActionQueue();
                mainNames[i] = q.selectedAction.HasValue ? q.selectedAction.Value.ItemData.ItemName : "NONE";
                subNames[i] = q.subAction.HasValue ? q.subAction.Value.ItemData.ItemName : "NONE";
            }
            Debug.Log($"[COMBAT] P0: main={mainNames[0]}, sub={subNames[0]} | P1: main={mainNames[1]}, sub={subNames[1]}");

            var snapshot = _combatEngine.CapturePreCombatState(
                _players[0], _players[1], _tempsAtTurnStart[0], _tempsAtTurnStart[1]);

            yield return _waitOne;
            if (!IsSpawned) yield break;

            var result = _combatEngine.ResolveCombat(
                _players[0], _players[1], _modifiers, _tempSystem, _buffSystem,
                ActiveEnvironment.Value, snapshot, ref _resultSequence, GetDropTable());

            string summary = $"Turn{TurnNumber.Value}" +
                $" | P0: {mainNames[0]}(sub:{subNames[0]}) P1: {mainNames[1]}(sub:{subNames[1]})" +
                $" | P0: {_tempsAtTurnStart[0]:F1}→{_players[0].Temperature.Value:F1}°" +
                $" P1: {_tempsAtTurnStart[1]:F1}→{_players[1].Temperature.Value:F1}°" +
                $" | {(result.WinnerIndex >= 0 ? $"P{result.WinnerIndex} WINS" : "no death")}";
            PublishDebugLog(summary);

            var mcr = MatchCompositionRoot.Instance;
            if (mcr != null)
            {
                var expectedIds = new List<ulong>();
                foreach (var p in mcr.Registry.Players)
                    expectedIds.Add(p.Identity.ClientId);
                _barrier.Begin(result.ResultSequence, expectedIds);
            }

            var netData = result.ToNetData();
            int combatWinner = result.WinnerIndex;
            netData.EndsMatch = combatWinner >= 0 && _matchManager != null
                && _matchManager.WouldEndMatch(combatWinner);
            PublishCombatResult(netData);

            yield return StartCoroutine(_barrier.WaitForCompletion(Mathf.Max(presentationTimeoutSeconds, 15f)));

            if (_barrier.State == BarrierState.TimedOut)
                Debug.LogWarning($"[TurnManager] Barrier timed out (seq={result.ResultSequence}) — proceeding");

            yield return _waitOne;
            if (!IsSpawned) yield break;

            yield return StartCoroutine(ResolutionPhaseRoutine(result));
        }

        // ─── Resolution Phase ────────────────────────────────

        IEnumerator ResolutionPhaseRoutine(CombatResult result)
        {
            if (!IsSpawned) yield break;
            CurrentPhase.Value = TurnPhase.ResolutionPhase;

            if (result.WinnerIndex >= 0)
            {
                yield return StartCoroutine(HandleRoundEnd(result.WinnerIndex));
                yield break;
            }

            for (int i = 0; i < _players.Length; i++)
                _players[i].GetInventory().CompactSlots();

            yield return _waitOne;
            if (!IsSpawned) yield break;

            if (TurnNumber.Value == 1 && ActiveEnvironment.Value == EnvironmentType.None)
            {
                yield return StartCoroutine(EnvironmentAnnouncementRoutine());
                if (!IsSpawned) yield break;
            }

            yield return StartCoroutine(PrepPhaseRoutine());
        }

        // ─── Round End ───────────────────────────────────────

        IEnumerator HandleRoundEnd(int winnerIndex)
        {
            if (!IsSpawned) yield break;
            LastRoundWinner.Value = winnerIndex;

            bool endsMatch = winnerIndex >= 0 && _matchManager != null
                && _matchManager.WouldEndMatch(winnerIndex);

            if (winnerIndex >= 0)
            {
                for (int i = 0; i < _players.Length; i++)
                    if (i != winnerIndex)
                        PublishDeathSequence(i, endsMatch);
            }

            if (winnerIndex >= 0 && _matchManager != null)
                _matchManager.EndRound(winnerIndex);

            CurrentPhase.Value = TurnPhase.RoundOver;
            PublishPhaseChanged(TurnPhase.RoundOver, TurnNumber.Value);

            string winnerText = winnerIndex >= 0 ? $"P{winnerIndex + 1}" : "Draw";
            Debug.Log($"[TurnManager] Round over — Winner: {winnerText}");

            yield return _waitSix;
            if (!IsSpawned) yield break;

            if (_matchManager != null && _matchManager.IsMatchComplete())
            {
                Debug.Log("[TurnManager] Match complete. Stopping.");
                yield break;
            }

            yield return StartCoroutine(StartNextRound(winnerIndex < 0));
        }

        IEnumerator StartNextRound(bool isDraw = false)
        {
            _roundLifecycle.ResetPlayersForNewRound(_players[0], _players[1]);
            _roundLifecycle.GrantStartingItems(_players[0], _players[1], GetDropTable());

            _buffSystem.ClearAll();
            ActiveEnvironment.Value = EnvironmentType.None;
            TurnNumber.Value = 0;

            if (!isDraw && _matchManager != null)
                _matchManager.StartRound();

            PublishReviveVisuals();

            Debug.Log(isDraw
                ? "[TurnManager] Draw — round voided, replaying"
                : "[TurnManager] New round started — temperatures reset");

            yield return StartCoroutine(PrepPhaseRoutine());
        }

        // ─── Environment ─────────────────────────────────────

        IEnumerator EnvironmentAnnouncementRoutine()
        {
            ActiveEnvironment.Value = _envRules.SelectRandom();

            Debug.Log($"[ENV] Environment selected: {ActiveEnvironment.Value} ({EnvironmentRuleService.GetName(ActiveEnvironment.Value)})");

            PublishEnvironment(ActiveEnvironment.Value);

            yield return _waitFour;
        }

        // ─── Client RPCs ─────────────────────────────────────

        [Rpc(SendTo.Everyone)]
        void AnnounceEnvironmentClientRpc(EnvironmentType env)
        {
            OnEnvironmentAnnounced?.Invoke(env);
            StartCoroutine(EnvironmentCameraRoutine());
        }

        IEnumerator EnvironmentCameraRoutine()
        {
            var cam = Camera.main;
            if (cam == null) yield break;

            Vector3 startEuler = cam.transform.eulerAngles;
            float startY = startEuler.y;
            float targetY = startY - 25f;

            const float panDuration = 0.6f;
            float t = 0f;
            while (t < panDuration)
            {
                t += Time.deltaTime;
                float ratio = Mathf.SmoothStep(0f, 1f, t / panDuration);
                cam.transform.eulerAngles = new Vector3(startEuler.x, Mathf.Lerp(startY, targetY, ratio), startEuler.z);
                yield return null;
            }
            cam.transform.eulerAngles = new Vector3(startEuler.x, targetY, startEuler.z);

            yield return _waitTwo;

            t = 0f;
            while (t < panDuration)
            {
                t += Time.deltaTime;
                float ratio = Mathf.SmoothStep(0f, 1f, t / panDuration);
                cam.transform.eulerAngles = new Vector3(startEuler.x, Mathf.Lerp(targetY, startY, ratio), startEuler.z);
                yield return null;
            }
            cam.transform.eulerAngles = startEuler;
        }

        [Rpc(SendTo.Everyone)]
        void ReviveVisualsClientRpc()
        {
            var mcr = MatchCompositionRoot.Instance;
            if (mcr == null) return;
            foreach (var p in mcr.Registry.Players)
            {
                var visual = p.State.GetComponent<AZPlayerVisual>();
                if (visual != null) visual.ReviveVisual();
            }
        }

        [Rpc(SendTo.Everyone)]
        void OnPhaseChangedClientRpc(TurnPhase phase, int turnNumber)
        {
        }

        [Rpc(SendTo.Everyone)]
        public void OnItemUsedClientRpc(byte playerIndex, byte slotIndex, byte category, bool isInstant)
        {
        }

        public static event System.Action<byte, short> OnOpponentRevealed;

        [Rpc(SendTo.Everyone)]
        public void RevealOpponentItemClientRpc(byte forPlayerIndex, short opponentItemId)
        {
            OnOpponentRevealed?.Invoke(forPlayerIndex, opponentItemId);
        }

        [Rpc(SendTo.Everyone)]
        void TriggerDeathSequenceRpc(int loserIndex, bool endsMatch)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            foreach (var kvp in nm.SpawnManager.SpawnedObjects)
            {
                var netObj = kvp.Value;
                if (netObj == null || !netObj.IsPlayerObject) continue;
                var ps = netObj.GetComponent<PlayerState>();
                if (ps != null && ps.PlayerIndex == loserIndex)
                {
                    var visual = netObj.GetComponent<AZPlayerVisual>();
                    if (visual != null) visual.PlayDeathSequence(endsMatch);
                    return;
                }
            }
        }

        [Rpc(SendTo.Everyone)]
        void CombatDebugLogRpc(string message)
        {
            Debug.Log($"[COMBAT-SYNC] {message}");
        }

        [Rpc(SendTo.Everyone)]
        void OnCombatResultClientRpc(CombatResultData resultData)
        {
            OnCombatResult?.Invoke(resultData);
        }

        [Rpc(SendTo.Everyone)]
        void KidsStealStagingClientRpc()
        {
            var vfx = EnvironmentVFXManager.Instance;
            if (vfx != null) vfx.PlayKidsStealStaging();
        }

        [Rpc(SendTo.Everyone)]
        void AmbulanceBlanketStagingClientRpc(bool p1IsLower)
        {
            var vfx = EnvironmentVFXManager.Instance;
            if (vfx == null) return;

            byte localSeat = 0;
            var mcr = MatchCompositionRoot.Instance;
            if (mcr != null)
            {
                var localId = NetworkManager.Singleton.LocalClientId;
                foreach (var p in mcr.Registry.Players)
                {
                    if (p.Identity.ClientId == localId)
                    {
                        localSeat = p.Identity.PlayerIndex;
                        break;
                    }
                }
            }
            bool isLocalP1 = localSeat == 0;
            bool healSelf = (p1IsLower && isLocalP1) || (!p1IsLower && !isLocalP1);
            vfx.PlayAmbulanceBlanketStaging(healSelf);
        }
    }
}
