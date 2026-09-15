using AbsoluteZero.Core.Item;

namespace AbsoluteZero.Core.Player
{
    public struct PlayerModifiers
    {
        public bool BasicItemsBlocked;
        public bool ActionNeutralized;
        public DefenseInfo? ActiveDefense;
        public bool HasExtraAction;
        public bool OpponentRevealed;
        public float FanSpeedMultiplier;
        public float RecoveryMultiplier;

        public void Reset()
        {
            BasicItemsBlocked = false;
            ActionNeutralized = false;
            ActiveDefense = null;
            HasExtraAction = false;
            OpponentRevealed = false;
            FanSpeedMultiplier = 1f;
            RecoveryMultiplier = 1f;
        }
    }

    public struct DefenseInfo
    {
        public short ItemId;
        public DamageFilter Filter;
        public float BlockAmount;
    }
}
