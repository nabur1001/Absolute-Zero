using System;
using System.Collections.Generic;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Match
{
    public class GhostSkillService : IDisposable
    {
        public const byte SKILL_FROST_STRIKE = 0;
        public const byte SKILL_CHILL_AURA = 1;

        public const byte FROST_STRIKE_COOLDOWN = 3;
        public const byte CHILL_AURA_COOLDOWN = 2;
        public const float FROST_STRIKE_DAMAGE = 15f;
        public const float CHILL_AURA_FAN_MULTIPLIER = 2f;
        public const float CHILL_AURA_RECOVERY_MULTIPLIER = 0.5f;

        public struct GhostDebuffEntry
        {
            public byte GhostSeat;
            public byte SkillIndex;
            public int AppliedTurn;
        }

        readonly Dictionary<byte, GhostDebuffEntry> _activeDebuffs = new();
        MatchRoster _roster;
        PlayerModifiers[] _modifiers;
        bool _disposed;

        public IReadOnlyDictionary<byte, GhostDebuffEntry> ActiveDebuffs => _activeDebuffs;

        public GhostSkillService(MatchRoster roster, PlayerModifiers[] modifiers)
        {
            _roster = roster;
            _modifiers = modifiers;
            if (_roster != null)
                _roster.OnPlayerDisconnectedFromSeat += OnSeatDisconnected;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_roster != null)
            {
                _roster.OnPlayerDisconnectedFromSeat -= OnSeatDisconnected;
                _roster = null;
            }

            if (_modifiers != null)
            {
                RemoveAllActiveDebuffs();
                _modifiers = null;
            }
        }

        void OnSeatDisconnected(ulong clientId, byte seat)
        {
            if (_disposed || _modifiers == null) return;
            RemoveDebuffsForSeat(seat);
        }

        void RemoveDebuffsForSeat(byte seat)
        {
            var toRemove = new List<byte>();
            foreach (var kvp in _activeDebuffs)
            {
                if (kvp.Key == seat || kvp.Value.GhostSeat == seat)
                {
                    if (kvp.Value.SkillIndex == SKILL_CHILL_AURA)
                        RevertChillAura(kvp.Key, _modifiers);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var key in toRemove)
                _activeDebuffs.Remove(key);
        }

        void RemoveAllActiveDebuffs()
        {
            foreach (var kvp in _activeDebuffs)
            {
                if (kvp.Value.SkillIndex == SKILL_CHILL_AURA)
                    RevertChillAura(kvp.Key, _modifiers);
            }
            _activeDebuffs.Clear();
        }

        public bool TryUseFrostStrike(byte ghostSeat, byte targetSeat,
            MatchNetworkState netState, AuthoritativeDeathService deathService,
            PlayerState[] players, ISeatStateAccessor roster, int currentTurn)
        {
            if (_disposed) return false;
            if (netState.ServerGetCooldown(ghostSeat, SKILL_FROST_STRIKE) > 0) return false;
            if (targetSeat >= players.Length || players[targetSeat] == null) return false;
            if (roster.GetLifeState(targetSeat) != LifeState.Alive) return false;

            float before = players[targetSeat].Temperature.Value;
            float newTemp = Mathf.Max(TemperatureSystem.MIN_TEMP, before - FROST_STRIKE_DAMAGE);
            players[targetSeat].Temperature.Value = newTemp;
            Debug.Log($"[Ghost] FrostStrike: Ghost P{ghostSeat} → P{targetSeat}, {before:F1}→{newTemp:F1}°");

            if (newTemp <= TemperatureSystem.MIN_TEMP && deathService != null)
            {
                var src = DamageSource.Create(ghostSeat, DamageOrigin.GhostFrost);
                deathService.TryKill(targetSeat, src);
                deathService.FlushDeathQueue();
            }

            netState.ServerSetCooldown(ghostSeat, SKILL_FROST_STRIKE, FROST_STRIKE_COOLDOWN);
            return true;
        }

        public bool TryUseChillAura(byte ghostSeat, byte targetSeat,
            MatchNetworkState netState, PlayerModifiers[] modifiers,
            ISeatStateAccessor roster, int currentTurn)
        {
            if (_disposed) return false;
            if (netState.ServerGetCooldown(ghostSeat, SKILL_CHILL_AURA) > 0) return false;
            if (targetSeat >= modifiers.Length) return false;
            if (roster.GetLifeState(targetSeat) != LifeState.Alive) return false;
            if (!roster.IsConnected(targetSeat)) return false;

            if (_activeDebuffs.TryGetValue(targetSeat, out var existing))
            {
                RevertChillAura(targetSeat, modifiers);
                _activeDebuffs.Remove(targetSeat);
            }

            modifiers[targetSeat].FanSpeedMultiplier = CHILL_AURA_FAN_MULTIPLIER;
            modifiers[targetSeat].RecoveryMultiplier = CHILL_AURA_RECOVERY_MULTIPLIER;

            _activeDebuffs[targetSeat] = new GhostDebuffEntry
            {
                GhostSeat = ghostSeat,
                SkillIndex = SKILL_CHILL_AURA,
                AppliedTurn = currentTurn
            };

            netState.ServerSetCooldown(ghostSeat, SKILL_CHILL_AURA, CHILL_AURA_COOLDOWN);
            Debug.Log($"[Ghost] ChillAura: Ghost P{ghostSeat} → P{targetSeat}, fan×{CHILL_AURA_FAN_MULTIPLIER} rec×{CHILL_AURA_RECOVERY_MULTIPLIER}");
            return true;
        }

        public void ExpireChillAuras(int currentTurn, PlayerModifiers[] modifiers)
        {
            if (_disposed) return;
            var toRemove = new List<byte>();
            foreach (var kvp in _activeDebuffs)
            {
                if (kvp.Value.SkillIndex != SKILL_CHILL_AURA) continue;
                if (currentTurn > kvp.Value.AppliedTurn + 1)
                {
                    RevertChillAura(kvp.Key, modifiers);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var seat in toRemove)
                _activeDebuffs.Remove(seat);
        }

        void RevertChillAura(byte targetSeat, PlayerModifiers[] modifiers)
        {
            if (targetSeat >= modifiers.Length) return;
            modifiers[targetSeat].FanSpeedMultiplier = 1f;
            modifiers[targetSeat].RecoveryMultiplier = 1f;
            Debug.Log($"[Ghost] ChillAura expired on P{targetSeat}");
        }

        public void ReapplyActiveChillAuras(PlayerModifiers[] modifiers)
        {
            if (_disposed) return;
            foreach (var kvp in _activeDebuffs)
            {
                if (kvp.Value.SkillIndex != SKILL_CHILL_AURA) continue;
                if (kvp.Key >= modifiers.Length) continue;
                modifiers[kvp.Key].FanSpeedMultiplier = CHILL_AURA_FAN_MULTIPLIER;
                modifiers[kvp.Key].RecoveryMultiplier = CHILL_AURA_RECOVERY_MULTIPLIER;
            }
        }

        public void ClearAll(PlayerModifiers[] modifiers)
        {
            if (_disposed) return;
            foreach (var kvp in _activeDebuffs)
            {
                if (kvp.Value.SkillIndex == SKILL_CHILL_AURA)
                    RevertChillAura(kvp.Key, modifiers);
            }
            _activeDebuffs.Clear();
        }
    }
}
