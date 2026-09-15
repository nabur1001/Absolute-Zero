using System;
using AbsoluteZero.Core.Buff;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;

namespace AbsoluteZero.Core.Combat
{
    public readonly struct MatchCombatSnapshot
    {
        public readonly float[] TemperaturesAtTurnStart;
        public readonly float[] CurrentTemperatures;
        public readonly PlayerModifiers[] Modifiers;
        public readonly LifeState[] LifeStates;
        public readonly InventorySnapshot[] Inventories;
        public readonly ScheduledEffectSnapshot[] ScheduledEffects;
        public readonly int[] CurrentKillScores;
        public readonly bool[] IsReady;
        public readonly EnvironmentType Environment;
        public readonly GameModeRuleSnapshot Rule;
        public readonly ItemEffectRuleSnapshot[] ItemRules;

        public int SeatCount => CurrentTemperatures?.Length ?? 0;

        public MatchCombatSnapshot(
            float[] temperaturesAtTurnStart,
            float[] currentTemperatures,
            PlayerModifiers[] modifiers,
            LifeState[] lifeStates,
            InventorySnapshot[] inventories,
            ScheduledEffectSnapshot[] scheduledEffects,
            int[] currentKillScores,
            bool[] isReady,
            EnvironmentType environment,
            GameModeRuleSnapshot rule,
            ItemEffectRuleSnapshot[] itemRules)
        {
            TemperaturesAtTurnStart = (float[])temperaturesAtTurnStart?.Clone();
            CurrentTemperatures = (float[])currentTemperatures?.Clone();
            Modifiers = (PlayerModifiers[])modifiers?.Clone();
            LifeStates = (LifeState[])lifeStates?.Clone();
            Inventories = DeepCopyInventories(inventories);
            ScheduledEffects = (ScheduledEffectSnapshot[])scheduledEffects?.Clone();
            CurrentKillScores = (int[])currentKillScores?.Clone();
            IsReady = (bool[])isReady?.Clone();
            Environment = environment;
            Rule = rule;
            ItemRules = DeepCopyItemRules(itemRules);
        }

        static InventorySnapshot[] DeepCopyInventories(InventorySnapshot[] source)
        {
            if (source == null) return null;
            var copy = new InventorySnapshot[source.Length];
            for (int i = 0; i < source.Length; i++)
                copy[i] = new InventorySnapshot(source[i].SeatIndex, (SlotSnapshot[])source[i].Slots?.Clone());
            return copy;
        }

        static ItemEffectRuleSnapshot[] DeepCopyItemRules(ItemEffectRuleSnapshot[] source)
        {
            if (source == null) return null;
            var copy = new ItemEffectRuleSnapshot[source.Length];
            for (int i = 0; i < source.Length; i++)
                copy[i] = source[i].DeepCopy();
            return copy;
        }
    }
}
