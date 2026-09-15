using Unity.Netcode;

namespace AbsoluteZero.Core.Match
{
    public struct GhostCooldownNetData : INetworkSerializable, System.IEquatable<GhostCooldownNetData>
    {
        public byte Seat;
        public byte Skill;
        public byte RemainingTurns;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Seat);
            s.SerializeValue(ref Skill);
            s.SerializeValue(ref RemainingTurns);
        }

        public bool Equals(GhostCooldownNetData other)
            => Seat == other.Seat && Skill == other.Skill && RemainingTurns == other.RemainingTurns;

        public override int GetHashCode() => (Seat << 16) | (Skill << 8) | RemainingTurns;
    }
}
