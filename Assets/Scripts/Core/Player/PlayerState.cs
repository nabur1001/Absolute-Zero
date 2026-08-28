using System;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Emote;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player.Identity;
using AbsoluteZero.Core.Turn;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Player
{
    public class PlayerState : NetworkBehaviour
    {
        public readonly NetworkVariable<float> Temperature = new(
            37f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<float> FanSpeed = new(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> IsReady = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> IsFanActive = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> SyncedPlayerIndex = new(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> HasSelectedItem = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> IsFanUpgraded = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> IsBasicBlocked = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public static event Action<Transform, Vector3, byte> OnEmoteRequested;

        readonly ActionQueue _actionQueue = new();
        PlayerInventory _inventory;
        PlayerIdentity? _cachedIdentity;
        ITurnContext _turnContext;

        const float MINIGAME_GRACE_SEC = 0.5f;
        int _pendingMiniGameSlot = -1;
        double _pendingMiniGameDeadline;

        public event System.Action<byte, MiniGameType, float, int> OnMiniGameStart;

        public int PlayerIndex => SyncedPlayerIndex.Value;
        public ActionQueue GetActionQueue() => _actionQueue;

        public PlayerInventory GetInventory()
        {
            if (_inventory == null)
                _inventory = GetComponent<PlayerInventory>();
            return _inventory;
        }

        public void Initialize(int playerIndex, PlayerInventory inventory)
        {
            SyncedPlayerIndex.Value = playerIndex;
            _inventory = inventory;
        }

        public void BindTurnContext(ITurnContext ctx) => _turnContext = ctx;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            var mcr = MatchCompositionRoot.Instance;
            if (mcr == null)
            {
                Debug.LogWarning("[PlayerState] MatchCompositionRoot not found — skipping registry");
                return;
            }

            var binding = new PlayerBinding(this, GetInventory(), NetworkObject);
            mcr.WritableRegistry.RegisterPending(OwnerClientId, binding);

            SyncedPlayerIndex.OnValueChanged += OnIndexAssigned;

            if (SyncedPlayerIndex.Value >= 0)
                OnIndexAssigned(-1, SyncedPlayerIndex.Value);
        }

        void OnIndexAssigned(int prev, int cur)
        {
            if (cur < 0) return;
            if (_cachedIdentity.HasValue) return;

            var identity = new PlayerIdentity((byte)cur, OwnerClientId);
            _cachedIdentity = identity;

            MatchCompositionRoot.Instance?.WritableRegistry
                .PromoteToReady(OwnerClientId, (byte)cur);
        }

        public override void OnNetworkDespawn()
        {
            SyncedPlayerIndex.OnValueChanged -= OnIndexAssigned;

            var registry = MatchCompositionRoot.Instance?.WritableRegistry;
            if (registry != null)
            {
                if (_cachedIdentity.HasValue)
                    registry.Unregister(_cachedIdentity.Value);
                else
                    registry.UnregisterByClientId(OwnerClientId);
            }

            _cachedIdentity = null;
            base.OnNetworkDespawn();
        }

        public void ResetForNewTurn()
        {
            HasSelectedItem.Value = false;
            _actionQueue.Clear();
            _pendingMiniGameSlot = -1;
        }

        ItemContext BuildContext()
        {
            int myIndex = SyncedPlayerIndex.Value;

            PlayerState opponent = null;
            var mcr = MatchCompositionRoot.Instance;
            if (mcr != null)
            {
                foreach (var p in mcr.Registry.Players)
                {
                    if (p.Identity.PlayerIndex != (byte)myIndex)
                    {
                        opponent = p.State;
                        break;
                    }
                }
            }
            opponent ??= _turnContext.GetPlayer(myIndex == 0 ? 1 : 0);

            return new ItemContext
            {
                User = this,
                Target = opponent,
                UserIndex = myIndex,
                TargetIndex = opponent.PlayerIndex,
                UserInventory = _inventory,
                TargetInventory = opponent.GetInventory(),
                AllModifiers = _turnContext.GetModifiers(),
                TempSystem = _turnContext.GetTempSystem(),
                BuffSystem = _turnContext.GetBuffSystem(),
                DropTable = _turnContext.GetDropTable(),
            };
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SelectItemServerRpc(byte slotIndex, RpcParams rpcParams = default)
        {
            if (!IsServer) return;
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (_turnContext == null || _turnContext.Phase != TurnPhase.PrepPhase) return;
            if (IsReady.Value) return;
            if (_pendingMiniGameSlot >= 0)
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] SelectItem rejected: mini-game in progress");
                return;
            }
            if (slotIndex >= _inventory.SlotStates.Count) return;

            var slot = _inventory.SlotStates[slotIndex];
            if (!slot.IsUsable)
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] SelectItem rejected: slot {slotIndex} not usable");
                return;
            }

            var itemData = _inventory.GetItemData(slotIndex);
            if (itemData == null) return;

            var ctx = BuildContext();
            ctx.UserSlot = slot;
            ctx.SlotIndex = slotIndex;
            if (!itemData.CanUse(ctx))
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] SelectItem rejected: CanUse false");
                return;
            }

            if (HasSelectedItem.Value)
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] SelectItem rejected: already selected an item this turn");
                return;
            }

            if (itemData.RequiresMiniGame)
            {
                double now = NetworkManager.ServerTime.Time;
                double prepEnd = _turnContext.PrepStartTime + _turnContext.PrepDurationSeconds;
                _pendingMiniGameSlot = slotIndex;
                _pendingMiniGameDeadline = System.Math.Min(now + itemData.MiniGameTimeLimit, prepEnd) + MINIGAME_GRACE_SEC;

                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Mini-game START: {itemData.ItemName} " +
                          $"({itemData.MiniGameType}, {itemData.MiniGameTimeLimit}s, goal={itemData.MiniGameGoal})");

                StartMiniGameClientRpc(slotIndex, (byte)itemData.MiniGameType,
                                       itemData.MiniGameTimeLimit, itemData.MiniGameGoal);
                return;
            }

            ServerQueueItem(slotIndex, itemData);
        }

        void ServerQueueItem(byte slotIndex, ItemDataSO itemData)
        {
            if (HasSelectedItem.Value)
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Queue rejected: already selected an item this turn");
                return;
            }

            _actionQueue.SetSelected(slotIndex, itemData);
            HasSelectedItem.Value = true;

            Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Item selected: {itemData.ItemName} (queued for Attack)");

            _turnContext.PublishItemUsed(
                (byte)SyncedPlayerIndex.Value, slotIndex, (byte)itemData.Category, false);
        }

        [Rpc(SendTo.Owner)]
        void StartMiniGameClientRpc(byte slotIndex, byte miniGameType, float timeLimit, int goal)
        {
            OnMiniGameStart?.Invoke(slotIndex, (MiniGameType)miniGameType, timeLimit, goal);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SubmitMiniGameResultServerRpc(byte slotIndex, bool success, RpcParams rpcParams = default)
        {
            if (!IsServer) return;
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (_pendingMiniGameSlot != slotIndex)
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Mini-game result rejected: no pending game for slot {slotIndex}");
                return;
            }
            _pendingMiniGameSlot = -1;

            if (_turnContext == null || _turnContext.Phase != TurnPhase.PrepPhase)
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Mini-game result rejected: prep phase already over");
                return;
            }
            if (IsReady.Value) return;
            if (NetworkManager.ServerTime.Time > _pendingMiniGameDeadline)
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Mini-game result rejected: past deadline");
                return;
            }

            if (!success)
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Mini-game FAILED: slot {slotIndex} — consuming 1 use");
                _inventory.ConsumeItem(slotIndex);
                _inventory.CompactSlots();
                return;
            }

            if (slotIndex >= _inventory.SlotStates.Count) return;
            var slot = _inventory.SlotStates[slotIndex];
            if (!slot.IsUsable) return;

            var itemData = _inventory.GetItemData(slotIndex);
            if (itemData == null) return;

            var ctx = BuildContext();
            ctx.UserSlot = slot;
            ctx.SlotIndex = slotIndex;
            if (!itemData.CanUse(ctx)) return;

            Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Mini-game SUCCESS: {itemData.ItemName} → queueing");
            ServerQueueItem(slotIndex, itemData);
        }

        void ExecuteFreeAction(byte slotIndex, ItemDataSO itemData, ItemContext ctx)
        {
            itemData.ExecuteEffect(ctx);
            _inventory.ConsumeItem(slotIndex);

            Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Free action executed: {itemData.ItemName}");

            if (ctx.UserModifiers.OpponentRevealed)
            {
                var opponent = ctx.Target;
                var oppQueue = opponent.GetActionQueue();
                short oppItemId = -1;
                if (oppQueue.selectedAction.HasValue)
                {
                    var oppInv = opponent.GetInventory();
                    byte oppSlot = oppQueue.selectedAction.Value.SlotIndex;
                    if (oppSlot < oppInv.SlotStates.Count)
                        oppItemId = oppInv.SlotStates[oppSlot].ItemId;
                }
                _turnContext.PublishOpponentRevealed(
                    (byte)SyncedPlayerIndex.Value, oppItemId);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void CancelSelectionServerRpc(RpcParams rpcParams = default)
        {
            if (!IsServer) return;
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (_turnContext == null || _turnContext.Phase != TurnPhase.PrepPhase) return;
            if (IsReady.Value) return;
            if (!HasSelectedItem.Value) return;

            _actionQueue.selectedAction = null;
            HasSelectedItem.Value = false;

            Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Selection cancelled");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void PressReadyServerRpc(RpcParams rpcParams = default)
        {
            if (!IsServer) return;
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (_turnContext == null || _turnContext.Phase != TurnPhase.PrepPhase) return;
            if (IsReady.Value) return;

            _pendingMiniGameSlot = -1;
            _actionQueue.SetReady(Time.time);
            IsReady.Value = true;
            IsFanActive.Value = false;

            Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Ready pressed (hasItem={HasSelectedItem.Value})");
        }

        // ─── Presentation ACK ────────────────────────────────────

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void PresentationAckServerRpc(uint sequence, RpcParams rpcParams = default)
        {
            if (!IsServer) return;
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            _turnContext?.ReceivePresentationAck(
                sequence, rpcParams.Receive.SenderClientId);
        }

        // ─── 도발 이모티콘 ──────────────────────────────────────

        double _lastEmoteServerTime = -100.0;
        public double LastEmoteServerTime => _lastEmoteServerTime;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SendEmoteServerRpc(byte emoteId, RpcParams rpcParams = default)
        {
            if (!IsServer) return;
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (_turnContext == null || !_turnContext.CanAcceptEmotes) return;
            if (emoteId >= EmoteCatalog.Count) return;

            _lastEmoteServerTime = NetworkManager.ServerTime.Time;
            ShowEmoteClientRpc(emoteId);
        }

        [Rpc(SendTo.Everyone)]
        void ShowEmoteClientRpc(byte emoteId)
        {
            if (IsOwner) return;

            var visual = GetComponent<AZPlayerVisual>();
            Transform root = visual != null ? visual.GetVisualRoot() : null;
            if (root == null) return;

            OnEmoteRequested?.Invoke(root, root.position, emoteId);
        }
    }
}
