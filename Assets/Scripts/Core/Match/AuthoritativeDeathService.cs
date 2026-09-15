using System;
using System.Collections.Generic;
using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Match
{
    public sealed class AuthoritativeDeathService
    {
        readonly MatchRoster _roster;
        readonly MatchNetworkState _networkState;
        readonly List<(byte seat, DamageSource source)> _deathQueue = new();
        IPlayerTurnCancellation _turnCancellation;
        bool _isFlushing;
        byte _pendingDeathMask;

        public event Action<byte, DamageSource> OnSeatKilled;
        public event Action<RoundEndResult> OnRoundEndEvaluated;

        int RequiredCount => _roster.RequiredPlayerCount;

        public AuthoritativeDeathService(MatchRoster roster, MatchNetworkState networkState)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _networkState = networkState ?? throw new ArgumentNullException(nameof(networkState));
        }

        public void SetTurnCancellation(IPlayerTurnCancellation cancellation)
            => _turnCancellation = cancellation;

        public bool TryKill(byte victimSeat, DamageSource source)
        {
            if (!Unity.Netcode.NetworkManager.Singleton?.IsServer ?? true) return false;
            if (victimSeat >= RequiredCount) return false;
            if (!_roster.IsActive(victimSeat)) return false;
            if (_roster.GetLifeState(victimSeat) != LifeState.Alive) return false;

            _roster.SetLifeState(victimSeat, LifeState.Ghost);
            _turnCancellation?.CancelTurnParticipation(victimSeat);
            _roster.ClearInventory(victimSeat);

            if (!source.IsNone && source.Origin != DamageOrigin.Natural
                && source.AttackerSeat < RequiredCount
                && source.AttackerSeat != victimSeat)
            {
                _networkState?.ServerAddKill(source.AttackerSeat);
            }

            _pendingDeathMask |= (byte)(1 << victimSeat);
            _deathQueue.Add((victimSeat, source));
            Debug.Log($"[DeathService] Seat {victimSeat} killed by seat {source.AttackerSeat} ({source.Origin})");
            return true;
        }

        public void FlushDeathQueue()
        {
            if (_deathQueue.Count == 0) return;
            if (_isFlushing) return;

            _isFlushing = true;
            try
            {
                while (_deathQueue.Count > 0)
                {
                    var snapshot = new List<(byte seat, DamageSource source)>(_deathQueue);
                    _deathQueue.Clear();

                    foreach (var (seat, source) in snapshot)
                        OnSeatKilled?.Invoke(seat, source);
                }
            }
            finally
            {
                _isFlushing = false;
            }

            var result = EvaluateRoundEnd();
            if (result.IsRoundOver)
                OnRoundEndEvaluated?.Invoke(result);

            if (_deathQueue.Count > 0)
                FlushDeathQueue();
        }

        public bool HasPendingDeaths => _deathQueue.Count > 0;

        public byte ConsumeDeathMask()
        {
            byte mask = _pendingDeathMask;
            _pendingDeathMask = 0;
            return mask;
        }

        public RoundEndResult EvaluateRoundEnd()
        {
            int aliveCount = _roster.CountAliveForRoundEnd();
            int ghostCount = _roster.CountAllGhosts();
            int activeCount = _roster.ActiveCount;

            bool allGhost = activeCount > 0 && ghostCount >= activeCount;
            bool oneOrFewerAlive = aliveCount <= 1;

            if (!allGhost && !oneOrFewerAlive)
                return new RoundEndResult { IsRoundOver = false };

            byte lastAliveSeat = byte.MaxValue;
            int aliveFound = 0;
            for (byte s = 0; s < RequiredCount; s++)
            {
                if (_roster.CountsAsAliveForRoundEnd(s))
                {
                    lastAliveSeat = s;
                    aliveFound++;
                }
            }

            if (allGhost)
            {
                return new RoundEndResult
                {
                    IsRoundOver = true,
                    IsDraw = true,
                    WinnerSeat = byte.MaxValue,
                    SurvivorMask = 0
                };
            }

            byte winnerMask = 0;
            if (aliveFound == 1)
                winnerMask = (byte)(1 << lastAliveSeat);

            return new RoundEndResult
            {
                IsRoundOver = true,
                IsDraw = aliveFound == 0,
                WinnerSeat = aliveFound == 1 ? lastAliveSeat : byte.MaxValue,
                SurvivorMask = winnerMask
            };
        }
    }

    public struct RoundEndResult
    {
        public bool IsRoundOver;
        public bool IsDraw;
        public byte WinnerSeat;
        public byte SurvivorMask;
    }
}
