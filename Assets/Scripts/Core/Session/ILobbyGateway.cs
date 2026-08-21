using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace AbsoluteZero.Core.Session
{
    public interface ILobbyGateway
    {
        Task<Result<Lobby>> CreateAsync(string name, int maxPlayers, CreateLobbyOptions options);
        Task<Result<Lobby>> JoinByCodeAsync(string code, JoinLobbyByCodeOptions options);
        Task<Result<Lobby>> JoinByIdAsync(string id, JoinLobbyByIdOptions options);
        Task<Result<Lobby>> QuickJoinAsync(QuickJoinLobbyOptions options);
        Task<Result<List<Lobby>>> QueryAsync(QueryLobbiesOptions options);
        Task<Result<Lobby>> GetAsync(string lobbyId);
        Task<Result<Lobby>> UpdateAsync(string lobbyId, UpdateLobbyOptions options);
        Task<Result<Lobby>> UpdatePlayerAsync(string lobbyId, string playerId, UpdatePlayerOptions options);
        Task<Result<Unit>> DeleteAsync(string lobbyId);
        Task<Result<Unit>> RemovePlayerAsync(string lobbyId, string playerId);
        Task<Result<Unit>> SendHeartbeatAsync(string lobbyId);
    }
}
