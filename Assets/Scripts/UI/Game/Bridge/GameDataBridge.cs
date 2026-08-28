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

        readonly Dictionary<byte, PlayerState> _boundStates = new();
        readonly Dictionary<byte, SeatCallbacks> _seatCallbacks = new();

        struct SeatCallbacks
        {
            public NetworkVariable<float>.OnValueChangedDelegate OnTemp;
            public NetworkVariable<float>.OnValueChangedDelegate OnFanSpeed;
            public NetworkVariable<bool>.OnValueChangedDelegate OnReady;
            public NetworkVariable<bool>.OnValueChangedDelegate OnFanActive;
            public NetworkVariable<bool>.OnValueChangedDelegate OnFanUpgraded;
            public NetworkVariable<bool>.OnValueChangedDelegate OnBasicBlocked;
            public NetworkVariable<bool>.OnValueChangedDelegate OnHasSelected;
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
            CombatVFXManager.OnTempOverridesClear += HandleTempOverridesClear;
            CombatVFXManager.OnTempTargetsOverride += HandleTempTargetsOverride;
            CombatVFXManager.OnPlayerTempOverride += HandlePlayerTempOverride;
            CombatVFXManager.OnAttackerChanged += HandleAttackerChanged;
        }

        void SubscribeMatchManager()
        {
            _mm.RoundNumber.OnValueChanged += OnMatchNVChanged_Int;
            _mm.P1RoundWins.OnValueChanged += OnMatchNVChanged_Int;
            _mm.P2RoundWins.OnValueChanged += OnMatchNVChanged_Int;
            _mm.CurrentMatchState.OnValueChanged += OnMatchStateNVChanged;
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
            };
            _matchDirty = true;
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
                }
            };
            _seatCallbacks[idx] = cb;

            ps.Temperature.OnValueChanged += cb.OnTemp;
            ps.FanSpeed.OnValueChanged += cb.OnFanSpeed;
            ps.IsReady.OnValueChanged += cb.OnReady;
            ps.IsFanActive.OnValueChanged += cb.OnFanActive;
            ps.IsFanUpgraded.OnValueChanged += cb.OnFanUpgraded;
            ps.IsBasicBlocked.OnValueChanged += cb.OnBasicBlocked;
            ps.HasSelectedItem.OnValueChanged += cb.OnHasSelected;

            ResolveLocalSeat();
            BuildSeatSnapshot(idx, ps);
            OnSeatRegistered?.Invoke(idx, _seats[idx]);
        }

        void UnbindSeat(byte idx)
        {
            if (_boundStates.TryGetValue(idx, out var ps) && _seatCallbacks.TryGetValue(idx, out var cb))
            {
                ps.Temperature.OnValueChanged -= cb.OnTemp;
                ps.FanSpeed.OnValueChanged -= cb.OnFanSpeed;
                ps.IsReady.OnValueChanged -= cb.OnReady;
                ps.IsFanActive.OnValueChanged -= cb.OnFanActive;
                ps.IsFanUpgraded.OnValueChanged -= cb.OnFanUpgraded;
                ps.IsBasicBlocked.OnValueChanged -= cb.OnBasicBlocked;
                ps.HasSelectedItem.OnValueChanged -= cb.OnHasSelected;
            }
            _seatCallbacks.Remove(idx);
            _seats.Remove(idx);
            _boundStates.Remove(idx);
            _dirtySeatIndices.Remove(idx);
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
            }

            OnMatchSnapshotChanged?.Invoke(_currentMatch);
        }

        void ProcessRoundResult()
        {
            if (!_roundResultPending) return;

            _roundResultTimer += Time.unscaledDeltaTime;
            if (_roundResultTimer < SETTLE_TIME) return;

            _roundResultPending = false;
            OnRoundResult?.Invoke(_currentMatch);
        }

        void ProcessMatchEnd()
        {
            if (!_matchEndPending) return;

            _matchEndTimer += Time.unscaledDeltaTime;
            if (_matchEndTimer < SETTLE_TIME) return;

            _matchEndPending = false;
            OnMatchEnd?.Invoke(_currentMatch);
        }

        // ─── Cleanup ──────────────────────────────────────────────

        void OnDestroy()
        {
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
            }

            TurnManager.OnEnvironmentAnnounced -= HandleEnvironmentAnnounced;
            TurnManager.OnOpponentRevealed -= HandleOpponentRevealed;
            CombatVFXManager.OnTempOverridesClear -= HandleTempOverridesClear;
            CombatVFXManager.OnTempTargetsOverride -= HandleTempTargetsOverride;
            CombatVFXManager.OnPlayerTempOverride -= HandlePlayerTempOverride;
            CombatVFXManager.OnAttackerChanged -= HandleAttackerChanged;
        }
    }
}
