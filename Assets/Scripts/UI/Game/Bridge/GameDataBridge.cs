using System;
using System.Collections;
using System.Collections.Generic;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Player;
using AbsoluteZero.Core.Player.Identity;
using AbsoluteZero.Core.Turn;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.UI.Game.Bridge
{
    public sealed class GameDataBridge : MonoBehaviour, IGameDataBridge
    {
        readonly Dictionary<byte, SeatSnapshot> _seats = new();
        readonly HashSet<byte> _dirtySeatIndices = new();
        bool _matchDirty;

        MatchSnapshot _currentMatch;
        byte _localSeatIndex;
        bool _localSeatResolved;

        bool _roundResultPending;
        float _roundResultTimer;
        bool _matchEndPending;
        float _matchEndTimer;
        const float SETTLE_TIME = 0.15f;

        IReadOnlyPlayerRegistry _registry;
        TurnManager _tm;
        MatchManager _mm;
        MatchNetworkState _mns;

        readonly Dictionary<byte, PlayerState> _boundStates = new();
        readonly Dictionary<byte, SeatCallbacks> _seatCallbacks = new();
        readonly Dictionary<byte, LifeState> _cachedLifeStates = new();

        struct SeatCallbacks
        {
            public NetworkVariable<float>.OnValueChangedDelegate OnTemp;
            public NetworkVariable<float>.OnValueChangedDelegate OnFanSpeed;
            public NetworkVariable<bool>.OnValueChangedDelegate OnReady;
            public NetworkVariable<bool>.OnValueChangedDelegate OnFanActive;
            public NetworkVariable<bool>.OnValueChangedDelegate OnFanUpgraded;
            public NetworkVariable<bool>.OnValueChangedDelegate OnBasicBlocked;
            public NetworkVariable<bool>.OnValueChangedDelegate OnHasSelected;
            public NetworkVariable<LifeState>.OnValueChangedDelegate OnLifeState;
        }

        public int SeatCount => _seats.Count;
        public byte LocalSeatIndex => _localSeatIndex;
        public MatchSnapshot CurrentMatch => _currentMatch;

        public event Action<byte, SeatSnapshot> OnSeatSnapshotChanged;
        public event Action<MatchSnapshot> OnMatchSnapshotChanged;
        public event Action<byte, SeatSnapshot> OnSeatRegistered;
        public event Action<byte> OnSeatUnregistered;
        public event Action<TurnPhase, TurnPhase> OnPhaseChanged;
        public event Action<EnvironmentType> OnEnvironmentAnnounced;
        public event Action<byte, float> OnTempOverride;
        public event Action OnTempOverridesClear;
        public event Action<bool> OnLocalHasSelectedItemChanged;
        public event Action<byte, short> OnOpponentRevealed;
        public event Action<int> OnCurrentAttackerChanged;
        public event Action<MatchSnapshot> OnRoundResult;
        public event Action<MatchSnapshot> OnMatchEnd;
        public event Action<byte> OnRematchDecisionChanged;

        public bool TryGetSeat(byte seat, out SeatSnapshot snapshot)
        {
            return _seats.TryGetValue(seat, out snapshot);
        }

        public void Initialize(IReadOnlyPlayerRegistry registry)
        {
            _registry = registry;
            _registry.Registered += OnPlayerRegistered;
            _registry.Unregistered += OnPlayerUnregistered;

            foreach (var p in _registry.Players)
                BindSeat(p);

            StartCoroutine(WaitForManagers());
        }

        IEnumerator WaitForManagers()
        {
            while (TurnManager.Instance == null)
                yield return null;
            _tm = TurnManager.Instance;
            SubscribeTurnManager();

            var mcr = MatchCompositionRoot.Instance;
            if (mcr != null)
                _mm = mcr.MatchManager;

            while (_mm == null)
            {
                mcr = MatchCompositionRoot.Instance;
                if (mcr != null) _mm = mcr.MatchManager;
                if (_mm != null) break;
                yield return null;
            }
            SubscribeMatchManager();

            if (mcr != null)
                _mns = mcr.NetworkState;
            if (_mns != null)
                SubscribeMatchNetworkState();

            ReadCurrentMatchValues();
        }

        void SubscribeTurnManager()
        {
            _tm.CurrentPhase.OnValueChanged += OnPhaseNVChanged;
            _tm.TurnNumber.OnValueChanged += OnMatchNVChanged_Int;
            _tm.RemainingTime.OnValueChanged += OnMatchNVChanged_Int;
            _tm.PrepDuration.OnValueChanged += OnMatchNVChanged_Float;
            _tm.LastRoundWinner.OnValueChanged += OnMatchNVChanged_Int;
            _tm.ActiveEnvironment.OnValueChanged += OnMatchNVChanged_Env;
            _tm.FirstReadySeat.OnValueChanged += OnMatchNVChanged_Byte;

            TurnManager.OnEnvironmentAnnounced += HandleEnvironmentAnnounced;
            TurnManager.OnOpponentRevealed += HandleOpponentRevealed;
            TurnManager.OnMultiMatchOutcome += HandleMultiMatchOutcome;
            CombatVFXManager.OnTempOverridesClear += HandleTempOverridesClear;
            CombatVFXManager.OnTempTargetsOverride += HandleTempTargetsOverride;
            CombatVFXManager.OnPlayerTempOverride += HandlePlayerTempOverride;
            CombatVFXManager.OnAttackerChanged += HandleAttackerChanged;
            CombatVFXManager.OnPresentationSettled += HandlePresentationSettled;
        }

        void SubscribeMatchManager()
        {
            _mm.RoundNumber.OnValueChanged += OnMatchNVChanged_Int;
            _mm.P1RoundWins.OnValueChanged += OnMatchNVChanged_Int;
            _mm.P2RoundWins.OnValueChanged += OnMatchNVChanged_Int;
            _mm.CurrentMatchState.OnValueChanged += OnMatchStateNVChanged;
            _mm.RematchDecisionMask.OnValueChanged += OnRematchDecisionMaskChanged;
            _mm.RematchDeadlineServerTime.OnValueChanged += OnMatchNVChanged_Double;
            _mm.RematchVoteEpoch.OnValueChanged += OnMatchNVChanged_UInt;
        }

        void SubscribeMatchNetworkState()
        {
            _mns.Config.OnValueChanged += OnConfigNVChanged;
            _mns.TerminalResult.OnValueChanged += OnTerminalResultChanged;
            _mns.KillScores.OnListChanged += OnKillScoresChanged;
        }

        void OnConfigNVChanged(MatchConfigNetData _, MatchConfigNetData __) => _matchDirty = true;
        void OnKillScoresChanged(NetworkListEvent<int> _) => _matchDirty = true;
        void OnTerminalResultChanged(MultiTerminalResultNetData _, MultiTerminalResultNetData newValue)
        {
            _matchDirty = true;
            _matchEndPending = newValue.IsValid;
            if (newValue.IsValid)
                _roundResultPending = false;
        }

        void HandlePresentationSettled(uint sequence)
        {
            if (_currentMatch.Mode != GameMode.Multi) return;
            ReadMatchNetworkStateValues();
            if (HasValidMultiTerminal()
                && _currentMatch.MultiDecidingSequence == sequence)
                _matchEndPending = true;
        }

        void ReadCurrentMatchValues()
        {
            _currentMatch = new MatchSnapshot
            {
                CurrentPhase = _tm.CurrentPhase.Value,
                TurnNumber = _tm.TurnNumber.Value,
                RemainingTime = _tm.RemainingTime.Value,
                PrepDuration = _tm.PrepDuration.Value,
                ActiveEnvironment = _tm.ActiveEnvironment.Value,
                LastRoundWinner = _tm.LastRoundWinner.Value,
                FirstReadySeat = _tm.FirstReadySeat.Value,
                RoundNumber = _mm.RoundNumber.Value,
                P1RoundWins = _mm.P1RoundWins.Value,
                P2RoundWins = _mm.P2RoundWins.Value,
                MatchState = _mm.CurrentMatchState.Value,
                RematchDecisionMask = _mm.RematchDecisionMask.Value,
                RematchDeadlineServerTime = _mm.RematchDeadlineServerTime.Value,
                RematchVoteEpoch = _mm.RematchVoteEpoch.Value,
            };
            ReadMatchNetworkStateValues();
            _matchDirty = true;

            if (_currentMatch.MatchState == MatchState.MatchComplete)
                _matchEndPending = true;
            else if (_currentMatch.CurrentPhase == TurnPhase.RoundOver
                && !HasValidMultiTerminal())
                _roundResultPending = true;
        }

        bool HasValidMultiTerminal()
        {
            return _currentMatch.MultiDecidingSequence != 0
                && _currentMatch.MultiOutcome != MultiMatchOutcome.InProgress
                && _currentMatch.MultiWinnerMask != 0;
        }

        void ReadMatchNetworkStateValues()
        {
            if (_mns == null) return;
            var cfg = _mns.Config.Value;
            _currentMatch.Mode = (GameMode)cfg.Mode;
            _currentMatch.RequiredPlayerCount = cfg.RequiredPlayerCount;
            int count = _mns.KillScores.Count;
            if (count > 0)
            {
                _currentMatch.KillScores = new int[count];
                for (int i = 0; i < count; i++)
                    _currentMatch.KillScores[i] = _mns.KillScores[i];
            }

            int pc = cfg.RequiredPlayerCount > 0 ? cfg.RequiredPlayerCount : 2;
            var ls = new LifeState[pc];
            for (int i = 0; i < pc; i++)
            {
                byte seat = (byte)i;
                if (_boundStates.TryGetValue(seat, out var boundPs))
                    ls[i] = boundPs.CurrentLifeState.Value;
                else if (_cachedLifeStates.TryGetValue(seat, out var cached))
                    ls[i] = cached;
                else
                    ls[i] = LifeState.Alive;
            }
            _currentMatch.LifeStates = ls;
            var terminal = _mns.TerminalResult.Value;
            _currentMatch.MultiOutcome = terminal.Outcome;
            _currentMatch.MultiWinnerMask = terminal.WinnerMask;
            _currentMatch.MultiDecidingSequence = terminal.DecidingSequence;
            _currentMatch.MultiResultReleased = terminal.Released;
        }

        // ─── Seat binding ────────────────────────────────────────

        void OnPlayerRegistered(PlayerBinding binding)
        {
            BindSeat(binding);
        }

        void OnPlayerUnregistered(PlayerIdentity identity)
        {
            UnbindSeat(identity.PlayerIndex);
            OnSeatUnregistered?.Invoke(identity.PlayerIndex);
        }

        void BindSeat(PlayerBinding binding)
        {
            byte idx = binding.Identity.PlayerIndex;
            if (_boundStates.ContainsKey(idx)) return;

            var ps = binding.State;
            _boundStates[idx] = ps;

            var cb = new SeatCallbacks
            {
                OnTemp = (_, _) => MarkSeatDirty(idx),
                OnFanSpeed = (_, _) => MarkSeatDirty(idx),
                OnReady = (_, _) => MarkSeatDirty(idx),
                OnFanActive = (_, _) => MarkSeatDirty(idx),
                OnFanUpgraded = (_, _) => MarkSeatDirty(idx),
                OnBasicBlocked = (_, _) => MarkSeatDirty(idx),
                OnHasSelected = (_, cur) =>
                {
                    MarkSeatDirty(idx);
                    if (IsLocalSeat(idx))
                        OnLocalHasSelectedItemChanged?.Invoke(cur);
                },
                OnLifeState = (_, _) => _matchDirty = true
            };
            _seatCallbacks[idx] = cb;

            ps.Temperature.OnValueChanged += cb.OnTemp;
            ps.FanSpeed.OnValueChanged += cb.OnFanSpeed;
            ps.IsReady.OnValueChanged += cb.OnReady;
            ps.IsFanActive.OnValueChanged += cb.OnFanActive;
            ps.IsFanUpgraded.OnValueChanged += cb.OnFanUpgraded;
            ps.IsBasicBlocked.OnValueChanged += cb.OnBasicBlocked;
            ps.HasSelectedItem.OnValueChanged += cb.OnHasSelected;
            ps.CurrentLifeState.OnValueChanged += cb.OnLifeState;

            ResolveLocalSeat();
            BuildSeatSnapshot(idx, ps);
            OnSeatRegistered?.Invoke(idx, _seats[idx]);
        }

        void UnbindSeat(byte idx)
        {
            if (_boundStates.TryGetValue(idx, out var ps) && _seatCallbacks.TryGetValue(idx, out var cb))
            {
                _cachedLifeStates[idx] = ps.CurrentLifeState.Value;

                ps.Temperature.OnValueChanged -= cb.OnTemp;
                ps.FanSpeed.OnValueChanged -= cb.OnFanSpeed;
                ps.IsReady.OnValueChanged -= cb.OnReady;
                ps.IsFanActive.OnValueChanged -= cb.OnFanActive;
                ps.IsFanUpgraded.OnValueChanged -= cb.OnFanUpgraded;
                ps.IsBasicBlocked.OnValueChanged -= cb.OnBasicBlocked;
                ps.HasSelectedItem.OnValueChanged -= cb.OnHasSelected;
                ps.CurrentLifeState.OnValueChanged -= cb.OnLifeState;
            }
            _seatCallbacks.Remove(idx);
            _seats.Remove(idx);
            _boundStates.Remove(idx);
            _dirtySeatIndices.Remove(idx);
            _matchDirty = true;
        }

        void ResolveLocalSeat()
        {
            if (_localSeatResolved) return;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (_registry.TryGetByClientId(nm.LocalClientId, out var binding))
            {
                _localSeatIndex = binding.Identity.PlayerIndex;
                _localSeatResolved = true;
            }
        }

        bool IsLocalSeat(byte idx) => _localSeatResolved && idx == _localSeatIndex;

        void BuildSeatSnapshot(byte idx, PlayerState ps)
        {
            _seats[idx] = new SeatSnapshot
            {
                SeatIndex = idx,
                ClientId = ps.NetworkObject.OwnerClientId,
                Temperature = ps.Temperature.Value,
                FanSpeed = ps.FanSpeed.Value,
                IsReady = ps.IsReady.Value,
                IsFanActive = ps.IsFanActive.Value,
                IsFanUpgraded = ps.IsFanUpgraded.Value,
                IsBasicBlocked = ps.IsBasicBlocked.Value,
                HasSelectedItem = ps.HasSelectedItem.Value,
                IsLocal = IsLocalSeat(idx),
            };
        }

        void MarkSeatDirty(byte idx) => _dirtySeatIndices.Add(idx);

        // ─── Match NV handlers ─────────────────────────────────────

        void OnPhaseNVChanged(TurnPhase oldVal, TurnPhase newVal)
        {
            _currentMatch.CurrentPhase = newVal;
            _matchDirty = true;
            FlushSeats();
            OnPhaseChanged?.Invoke(oldVal, newVal);

            if (newVal == TurnPhase.RoundOver)
            {
                _roundResultPending = true;
                _roundResultTimer = 0f;
            }
        }

        void OnMatchNVChanged_Int(int _, int __) => _matchDirty = true;
        void OnMatchNVChanged_Float(float _, float __) => _matchDirty = true;
        void OnMatchNVChanged_Env(EnvironmentType _, EnvironmentType __) => _matchDirty = true;
        void OnMatchNVChanged_Byte(byte _, byte __) => _matchDirty = true;
        void OnMatchNVChanged_Double(double _, double __) => _matchDirty = true;
        void OnMatchNVChanged_UInt(uint _, uint __) => _matchDirty = true;

        void OnRematchDecisionMaskChanged(byte _, byte newVal)
        {
            _matchDirty = true;
            OnRematchDecisionChanged?.Invoke(newVal);
        }

        void OnMatchStateNVChanged(MatchState _, MatchState newVal)
        {
            _matchDirty = true;
            if (newVal == MatchState.MatchComplete)
            {
                _matchEndPending = true;
                _matchEndTimer = 0f;
            }
        }

        // ─── Edge event handlers (immediate) ───────────────────────

        void HandleEnvironmentAnnounced(EnvironmentType env) => OnEnvironmentAnnounced?.Invoke(env);
        void HandleOpponentRevealed(byte seat, short itemId) => OnOpponentRevealed?.Invoke(seat, itemId);

        void HandleMultiMatchOutcome(Core.Match.MultiMatchOutcome outcome, byte winnerMask)
        {
            _currentMatch.MultiOutcome = outcome;
            _currentMatch.MultiWinnerMask = winnerMask;
        }
        void HandleAttackerChanged(int seatIdx) => OnCurrentAttackerChanged?.Invoke(seatIdx);
        void HandleTempOverridesClear() => OnTempOverridesClear?.Invoke();

        void HandleTempTargetsOverride(float p1Temp, float p2Temp)
        {
            OnTempOverride?.Invoke(0, p1Temp);
            OnTempOverride?.Invoke(1, p2Temp);
        }

        void HandlePlayerTempOverride(int playerIdx, float temp)
        {
            OnTempOverride?.Invoke((byte)playerIdx, temp);
        }

        // ─── LateUpdate flush ──────────────────────────────────────

        void LateUpdate()
        {
            FlushSeats();
            FlushMatch();
            ProcessRoundResult();
            ProcessMatchEnd();
        }

        void FlushSeats()
        {
            if (_dirtySeatIndices.Count == 0) return;

            foreach (byte idx in _dirtySeatIndices)
            {
                if (!_boundStates.TryGetValue(idx, out var ps)) continue;
                BuildSeatSnapshot(idx, ps);
                OnSeatSnapshotChanged?.Invoke(idx, _seats[idx]);
            }
            _dirtySeatIndices.Clear();
        }

        void FlushMatch()
        {
            if (!_matchDirty) return;
            _matchDirty = false;

            if (_tm != null)
            {
                _currentMatch.CurrentPhase = _tm.CurrentPhase.Value;
                _currentMatch.TurnNumber = _tm.TurnNumber.Value;
                _currentMatch.RemainingTime = _tm.RemainingTime.Value;
                _currentMatch.PrepDuration = _tm.PrepDuration.Value;
                _currentMatch.ActiveEnvironment = _tm.ActiveEnvironment.Value;
                _currentMatch.LastRoundWinner = _tm.LastRoundWinner.Value;
                _currentMatch.FirstReadySeat = _tm.FirstReadySeat.Value;
            }
            if (_mm != null)
            {
                _currentMatch.RoundNumber = _mm.RoundNumber.Value;
                _currentMatch.P1RoundWins = _mm.P1RoundWins.Value;
                _currentMatch.P2RoundWins = _mm.P2RoundWins.Value;
                _currentMatch.MatchState = _mm.CurrentMatchState.Value;
                _currentMatch.RematchDecisionMask = _mm.RematchDecisionMask.Value;
                _currentMatch.RematchDeadlineServerTime = _mm.RematchDeadlineServerTime.Value;
                _currentMatch.RematchVoteEpoch = _mm.RematchVoteEpoch.Value;
            }
            ReadMatchNetworkStateValues();

            OnMatchSnapshotChanged?.Invoke(_currentMatch);
        }

        void ProcessRoundResult()
        {
            if (!_roundResultPending) return;

            if (_currentMatch.Mode == GameMode.Multi)
            {
                ReadMatchNetworkStateValues();
                if (HasValidMultiTerminal()
                    || _currentMatch.MatchState == MatchState.MatchComplete)
                {
                    _roundResultPending = false;
                    return;
                }

                // RoundEnd is written before RoundOver on the server. If the phase
                // arrives first, wait for the matching state instead of briefly
                // showing a round result for a terminal action.
                if (_currentMatch.MatchState == MatchState.RoundInProgress)
                    return;
            }

            _roundResultTimer += Time.unscaledDeltaTime;
            if (_roundResultTimer < SETTLE_TIME) return;

            _roundResultPending = false;
            OnRoundResult?.Invoke(_currentMatch);
        }

        void ProcessMatchEnd()
        {
            if (!_matchEndPending) return;

            if (_currentMatch.Mode == GameMode.Multi)
            {
                ReadMatchNetworkStateValues();
                if (_currentMatch.MatchState != MatchState.MatchComplete
                    || !_currentMatch.MultiResultReleased
                    || _currentMatch.MultiOutcome == MultiMatchOutcome.InProgress
                    || _currentMatch.MultiWinnerMask == 0)
                    return;
                var vfx = CombatVFXManager.Instance;
                if (vfx != null && vfx.HasPendingPresentation(_currentMatch.MultiDecidingSequence))
                {
                    vfx.ForceSettleMultiPresentation(_currentMatch.MultiDecidingSequence);
                    return;
                }
                _matchEndPending = false;
                OnMatchEnd?.Invoke(_currentMatch);
                return;
            }

            _matchEndTimer += Time.unscaledDeltaTime;
            if (_matchEndTimer < SETTLE_TIME) return;

            _matchEndPending = false;
            OnMatchEnd?.Invoke(_currentMatch);
        }

        // ─── Cleanup ──────────────────────────────────────────────

        void OnDestroy()
        {
            foreach (var idx in new List<byte>(_boundStates.Keys))
                UnbindSeat(idx);

            if (_registry != null)
            {
                _registry.Registered -= OnPlayerRegistered;
                _registry.Unregistered -= OnPlayerUnregistered;
            }

            if (_tm != null)
            {
                _tm.CurrentPhase.OnValueChanged -= OnPhaseNVChanged;
                _tm.TurnNumber.OnValueChanged -= OnMatchNVChanged_Int;
                _tm.RemainingTime.OnValueChanged -= OnMatchNVChanged_Int;
                _tm.PrepDuration.OnValueChanged -= OnMatchNVChanged_Float;
                _tm.LastRoundWinner.OnValueChanged -= OnMatchNVChanged_Int;
                _tm.ActiveEnvironment.OnValueChanged -= OnMatchNVChanged_Env;
                _tm.FirstReadySeat.OnValueChanged -= OnMatchNVChanged_Byte;
            }

            if (_mm != null)
            {
                _mm.RoundNumber.OnValueChanged -= OnMatchNVChanged_Int;
                _mm.P1RoundWins.OnValueChanged -= OnMatchNVChanged_Int;
                _mm.P2RoundWins.OnValueChanged -= OnMatchNVChanged_Int;
                _mm.CurrentMatchState.OnValueChanged -= OnMatchStateNVChanged;
                _mm.RematchDecisionMask.OnValueChanged -= OnRematchDecisionMaskChanged;
                _mm.RematchDeadlineServerTime.OnValueChanged -= OnMatchNVChanged_Double;
                _mm.RematchVoteEpoch.OnValueChanged -= OnMatchNVChanged_UInt;
            }

            if (_mns != null)
            {
                _mns.Config.OnValueChanged -= OnConfigNVChanged;
                _mns.TerminalResult.OnValueChanged -= OnTerminalResultChanged;
                _mns.KillScores.OnListChanged -= OnKillScoresChanged;
            }

            TurnManager.OnEnvironmentAnnounced -= HandleEnvironmentAnnounced;
            TurnManager.OnOpponentRevealed -= HandleOpponentRevealed;
            TurnManager.OnMultiMatchOutcome -= HandleMultiMatchOutcome;
            CombatVFXManager.OnTempOverridesClear -= HandleTempOverridesClear;
            CombatVFXManager.OnTempTargetsOverride -= HandleTempTargetsOverride;
            CombatVFXManager.OnPlayerTempOverride -= HandlePlayerTempOverride;
            CombatVFXManager.OnAttackerChanged -= HandleAttackerChanged;
            CombatVFXManager.OnPresentationSettled -= HandlePresentationSettled;
        }
    }
}
