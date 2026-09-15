using Unity.Netcode;

namespace AbsoluteZero.Core.Match
{
    public struct MatchConfigNetData : INetworkSerializable
    {
        public byte Mode;
        public byte RequiredPlayerCount;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Mode);
            serializer.SerializeValue(ref RequiredPlayerCount);
        }
    }
}
