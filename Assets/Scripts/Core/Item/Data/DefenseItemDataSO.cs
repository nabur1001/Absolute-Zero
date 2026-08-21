using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Item.Data
{
    [CreateAssetMenu(fileName = "New Defense Item", menuName = "AbsoluteZero/Items/Defense Item")]
    public class DefenseItemDataSO : ItemDataSO
    {
        [Header("Defense")]
        public float BlockAmount;
        public DamageFilter Filter;

        public override ItemEffectOutcome ComputeEffect(ItemContext ctx)
        {
            Debug.Log($"[COMBAT] DefenseItem '{ItemName}': P{ctx.UserIndex} activated defense — filter={Filter}, block={BlockAmount}");
            return new ItemEffectOutcome
            {
                SetUserDefense = new DefenseInfo
                {
                    Filter = this.Filter,
                    BlockAmount = this.BlockAmount
                }
            };
        }
    }
}
