using Unity.Networking.Transport.Relay;

namespace AbsoluteZero.Core.Session
{
    public interface INetworkRuntime
    {
        bool IsListening { get; }
        bool IsHost { get; }
        bool IsClient { get; }
        ulong LocalClientId { get; }
        int ConnectedClientCount { get; }

        Result<Unit> StartHost(RelayServerData relayData);
        Result<Unit> StartClient(RelayServerData relayData);
        void Shutdown();
        Result<Unit> LoadNetworkScene(string sceneName);
    }
}
