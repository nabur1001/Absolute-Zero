using UnityEngine;

namespace AbsoluteZero.Core.Item.Data
{
    [CreateAssetMenu(fileName = "New Sabotage Item", menuName = "AbsoluteZero/Items/Sabotage Item")]
    public class SabotageItemDataSO : ItemDataSO
    {
        [Header("Sabotage")]
        public SabotageType SabotageType;

        public override ItemEffectOutcome ComputeEffect(ItemContext ctx)
        {
            Debug.Log($"[COMBAT] SabotageItem '{ItemName}': P{ctx.UserIndex} → P{ctx.TargetIndex}, type={SabotageType}");

            var outcome = new ItemEffectOutcome();
            switch (SabotageType)
            {
                case SabotageType.Reroll:
                    Debug.Log($"[COMBAT] SabotageItem '{ItemName}': P{ctx.TargetIndex} random items rerolled");
                    outcome.InventoryAction = InventoryMutationType.RerollTarget;
                    break;
                case SabotageType.Steal:
                    Debug.Log($"[COMBAT] SabotageItem '{ItemName}': P{ctx.UserIndex} stole from P{ctx.TargetIndex}");
                    outcome.InventoryAction = InventoryMutationType.StealFromTarget;
                    break;
                case SabotageType.BlockBasic:
                    Debug.Log($"[COMBAT] SabotageItem '{ItemName}': P{ctx.TargetIndex} basic items BLOCKED next turn");
                    outcome.BlockTargetBasics = true;
                    break;
                case SabotageType.Neutralize:
                    Debug.Log($"[COMBAT] SabotageItem '{ItemName}': P{ctx.TargetIndex} main action neutralized");
                    outcome.NeutralizeTarget = true;
                    break;
            }
            return outcome;
        }
    }
}
