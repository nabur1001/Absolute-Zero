namespace AbsoluteZero.UI.Game.Bridge
{
    public struct SeatSnapshot
    {
        public byte SeatIndex;
        public ulong ClientId;
        public float Temperature;
        public float FanSpeed;
        public bool IsReady;
        public bool IsFanActive;
        public bool IsFanUpgraded;
        public bool IsBasicBlocked;
        public bool HasSelectedItem;
        public bool IsLocal;
    }
}
