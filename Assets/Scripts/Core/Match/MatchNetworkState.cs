using System;
using AbsoluteZero.Core.Network;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

namespace AbsoluteZero.Core.Match
{
    public class MatchNetworkState : NetworkBehaviour
    {
        public readonly NetworkVariable<FixedString128Bytes> InitializationError = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public readonly NetworkVariable<MatchConfigNetData> Config = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<MultiTerminalResultNetData> TerminalResult = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkList<int> KillScores { get; private set; }
        public NetworkList<GhostCooldownNetData> GhostCooldowns { get; private set; }

        public event Action<MatchConfigNetData> OnConfigSynced;

        void Awake()
        {
            KillScores = new NetworkList<int>();
            GhostCooldowns = new NetworkList<GhostCooldownNetData>();
        }

        public override void OnNetworkSpawn()
        {
            Config.OnValueChanged += OnConfigChanged;
            KillScores.OnListChanged += OnKillScoresChanged;
            GhostCooldowns.OnListChanged += OnGhostCooldownsChanged;

            if ((GameMode)Config.Value.Mode != GameMode.None)
                OnConfigReceived(Config.Value);

            if (KillScores.Count > 0)
                RefreshKillScoresFromCurrentState();
        }

        public override void OnNetworkDespawn()
        {
            Config.OnValueChanged -= OnConfigChanged;
            KillScores.OnListChanged -= OnKillScoresChanged;
            GhostCooldowns.OnListChanged -= OnGhostCooldownsChanged;
        }

        public override void OnDestroy()
        {
            KillScores?.Dispose();
            GhostCooldowns?.Dispose();
            base.OnDestroy();
        }

        // ─── Server API ──────────────────────────────────────

        public bool ServerInitialize(MatchConfigNetData configData)
        {
            if (!IsSpawned || !IsServer) return false;

            if (Config.Value.RequiredPlayerCount > 0)
                return Config.Value.Mode == configData.Mode
                    && Config.Value.RequiredPlayerCount == configData.RequiredPlayerCount;

            int playerCount = configData.RequiredPlayerCount;
            if (playerCount < 1 || playerCount > 4)
            {
                Debug.LogError($"[MatchNetworkState] Invalid RequiredPlayerCount: {playerCount}");
                return false;
            }

            KillScores.Clear();
            for (int i = 0; i < playerCount; i++)
                KillScores.Add(0);

            TerminalResult.Value = default;
            Config.Value = configData;

            Debug.Log($"[MatchNetworkState] Server initialized — Mode={(GameMode)configData.Mode}, Players={playerCount}");
            return true;
        }

        public void ServerAddKill(int seatIndex)
        {
            if (!IsServer) return;
            if (seatIndex < 0 || seatIndex >= KillScores.Count) return;

            KillScores[seatIndex]++;
        }

        public void ServerResetKillScores()
        {
            if (!IsServer) return;
            for (int i = 0; i < KillScores.Count; i++)
                KillScores[i] = 0;
        }

        public void ServerSetTerminalResult(uint decidingSequence, MultiMatchOutcome outcome,
            byte winnerMask, bool released)
        {
            if (!IsServer || decidingSequence == 0 || winnerMask == 0) return;
            TerminalResult.Value = new MultiTerminalResultNetData
            {
                DecidingSequence = decidingSequence,
                Outcome = outcome,
                WinnerMask = winnerMask,
                Released = released
            };
        }

        public void ServerClearTerminalResult()
        {
            if (!IsServer) return;
            TerminalResult.Value = default;
        }

        // ─── Change Handlers ─────────────────────────────────

        void OnConfigChanged(MatchConfigNetData prev, MatchConfigNetData current)
        {
            OnConfigReceived(current);
        }

        void OnConfigReceived(MatchConfigNetData data)
        {
            Debug.Log($"[MatchNetworkState] Config received — Mode={(GameMode)data.Mode}, RequiredPlayers={data.RequiredPlayerCount}");
            OnConfigSynced?.Invoke(data);
        }

        void OnKillScoresChanged(NetworkListEvent<int> changeEvent)
        {
        }

        void OnGhostCooldownsChanged(NetworkListEvent<GhostCooldownNetData> changeEvent)
        {
        }

        public void ServerSetCooldown(byte seat, byte skill, byte remainingTurns)
        {
            if (!IsServer) return;
            for (int i = 0; i < GhostCooldowns.Count; i++)
            {
                var cd = GhostCooldowns[i];
                if (cd.Seat == seat && cd.Skill == skill)
                {
                    if (remainingTurns == 0)
                        GhostCooldowns.RemoveAt(i);
                    else
                        GhostCooldowns[i] = new GhostCooldownNetData
                            { Seat = seat, Skill = skill, RemainingTurns = remainingTurns };
                    return;
                }
            }
            if (remainingTurns > 0)
                GhostCooldowns.Add(new GhostCooldownNetData
                    { Seat = seat, Skill = skill, RemainingTurns = remainingTurns });
        }

        public byte ServerGetCooldown(byte seat, byte skill)
        {
            for (int i = 0; i < GhostCooldowns.Count; i++)
            {
                var cd = GhostCooldowns[i];
                if (cd.Seat == seat && cd.Skill == skill)
                    return cd.RemainingTurns;
            }
            return 0;
        }

        public void ServerTickAllCooldowns()
        {
            if (!IsServer) return;
            for (int i = GhostCooldowns.Count - 1; i >= 0; i--)
            {
                var cd = GhostCooldowns[i];
                if (cd.RemainingTurns <= 1)
                    GhostCooldowns.RemoveAt(i);
                else
                    GhostCooldowns[i] = new GhostCooldownNetData
                        { Seat = cd.Seat, Skill = cd.Skill, RemainingTurns = (byte)(cd.RemainingTurns - 1) };
            }
        }

        public void ServerClearAllCooldowns()
        {
            if (!IsServer) return;
            GhostCooldowns.Clear();
        }

        void RefreshKillScoresFromCurrentState()
        {
            for (int i = 0; i < KillScores.Count; i++)
                Debug.Log($"[MatchNetworkState] Initial KillScore[{i}]={KillScores[i]}");
        }
    }
}
