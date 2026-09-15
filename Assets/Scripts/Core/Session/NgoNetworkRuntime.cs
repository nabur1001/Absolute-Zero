using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AbsoluteZero.Core.Session
{
    public sealed class NgoNetworkRuntime : INetworkRuntime
    {
        const string LogPrefix = "[NetworkRuntime]";

        NetworkManager Nm => NetworkManager.Singleton;

        public bool IsListening => Nm != null && Nm.IsListening;
        public bool IsHost => Nm != null && Nm.IsHost;
        public bool IsClient => Nm != null && Nm.IsClient;
        public ulong LocalClientId => Nm?.LocalClientId ?? 0;
        public int ConnectedClientCount => Nm?.ConnectedClientsList?.Count ?? 0;

        public Result<Unit> StartHost(RelayServerData relayData)
        {
            var nm = Nm;
            if (nm == null)
                return Result<Unit>.Failure(OperationErrorCode.NetworkStartFailed, "NetworkManager not found");

            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null)
                return Result<Unit>.Failure(OperationErrorCode.NetworkStartFailed, "UnityTransport not found");

            transport.SetRelayServerData(relayData);

            if (!nm.StartHost())
                return Result<Unit>.Failure(OperationErrorCode.NetworkStartFailed, "StartHost returned false");

            Debug.Log($"{LogPrefix} Host started");
            return Result<Unit>.Success(Unit.Value);
        }

        public Result<Unit> StartClient(RelayServerData relayData)
        {
            var nm = Nm;
            if (nm == null)
                return Result<Unit>.Failure(OperationErrorCode.NetworkStartFailed, "NetworkManager not found");

            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null)
                return Result<Unit>.Failure(OperationErrorCode.NetworkStartFailed, "UnityTransport not found");

            // The host requires approval data in the NGO connection handshake.
            // This must match before StartClient serializes its request.
            nm.NetworkConfig.ConnectionApproval = true;
            transport.SetRelayServerData(relayData);

            if (!nm.StartClient())
                return Result<Unit>.Failure(OperationErrorCode.NetworkStartFailed, "StartClient returned false");

            Debug.Log($"{LogPrefix} Client started");
            return Result<Unit>.Success(Unit.Value);
        }

        public void Shutdown()
        {
            var nm = Nm;
            if (nm != null && nm.IsListening)
            {
                nm.Shutdown();
                Debug.Log($"{LogPrefix} Shutdown");
            }
        }

        public Result<Unit> LoadNetworkScene(string sceneName)
        {
            var nm = Nm;
            if (nm == null)
                return Result<Unit>.Failure(OperationErrorCode.InvalidState, "NetworkManager not found");

            if (!nm.IsHost)
                return Result<Unit>.Failure(OperationErrorCode.InvalidState, "Only host can load scenes");

            if (nm.SceneManager == null)
                return Result<Unit>.Failure(OperationErrorCode.InvalidState, "SceneManager not available");

            var status = nm.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
                return Result<Unit>.Failure(OperationErrorCode.InvalidState, $"Scene load failed: {status}");

            Debug.Log($"{LogPrefix} Loading scene: {sceneName}");
            return Result<Unit>.Success(Unit.Value);
        }
    }
}
