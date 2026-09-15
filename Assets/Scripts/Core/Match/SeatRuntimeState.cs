using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Player;

namespace AbsoluteZero.Core.Match
{
    public struct SeatRuntimeState
    {
        public byte SeatIndex;
        public float Temperature;
        public LifeState CurrentLifeState;
        public float FanSpeed;
        public bool IsFanActive;
        public bool IsFanUpgraded;
        public bool IsBasicBlocked;
        public PlayerModifiers Modifiers;
        public ItemSlotNetData[] InventorySlots;
        public DamageSource LastDamageSource;

        public static SeatRuntimeState CreateDefault(byte seat) => new()
        {
            SeatIndex = seat,
            Temperature = 37f,
            CurrentLifeState = LifeState.Alive,
            FanSpeed = 1f,
            LastDamageSource = DamageSource.None
        };
    }
}
