using UnityEngine;

namespace AbsoluteZero.Core.Item.Data
{
    [CreateAssetMenu(fileName = "New Debuff Item", menuName = "AbsoluteZero/Items/Debuff Item")]
    public class DebuffItemDataSO : ItemDataSO
    {
        [Header("Debuff (opponent)")]
        public float ImmediateTempDelta;
        public float DelayedTempDelta;
        public int DelayTurns = 1;
        public DamageFilter AttackFilter = DamageFilter.Food;

        public override ItemEffectOutcome ComputeEffect(ItemContext ctx)
        {
            Debug.Log($"[COMBAT] DebuffItem '{ItemName}': P{ctx.UserIndex} → P{ctx.TargetIndex}, immediate={ImmediateTempDelta}, delayed={DelayedTempDelta} in {DelayTurns}t, filter={AttackFilter}");

            var defense = ctx.TargetModifiers.ActiveDefense;
            if (defense.HasValue &&
                (defense.Value.Filter == AttackFilter || defense.Value.Filter == DamageFilter.All))
            {
                Debug.Log($"[COMBAT] DebuffItem '{ItemName}': FULLY BLOCKED by defense (defFilter={defense.Value.Filter})");
                return new ItemEffectOutcome { Blocked = true, TargetDefenseCheck = defense };
            }

            var outcome = new ItemEffectOutcome();

            if (!Mathf.Approximately(ImmediateTempDelta, 0f))
            {
                if (ImmediateTempDelta > 0f)
                {
                    Debug.Log($"[COMBAT] DebuffItem '{ItemName}': immediate HEAL target +{ImmediateTempDelta}");
                    outcome.TargetHeal = ImmediateTempDelta;
                }
                else
                {
                    Debug.Log($"[COMBAT] DebuffItem '{ItemName}': immediate DAMAGE target {ImmediateTempDelta}");
                    outcome.TargetDamage = -ImmediateTempDelta;
                    outcome.TargetDamageFilter = AttackFilter;
                }
            }

            if (!Mathf.Approximately(DelayedTempDelta, 0f))
            {
                Debug.Log($"[COMBAT] DebuffItem '{ItemName}': scheduled delayed={DelayedTempDelta} on P{ctx.TargetIndex} in {DelayTurns} turn(s)");
                outcome.HasScheduledEffect = true;
                outcome.ScheduledTargetIndex = ctx.TargetIndex;
                outcome.ScheduledType = EffectType.TempChange;
                outcome.ScheduledValue = DelayedTempDelta;
                outcome.ScheduledDelayTurns = DelayTurns;
            }

            return outcome;
        }
    }
}
