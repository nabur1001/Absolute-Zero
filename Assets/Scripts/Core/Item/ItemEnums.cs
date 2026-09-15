namespace AbsoluteZero.Core.Item
{
    public enum ItemCategory : byte { Attack, Defense, Recovery, Buff, Debuff, Sabotage, Special }

    public enum ItemSlotType : byte { Main, Sub }

    public enum ItemPersistence : byte { Permanent, BasicConsumable, RandomConsumable }

    public enum SabotageType : byte { Reroll, Steal, BlockBasic, Neutralize }

    public enum MiniGameType : byte
    {
        None, HitTargets, HugCharacter, PatternUnlock, TapRepeat,
        GaugeMatch, BoilWater, TightenScrews, PickCard, ClawGrab, TimingCut, TapCard, FanBoost
    }

    public enum EffectType : byte { TempChange, FanSpeedChange, BasicBlock, RecoveryRateChange }

    public enum CombatEventType : byte { MainEffect, DefenseActivated, Neutralized, Death }

    public enum DamageFilter : byte { Temperature, Food, All }

    public enum TargetMode : byte { Self, SingleTarget }

    public enum SpecialEffectType : byte { FanSpeedChange, ExtraAction, RevealOpponent }

    public enum EnvironmentType : byte
    {
        None,
        SunnyDay,
        CoolBreeze,
        CicadaSong,
        Kids,
        Ambulance,
        SummerVacation,
        HeatWaveWarning
    }
}
