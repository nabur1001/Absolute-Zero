namespace AbsoluteZero.Core.Item
{
    public readonly struct SlotSnapshot
    {
        public readonly short ItemId;
        public readonly bool IsUnlimited;
        public readonly byte RemainingUses;

        public SlotSnapshot(short itemId, bool isUnlimited, byte remainingUses)
        {
            ItemId = itemId;
            IsUnlimited = isUnlimited;
            RemainingUses = remainingUses;
        }
    }

    public readonly struct InventorySnapshot
    {
        public readonly byte SeatIndex;
        public readonly SlotSnapshot[] Slots;

        public InventorySnapshot(byte seatIndex, SlotSnapshot[] slots)
        {
            SeatIndex = seatIndex;
            Slots = slots;
        }
    }
}
