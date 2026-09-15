namespace AbsoluteZero.Core.Player
{
    public readonly struct ActionIntent
    {
        public readonly byte SourceSeat;
        public readonly byte SlotIndex;
        public readonly short ItemId;
        public readonly byte TargetSeat;
        public readonly int ReadyServerTick;
        readonly bool _isValid;

        public const byte NoTarget = DamageSource.InvalidSeat;

        public bool HasTarget => _isValid && TargetSeat != NoTarget;
        public bool IsEmpty => !_isValid;

        public static readonly ActionIntent Empty = default;

        public ActionIntent(byte sourceSeat, byte slotIndex, short itemId, byte targetSeat, int readyServerTick)
        {
            SourceSeat = sourceSeat;
            SlotIndex = slotIndex;
            ItemId = itemId;
            TargetSeat = targetSeat;
            ReadyServerTick = readyServerTick;
            _isValid = true;
        }

        public override string ToString()
            => _isValid
                ? $"Intent(seat={SourceSeat}, slot={SlotIndex}, item={ItemId}, target={TargetSeat}, tick={ReadyServerTick})"
                : "Intent(Empty)";
    }
}
