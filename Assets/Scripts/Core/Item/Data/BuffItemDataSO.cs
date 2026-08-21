using UnityEngine;

namespace AbsoluteZero.Core.Item.Data
{
    [CreateAssetMenu(fileName = "New Buff Item", menuName = "AbsoluteZero/Items/Buff Item")]
    public class BuffItemDataSO : ItemDataSO
    {
        [Header("Buff (self)")]
        public float ImmediateTempDelta;
        public float DelayedTempDelta;
        public int DelayTurns = 1;

        public override ItemEffectOutcome ComputeEffect(ItemContext ctx)
        {
            Debug.Log($"[COMBAT] BuffItem '{ItemName}': P{ctx.UserIndex} self-buff, immediate={ImmediateTempDelta}, delayed={DelayedTempDelta} in {DelayTurns}t");

            var outcome = new ItemEffectOutcome();

            if (!Mathf.Approximately(ImmediateTempDelta, 0f))
            {
                if (ImmediateTempDelta > 0f)
                {
                    Debug.Log($"[COMBAT] BuffItem '{ItemName}': immediate HEAL self +{ImmediateTempDelta}");
                    outcome.UserHeal = ImmediateTempDelta;
                }
                else
                {
                    Debug.Log($"[COMBAT] BuffItem '{ItemName}': immediate DAMAGE self {ImmediateTempDelta}");
                    outcome.UserDamage = -ImmediateTempDelta;
                    outcome.UserDamageFilter = DamageFilter.All;
                }
            }

            if (!Mathf.Approximately(DelayedTempDelta, 0f))
            {
                Debug.Log($"[COMBAT] BuffItem '{ItemName}': scheduled delayed={DelayedTempDelta} on P{ctx.UserIndex} in {DelayTurns} turn(s)");
                outcome.HasScheduledEffect = true;
                outcome.ScheduledTargetIndex = ctx.UserIndex;
                outcome.ScheduledType = EffectType.TempChange;
                outcome.ScheduledValue = DelayedTempDelta;
                outcome.ScheduledDelayTurns = DelayTurns;
            }

            return outcome;
        }
    }
}
