using System.Threading.Tasks;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using AbsoluteZero.Core.Player.Identity;
using AbsoluteZero.Core.Session;
using AbsoluteZero.UI.MiniGame;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.UI.Game.Bridge
{
    public sealed class LocalPlayerCommandAdapter : ILocalPlayerCommands
    {
        readonly IReadOnlyPlayerRegistry _registry;
        PlayerState _localPlayer;

        public LocalPlayerCommandAdapter(IReadOnlyPlayerRegistry registry)
        {
            _registry = registry;
            _registry.Registered += OnRegistered;
            _registry.Unregistered += OnUnregistered;
            RebindLocal();
        }

        public void Dispose()
        {
            _registry.Registered -= OnRegistered;
            _registry.Unregistered -= OnUnregistered;
            _localPlayer = null;
        }

        void OnRegistered(PlayerBinding _) => RebindLocal();
        void OnUnregistered(PlayerIdentity _) => RebindLocal();

        void RebindLocal()
        {
            _localPlayer = null;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (_registry.TryGetByClientId(nm.LocalClientId, out var binding))
                _localPlayer = binding.State;
        }

        public bool TrySelectItem(byte slotIndex)
        {
            return TrySelectItemWithTarget(slotIndex, Core.Player.ActionIntent.NoTarget);
        }

        public bool TrySelectItemWithTarget(byte slotIndex, byte targetSeat)
        {
            if (_localPlayer == null)
            {
                Debug.LogWarning("[CommandAdapter] TrySelectItem: no local player bound");
                return false;
            }

            if (_localPlayer.IsReady.Value) return false;
            if (MiniGameHub.IsRunning) return false;
            if (_localPlayer.HasSelectedItem.Value) return false;

            _localPlayer.SelectItemServerRpc(slotIndex, targetSeat);
            return true;
        }

        public bool TryCancelSelection()
        {
            if (_localPlayer == null || _localPlayer.IsReady.Value || !_localPlayer.HasSelectedItem.Value)
                return false;

            _localPlayer.CancelSelectionServerRpc();
            return true;
        }

        public void PressReady()
        {
            if (_localPlayer == null)
            {
                Debug.LogWarning("[CommandAdapter] PressReady: no local player bound");
                return;
            }
            _localPlayer.PressReadyServerRpc();
        }

        public void UseGhostSkill(byte skillIndex, byte targetSeat)
        {
            var tm = Object.FindAnyObjectByType<Core.Turn.TurnManager>();
            if (tm == null)
            {
                Debug.LogWarning("[CommandAdapter] UseGhostSkill: TurnManager not found");
                return;
            }
            tm.UseGhostSkillRpc(skillIndex, targetSeat);
        }

        public async Task LeaveMatchAsync()
        {
            var coordinator = Object.FindAnyObjectByType<NetworkSessionCoordinator>();
            if (coordinator == null)
            {
                Debug.LogError("[CommandAdapter] LeaveMatchAsync: NetworkSessionCoordinator not found");
                return;
            }

            try
            {
                await coordinator.LeaveAsync();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        public void SubmitRematchDecision(bool accept, uint voteEpoch)
        {
            var mcr = MatchCompositionRoot.Instance;
            if (mcr == null || mcr.MatchManager == null)
            {
                Debug.LogWarning("[CommandAdapter] SubmitRematchDecision: MatchManager not available");
                return;
            }
            mcr.MatchManager.SubmitRematchDecisionRpc(accept, voteEpoch);
        }
    }
}
