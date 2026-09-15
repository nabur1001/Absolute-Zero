using UnityEngine;

namespace AbsoluteZero.Core.Item.Data
{
    [CreateAssetMenu(fileName = "New Recovery Item", menuName = "AbsoluteZero/Items/Recovery Item")]
    public class RecoveryItemDataSO : ItemDataSO
    {
        [Header("Recovery")]
        public float[] HealPerUse = { 7f };

        public override TargetMode GetTargetMode() => TargetMode.Self;

        public override ItemEffectOutcome ComputeEffect(ItemContext ctx)
        {
            int useIndex = MaxUses > 0 ? MaxUses - ctx.UserSlot.RemainingUses : 0;
            float heal = HealPerUse[Mathf.Clamp(useIndex, 0, HealPerUse.Length - 1)];
            Debug.Log($"[COMBAT] RecoveryItem '{ItemName}': P{ctx.UserIndex} heal={heal}, useIndex={useIndex}, remaining={ctx.UserSlot.RemainingUses}/{MaxUses}");
            return new ItemEffectOutcome { UserHeal = heal };
        }
    }
}
