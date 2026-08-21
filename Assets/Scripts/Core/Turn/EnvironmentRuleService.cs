using System.Collections.Generic;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Turn
{
    public class EnvironmentRuleService
    {
        public const float KIDS_STEAL_STAGING_SECONDS = 3.2f;
        public const float AMBULANCE_BLANKET_STAGING_SECONDS = 3f;

        static readonly EnvironmentType[] Pool =
        {
            EnvironmentType.SunnyDay,
            EnvironmentType.CoolBreeze,
            EnvironmentType.CicadaSong,
            EnvironmentType.Kids,
            EnvironmentType.Ambulance,
            EnvironmentType.SummerVacation,
            EnvironmentType.HeatWaveWarning
        };

        public EnvironmentType SelectRandom()
        {
            return Pool[Random.Range(0, Pool.Length)];
        }

        public float GetPrepDuration(EnvironmentType env, float baseDuration)
        {
            if (env == EnvironmentType.SummerVacation)
            {
                Debug.Log($"[ENV] SummerVacation: prep duration {baseDuration}s → 10s");
                return 10f;
            }
            return baseDuration;
        }

        public float GetRecoveryRate(EnvironmentType env)
        {
            if (env == EnvironmentType.SunnyDay) return 2f;
            if (env == EnvironmentType.CoolBreeze) return 0f;
            return TemperatureSystem.DEFAULT_RECOVERY_RATE;
        }

        public bool ShouldApplyKidsEffect(EnvironmentType env, int turnNumber)
        {
            return env == EnvironmentType.Kids && turnNumber == 3;
        }

        public bool ShouldApplyAmbulanceEffect(EnvironmentType env, int turnNumber)
        {
            return env == EnvironmentType.Ambulance && turnNumber == 3;
        }

        public int DetermineAmbulanceTarget(float p1Temp, float p2Temp)
        {
            if (p1Temp < p2Temp) return 0;
            if (p2Temp < p1Temp) return 1;
            return -1;
        }

        public void LogActiveEnvironment(EnvironmentType env, int turnNumber)
        {
            if (env == EnvironmentType.None) return;

            Debug.Log($"[ENV] ===== Turn {turnNumber} — active: {env} ({GetName(env)}) =====");
            switch (env)
            {
                case EnvironmentType.SunnyDay:
                    Debug.Log("[ENV] SunnyDay: recovery rate 1 → 2°/sec (fan-off recovery doubled)");
                    break;
                case EnvironmentType.CoolBreeze:
                    Debug.Log("[ENV] CoolBreeze: recovery rate 1 → 0°/sec (no fan-off recovery)");
                    break;
                case EnvironmentType.CicadaSong:
                    Debug.Log("[ENV] CicadaSong: audio/visual distraction (no gameplay effect yet)");
                    break;
                case EnvironmentType.HeatWaveWarning:
                    Debug.Log("[ENV] HeatWave: lower-temp player acts first this turn");
                    break;
            }
        }

        public void RemoveRandomUnusedItem(PlayerInventory inventory)
        {
            var candidates = new List<int>();
            for (int i = 0; i < inventory.SlotStates.Count; i++)
            {
                var slot = inventory.SlotStates[i];
                if (slot.IsEmpty) continue;
                var itemData = inventory.GetItemData(i);
                if (itemData == null) continue;
                if (itemData.Persistence != ItemPersistence.RandomConsumable) continue;
                candidates.Add(i);
            }

            if (candidates.Count == 0) return;

            int targetSlot = candidates[Random.Range(0, candidates.Count)];
            var targetItem = inventory.GetItemData(targetSlot);
            string itemName = targetItem != null ? targetItem.ItemName : "?";

            var removedSlot = inventory.SlotStates[targetSlot];
            removedSlot.ItemId = -1;
            removedSlot.RemainingUses = 0;
            inventory.SlotStates[targetSlot] = removedSlot;
            inventory.CompactSlots();

            Debug.Log($"[ENV] Kids: removed '{itemName}' from slot {targetSlot}");
        }

        public static string GetName(EnvironmentType env)
        {
            return env switch
            {
                EnvironmentType.SunnyDay => "햇살쨍쨍",
                EnvironmentType.CoolBreeze => "바람선선",
                EnvironmentType.CicadaSong => "매미울음",
                EnvironmentType.Kids => "잼민이들",
                EnvironmentType.Ambulance => "앰뷸런스",
                EnvironmentType.SummerVacation => "여름방학",
                EnvironmentType.HeatWaveWarning => "폭염경보",
                _ => ""
            };
        }
    }
}
