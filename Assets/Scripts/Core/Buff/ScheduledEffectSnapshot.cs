using AbsoluteZero.Core.Item;

namespace AbsoluteZero.Core.Buff
{
    public readonly struct ScheduledEffectSnapshot
    {
        public readonly byte TargetSeat;
        public readonly byte SourceSeat;
        public readonly EffectType Type;
        public readonly float Value;
        public readonly int TurnsRemaining;

        public ScheduledEffectSnapshot(byte targetSeat, byte sourceSeat, EffectType type, float value, int turnsRemaining)
        {
            TargetSeat = targetSeat;
            SourceSeat = sourceSeat;
            Type = type;
            Value = value;
            TurnsRemaining = turnsRemaining;
        }
    }
}
