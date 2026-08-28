using AbsoluteZero.Core.Network;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Match
{
    public class MatchManager : NetworkBehaviour
    {
        public readonly NetworkVariable<int> RoundNumber = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> P1RoundWins = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> P2RoundWins = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<MatchState> CurrentMatchState = new(
            MatchState.WaitingToStart, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        const int WINS_TO_MATCH = 2;

        public void StartRound()
        {
            if (!IsServer) return;

            RoundNumber.Value++;
            CurrentMatchState.Value = MatchState.RoundInProgress;
            Debug.Log($"[MatchManager] Round {RoundNumber.Value} started");
        }

        public void EndRound(int winnerIndex)
        {
            if (!IsServer || !IsSpawned) return;

            if (winnerIndex == 0)
                P1RoundWins.Value++;
            else if (winnerIndex == 1)
                P2RoundWins.Value++;

            CurrentMatchState.Value = MatchState.RoundEnd;

            OnRoundEndClientRpc(winnerIndex, RoundNumber.Value);
            Debug.Log($"[MatchManager] Round {RoundNumber.Value} ended — Winner: P{winnerIndex + 1} " +
                      $"(Wins: P1={P1RoundWins.Value}, P2={P2RoundWins.Value})");

            if (IsMatchComplete())
            {
                int matchWinner = P1RoundWins.Value >= WINS_TO_MATCH ? 0 : 1;
                CurrentMatchState.Value = MatchState.MatchComplete;
                OnMatchEndClientRpc(matchWinner);
                Debug.Log($"[MatchManager] Match complete — Winner: P{matchWinner + 1}");
            }
        }

        public bool IsMatchComplete()
        {
            return P1RoundWins.Value >= WINS_TO_MATCH || P2RoundWins.Value >= WINS_TO_MATCH;
        }

        public bool WouldEndMatch(int winnerIndex)
        {
            if (winnerIndex == 0)
                return P1RoundWins.Value + 1 >= WINS_TO_MATCH;
            if (winnerIndex == 1)
                return P2RoundWins.Value + 1 >= WINS_TO_MATCH;
            return false;
        }

        public bool IsRoundOver()
        {
            return CurrentMatchState.Value == MatchState.RoundEnd
                || CurrentMatchState.Value == MatchState.MatchComplete;
        }

        [Rpc(SendTo.Everyone)]
        void OnRoundEndClientRpc(int winnerIndex, int roundNumber)
        {
        }

        [Rpc(SendTo.Everyone)]
        void OnMatchEndClientRpc(int winnerIndex)
        {
        }
    }
}
