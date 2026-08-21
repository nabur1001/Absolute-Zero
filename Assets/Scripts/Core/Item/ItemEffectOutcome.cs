using AbsoluteZero.Core.Player;

namespace AbsoluteZero.Core.Item
{
    public enum InventoryMutationType : byte
    {
        None = 0,
        RerollTarget,
        StealFromTarget
    }

    public struct ItemEffectOutcome
    {
        public float UserHeal;
        public float UserDamage;
        public DamageFilter UserDamageFilter;

        public float TargetHeal;
        public float TargetDamage;
        public DamageFilter TargetDamageFilter;
        public DefenseInfo? TargetDefenseCheck;

        public DefenseInfo? SetUserDefense;
        public bool NeutralizeTarget;
        public bool GrantExtraAction;
        public bool RevealOpponent;

        public bool BlockTargetBasics;
        public bool WriteUserFanSpeed;
        public float UserFanSpeedValue;
        public bool WriteTargetFanSpeed;
        public float TargetFanSpeedValue;

        public bool HasScheduledEffect;
        public int ScheduledTargetIndex;
        public EffectType ScheduledType;
        public float ScheduledValue;
        public int ScheduledDelayTurns;

        public InventoryMutationType InventoryAction;
        public bool Blocked;
    }
}
