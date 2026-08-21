using UnityEngine;

namespace AbsoluteZero.Core.Item.Data
{
    [CreateAssetMenu(fileName = "New Attack Item", menuName = "AbsoluteZero/Items/Attack Item")]
    public class AttackItemDataSO : ItemDataSO
    {
        [Header("Attack")]
        public float Damage;
        public DamageFilter AttackFilter = DamageFilter.Temperature;

        [Header("Special Mode")]
        public bool EqualizeToUserTemp;

        public override ItemEffectOutcome ComputeEffect(ItemContext ctx)
        {
            var outcome = new ItemEffectOutcome();

            if (EqualizeToUserTemp)
            {
                float diff = ctx.Target.Temperature.Value - ctx.User.Temperature.Value;
                Debug.Log($"[COMBAT] AttackItem '{ItemName}': EQUALIZE mode — P{ctx.UserIndex}({ctx.User.Temperature.Value:F1}°) → P{ctx.TargetIndex}({ctx.Target.Temperature.Value:F1}°), diff={diff:F1}");
                if (diff > 0f)
                {
                    outcome.TargetDamage = diff;
                    outcome.TargetDamageFilter = AttackFilter;
                    outcome.TargetDefenseCheck = ctx.TargetModifiers.ActiveDefense;
                }
                else if (diff < 0f)
                {
                    outcome.TargetHeal = -diff;
                }
                else
                {
                    Debug.Log($"[COMBAT] AttackItem '{ItemName}': EQUALIZE — same temp, no effect");
                }
                return outcome;
            }

            Debug.Log($"[COMBAT] AttackItem '{ItemName}': P{ctx.UserIndex} → P{ctx.TargetIndex}, damage={Damage}, filter={AttackFilter}");
            outcome.TargetDamage = Damage;
            outcome.TargetDamageFilter = AttackFilter;
            outcome.TargetDefenseCheck = ctx.TargetModifiers.ActiveDefense;
            return outcome;
        }
    }
}
