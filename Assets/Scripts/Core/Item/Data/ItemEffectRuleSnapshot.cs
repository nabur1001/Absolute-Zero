using System;
using UnityEngine;

namespace AbsoluteZero.Core.Item.Data
{
    public enum ItemEffectKind : byte
    {
        DirectDamage,
        Equalize,
        Recovery,
        Defense,
        FanControl,
        Sabotage,
        Special,
        Buff,
        Debuff
    }

    public readonly struct ItemEffectRuleSnapshot
    {
        public readonly short ItemId;
        public readonly ItemCategory Category;
        public readonly TargetMode TargetMode;
        public readonly DamageFilter AttackFilter;
        public readonly ItemEffectKind EffectKind;

        public readonly float BaseDamage;
        public readonly bool EqualizeToUserTemp;

        public readonly bool IsDefense;
        public readonly float DefenseReduction;

        public readonly float[] HealPerUse;
        public readonly int MaxUses;

        public readonly bool WritesFanSpeed;
        public readonly float FanSpeedValue;
        public readonly bool WritesTargetFanSpeed;
        public readonly float TargetFanSpeedValue;
        public readonly bool BlocksTargetBasics;

        public readonly bool GrantsExtraAction;
        public readonly bool RequiresTargetReady;
        public readonly bool NeutralizesTarget;
        public readonly SpecialEffectType SpecialKind;

        public readonly InventoryMutationType InventoryAction;

        public readonly float ImmediateTempDelta;
        public readonly bool IsSelfTarget;

        public readonly bool HasScheduledEffect;
        public readonly EffectType ScheduledType;
        public readonly float ScheduledValue;
        public readonly int ScheduledDelay;

        ItemEffectRuleSnapshot(
            short itemId, ItemCategory category, TargetMode targetMode,
            DamageFilter attackFilter, ItemEffectKind effectKind,
            float baseDamage, bool equalizeToUserTemp,
            bool isDefense, float defenseReduction,
            float[] healPerUse, int maxUses,
            bool writesFanSpeed, float fanSpeedValue,
            bool writesTargetFanSpeed, float targetFanSpeedValue,
            bool blocksTargetBasics,
            bool grantsExtraAction, bool requiresTargetReady, bool neutralizesTarget,
            SpecialEffectType specialKind,
            InventoryMutationType inventoryAction,
            float immediateTempDelta, bool isSelfTarget,
            bool hasScheduledEffect, EffectType scheduledType, float scheduledValue, int scheduledDelay)
        {
            ItemId = itemId;
            Category = category;
            TargetMode = targetMode;
            AttackFilter = attackFilter;
            EffectKind = effectKind;
            BaseDamage = baseDamage;
            EqualizeToUserTemp = equalizeToUserTemp;
            IsDefense = isDefense;
            DefenseReduction = defenseReduction;
            HealPerUse = healPerUse;
            MaxUses = maxUses;
            WritesFanSpeed = writesFanSpeed;
            FanSpeedValue = fanSpeedValue;
            WritesTargetFanSpeed = writesTargetFanSpeed;
            TargetFanSpeedValue = targetFanSpeedValue;
            BlocksTargetBasics = blocksTargetBasics;
            GrantsExtraAction = grantsExtraAction;
            RequiresTargetReady = requiresTargetReady;
            NeutralizesTarget = neutralizesTarget;
            SpecialKind = specialKind;
            InventoryAction = inventoryAction;
            ImmediateTempDelta = immediateTempDelta;
            IsSelfTarget = isSelfTarget;
            HasScheduledEffect = hasScheduledEffect;
            ScheduledType = scheduledType;
            ScheduledValue = scheduledValue;
            ScheduledDelay = scheduledDelay;
        }

        public static ItemEffectRuleSnapshot From(ItemDataSO so, short registryIndex)
        {
            return FromInternal(so, registryIndex);
        }

        public static ItemEffectRuleSnapshot From(ItemDataSO so)
        {
            short id = so.GetInstanceID() > short.MaxValue ? (short)(so.GetInstanceID() & 0x7FFF) : (short)so.GetInstanceID();
            return FromInternal(so, id);
        }

        static ItemEffectRuleSnapshot FromInternal(ItemDataSO so, short id)
        {
            var targetMode = so.GetTargetMode();

            return so switch
            {
                AttackItemDataSO a => new ItemEffectRuleSnapshot(
                    itemId: id, category: ItemCategory.Attack, targetMode: targetMode,
                    attackFilter: a.AttackFilter,
                    effectKind: a.EqualizeToUserTemp ? ItemEffectKind.Equalize : ItemEffectKind.DirectDamage,
                    baseDamage: a.Damage, equalizeToUserTemp: a.EqualizeToUserTemp,
                    isDefense: false, defenseReduction: 0f,
                    healPerUse: null, maxUses: a.MaxUses,
                    writesFanSpeed: false, fanSpeedValue: 0f,
                    writesTargetFanSpeed: false, targetFanSpeedValue: 0f,
                    blocksTargetBasics: false,
                    grantsExtraAction: false, requiresTargetReady: false, neutralizesTarget: false,
                    specialKind: default,
                    inventoryAction: InventoryMutationType.None,
                    immediateTempDelta: 0f, isSelfTarget: false,
                    hasScheduledEffect: false, scheduledType: default, scheduledValue: 0f, scheduledDelay: 0),

                DefenseItemDataSO d => new ItemEffectRuleSnapshot(
                    itemId: id, category: ItemCategory.Defense, targetMode: targetMode,
                    attackFilter: d.Filter,
                    effectKind: ItemEffectKind.Defense,
                    baseDamage: 0f, equalizeToUserTemp: false,
                    isDefense: true, defenseReduction: d.BlockAmount,
                    healPerUse: null, maxUses: d.MaxUses,
                    writesFanSpeed: false, fanSpeedValue: 0f,
                    writesTargetFanSpeed: false, targetFanSpeedValue: 0f,
                    blocksTargetBasics: false,
                    grantsExtraAction: false, requiresTargetReady: false, neutralizesTarget: false,
                    specialKind: default,
                    inventoryAction: InventoryMutationType.None,
                    immediateTempDelta: 0f, isSelfTarget: true,
                    hasScheduledEffect: false, scheduledType: default, scheduledValue: 0f, scheduledDelay: 0),

                RecoveryItemDataSO r => new ItemEffectRuleSnapshot(
                    itemId: id, category: ItemCategory.Recovery, targetMode: targetMode,
                    attackFilter: default,
                    effectKind: ItemEffectKind.Recovery,
                    baseDamage: 0f, equalizeToUserTemp: false,
                    isDefense: false, defenseReduction: 0f,
                    healPerUse: r.HealPerUse != null ? (float[])r.HealPerUse.Clone() : null,
                    maxUses: r.MaxUses,
                    writesFanSpeed: false, fanSpeedValue: 0f,
                    writesTargetFanSpeed: false, targetFanSpeedValue: 0f,
                    blocksTargetBasics: false,
                    grantsExtraAction: false, requiresTargetReady: false, neutralizesTarget: false,
                    specialKind: default,
                    inventoryAction: InventoryMutationType.None,
                    immediateTempDelta: 0f, isSelfTarget: true,
                    hasScheduledEffect: false, scheduledType: default, scheduledValue: 0f, scheduledDelay: 0),

                BuffItemDataSO b => new ItemEffectRuleSnapshot(
                    itemId: id, category: ItemCategory.Buff, targetMode: targetMode,
                    attackFilter: default,
                    effectKind: ItemEffectKind.Buff,
                    baseDamage: 0f, equalizeToUserTemp: false,
                    isDefense: false, defenseReduction: 0f,
                    healPerUse: null, maxUses: b.MaxUses,
                    writesFanSpeed: false, fanSpeedValue: 0f,
                    writesTargetFanSpeed: false, targetFanSpeedValue: 0f,
                    blocksTargetBasics: false,
                    grantsExtraAction: false, requiresTargetReady: false, neutralizesTarget: false,
                    specialKind: default,
                    inventoryAction: InventoryMutationType.None,
                    immediateTempDelta: b.ImmediateTempDelta, isSelfTarget: true,
                    hasScheduledEffect: !Mathf.Approximately(b.DelayedTempDelta, 0f),
                    scheduledType: EffectType.TempChange,
                    scheduledValue: b.DelayedTempDelta,
                    scheduledDelay: b.DelayTurns),

                DebuffItemDataSO db => new ItemEffectRuleSnapshot(
                    itemId: id, category: ItemCategory.Debuff, targetMode: targetMode,
                    attackFilter: db.AttackFilter,
                    effectKind: ItemEffectKind.Debuff,
                    baseDamage: 0f, equalizeToUserTemp: false,
                    isDefense: false, defenseReduction: 0f,
                    healPerUse: null, maxUses: db.MaxUses,
                    writesFanSpeed: false, fanSpeedValue: 0f,
                    writesTargetFanSpeed: false, targetFanSpeedValue: 0f,
                    blocksTargetBasics: false,
                    grantsExtraAction: false, requiresTargetReady: false, neutralizesTarget: false,
                    specialKind: default,
                    inventoryAction: InventoryMutationType.None,
                    immediateTempDelta: db.ImmediateTempDelta, isSelfTarget: false,
                    hasScheduledEffect: !Mathf.Approximately(db.DelayedTempDelta, 0f),
                    scheduledType: EffectType.TempChange,
                    scheduledValue: db.DelayedTempDelta,
                    scheduledDelay: db.DelayTurns),

                SabotageItemDataSO s => new ItemEffectRuleSnapshot(
                    itemId: id, category: ItemCategory.Sabotage, targetMode: targetMode,
                    attackFilter: default,
                    effectKind: ItemEffectKind.Sabotage,
                    baseDamage: 0f, equalizeToUserTemp: false,
                    isDefense: false, defenseReduction: 0f,
                    healPerUse: null, maxUses: s.MaxUses,
                    writesFanSpeed: false, fanSpeedValue: 0f,
                    writesTargetFanSpeed: false, targetFanSpeedValue: 0f,
                    blocksTargetBasics: s.SabotageType == SabotageType.BlockBasic,
                    grantsExtraAction: false, requiresTargetReady: false,
                    neutralizesTarget: s.SabotageType == SabotageType.Neutralize,
                    specialKind: default,
                    inventoryAction: s.SabotageType switch
                    {
                        SabotageType.Reroll => InventoryMutationType.RerollTarget,
                        SabotageType.Steal => InventoryMutationType.StealFromTarget,
                        _ => InventoryMutationType.None
                    },
                    immediateTempDelta: 0f, isSelfTarget: false,
                    hasScheduledEffect: false, scheduledType: default, scheduledValue: 0f, scheduledDelay: 0),

                SpecialItemDataSO sp => BuildSpecial(id, targetMode, sp),

                _ => throw new InvalidOperationException($"Unknown ItemDataSO subclass: {so.GetType().Name}")
            };
        }

        public ItemEffectRuleSnapshot DeepCopy()
        {
            return new ItemEffectRuleSnapshot(
                ItemId, Category, TargetMode, AttackFilter, EffectKind,
                BaseDamage, EqualizeToUserTemp,
                IsDefense, DefenseReduction,
                HealPerUse != null ? (float[])HealPerUse.Clone() : null, MaxUses,
                WritesFanSpeed, FanSpeedValue,
                WritesTargetFanSpeed, TargetFanSpeedValue,
                BlocksTargetBasics,
                GrantsExtraAction, RequiresTargetReady, NeutralizesTarget,
                SpecialKind,
                InventoryAction,
                ImmediateTempDelta, IsSelfTarget,
                HasScheduledEffect, ScheduledType, ScheduledValue, ScheduledDelay);
        }

        static ItemEffectRuleSnapshot BuildSpecial(short id, TargetMode targetMode, SpecialItemDataSO sp)
        {
            bool isFanControl = sp.SpecialEffect == SpecialEffectType.FanSpeedChange;
            bool delayed = sp.DelayTurns > 0;

            return new ItemEffectRuleSnapshot(
                itemId: id, category: ItemCategory.Special, targetMode: targetMode,
                attackFilter: default,
                effectKind: isFanControl ? ItemEffectKind.FanControl : ItemEffectKind.Special,
                baseDamage: 0f, equalizeToUserTemp: false,
                isDefense: false, defenseReduction: 0f,
                healPerUse: null, maxUses: sp.MaxUses,
                writesFanSpeed: isFanControl && !delayed && sp.TargetsSelf,
                fanSpeedValue: sp.EffectValue,
                writesTargetFanSpeed: isFanControl && !delayed && !sp.TargetsSelf,
                targetFanSpeedValue: sp.EffectValue,
                blocksTargetBasics: false,
                grantsExtraAction: sp.SpecialEffect == SpecialEffectType.ExtraAction,
                requiresTargetReady: sp.SpecialEffect == SpecialEffectType.RevealOpponent,
                neutralizesTarget: false,
                specialKind: sp.SpecialEffect,
                inventoryAction: InventoryMutationType.None,
                immediateTempDelta: 0f, isSelfTarget: sp.TargetsSelf,
                hasScheduledEffect: isFanControl && delayed,
                scheduledType: EffectType.FanSpeedChange,
                scheduledValue: sp.EffectValue,
                scheduledDelay: sp.DelayTurns);
        }
    }
}
