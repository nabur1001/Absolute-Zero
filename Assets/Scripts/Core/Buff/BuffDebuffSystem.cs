using System.Collections.Generic;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Buff
{
    public class BuffDebuffSystem
    {
        struct ScheduledEffect
        {
            public int TargetPlayerIndex;
            public byte SourceSeat;
            public EffectType Type;
            public float Value;
            public int TurnsRemaining;
        }

        public struct AppliedEffect
        {
            public int TargetSeat;
            public byte SourceSeat;
            public EffectType Type;
            public float Value;
            public float TemperatureBefore;
            public float TemperatureAfter;
            public bool CausedDeath;
        }

        readonly List<ScheduledEffect> _pending = new();
        readonly List<AppliedEffect> _lastApplied = new();

        public void Schedule(int targetPlayer, EffectType type, float value, int delayTurns,
            byte sourceSeat = DamageSource.InvalidSeat)
        {
            Debug.Log($"[COMBAT] BuffSystem.Schedule: P{targetPlayer} {type} value={value} in {delayTurns} turn(s), source={sourceSeat}");
            _pending.Add(new ScheduledEffect
            {
                TargetPlayerIndex = targetPlayer,
                SourceSeat = sourceSeat,
                Type = type,
                Value = value,
                TurnsRemaining = delayTurns
            });
        }

        public IReadOnlyList<AppliedEffect> LastAppliedEffects => _lastApplied;

        public void ProcessTurnStart(PlayerState p1, PlayerState p2)
        {
            Debug.Log($"[COMBAT] BuffSystem.ProcessTurnStart: {_pending.Count} pending effect(s)");
            _lastApplied.Clear();

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var eff = _pending[i];
                eff.TurnsRemaining--;

                if (eff.TurnsRemaining <= 0)
                {
                    var target = eff.TargetPlayerIndex == 0 ? p1 : p2;
                    float tempBefore = target.Temperature.Value;
                    Debug.Log($"[COMBAT] BuffSystem: FIRING delayed effect on P{eff.TargetPlayerIndex} — {eff.Type} value={eff.Value}, source={eff.SourceSeat}");
                    ApplyEffect(target, eff);
                    float tempAfter = target.Temperature.Value;
                    _lastApplied.Add(new AppliedEffect
                    {
                        TargetSeat = eff.TargetPlayerIndex,
                        SourceSeat = eff.SourceSeat,
                        Type = eff.Type,
                        Value = eff.Value,
                        TemperatureBefore = tempBefore,
                        TemperatureAfter = tempAfter,
                        CausedDeath = tempBefore > 0f && tempAfter <= 0f
                    });
                    _pending.RemoveAt(i);
                }
                else
                {
                    Debug.Log($"[COMBAT] BuffSystem: P{eff.TargetPlayerIndex} {eff.Type}={eff.Value} — {eff.TurnsRemaining} turn(s) remaining");
                    _pending[i] = eff;
                }
            }
        }

        public void ProcessTurnStart(PlayerState[] players)
        {
            Debug.Log($"[COMBAT] BuffSystem.ProcessTurnStart(N={players.Length}): {_pending.Count} pending effect(s)");
            _lastApplied.Clear();

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var eff = _pending[i];
                eff.TurnsRemaining--;

                if (eff.TurnsRemaining <= 0)
                {
                    if (eff.TargetPlayerIndex < 0 || eff.TargetPlayerIndex >= players.Length
                        || players[eff.TargetPlayerIndex] == null)
                    {
                        _pending.RemoveAt(i);
                        continue;
                    }
                    var target = players[eff.TargetPlayerIndex];
                    if (target.CurrentLifeState.Value != LifeState.Alive)
                    {
                        _pending.RemoveAt(i);
                        continue;
                    }
                    float tempBefore = target.Temperature.Value;
                    Debug.Log($"[COMBAT] BuffSystem: FIRING delayed effect on P{eff.TargetPlayerIndex} — {eff.Type} value={eff.Value}, source={eff.SourceSeat}");
                    ApplyEffect(target, eff);
                    float tempAfter = target.Temperature.Value;
                    _lastApplied.Add(new AppliedEffect
                    {
                        TargetSeat = eff.TargetPlayerIndex,
                        SourceSeat = eff.SourceSeat,
                        Type = eff.Type,
                        Value = eff.Value,
                        TemperatureBefore = tempBefore,
                        TemperatureAfter = tempAfter,
                        CausedDeath = tempBefore > 0f && tempAfter <= 0f
                    });
                    _pending.RemoveAt(i);
                }
                else
                {
                    _pending[i] = eff;
                }
            }
        }

        public void BeginMultiTurnStart()
        {
            _lastApplied.Clear();
            for (int i = 0; i < _pending.Count; i++)
            {
                var effect = _pending[i];
                effect.TurnsRemaining--;
                _pending[i] = effect;
            }
        }

        public bool TryProcessNextDueMulti(PlayerState[] players, out AppliedEffect applied)
        {
            applied = default;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var effect = _pending[i];
                if (effect.TurnsRemaining > 0) continue;
                _pending.RemoveAt(i);

                if (effect.TargetPlayerIndex < 0 || effect.TargetPlayerIndex >= players.Length)
                    continue;
                var target = players[effect.TargetPlayerIndex];
                if (target == null || target.CurrentLifeState.Value != LifeState.Alive)
                    continue;

                float before = target.Temperature.Value;
                ApplyEffect(target, effect);
                applied = new AppliedEffect
                {
                    TargetSeat = effect.TargetPlayerIndex,
                    SourceSeat = effect.SourceSeat,
                    Type = effect.Type,
                    Value = effect.Value,
                    TemperatureBefore = before,
                    TemperatureAfter = target.Temperature.Value,
                    CausedDeath = before > 0f && target.Temperature.Value <= 0f
                };
                _lastApplied.Add(applied);
                return true;
            }
            return false;
        }

        void ApplyEffect(PlayerState target, ScheduledEffect eff)
        {
            float beforeTemp = target.Temperature.Value;
            switch (eff.Type)
            {
                case EffectType.TempChange:
                    target.Temperature.Value = Mathf.Clamp(
                        target.Temperature.Value + eff.Value, 0f, 37f);
                    Debug.Log($"[COMBAT] BuffSystem.ApplyEffect: P{eff.TargetPlayerIndex} TempChange {beforeTemp:F1} → {target.Temperature.Value:F1} (delta={eff.Value})");
                    break;
                case EffectType.FanSpeedChange:
                    if (target.IsFanUpgraded.Value)
                    {
                        Debug.Log($"[COMBAT] BuffSystem.ApplyEffect: P{eff.TargetPlayerIndex} FanSpeed ALREADY upgraded — skipping");
                        break;
                    }
                    float beforeFan = target.FanSpeed.Value;
                    target.FanSpeed.Value = eff.Value;
                    target.IsFanUpgraded.Value = true;
                    Debug.Log($"[COMBAT] BuffSystem.ApplyEffect: P{eff.TargetPlayerIndex} FanSpeed {beforeFan} → {eff.Value} (upgraded)");
                    break;
            }
        }

        public void ClearForSeat(int seatIndex)
        {
            _pending.RemoveAll(e => e.TargetPlayerIndex == seatIndex);
        }

        public void ClearAll()
        {
            _pending.Clear();
            _lastApplied.Clear();
        }

        public void ClearForSeat(byte seat)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].TargetPlayerIndex == seat)
                    _pending.RemoveAt(i);
            }
        }
    }
}
