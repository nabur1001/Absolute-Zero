using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Combat
{
    public class TemperatureSystem
    {
        public const float MAX_TEMP = 37f;
        public const float MIN_TEMP = 0f;
        public const float DEFAULT_FAN_SPEED = 1f;
        public const float DEFAULT_RECOVERY_RATE = 1f;
        public const float TICK_INTERVAL = 1f;

        float _tickTimer;

        public void ResetTimer()
        {
            _tickTimer = 0f;
        }

        public void Accumulate(float deltaTime)
        {
            _tickTimer += deltaTime;
        }

        public bool ConsumeTick()
        {
            if (_tickTimer >= TICK_INTERVAL)
            {
                _tickTimer -= TICK_INTERVAL;
                return true;
            }
            return false;
        }

        public void ApplyFanTick(PlayerState player, float fanSpeedMultiplier = 1f)
        {
            if (!player.IsFanActive.Value) return;
            float before = player.Temperature.Value;
            float effectiveSpeed = player.FanSpeed.Value * fanSpeedMultiplier;
            float newTemp = Mathf.Max(MIN_TEMP, before - effectiveSpeed);
            player.Temperature.Value = newTemp;
            Debug.Log($"[TEMP] FanTick: P{player.PlayerIndex} {before:F1}→{newTemp:F1}° (speed={player.FanSpeed.Value}×{fanSpeedMultiplier:F1})");
        }

        public void ApplyRecoveryTick(PlayerState player, float recoveryRate, float recoveryMultiplier = 1f)
        {
            if (player.IsFanActive.Value) return;
            if (!player.IsReady.Value) return;
            float before = player.Temperature.Value;
            float effectiveRate = recoveryRate * recoveryMultiplier;
            float newTemp = Mathf.Min(MAX_TEMP, before + effectiveRate);
            player.Temperature.Value = newTemp;
            Debug.Log($"[TEMP] RecoveryTick: P{player.PlayerIndex} {before:F1}→{newTemp:F1}° (rate={recoveryRate}×{recoveryMultiplier:F1})");
        }

        public float ApplyDamage(PlayerState target, float rawDamage, DamageFilter attackFilter,
                                  DefenseInfo? activeDefense)
        {
            float beforeTemp = target.Temperature.Value;
            float actualDamage = rawDamage;

            if (activeDefense.HasValue)
            {
                var defense = activeDefense.Value;
                if (defense.Filter == attackFilter || defense.Filter == DamageFilter.All)
                {
                    actualDamage = Mathf.Max(0f, rawDamage - defense.BlockAmount);
                    Debug.Log($"[COMBAT] ApplyDamage: defense BLOCKED — filter={defense.Filter}, block={defense.BlockAmount}, raw={rawDamage} → actual={actualDamage}");
                }
                else
                {
                    Debug.Log($"[COMBAT] ApplyDamage: defense MISS — defFilter={defense.Filter} vs atkFilter={attackFilter}, no reduction");
                }
            }
            else
            {
                Debug.Log($"[COMBAT] ApplyDamage: NO defense active");
            }

            target.Temperature.Value = Mathf.Max(MIN_TEMP, target.Temperature.Value - actualDamage);
            Debug.Log($"[COMBAT] ApplyDamage: P{target.PlayerIndex} temp {beforeTemp:F1} → {target.Temperature.Value:F1} (raw={rawDamage}, actual={actualDamage}, filter={attackFilter})");
            return actualDamage;
        }

        public void ApplyHeal(PlayerState target, float amount)
        {
            float beforeTemp = target.Temperature.Value;
            target.Temperature.Value = Mathf.Min(MAX_TEMP, target.Temperature.Value + amount);
            Debug.Log($"[COMBAT] ApplyHeal: P{target.PlayerIndex} temp {beforeTemp:F1} → {target.Temperature.Value:F1} (heal={amount})");
        }

        public bool IsDead(PlayerState player) => player.Temperature.Value <= MIN_TEMP;

        static readonly float[] THRESHOLDS = { 30f, 20f, 10f };
        static readonly int[] GRANTS_1V1 = { 1, 2, 3 };
        static readonly int[] GRANTS_MULTI = { 1, 1, 1 };

        public void CheckThresholds(PlayerState player, PlayerInventory inventory,
                                     bool[] thresholdGranted, ItemDropTable dropTable,
                                     bool isMulti = false, int maxRandomItems = int.MaxValue)
        {
            var grants = isMulti ? GRANTS_MULTI : GRANTS_1V1;
            for (int i = 0; i < THRESHOLDS.Length; i++)
            {
                if (!thresholdGranted[i] && player.Temperature.Value <= THRESHOLDS[i])
                {
                    thresholdGranted[i] = true;
                    Debug.Log($"[TEMP] Threshold: P{player.PlayerIndex} temp={player.Temperature.Value:F1}° ≤ {THRESHOLDS[i]}° → granting {grants[i]} random item(s)");
                    inventory.GrantRandomItems(grants[i], dropTable, maxRandomItems);
                }
            }
        }
    }
}
