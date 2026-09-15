namespace AbsoluteZero.Core.Player
{
    public enum DamageOrigin : byte
    {
        Natural = 0,
        Item = 1,
        GhostChill = 2,
        GhostFrost = 3,
        DelayedEffect = 4
    }

    public readonly struct DamageSource
    {
        public readonly byte AttackerSeat;
        public readonly DamageOrigin Origin;

        public bool IsNone => AttackerSeat == InvalidSeat;

        public const byte InvalidSeat = 255;
        public static readonly DamageSource None = new(InvalidSeat, DamageOrigin.Natural);

        DamageSource(byte attackerSeat, DamageOrigin origin)
        {
            AttackerSeat = attackerSeat;
            Origin = origin;
        }

        public static DamageSource Create(byte attackerSeat, DamageOrigin origin)
        {
            if (attackerSeat == InvalidSeat)
                return None;
            return new DamageSource(attackerSeat, origin);
        }
    }
}
