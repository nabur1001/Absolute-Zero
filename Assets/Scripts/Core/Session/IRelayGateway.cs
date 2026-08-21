using System.Threading.Tasks;

namespace AbsoluteZero.Core.Session
{
    public readonly struct RelayHostResult
    {
        public Unity.Networking.Transport.Relay.RelayServerData ServerData { get; }
        public string JoinCode { get; }

        public RelayHostResult(Unity.Networking.Transport.Relay.RelayServerData serverData, string joinCode)
        {
            ServerData = serverData;
            JoinCode = joinCode;
        }
    }

    public readonly struct RelayJoinResult
    {
        public Unity.Networking.Transport.Relay.RelayServerData ServerData { get; }

        public RelayJoinResult(Unity.Networking.Transport.Relay.RelayServerData serverData)
        {
            ServerData = serverData;
        }
    }

    public interface IRelayGateway
    {
        Task<Result<RelayHostResult>> AllocateAsync(int maxConnections);
        Task<Result<RelayJoinResult>> JoinAsync(string joinCode);
    }
}
