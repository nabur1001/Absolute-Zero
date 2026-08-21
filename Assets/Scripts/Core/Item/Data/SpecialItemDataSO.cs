using UnityEngine;

namespace AbsoluteZero.Core.Item.Data
{
    [CreateAssetMenu(fileName = "New Special Item", menuName = "AbsoluteZero/Items/Special Item")]
    public class SpecialItemDataSO : ItemDataSO
    {
        [Header("Special")]
        public SpecialEffectType SpecialEffect;
        public float EffectValue;
        public bool TargetsSelf;
        public int DelayTurns;

        public override bool CanUse(ItemContext ctx)
        {
            if (!base.CanUse(ctx)) return false;
            if (SpecialEffect == SpecialEffectType.RevealOpponent && !ctx.Target.IsReady.Value)
                return false;
            return true;
        }

        public override ItemEffectOutcome ComputeEffect(ItemContext ctx)
        {
            Debug.Log($"[COMBAT] SpecialItem '{ItemName}': P{ctx.UserIndex}, effect={SpecialEffect}, value={EffectValue}, targetsSelf={TargetsSelf}");

            var outcome = new ItemEffectOutcome();
            switch (SpecialEffect)
            {
                case SpecialEffectType.FanSpeedChange:
                    int targetIdx = TargetsSelf ? ctx.UserIndex : ctx.TargetIndex;
                    if (DelayTurns > 0)
                    {
                        Debug.Log($"[COMBAT] SpecialItem '{ItemName}': scheduled FanSpeed={EffectValue} on P{targetIdx} in {DelayTurns}t");
                        outcome.HasScheduledEffect = true;
                        outcome.ScheduledTargetIndex = targetIdx;
                        outcome.ScheduledType = EffectType.FanSpeedChange;
                        outcome.ScheduledValue = EffectValue;
                        outcome.ScheduledDelayTurns = DelayTurns;
                    }
                    else
                    {
                        Debug.Log($"[COMBAT] SpecialItem '{ItemName}': immediate FanSpeed={EffectValue} on P{targetIdx}");
                        if (TargetsSelf)
                        {
                            outcome.WriteUserFanSpeed = true;
                            outcome.UserFanSpeedValue = EffectValue;
                        }
                        else
                        {
                            outcome.WriteTargetFanSpeed = true;
                            outcome.TargetFanSpeedValue = EffectValue;
                        }
                    }
                    break;

                case SpecialEffectType.ExtraAction:
                    Debug.Log($"[COMBAT] SpecialItem '{ItemName}': P{ctx.UserIndex} granted ExtraAction");
                    outcome.GrantExtraAction = true;
                    break;

                case SpecialEffectType.RevealOpponent:
                    Debug.Log($"[COMBAT] SpecialItem '{ItemName}': P{ctx.UserIndex} revealed opponent");
                    outcome.RevealOpponent = true;
                    break;
            }
            return outcome;
        }
    }
}
