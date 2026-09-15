using UnityEngine;

namespace AbsoluteZero.Core.Item.Data
{
    public abstract class ItemDataSO : ScriptableObject
    {
        [Header("Basic Info")]
        public string ItemName;
        public string Description;
        public Sprite Icon;

        [Header("Classification")]
        public ItemCategory Category;
        public ItemPersistence Persistence;
        public ItemSlotType SlotType;

        public bool IsInstantUse => SlotType == ItemSlotType.Sub;

        [Header("Usage")]
        public int MaxUses = 1;

        [Header("Drop")]
        public float DropWeight;

        [Header("Special Behavior")]
        public bool IsFreeAction;

        [Header("Animation")]
        public string AnimTrigger;
        public string OpponentAnimTrigger;

        [Header("Animation Timing")]
        public float AnimDuration;
        public float EffectDelay = 0.5f;
        public int EffectHitCount = 1;
        public float EffectInterval;

        [Header("Mini-Game")]
        public bool RequiresMiniGame;
        public MiniGameType MiniGameType;
        public float MiniGameTimeLimit;
        public int MiniGameGoal = 1;

        public virtual void ExecuteEffect(ItemContext ctx)
        {
            var outcome = ComputeEffect(ctx);
            ItemEffectApplicator.Apply(ctx, outcome);
        }

        public virtual TargetMode GetTargetMode() => TargetMode.SingleTarget;

        public virtual ItemEffectOutcome ComputeEffect(ItemContext ctx) => default;

        public virtual bool CanUse(ItemContext ctx)
        {
            if (ctx.User.IsBasicBlocked.Value && SlotType == ItemSlotType.Main)
                return false;
            return true;
        }
    }
}
