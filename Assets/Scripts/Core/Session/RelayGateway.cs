using System;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using UnityEngine;

namespace AbsoluteZero.Core.Session
{
    public sealed class RelayGateway : IRelayGateway
    {
        const string LogPrefix = "[RelayGateway]";

        public async Task<Result<RelayHostResult>> AllocateAsync(int maxConnections)
        {
            try
            {
                Unity.Services.Relay.Models.Allocation allocation;
                try
                {
                    allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
                }
                catch (RelayServiceException)
                {
                    Debug.LogWarning($"{LogPrefix} Default region failed, retrying asia-southeast1");
                    allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections, "asia-southeast1");
                }

                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                var serverData = new RelayServerData(allocation, "dtls");

                Debug.Log($"{LogPrefix} Allocated, JoinCode: {joinCode}");
                return Result<RelayHostResult>.Success(new RelayHostResult(serverData, joinCode));
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"{LogPrefix} Allocate failed: {e.Message}");
                return Result<RelayHostResult>.Failure(OperationErrorCode.RelayFailed, e.Message);
            }
        }

        public async Task<Result<RelayJoinResult>> JoinAsync(string joinCode)
        {
            try
            {
                var joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                var serverData = new RelayServerData(joinAllocation, "dtls");

                Debug.Log($"{LogPrefix} Joined relay: {joinCode}");
                return Result<RelayJoinResult>.Success(new RelayJoinResult(serverData));
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"{LogPrefix} Join failed: {e.Message}");
                return Result<RelayJoinResult>.Failure(OperationErrorCode.RelayFailed, e.Message);
            }
        }
    }
}
