using Unity.Netcode;

namespace AbsoluteZero.Core.Combat
{
    public static class CombatImpactFlags
    {
        public const byte Defense = 1 << 0;
        public const byte Damage = 1 << 1;
        public const byte Recovery = 1 << 2;
    }

    public struct CombatEventNetData : INetworkSerializable
    {
        public byte ActorSeat;
        public byte TargetSeat;
        public short ItemId;
        public byte EventType;
        public float ActorResultTemp;
        public float TargetResultTemp;
        public byte Flags;
        public short DefenseItemId;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ActorSeat);
            s.SerializeValue(ref TargetSeat);
            s.SerializeValue(ref ItemId);
            s.SerializeValue(ref EventType);
            s.SerializeValue(ref ActorResultTemp);
            s.SerializeValue(ref TargetResultTemp);
            s.SerializeValue(ref Flags);
            s.SerializeValue(ref DefenseItemId);
        }

        public static CombatEventNetData FromCombatEvent(CombatEvent evt)
        {
            return new CombatEventNetData
            {
                ActorSeat = (byte)evt.SourcePlayer,
                TargetSeat = (byte)evt.TargetPlayer,
                ItemId = evt.ItemId,
                EventType = (byte)evt.Type,
                ActorResultTemp = evt.UserResultTemp,
                TargetResultTemp = evt.TargetResultTemp,
                Flags = evt.ImpactFlags,
                DefenseItemId = evt.DefenseItemId
            };
        }

        public CombatEvent ToCombatEvent()
        {
            return new CombatEvent
            {
                Type = (Item.CombatEventType)EventType,
                SourcePlayer = ActorSeat,
                TargetPlayer = TargetSeat,
                ItemId = ItemId,
                UserResultTemp = ActorResultTemp,
                TargetResultTemp = TargetResultTemp,
                ImpactFlags = Flags,
                DefenseItemId = DefenseItemId
            };
        }
    }
}
