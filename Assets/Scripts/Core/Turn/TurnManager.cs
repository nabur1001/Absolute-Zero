using System.Collections;
using System.Collections.Generic;
using AbsoluteZero.Core.Buff;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Player.Identity;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Turn
{
    public class TurnManager : NetworkBehaviour, ITurnContext, IPlayerTurnCancellation
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
        CombatResolver _combatResolver;
        AuthoritativeDeathService _deathService;
        GhostSkillService _ghostSkillService;

        [SerializeField, Min(1f)] float presentationTimeoutSeconds = 10f;

        float[] _tempsAtTurnStart = new float[2];
        uint _resultSequence;
        Coroutine _rematchWaitHandle;
        GameMode _gameMode = GameMode.OneVsOne;
        IGameModeRule _gameRule;

        const float EMOTE_DISPLAY_SEC = 1.0f;
        bool _emoteWindowClosed;
        bool _ghostRoundEndTriggered;
        bool _deathmatchGranted;
        byte _multiTerminalWinnerMask;
        bool _multiPresentationInFlight;
        bool _multiRoundEndInProgress;
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

        bool IsMulti => _gameMode == GameMode.Multi;

        public PlayerState GetPlayer(int index) => _players[index];
        public PlayerModifiers[] GetModifiers() => _modifiers;
        public TemperatureSystem GetTempSystem() => _tempSystem;
        public BuffDebuffSystem GetBuffSystem() => _buffSystem;
        public ItemDropTable GetDropTable() => _itemManager != null
            ? _itemManager.GetRuleAwareDropTable(_gameRule)
            : null;

        TurnPhase ITurnContext.Phase => CurrentPhase.Value;
        bool ITurnContext.CanAcceptEmotes => AcceptEmotes;
        double ITurnContext.PrepStartTime => PrepStartServerTime.Value;
        float ITurnContext.PrepDurationSeconds => PrepDuration.Value;

        void ITurnContext.PublishItemUsed(byte playerIdx, byte slotIdx, byte category, bool isSub)
            => OnItemUsedClientRpc(playerIdx, slotIdx, category, isSub);

        void ITurnContext.PublishOpponentRevealed(byte playerIdx, short itemId)
            => RevealOpponentItemClientRpc(playerIdx, itemId);

        void OnSeatKilledClearBuffs(byte seat, DamageSource _)
            => _buffSystem?.ClearForSeat(seat);

        // ─── IPlayerTurnCancellation ─────────────────────────

        public void CancelTurnParticipation(byte seat)
        {
            if (!IsServer) return;
            if (_players == null || seat >= _players.Length) return;
            var player = _players[seat];
            if (player == null) return;
            player.ClearPendingIntent();
            player.IsReady.Value = false;
            player.HasSelectedItem.Value = false;
        }

        public void ClearPendingIntent(byte seat)
        {
            if (!IsServer) return;
            if (_players == null || seat >= _players.Length) return;
            _players[seat]?.ClearPendingIntent();
        }

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

        void PublishAmbulanceBlanketStagingMulti(int healSeat)
            => AmbulanceBlanketStagingMultiClientRpc(healSeat);

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
                _combatResolver = new CombatResolver();
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
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnectForRematch;
                StartCoroutine(WaitForPlayersRoutine());
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_rematchWaitHandle != null)
            {
                StopCoroutine(_rematchWaitHandle);
                _rematchWaitHandle = null;
            }
            StopAllCoroutines();
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnectForBarrier;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnectForRematch;
            }
            if (_deathService != null)
                _deathService.OnSeatKilled -= OnSeatKilledClearBuffs;
            var rosterForUnsub = MatchCompositionRoot.Instance?.Roster;
            if (rosterForUnsub != null)
                rosterForUnsub.OnPlayerDisconnectedFromSeat -= OnSeatDisconnectedForceGhost;
            _ghostSkillService?.Dispose();
            _ghostSkillService = null;
            _barrier?.Reset();
            OnCombatResult = null;
            OnMultiCombatResult = null;
            OnMultiDeathPresentation = null;
            OnMultiMatchOutcome = null;
            OnEnvironmentAnnounced = null;
            if (Instance == this) Instance = null;
            base.OnNetworkDespawn();
        }

        void OnClientDisconnectForBarrier(ulong clientId)
        {
            _barrier?.HandleDisconnect(clientId);
        }

        void OnClientDisconnectForRematch(ulong clientId)
        {
            if (!IsServer) return;
            _matchManager?.RecordDisconnect(clientId);
        }

        void OnSeatDisconnectedForceGhost(ulong clientId, byte seat)
        {
            if (!IsServer || _deathService == null) return;
            var roster = MatchCompositionRoot.Instance?.Roster;
            if (roster == null) return;
            if (roster.GetLifeState(seat) != LifeState.Alive) return;

            Debug.Log($"[TurnManager] Seat {seat} disconnected while Alive — forcing Ghost");
            _deathService.TryKill(seat, DamageSource.None);
            _deathService.FlushDeathQueue();

            if (CurrentPhase.Value == TurnPhase.RoundOver || _multiTerminalWinnerMask != 0) return;
            TryGrantDeathmatchItems();
            // The running phase observes the updated roster at its next boundary.
            // Never start a second phase driver from a network callback.
        }

        bool TryGetCurrentMultiRoundEnd(out int winner)
        {
            winner = -1;
            if (!IsMulti || _deathService == null || _multiTerminalWinnerMask != 0) return false;
            var end = _deathService.EvaluateRoundEnd();
            if (!end.IsRoundOver) return false;
            winner = end.IsDraw ? -1 : end.WinnerSeat;
            return true;
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

            float deadline = Time.realtimeSinceStartup + MatchCompositionRoot.InitializationTimeout;
            while (mcr != null && mcr.ActiveConfig == null)
            {
                if (!IsSpawned || mcr.InitializationFailure != null) yield break;
                if (IsServer) mcr.ServerBootstrapMatch();
                if (Time.realtimeSinceStartup >= deadline)
                {
                    mcr.FailInitialization("Timed out waiting for network match configuration");
                    yield break;
                }
                yield return _waitHalf;
            }
            if (mcr == null || !IsSpawned) yield break;

            _gameMode = mcr.ActiveConfig.Mode;
            _gameRule = mcr.ActiveConfig.Rule;
            int requiredCount = mcr.ActiveConfig.RequiredPlayerCount;

            if (_players.Length < requiredCount)
            {
                _players = new PlayerState[requiredCount];
                _modifiers = new PlayerModifiers[requiredCount];
                _tempsAtTurnStart = new float[requiredCount];
            }

            var registry = mcr.WritableRegistry;
            deadline = Time.realtimeSinceStartup + MatchCompositionRoot.InitializationTimeout;
            while (registry.TotalCount < requiredCount)
            {
                if (!IsSpawned || mcr == null) yield break;
                if (Time.realtimeSinceStartup >= deadline)
                {
                    mcr.FailInitialization($"Timed out waiting for players: {registry.TotalCount}/{requiredCount}");
                    yield break;
                }
                yield return _waitHalf;
            }

            if (IsServer && mcr.Roster == null)
            {
                var nm = NetworkManager.Singleton;
                mcr.ServerCreateRoster(requiredCount, nm.ConnectedClientsIds);
                if (mcr.Roster == null) yield break;
            }

            var pending = new List<PlayerBinding>(registry.EnumeratePending());
            var roster = mcr.Roster;

            if (IsServer && roster != null)
                roster.SetModifiersSource(
                    seat => seat < _modifiers.Length ? _modifiers[seat] : default,
                    (seat, val) => { if (seat < _modifiers.Length) _modifiers[seat] = val; });

            if (roster != null && roster.RosterReady)
            {
                foreach (var p in pending)
                {
                    if (!roster.TryGetSeatByClientId(p.NetworkObject.OwnerClientId, out byte seat))
                        continue;
                    if (seat >= _players.Length) continue;
                    _players[seat] = p.State;
                    _players[seat].Initialize(seat, _players[seat].GetComponent<PlayerInventory>());
                    _players[seat].BindTurnContext(this);
                }
            }
            else
            {
                pending.Sort((a, b) =>
                    a.NetworkObject.OwnerClientId.CompareTo(b.NetworkObject.OwnerClientId));

                for (int i = 0; i < _players.Length && i < pending.Count; i++)
                {
                    _players[i] = pending[i].State;
                    _players[i].Initialize(i, _players[i].GetComponent<PlayerInventory>());
                    _players[i].BindTurnContext(this);
                }
            }

            if (IsMulti && _gameRule == null)
            {
                mcr.FailInitialization("Multi mode requires a game rule");
                yield break;
            }

            _ghostSkillService?.Dispose();
            _ghostSkillService = null;

            if (IsMulti && roster != null && mcr.NetworkState != null)
            {
                _deathService = new AuthoritativeDeathService(roster, mcr.NetworkState);
                _deathService.SetTurnCancellation(this);
                _deathService.OnSeatKilled += OnSeatKilledClearBuffs;

                roster.OnPlayerDisconnectedFromSeat += OnSeatDisconnectedForceGhost;

                if (_gameRule != null && _gameRule.EnableGhostSystem)
                    _ghostSkillService = new GhostSkillService(roster, _modifiers);
            }

            if (_players.Length != _modifiers.Length || _players.Length != _tempsAtTurnStart.Length
                || _players.Length != requiredCount)
            {
                mcr.FailInitialization("Player arrays do not match the configured roster");
                yield break;
            }

            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i] == null)
                {
                    if (IsMulti)
                    {
                        mcr.FailInitialization($"Player state is missing for seat {i}");
                        yield break;
                    }
                    continue;
                }
                if (_players[i].PlayerIndex != i)
                {
                    mcr.FailInitialization($"Seat {i} identity mismatch: PlayerIndex={_players[i].PlayerIndex}");
                    yield break;
                }
            }

            if (_itemManager != null)
            {
                for (int i = 0; i < _players.Length; i++)
                {
                    if (_players[i] == null) continue;
                    if (IsMulti)
                        _itemManager.InitializePlayerInventory(_players[i].GetInventory(), _gameRule);
                    else
                        _itemManager.InitializePlayerInventory(_players[i].GetInventory());
                }
            }

            if (_matchManager != null)
            {
                _matchManager.FixMatchRoster(_players);
                _matchManager.StartRound();
            }

            var logParts = new System.Text.StringBuilder("[TurnManager] Players found via Registry:");
            for (int i = 0; i < _players.Length; i++)
                logParts.Append($" P{i}={(_players[i] != null ? _players[i].OwnerClientId.ToString() : "null")}");
            Debug.Log(logParts.ToString());

            yield return StartCoroutine(PrepPhaseRoutine());
        }

        // ─── Prep Phase ──────────────────────────────────────

        IEnumerator PrepPhaseRoutine()
        {
            if (!IsSpawned) yield break;
            if (_multiTerminalWinnerMask != 0 || _multiRoundEndInProgress) yield break;
            if (TryGetCurrentMultiRoundEnd(out int entryWinner))
            {
                yield return StartCoroutine(HandleRoundEnd(entryWinner));
                yield break;
            }
            TurnNumber.Value++;
            LastRoundWinner.Value = -1;
            FirstReadySeat.Value = byte.MaxValue;
            _ghostRoundEndTriggered = false;

            if (IsMulti)
            {
                if (_ghostSkillService != null)
                    _ghostSkillService.ExpireChillAuras(TurnNumber.Value, _modifiers);
                _roundLifecycle.ResetForNewTurn(_players, _modifiers);
                if (_ghostSkillService != null)
                    _ghostSkillService.ReapplyActiveChillAuras(_modifiers);
                var nState = MatchCompositionRoot.Instance?.NetworkState;
                if (nState != null)
                    nState.ServerTickAllCooldowns();
            }
            else
                _roundLifecycle.ResetForNewTurn(_players[0], _players[1], _modifiers);

            _envRules.LogActiveEnvironment(ActiveEnvironment.Value, TurnNumber.Value);

            float currentPrepDuration = _envRules.GetPrepDuration(ActiveEnvironment.Value, prepDuration);

            if (_envRules.ShouldApplyKidsEffect(ActiveEnvironment.Value, TurnNumber.Value))
            {
                Debug.Log("[ENV] Kids: steal staging + removing 1 random item from each player");
                PublishKidsStealStaging();
                yield return _waitKidsSteal;
                for (int i = 0; i < _players.Length; i++)
                {
                    if (_players[i] == null) continue;
                    if (IsMulti && _players[i].CurrentLifeState.Value != LifeState.Alive) continue;
                    _envRules.RemoveRandomUnusedItem(_players[i].GetInventory());
                }
            }

            if (_envRules.ShouldApplyAmbulanceEffect(ActiveEnvironment.Value, TurnNumber.Value))
            {
                int healTarget;
                if (IsMulti)
                {
                    var roster = MatchCompositionRoot.Instance?.Roster;
                    healTarget = _envRules.DetermineAmbulanceTargetMulti(_players, roster);
                    Debug.Log($"[ENV] Ambulance Multi: Turn 3 triggered — target=P{healTarget}");
                }
                else
                {
                    Debug.Log($"[ENV] Ambulance: Turn 3 triggered — P0={_players[0].Temperature.Value:F1}° P1={_players[1].Temperature.Value:F1}°");
                    healTarget = _envRules.DetermineAmbulanceTarget(
                        _players[0].Temperature.Value, _players[1].Temperature.Value);
                }

                if (healTarget >= 0)
                {
                    if (IsMulti)
                        PublishAmbulanceBlanketStagingMulti(healTarget);
                    else
                        PublishAmbulanceBlanketStaging(healTarget == 0);
                    yield return _waitAmbulanceBlanket;
                }

                if (healTarget >= 0 && healTarget < _players.Length && _players[healTarget] != null)
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
                _tempsAtTurnStart[i] = _players[i] != null ? _players[i].Temperature.Value : 0f;

            _tempSystem.ResetTimer();
            float elapsed = 0f;
            bool skipFirstFanTick = true;

            RemainingTime.Value = Mathf.CeilToInt(currentPrepDuration);

            while (elapsed < currentPrepDuration)
            {
                if (_ghostRoundEndTriggered) yield break;
                if (IsMulti && _multiPresentationInFlight)
                {
                    yield return null;
                    continue;
                }

                if (TryGetCurrentMultiRoundEnd(out int disconnectWinner))
                {
                    yield return StartCoroutine(HandleRoundEnd(disconnectWinner));
                    yield break;
                }

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
                    if (IsMulti)
                    {
                        int[] scoresBefore = CaptureKillScores();
                        for (int i = 0; i < _players.Length; i++)
                        {
                            if (_players[i] == null || _players[i].CurrentLifeState.Value != LifeState.Alive)
                                continue;
                            if (!skipFirstFanTick)
                                _tempSystem.ApplyFanTick(_players[i], _modifiers[i].FanSpeedMultiplier);
                            _tempSystem.ApplyRecoveryTick(_players[i], recoveryRate, _modifiers[i].RecoveryMultiplier);
                        }

                        for (int i = 0; i < _players.Length; i++)
                        {
                            if (_players[i] == null || !_tempSystem.IsDead(_players[i]) || _deathService == null)
                                continue;
                            var source = DamageSource.None;
                            if (_ghostSkillService != null
                                && _ghostSkillService.ActiveDebuffs.TryGetValue((byte)i, out var debuff))
                                source = DamageSource.Create(debuff.GhostSeat, DamageOrigin.GhostChill);
                            _deathService.TryKill((byte)i, source);
                        }
                        _deathService?.FlushDeathQueue();
                        byte deathMask = _deathService?.ConsumeDeathMask() ?? 0;
                        byte matchWinners = FindNewMultiWinners(scoresBefore);
                        if (matchWinners != 0)
                        {
                            LatchMultiVictory(matchWinners);
                            if (deathMask != 0)
                                yield return StartCoroutine(PresentMultiDeathsAndWait(deathMask, true));
                            CompleteLatchedMultiMatch(_resultSequence);
                            yield break;
                        }

                        int maxRandom = _gameRule != null ? _gameRule.MaxRandomItems : int.MaxValue;
                        for (int i = 0; i < _players.Length; i++)
                        {
                            if (_players[i] == null || _players[i].CurrentLifeState.Value != LifeState.Alive)
                                continue;
                            var inventory = _players[i].GetInventory();
                            if (inventory != null)
                                _tempSystem.CheckThresholds(_players[i], inventory,
                                    inventory.GetThresholdGranted(), dropTable, true, maxRandom);
                        }

                        if (deathMask != 0) TryGrantDeathmatchItems();
                        var roundEnd = _deathService?.EvaluateRoundEnd() ?? default;
                        if (deathMask != 0)
                            yield return StartCoroutine(PresentMultiDeathsAndWait(deathMask, roundEnd.IsRoundOver));
                        if (roundEnd.IsRoundOver)
                        {
                            yield return StartCoroutine(HandleRoundEnd(
                                roundEnd.IsDraw ? -1 : roundEnd.WinnerSeat));
                            yield break;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < _players.Length; i++)
                        {
                            if (_players[i] == null) continue;
                            if (!skipFirstFanTick) _tempSystem.ApplyFanTick(_players[i]);
                            _tempSystem.ApplyRecoveryTick(_players[i], recoveryRate);
                            var inventory = _players[i].GetInventory();
                            if (inventory != null)
                                _tempSystem.CheckThresholds(_players[i], inventory,
                                    inventory.GetThresholdGranted(), dropTable);
                        }
                    }
                    skipFirstFanTick = false;
                }

                if (!IsMulti)
                {
                    var deathWinner = _roundLifecycle.DetermineDeathWinner(_tempSystem, _players[0], _players[1]);
                    if (deathWinner.HasValue)
                    {
                        yield return StartCoroutine(HandleRoundEnd(deathWinner.Value));
                        yield break;
                    }
                }

                bool allReady = true;
                for (int i = 0; i < _players.Length; i++)
                {
                    if (_players[i] == null) continue;
                    if (IsMulti && _players[i].CurrentLifeState.Value != LifeState.Alive) continue;
                    if (!_players[i].IsReady.Value) { allReady = false; break; }
                }
                if (allReady) break;

                yield return null;
            }

            if (_ghostRoundEndTriggered) yield break;

            RemainingTime.Value = 0;

            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i] == null) continue;
                if (IsMulti && _players[i].CurrentLifeState.Value != LifeState.Alive) continue;
                if (!_players[i].IsReady.Value) _roundLifecycle.ForceReady(_players[i]);
                _roundLifecycle.RevertFanUpgrade(_players[i]);
            }

            byte firstSeat = byte.MaxValue;
            float earliestTimestamp = float.MaxValue;
            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i] == null) continue;
                if (IsMulti && _players[i].CurrentLifeState.Value != LifeState.Alive) continue;
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
            {
                if (_players[i] == null) continue;
                if (IsMulti && _players[i].CurrentLifeState.Value != LifeState.Alive) continue;
                lastEmote = System.Math.Max(lastEmote, _players[i].LastEmoteServerTime);
            }

            float emoteRemain = (float)(EMOTE_DISPLAY_SEC - (NetworkManager.ServerTime.Time - lastEmote));
            for (float t = 0f; t < emoteRemain; t += Time.deltaTime)
            {
                if (_ghostRoundEndTriggered) yield break;
                yield return null;
            }

            if (_ghostRoundEndTriggered) yield break;
            yield return StartCoroutine(AttackPhaseRoutine());
        }

        // ─── Multi Combat Helpers ─────────────────────────────

        ItemEffectRuleSnapshot[] BuildItemRules()
        {
            var allItems = _itemManager.GetAllItems();
            if (allItems == null) return System.Array.Empty<ItemEffectRuleSnapshot>();
            var rules = new ItemEffectRuleSnapshot[allItems.Length];
            for (int i = 0; i < allItems.Length; i++)
            {
                if (allItems[i] == null) continue;
                rules[i] = ItemEffectRuleSnapshot.From(allItems[i], (short)i);
            }
            return rules;
        }

        ActionIntent[] BuildActionIntents()
        {
            var intents = new ActionIntent[_players.Length];
            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i] == null) { intents[i] = ActionIntent.Empty; continue; }
                if (IsMulti && _players[i].CurrentLifeState.Value != LifeState.Alive)
                    { intents[i] = ActionIntent.Empty; continue; }
                var q = _players[i].GetActionQueue();
                if (!q.selectedAction.HasValue) { intents[i] = ActionIntent.Empty; continue; }

                var action = q.selectedAction.Value;
                var inv = _players[i].GetInventory();
                if (action.SlotIndex < 0 || action.SlotIndex >= inv.SlotStates.Count)
                {
                    Debug.LogError($"[TurnManager] BuildActionIntents: P{i} SlotIndex {action.SlotIndex} out of range ({inv.SlotStates.Count}) — skipping");
                    intents[i] = ActionIntent.Empty;
                    continue;
                }
                short itemId = inv.SlotStates[action.SlotIndex].ItemId;
                byte targetSeat = action.TargetSeat;

                intents[i] = new ActionIntent(
                    sourceSeat: (byte)i,
                    slotIndex: (byte)action.SlotIndex,
                    itemId: itemId,
                    targetSeat: targetSeat,
                    readyServerTick: (int)(q.readyTimestamp * 1000f));
            }
            return intents;
        }

        ActionIntent BuildActionIntentForSeat(int seat)
        {
            if (seat < 0 || seat >= _players.Length || _players[seat] == null
                || _players[seat].CurrentLifeState.Value != LifeState.Alive)
                return ActionIntent.Empty;
            var queue = _players[seat].GetActionQueue();
            if (!queue.selectedAction.HasValue) return ActionIntent.Empty;
            var action = queue.selectedAction.Value;
            var inventory = _players[seat].GetInventory();
            if (inventory == null || action.SlotIndex >= inventory.SlotStates.Count)
                return ActionIntent.Empty;
            var slot = inventory.SlotStates[action.SlotIndex];
            if (!slot.IsUsable || inventory.GetItemData(action.SlotIndex) != action.ItemData)
                return ActionIntent.Empty;
            return new ActionIntent((byte)seat, action.SlotIndex, slot.ItemId,
                action.TargetSeat, (int)(queue.readyTimestamp * 1000f));
        }

        static void AppendCommittedResolution(MultiCombatResolution aggregate,
            MultiCombatResolution action, int actorSeat)
        {
            aggregate.MainItemIds[actorSeat] = action.MainItemIds[actorSeat];
            aggregate.DeadMask |= action.DeadMask;
            for (int i = 0; i < action.EventCount; i++)
                aggregate.AddEvent(action.OrderedEvents[i]);
        }

        int[] CaptureKillScores()
        {
            var scores = MatchCompositionRoot.Instance?.NetworkState?.KillScores;
            var copy = new int[_players.Length];
            if (scores != null)
                for (int i = 0; i < copy.Length && i < scores.Count; i++) copy[i] = scores[i];
            return copy;
        }

        byte FindNewMultiWinners(int[] before)
        {
            if (!IsMulti || _gameRule == null || before == null) return 0;
            return MultiVictoryRules.FindThresholdCrossings(
                before, CaptureKillScores(), _gameRule.KillsToWin);
        }

        void LatchMultiVictory(byte winnerMask)
        {
            if (_multiTerminalWinnerMask != 0 || winnerMask == 0) return;
            _multiTerminalWinnerMask = winnerMask;
            _ghostRoundEndTriggered = true;
            for (byte seat = 0; seat < _players.Length; seat++)
                CancelTurnParticipation(seat);
        }

        MatchCombatSnapshot BuildMultiSnapshot(ItemEffectRuleSnapshot[] itemRules)
        {
            int count = _players.Length;
            var currentTemps = new float[count];
            var mods = new PlayerModifiers[count];
            var lifeStates = new LifeState[count];
            var inventories = new InventorySnapshot[count];
            var isReady = new bool[count];
            var killScores = new int[count];

            var mcr = MatchCompositionRoot.Instance;
            var roster = mcr?.Roster;

            for (int i = 0; i < count; i++)
            {
                if (_players[i] == null)
                {
                    currentTemps[i] = 0f;
                    lifeStates[i] = LifeState.Ghost;
                    inventories[i] = new InventorySnapshot((byte)i, System.Array.Empty<SlotSnapshot>());
                    continue;
                }
                currentTemps[i] = _players[i].Temperature.Value;
                mods[i] = _modifiers[i];
                lifeStates[i] = roster != null ? roster.GetLifeState((byte)i) : LifeState.Alive;
                isReady[i] = _players[i].IsReady.Value;

                var inv = _players[i].GetInventory();
                var slotCount = inv.SlotStates.Count;
                var slots = new SlotSnapshot[slotCount];
                for (int s = 0; s < slotCount; s++)
                {
                    var slot = inv.SlotStates[s];
                    slots[s] = new SlotSnapshot(slot.ItemId, slot.IsUnlimited, slot.RemainingUses);
                }
                inventories[i] = new InventorySnapshot((byte)i, slots);
            }

            if (mcr?.NetworkState?.KillScores != null)
                for (int i = 0; i < count && i < mcr.NetworkState.KillScores.Count; i++)
                    killScores[i] = mcr.NetworkState.KillScores[i];

            var ruleSnap = _gameRule != null
                ? GameModeRuleSnapshot.From(_gameRule)
                : default;

            return new MatchCombatSnapshot(
                _tempsAtTurnStart, currentTemps, mods, lifeStates,
                inventories, System.Array.Empty<ScheduledEffectSnapshot>(),
                killScores, isReady, ActiveEnvironment.Value, ruleSnap, itemRules);
        }

        // ─── Attack Phase ────────────────────────────────────

        IEnumerator AttackPhaseRoutine()
        {
            if (!IsSpawned) yield break;
            if (_multiTerminalWinnerMask != 0 || _multiRoundEndInProgress) yield break;
            if (TryGetCurrentMultiRoundEnd(out int entryWinner))
            {
                yield return StartCoroutine(HandleRoundEnd(entryWinner));
                yield break;
            }
            CurrentPhase.Value = TurnPhase.AttackPhase;
            PublishPhaseChanged(TurnPhase.AttackPhase, TurnNumber.Value);

            Debug.Log($"[COMBAT] ========== TURN {TurnNumber.Value} ATTACK PHASE START ==========");
            var tempLogStart = new System.Text.StringBuilder("[COMBAT]");
            for (int i = 0; i < _players.Length; i++)
                if (_players[i] != null) tempLogStart.Append($" P{i} temp={_players[i].Temperature.Value:F1}°");
            Debug.Log(tempLogStart.ToString());

            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i] == null) continue;
                _players[i].IsBasicBlocked.Value = false;
            }

            Debug.Log($"[COMBAT] --- Processing delayed buffs/debuffs ---");
            if (IsMulti)
            {
                _buffSystem.BeginMultiTurnStart();
                while (_buffSystem.TryProcessNextDueMulti(_players, out var effect))
                {
                    int[] scoresBefore = CaptureKillScores();
                    if (effect.CausedDeath && _deathService != null)
                    {
                        var src = effect.SourceSeat != DamageSource.InvalidSeat
                            ? DamageSource.Create(effect.SourceSeat, DamageOrigin.DelayedEffect)
                            : DamageSource.None;
                        _deathService.TryKill((byte)effect.TargetSeat, src);
                        _deathService.FlushDeathQueue();
                    }

                    byte deathMask = _deathService?.ConsumeDeathMask() ?? 0;
                    byte matchWinners = FindNewMultiWinners(scoresBefore);
                    if (matchWinners != 0)
                    {
                        LatchMultiVictory(matchWinners);
                        if (deathMask != 0)
                            yield return StartCoroutine(PresentMultiDeathsAndWait(deathMask, true));
                        CompleteLatchedMultiMatch(_resultSequence);
                        yield break;
                    }

                    TryGrantDeathmatchItems();
                    var roundEnd = _deathService.EvaluateRoundEnd();
                    if (deathMask != 0)
                        yield return StartCoroutine(PresentMultiDeathsAndWait(deathMask, roundEnd.IsRoundOver));
                    roundEnd = _deathService.EvaluateRoundEnd();
                    if (roundEnd.IsRoundOver)
                    {
                        int winner = roundEnd.IsDraw ? -1 : roundEnd.WinnerSeat;
                        yield return StartCoroutine(HandleRoundEnd(winner));
                        yield break;
                    }
                }
            }
            else
                _buffSystem.ProcessTurnStart(_players[0], _players[1]);

            var tempLog = new System.Text.StringBuilder("[COMBAT] After buffs:");
            for (int i = 0; i < _players.Length; i++)
                if (_players[i] != null) tempLog.Append($" P{i}={_players[i].Temperature.Value:F1}°");
            Debug.Log(tempLog.ToString());

            var mainNames = new string[_players.Length];
            var subNames = new string[_players.Length];
            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i] == null) { mainNames[i] = "N/A"; subNames[i] = "N/A"; continue; }
                var q = _players[i].GetActionQueue();
                mainNames[i] = q.selectedAction.HasValue ? q.selectedAction.Value.ItemData.ItemName : "NONE";
                subNames[i] = q.subAction.HasValue ? q.subAction.Value.ItemData.ItemName : "NONE";
            }
            var actionLog = new System.Text.StringBuilder("[COMBAT] Actions:");
            for (int i = 0; i < _players.Length; i++)
                actionLog.Append($" P{i}: main={mainNames[i]} sub={subNames[i]}");
            Debug.Log(actionLog.ToString());

            yield return _waitOne;
            if (!IsSpawned) yield break;

            if (IsMulti)
            {
                var itemRules = BuildItemRules();
                var mcr = MatchCompositionRoot.Instance;
                var roster = mcr?.Roster;
                if (roster == null || _deathService == null)
                {
                    Debug.LogError($"[TurnManager] Multi combat aborted: roster={roster != null} deathService={_deathService != null}");
                    yield break;
                }

                var initialSnapshot = BuildMultiSnapshot(itemRules);
                if (TryGetCurrentMultiRoundEnd(out int disconnectedWinner))
                {
                    yield return StartCoroutine(HandleRoundEnd(disconnectedWinner));
                    yield break;
                }
                var initialIntents = BuildActionIntents();
                var actionOrder = _combatResolver.BuildActionOrder(
                    initialSnapshot, initialIntents, initialSnapshot.SeatCount);
                var aggregate = MultiCombatResolution.Create(initialSnapshot.SeatCount);
                System.Array.Copy(actionOrder, aggregate.ActionOrder, actionOrder.Length);
                var roundEnd = default(RoundEndResult);
                var inventoryMutator = new SeatInventoryMutator(
                    _players, roster, GetDropTable(), initialSnapshot.SeatCount);
                var applicator = new MultiCombatApplicator(
                    roster, _deathService, _buffSystem, inventoryMutator);

                var defenseResolution = _combatResolver.ResolveMultiDefenses(initialSnapshot, initialIntents);
                if (!applicator.Apply(defenseResolution, initialSnapshot))
                {
                    Debug.LogError("[TurnManager] Multi defense preflight failed — stopping attack phase");
                    yield break;
                }

                for (int orderIndex = 0; orderIndex < actionOrder.Length; orderIndex++)
                {
                    int actorSeat = actionOrder[orderIndex];
                    var originalIntent = initialIntents[actorSeat];
                    if (originalIntent.IsEmpty) continue;
                    var originalItem = _itemManager.GetItemData(originalIntent.ItemId);
                    if (originalItem is DefenseItemDataSO) continue;

                    var currentIntent = BuildActionIntentForSeat(actorSeat);
                    if (currentIntent.IsEmpty) continue;
                    var actionSnapshot = BuildMultiSnapshot(itemRules);
                    var actionResolution = _combatResolver.ResolveMultiAction(actionSnapshot, currentIntent);
                    if (actionResolution.EventCount == 0) continue;

                    int[] scoresBefore = CaptureKillScores();
                    if (!applicator.Apply(actionResolution, actionSnapshot))
                    {
                        Debug.LogError($"[TurnManager] Multi action preflight failed: seat={actorSeat}, item={currentIntent.ItemId}");
                        yield break;
                    }
                    AppendCommittedResolution(aggregate, actionResolution, actorSeat);
                    _deathService.ConsumeDeathMask();

                    byte matchWinners = FindNewMultiWinners(scoresBefore);
                    if (matchWinners != 0)
                    {
                        LatchMultiVictory(matchWinners);
                        break;
                    }

                    TryGrantDeathmatchItems();
                    roundEnd = _deathService.EvaluateRoundEnd();
                    if (roundEnd.IsRoundOver) break;
                }

                var finalSnapshot = BuildMultiSnapshot(itemRules);
                for (int i = 0; i < aggregate.TemperatureDeltas.Length; i++)
                    aggregate.TemperatureDeltas[i] = finalSnapshot.CurrentTemperatures[i]
                        - initialSnapshot.CurrentTemperatures[i];

                byte roundWinnerMask = roundEnd.IsRoundOver && !roundEnd.IsDraw
                    ? (byte)(1 << roundEnd.WinnerSeat)
                    : (byte)0;
                uint nextSeq = _resultSequence + 1;
                if (!CombatResolutionBatchNetData.TryFromResolution(
                    aggregate, initialSnapshot, roundWinnerMask, nextSeq, out var batchData))
                {
                    Debug.LogError($"[TurnManager] TryFromResolution failed (seq={nextSeq}) — aborting match");
                    yield break;
                }
                batchData = batchData.WithMatchWinnerMask(_multiTerminalWinnerMask);
                _resultSequence = nextSeq;

                if (_multiTerminalWinnerMask != 0)
                {
                    var outcome = CountBits(_multiTerminalWinnerMask) > 1
                        ? MultiMatchOutcome.JointVictory
                        : MultiMatchOutcome.SingleWinner;
                    mcr?.NetworkState?.ServerSetTerminalResult(
                        _resultSequence, outcome, _multiTerminalWinnerMask, released: false);
                }

                if (mcr != null)
                {
                    var expectedIds = new List<ulong>();
                    foreach (var p in mcr.Registry.Players)
                        expectedIds.Add(p.Identity.ClientId);
                    if (!_barrier.Begin(_resultSequence, expectedIds))
                    {
                        Debug.LogError($"[TurnManager] Multi presentation overlap rejected (seq={_resultSequence})");
                        yield break;
                    }
                }

                OnMultiCombatResultClientRpc(batchData);

                float timeout = Mathf.Max(presentationTimeoutSeconds,
                    2f + aggregate.EventCount * 3f + CountBits(aggregate.DeadMask) * 2f);
                yield return StartCoroutine(_barrier.WaitForCompletion(timeout));

                if (_barrier.State == BarrierState.TimedOut)
                    Debug.LogWarning($"[TurnManager] Barrier timed out (seq={_resultSequence}) — proceeding");

                var summaryMulti = new System.Text.StringBuilder($"Turn{TurnNumber.Value}");
                for (int i = 0; i < _players.Length; i++)
                    if (_players[i] != null)
                        summaryMulti.Append($" | P{i}: {_tempsAtTurnStart[i]:F1}→{_players[i].Temperature.Value:F1}°");
                summaryMulti.Append(roundEnd.IsRoundOver ? $" | ROUND OVER (winner={roundEnd.WinnerSeat})" : " | continue");
                PublishDebugLog(summaryMulti.ToString());

                if (_multiTerminalWinnerMask != 0)
                {
                    CompleteLatchedMultiMatch(_resultSequence);
                    yield break;
                }

                yield return _waitOne;
                if (!IsSpawned) yield break;

                yield return StartCoroutine(MultiResolutionPhaseRoutine(roundEnd));
            }
            else
            {
                var snapshot = _combatEngine.CapturePreCombatState(
                    _players[0], _players[1], _tempsAtTurnStart[0], _tempsAtTurnStart[1]);

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
                    if (!_barrier.Begin(result.ResultSequence, expectedIds))
                    {
                        Debug.LogError($"[TurnManager] 1v1 presentation overlap rejected (seq={result.ResultSequence})");
                        yield break;
                    }
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
                if (_players[i] != null)
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

        IEnumerator MultiResolutionPhaseRoutine(RoundEndResult roundEnd)
        {
            if (!IsSpawned) yield break;
            if (_multiTerminalWinnerMask != 0 || _multiRoundEndInProgress) yield break;
            // Re-read after the presentation wait: the roster may have changed.
            roundEnd = _deathService.EvaluateRoundEnd();
            CurrentPhase.Value = TurnPhase.ResolutionPhase;

            if (roundEnd.IsRoundOver)
            {
                int winner = roundEnd.IsDraw ? -1 : roundEnd.WinnerSeat;
                yield return StartCoroutine(HandleRoundEnd(winner));
                yield break;
            }

            for (int i = 0; i < _players.Length; i++)
                if (_players[i] != null)
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

        IEnumerator GhostKillDeathPresentation(byte deathMask, RoundEndResult roundEnd)
        {
            yield return StartCoroutine(PresentMultiDeathsAndWait(
                deathMask, roundEnd.IsRoundOver, presentationSlotReserved: true));

            if (roundEnd.IsRoundOver)
                yield return StartCoroutine(HandleRoundEnd(roundEnd.IsDraw ? -1 : roundEnd.WinnerSeat));
        }

        IEnumerator GhostTerminalDeathPresentation(byte deathMask)
        {
            if (deathMask != 0)
                yield return StartCoroutine(PresentMultiDeathsAndWait(
                    deathMask, true, presentationSlotReserved: true));
            else
                _multiPresentationInFlight = false;
            CompleteLatchedMultiMatch(_resultSequence);
        }

        IEnumerator PresentMultiDeathsAndWait(byte deathMask, bool endsRound,
            bool presentationSlotReserved = false)
        {
            if (deathMask == 0) yield break;
            if (!presentationSlotReserved)
            {
                while (_multiPresentationInFlight)
                    yield return null;
                _multiPresentationInFlight = true;
            }

            try
            {
                while (_barrier.IsActive)
                    yield return null;

                _resultSequence++;
                var mcr = MatchCompositionRoot.Instance;
                if (_multiTerminalWinnerMask != 0)
                {
                    var outcome = CountBits(_multiTerminalWinnerMask) > 1
                        ? MultiMatchOutcome.JointVictory
                        : MultiMatchOutcome.SingleWinner;
                    mcr?.NetworkState?.ServerSetTerminalResult(
                        _resultSequence, outcome, _multiTerminalWinnerMask, released: false);
                }
                if (mcr != null)
                {
                    var expectedIds = new List<ulong>();
                    foreach (var p in mcr.Registry.Players)
                        if (mcr.Roster.IsConnected(p.Identity.PlayerIndex))
                            expectedIds.Add(p.Identity.ClientId);
                    if (!_barrier.Begin(_resultSequence, expectedIds))
                    {
                        Debug.LogError($"[TurnManager] Death presentation overlap rejected (seq={_resultSequence})");
                        yield break;
                    }
                }

                PresentMultiDeathsRpc(deathMask, endsRound, _resultSequence);

                yield return StartCoroutine(_barrier.WaitForCompletion(presentationTimeoutSeconds));

                if (_barrier.State == BarrierState.TimedOut)
                    Debug.LogWarning($"[TurnManager] Death barrier timed out (seq={_resultSequence})");
            }
            finally
            {
                _multiPresentationInFlight = false;
            }
        }

        // ─── Round End ───────────────────────────────────────

        IEnumerator HandleRoundEnd(int winnerIndex)
        {
            if (!IsSpawned) yield break;
            if (IsMulti)
            {
                if (_multiTerminalWinnerMask != 0 || _multiRoundEndInProgress) yield break;
                _multiRoundEndInProgress = true;
                var currentEnd = _deathService.EvaluateRoundEnd();
                if (currentEnd.IsRoundOver)
                    winnerIndex = currentEnd.IsDraw ? -1 : currentEnd.WinnerSeat;
            }
            LastRoundWinner.Value = winnerIndex;

            bool endsMatch;
            if (IsMulti)
            {
                if (_matchManager != null
                    && !_matchManager.TryTransitionMatchState(
                        Network.MatchState.RoundInProgress, Network.MatchState.RoundEnd))
                {
                    Debug.LogWarning("[TurnManager] Multi HandleRoundEnd: RoundInProgress→RoundEnd transition failed");
                }
                endsMatch = _multiTerminalWinnerMask != 0;
            }
            else
            {
                endsMatch = winnerIndex >= 0 && _matchManager != null
                    && _matchManager.WouldEndMatch(winnerIndex);

                if (winnerIndex >= 0)
                {
                    for (int i = 0; i < _players.Length; i++)
                        if (i != winnerIndex)
                            PublishDeathSequence(i, endsMatch);
                }

                if (winnerIndex >= 0 && _matchManager != null)
                    _matchManager.EndRound(winnerIndex);
            }

            CurrentPhase.Value = TurnPhase.RoundOver;
            PublishPhaseChanged(TurnPhase.RoundOver, TurnNumber.Value);

            string winnerText = winnerIndex >= 0 ? $"P{winnerIndex + 1}" : "Draw";
            Debug.Log($"[TurnManager] Round over — Winner: {winnerText}");

            yield return _waitSix;
            if (!IsSpawned) yield break;

            bool matchComplete = IsMulti ? endsMatch :
                (_matchManager != null && _matchManager.IsMatchComplete());
            if (matchComplete)
            {
                if (IsMulti && _matchManager != null)
                {
                    var multiWinnerMask = _multiTerminalWinnerMask;
                    var multiOutcome = CountBits(multiWinnerMask) > 1
                        ? MultiMatchOutcome.JointVictory
                        : MultiMatchOutcome.SingleWinner;
                    if (!_matchManager.TryTransitionMatchState(
                        Network.MatchState.RoundEnd, Network.MatchState.MatchComplete))
                    {
                        Debug.LogWarning("[TurnManager] Multi MatchComplete transition failed — proceeding to next round");
                        yield return StartCoroutine(StartNextRound(winnerIndex < 0));
                        yield break;
                    }
                    OnMultiMatchEndClientRpc((byte)multiOutcome, multiWinnerMask);
                    Debug.Log($"[TurnManager] Multi match complete — outcome={multiOutcome}, winnerMask={multiWinnerMask:X2}");
                    yield break;
                }

                _matchManager.DisconnectedMask = _matchManager.BuildInitialDisconnectMask();

                if (_matchManager.DisconnectedMask != 0)
                {
                    Debug.Log("[TurnManager] Opponent disconnected during result screen — skipping rematch vote");
                    _matchManager.TryTransitionMatchState(
                        Network.MatchState.MatchComplete, Network.MatchState.RematchDeclined);
                    yield break;
                }

                if (!_matchManager.EnterRematchVote())
                {
                    Debug.Log("[TurnManager] EnterRematchVote failed — stopping");
                    yield break;
                }

                _rematchWaitHandle = StartCoroutine(WaitForRematchDecision());
                yield return _rematchWaitHandle;
                yield break;
            }

            yield return StartCoroutine(StartNextRound(winnerIndex < 0));
        }

        static int CountBits(byte mask)
        {
            int count = 0;
            while (mask != 0)
            {
                count += mask & 1;
                mask >>= 1;
            }
            return count;
        }

        void CompleteLatchedMultiMatch(uint decidingSequence)
        {
            if (!IsServer || !IsMulti || _multiTerminalWinnerMask == 0) return;

            var networkState = MatchCompositionRoot.Instance?.NetworkState;
            if (networkState == null)
            {
                Debug.LogError("[TurnManager] Cannot release Multi terminal result: MatchNetworkState missing");
                return;
            }
            var existingTerminal = networkState.TerminalResult.Value;
            if (existingTerminal.IsValid && existingTerminal.Released)
                return;

            CurrentPhase.Value = TurnPhase.RoundOver;
            PublishPhaseChanged(TurnPhase.RoundOver, TurnNumber.Value);
            LastRoundWinner.Value = -1;

            if (_matchManager != null
                && _matchManager.CurrentMatchState.Value == Network.MatchState.RoundInProgress)
                _matchManager.TryTransitionMatchState(
                    Network.MatchState.RoundInProgress, Network.MatchState.RoundEnd);
            if (_matchManager != null
                && _matchManager.CurrentMatchState.Value == Network.MatchState.RoundEnd)
                _matchManager.TryTransitionMatchState(
                    Network.MatchState.RoundEnd, Network.MatchState.MatchComplete);

            var outcome = CountBits(_multiTerminalWinnerMask) > 1
                ? MultiMatchOutcome.JointVictory
                : MultiMatchOutcome.SingleWinner;
            networkState.ServerSetTerminalResult(
                decidingSequence, outcome, _multiTerminalWinnerMask, released: true);
            OnMultiMatchEndClientRpc((byte)outcome, _multiTerminalWinnerMask);
            Debug.Log($"[TurnManager] Multi match complete at action boundary — sequence={decidingSequence}, winnerMask={_multiTerminalWinnerMask:X2}");
        }

        [Rpc(SendTo.Everyone)]
        void OnMultiMatchEndClientRpc(byte outcome, byte winnerMask)
        {
            var o = (MultiMatchOutcome)outcome;
            Debug.Log($"[TurnManager] Multi match end — outcome: {o}, winnerMask: {winnerMask:X2}");
            OnMultiMatchOutcome?.Invoke(o, winnerMask);
        }

        [Rpc(SendTo.Server)]
        public void UseGhostSkillRpc(byte skillIndex, byte targetSeat, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsMulti) return;
            if (_multiTerminalWinnerMask != 0) return;
            if (_multiPresentationInFlight || (_barrier?.IsActive ?? false)) return;
            if (_gameRule == null || !_gameRule.EnableGhostSystem) return;
            if (_ghostSkillService == null) return;
            if (CurrentPhase.Value != TurnPhase.PrepPhase) return;

            ulong senderId = rpcParams.Receive.SenderClientId;
            var mcr = MatchCompositionRoot.Instance;
            if (mcr == null) return;

            var roster = mcr.Roster;
            var nState = mcr.NetworkState;
            if (roster == null || nState == null) return;

            if (!roster.TryGetSeatByClientId(senderId, out byte ghostSeat)) return;
            if (ghostSeat >= _players.Length || _players[ghostSeat] == null) return;
            if (!roster.IsConnected(ghostSeat)) return;
            if (_players[ghostSeat].CurrentLifeState.Value != LifeState.Ghost) return;

            int seatCount = _players.Length;
            if (targetSeat >= seatCount || targetSeat == ghostSeat) return;
            if (!roster.IsConnected(targetSeat)) return;
            if (roster.GetLifeState(targetSeat) != LifeState.Alive) return;

            bool success = false;
            if (skillIndex == GhostSkillService.SKILL_FROST_STRIKE)
            {
                int[] scoresBefore = CaptureKillScores();
                success = _ghostSkillService.TryUseFrostStrike(
                    ghostSeat, targetSeat, nState, _deathService, _players, roster, TurnNumber.Value);

                if (success)
                {
                    byte ghostKillMask = _deathService?.ConsumeDeathMask() ?? 0;
                    byte matchWinners = FindNewMultiWinners(scoresBefore);
                    if (matchWinners != 0)
                    {
                        LatchMultiVictory(matchWinners);
                        GhostSkillUsedClientRpc(ghostSeat, skillIndex, targetSeat);
                        _multiPresentationInFlight = true;
                        StartCoroutine(GhostTerminalDeathPresentation(ghostKillMask));
                        return;
                    }

                    TryGrantDeathmatchItems();
                    var roundEnd = _deathService?.EvaluateRoundEnd() ?? default;

                    if (roundEnd.IsRoundOver)
                        _ghostRoundEndTriggered = true;

                    if (ghostKillMask != 0)
                    {
                        _multiPresentationInFlight = true;
                        StartCoroutine(GhostKillDeathPresentation(ghostKillMask, roundEnd));
                    }
                    else if (roundEnd.IsRoundOver)
                        StartCoroutine(HandleRoundEnd(roundEnd.IsDraw ? -1 : roundEnd.WinnerSeat));
                }
            }
            else if (skillIndex == GhostSkillService.SKILL_CHILL_AURA)
            {
                success = _ghostSkillService.TryUseChillAura(
                    ghostSeat, targetSeat, nState, _modifiers, roster, TurnNumber.Value);
            }

            if (success)
                GhostSkillUsedClientRpc(ghostSeat, skillIndex, targetSeat);
        }

        [Rpc(SendTo.Everyone)]
        void GhostSkillUsedClientRpc(byte ghostSeat, byte skillIndex, byte targetSeat)
        {
            Debug.Log($"[Ghost] Skill used: Ghost P{ghostSeat} → P{targetSeat}, skill={skillIndex}");
            OnGhostSkillUsed?.Invoke(ghostSeat, skillIndex, targetSeat);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool DebugForceGhostForVisual(byte seat)
        {
            if (!IsServer || !IsMulti || _deathService == null || _players == null
                || seat >= _players.Length || _players[seat] == null
                || _players[seat].CurrentLifeState.Value != LifeState.Alive
                || _multiPresentationInFlight || (_barrier?.IsActive ?? false))
                return false;

            _players[seat].Temperature.Value = TemperatureSystem.MIN_TEMP;
            if (!_deathService.TryKill(seat, DamageSource.None)) return false;
            _deathService.FlushDeathQueue();
            byte deathMask = _deathService.ConsumeDeathMask();
            TryGrantDeathmatchItems();
            var roundEnd = _deathService.EvaluateRoundEnd();
            if (roundEnd.IsRoundOver) _ghostRoundEndTriggered = true;
            _multiPresentationInFlight = true;
            StartCoroutine(GhostKillDeathPresentation(deathMask, roundEnd));
            Debug.Log($"[VISUAL] FORCE_GHOST seat={seat} deathMask={deathMask:X2}");
            return true;
        }
#endif

        IEnumerator WaitForRematchDecision()
        {
            try
            {
                while (true)
                {
                    if (_matchManager.DisconnectedMask != 0 || _matchManager.AnyExplicitDecline())
                    {
                        _matchManager.ForceDecline();
                        yield break;
                    }

                    if (_matchManager.RematchCommitted)
                    {
                        _matchManager.CommitRematch(_matchManager.RematchVoteEpoch.Value);
                        BootstrapNewMatch(clearTerminalResult: true);
                        _matchManager.StartRound();
                        PublishReviveVisuals();
                        Debug.Log("[TurnManager] Rematch accepted — starting new match");
                        yield return StartCoroutine(PrepPhaseRoutine());
                        yield break;
                    }

                    if (NetworkManager.ServerTime.Time >= _matchManager.RematchDeadlineServerTime.Value)
                    {
                        _matchManager.ForceDecline();
                        Debug.Log("[TurnManager] Rematch vote timed out");
                        yield break;
                    }

                    yield return null;
                }
            }
            finally
            {
                _rematchWaitHandle = null;
            }
        }

        void BootstrapNewMatch(bool clearTerminalResult)
        {
            _multiRoundEndInProgress = false;
            _buffSystem.ClearAll();
            ActiveEnvironment.Value = EnvironmentType.None;
            TurnNumber.Value = 0;
            LastRoundWinner.Value = -1;
            _emoteWindowClosed = false;
            _ghostRoundEndTriggered = false;
            _deathmatchGranted = false;
            if (clearTerminalResult)
                _multiTerminalWinnerMask = 0;
            _multiPresentationInFlight = false;

            if (IsMulti)
            {
                _ghostSkillService?.ClearAll(_modifiers);
                var nState = MatchCompositionRoot.Instance?.NetworkState;
                nState?.ServerClearAllCooldowns();
                if (clearTerminalResult)
                    nState?.ServerClearTerminalResult();

                _roundLifecycle.ResetPlayersForNewRound(_players);
                int startItems = _gameRule?.InitialRandomItems ?? 4;
                int maxRandom = _gameRule?.MaxRandomItems ?? int.MaxValue;
                _roundLifecycle.GrantStartingItems(_players, GetDropTable(), startItems, maxRandom);

                for (int i = 0; i < _modifiers.Length; i++)
                    _modifiers[i].Reset();
                for (int i = 0; i < _players.Length; i++)
                    if (_players[i] != null)
                        _players[i].ResetForNewTurn();
            }
            else
            {
                _roundLifecycle.ResetPlayersForNewRound(_players[0], _players[1]);
                _roundLifecycle.GrantStartingItems(_players[0], _players[1], GetDropTable());

                _modifiers[0].Reset();
                _modifiers[1].Reset();
                _players[0].ResetForNewTurn();
                _players[1].ResetForNewTurn();
            }
        }

        void TryGrantDeathmatchItems()
        {
            if (_deathmatchGranted || !IsMulti || _gameRule == null) return;
            if (_gameRule.DeathmatchGrantCount <= 0) return;

            var mcr = MatchCompositionRoot.Instance;
            var roster = mcr?.Roster;
            if (roster == null) return;

            int alive = roster.CountAliveForRoundEnd();
            if (alive != 2) return;

            _deathmatchGranted = true;
            var im = ItemManager.Instance;
            if (im == null) return;

            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i] == null) continue;
                if (_players[i].CurrentLifeState.Value != LifeState.Alive) continue;
                im.GrantDeathmatchItems(_players[i].GetInventory(), _gameRule);
            }
            Debug.Log($"[TurnManager] Deathmatch top-up: 2 alive → fill random slots to {_gameRule.MaxRandomItems}");
        }

        IEnumerator StartNextRound(bool isDraw = false)
        {
            if (_multiTerminalWinnerMask != 0)
            {
                Debug.LogError("[TurnManager] Next round rejected because a Multi terminal winner is latched");
                yield break;
            }
            _multiRoundEndInProgress = false;
            BootstrapNewMatch(clearTerminalResult: false);

            if (_matchManager != null && (!isDraw || IsMulti))
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

        public static event System.Action<CombatResolutionBatchNetData> OnMultiCombatResult;

        [Rpc(SendTo.Everyone)]
        void OnMultiCombatResultClientRpc(CombatResolutionBatchNetData batchData)
        {
            OnMultiCombatResult?.Invoke(batchData);
        }

        public static event System.Action<byte, bool, uint> OnMultiDeathPresentation;
        public static event System.Action<Match.MultiMatchOutcome, byte> OnMultiMatchOutcome;
        public static event System.Action<byte, byte, byte> OnGhostSkillUsed;

        [Rpc(SendTo.Everyone)]
        void PresentMultiDeathsRpc(byte deathMask, bool endsRound, uint presentationId)
        {
            OnMultiDeathPresentation?.Invoke(deathMask, endsRound, presentationId);
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

        [Rpc(SendTo.Everyone)]
        void AmbulanceBlanketStagingMultiClientRpc(int healSeat)
        {
            var vfx = EnvironmentVFXManager.Instance;
            if (vfx == null) return;

            int localSeat = -1;
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
            if (localSeat < 0) return;
            bool healSelf = localSeat == healSeat;
            vfx.PlayAmbulanceBlanketStaging(healSelf);
        }
    }
}
