using System.Collections.Generic;
using System.Linq;
using AbsoluteZero.Core.Network;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Match
{
    public class MatchManager : NetworkBehaviour
    {
        public readonly NetworkVariable<int> RoundNumber = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> P1RoundWins = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> P2RoundWins = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<MatchState> CurrentMatchState = new(
            MatchState.WaitingToStart, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<byte> RematchDecisionMask = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<double> RematchDeadlineServerTime = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<uint> RematchVoteEpoch = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        const int WINS_TO_MATCH = 2;
        const double REMATCH_VOTE_DURATION = 15.0;

        enum SeatDecision : byte { Pending, Accepted, Declined }

        SeatDecision[] _seatDecisions = new SeatDecision[2];
        byte _disconnectedMask;
        readonly Dictionary<ulong, byte> _matchRoster = new();
        uint _epoch;
        bool _rematchCommitted;

        static readonly HashSet<(MatchState, MatchState)> _allowedTransitions = new()
        {
            (MatchState.WaitingToStart, MatchState.RoundInProgress),
            (MatchState.RoundInProgress, MatchState.RoundEnd),
            (MatchState.RoundEnd, MatchState.RoundInProgress),
            (MatchState.RoundEnd, MatchState.MatchComplete),
            (MatchState.MatchComplete, MatchState.RematchVote),
            (MatchState.MatchComplete, MatchState.RematchDeclined),
            (MatchState.RematchVote, MatchState.RematchDeclined),
            (MatchState.RematchVote, MatchState.RoundInProgress),
        };

        // ─── State Transition ─────────────────────────────────

        public bool TryTransitionMatchState(MatchState expected, MatchState next)
        {
            if (!IsServer) return false;

            if (CurrentMatchState.Value != expected)
            {
                Debug.LogWarning($"[MatchManager] Transition rejected: expected {expected}, actual {CurrentMatchState.Value}");
                return false;
            }

            if (!_allowedTransitions.Contains((expected, next)))
            {
                Debug.LogWarning($"[MatchManager] Transition not allowed: {expected} → {next}");
                return false;
            }

            CurrentMatchState.Value = next;
            Debug.Log($"[MatchManager] State: {expected} → {next}");
            return true;
        }

        // ─── Match Lifecycle ──────────────────────────────────

        public void StartRound()
        {
            if (!IsServer) return;
            if (!TryTransitionMatchState(CurrentMatchState.Value, MatchState.RoundInProgress))
                return;
            RoundNumber.Value++;
            Debug.Log($"[MatchManager] Round {RoundNumber.Value} started");
        }

        public void EndRound(int winnerIndex)
        {
            if (!IsServer || !IsSpawned) return;
            if (!TryTransitionMatchState(MatchState.RoundInProgress, MatchState.RoundEnd))
                return;

            if (winnerIndex == 0)
                P1RoundWins.Value++;
            else if (winnerIndex == 1)
                P2RoundWins.Value++;

            OnRoundEndClientRpc(winnerIndex, RoundNumber.Value);
            Debug.Log($"[MatchManager] Round {RoundNumber.Value} ended — Winner: P{winnerIndex + 1} " +
                      $"(Wins: P1={P1RoundWins.Value}, P2={P2RoundWins.Value})");

            if (IsMatchComplete())
            {
                int matchWinner = P1RoundWins.Value >= WINS_TO_MATCH ? 0 : 1;
                TryTransitionMatchState(MatchState.RoundEnd, MatchState.MatchComplete);
                OnMatchEndClientRpc(matchWinner);
                Debug.Log($"[MatchManager] Match complete — Winner: P{matchWinner + 1}");
            }
        }

        public bool IsMatchComplete()
        {
            return P1RoundWins.Value >= WINS_TO_MATCH || P2RoundWins.Value >= WINS_TO_MATCH;
        }

        public bool WouldEndMatch(int winnerIndex)
        {
            if (winnerIndex == 0)
                return P1RoundWins.Value + 1 >= WINS_TO_MATCH;
            if (winnerIndex == 1)
                return P2RoundWins.Value + 1 >= WINS_TO_MATCH;
            return false;
        }

        public bool IsRoundOver()
        {
            return CurrentMatchState.Value == MatchState.RoundEnd
                || CurrentMatchState.Value == MatchState.MatchComplete;
        }

        // ─── Roster ───────────────────────────────────────────

        public void FixMatchRoster(Player.PlayerState[] players)
        {
            _matchRoster.Clear();
            foreach (var p in players)
            {
                if (p != null && p.NetworkObject != null)
                    _matchRoster[p.OwnerClientId] = (byte)p.PlayerIndex;
            }
            Debug.Log($"[MatchManager] Roster fixed: {_matchRoster.Count} seats");
        }

        public byte BuildInitialDisconnectMask()
        {
            byte mask = 0;
            if (NetworkManager.Singleton == null) return mask;
            var connected = NetworkManager.Singleton.ConnectedClientsIds;
            foreach (var kvp in _matchRoster)
            {
                if (!connected.Contains(kvp.Key))
                    mask |= (byte)(1 << kvp.Value);
            }
            return mask;
        }

        public bool TryGetSeatByClientId(ulong clientId, out byte seat)
        {
            return _matchRoster.TryGetValue(clientId, out seat);
        }

        // ─── Rematch Vote ─────────────────────────────────────

        public byte DisconnectedMask
        {
            get => _disconnectedMask;
            set => _disconnectedMask = value;
        }

        public bool RematchCommitted => _rematchCommitted;

        public bool EnterRematchVote()
        {
            if (!IsServer) return false;
            if (CurrentMatchState.Value != MatchState.MatchComplete) return false;

            _epoch++;
            _rematchCommitted = false;
            _seatDecisions[0] = SeatDecision.Pending;
            _seatDecisions[1] = SeatDecision.Pending;

            RematchVoteEpoch.Value = _epoch;
            RematchDecisionMask.Value = 0;
            RematchDeadlineServerTime.Value = NetworkManager.ServerTime.Time + REMATCH_VOTE_DURATION;

            TryTransitionMatchState(MatchState.MatchComplete, MatchState.RematchVote);
            Debug.Log($"[MatchManager] RematchVote started (epoch={_epoch}, deadline={RematchDeadlineServerTime.Value:F1})");
            return true;
        }

        [Rpc(SendTo.Server)]
        public void SubmitRematchDecisionRpc(bool accept, uint voteEpoch, RpcParams rpcParams = default)
        {
            if (CurrentMatchState.Value != MatchState.RematchVote) return;
            if (voteEpoch != _epoch) return;
            if (NetworkManager.ServerTime.Time >= RematchDeadlineServerTime.Value) return;
            if (_rematchCommitted) return;

            ulong senderId = rpcParams.Receive.SenderClientId;
            if (!_matchRoster.TryGetValue(senderId, out byte seat)) return;
            if (_seatDecisions[seat] != SeatDecision.Pending) return;

            _seatDecisions[seat] = accept ? SeatDecision.Accepted : SeatDecision.Declined;
            RematchDecisionMask.Value |= (byte)(1 << seat);

            Debug.Log($"[MatchManager] Seat {seat} decided: {(accept ? "Accept" : "Decline")} (epoch={voteEpoch})");

            if (accept && AllAccepted() && _disconnectedMask == 0)
            {
                if (TryBeginRematchCommit(_epoch))
                    _rematchCommitted = true;
            }
        }

        public bool AllAccepted()
        {
            foreach (var kvp in _matchRoster)
            {
                if (_seatDecisions[kvp.Value] != SeatDecision.Accepted)
                    return false;
            }
            return true;
        }

        public bool AnyExplicitDecline()
        {
            foreach (var kvp in _matchRoster)
            {
                if (_seatDecisions[kvp.Value] == SeatDecision.Declined)
                    return true;
            }
            return false;
        }

        public bool TryBeginRematchCommit(uint epoch)
        {
            if (_rematchCommitted) return false;
            if (CurrentMatchState.Value != MatchState.RematchVote) return false;
            if (epoch != _epoch) return false;
            if (NetworkManager.ServerTime.Time >= RematchDeadlineServerTime.Value) return false;
            if (AnyExplicitDecline()) return false;
            if (_disconnectedMask != 0) return false;
            if (!AllAccepted()) return false;

            return true;
        }

        public void CommitRematch(uint epoch)
        {
            if (!IsServer) return;
            if (epoch != _epoch) return;
            if (!_rematchCommitted) return;
            P1RoundWins.Value = 0;
            P2RoundWins.Value = 0;
            RoundNumber.Value = 0;
            RematchDecisionMask.Value = 0;
            Debug.Log($"[MatchManager] Rematch committed (epoch={epoch})");
        }

        public void ForceDecline()
        {
            if (!IsServer) return;
            TryTransitionMatchState(MatchState.RematchVote, MatchState.RematchDeclined);
        }

        public void RecordDisconnect(ulong clientId)
        {
            if (CurrentMatchState.Value != MatchState.MatchComplete &&
                CurrentMatchState.Value != MatchState.RematchVote) return;

            if (_matchRoster.TryGetValue(clientId, out byte seat))
            {
                _disconnectedMask |= (byte)(1 << seat);
                Debug.Log($"[MatchManager] Disconnect recorded: seat {seat} (state={CurrentMatchState.Value})");
            }
        }

        // ─── RPCs ─────────────────────────────────────────────

        [Rpc(SendTo.Everyone)]
        void OnRoundEndClientRpc(int winnerIndex, int roundNumber)
        {
        }

        [Rpc(SendTo.Everyone)]
        void OnMatchEndClientRpc(int winnerIndex)
        {
        }
    }
}
