using System;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Cosmetic;
using AbsoluteZero.Core.Emote;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player.Identity;
using AbsoluteZero.Core.Turn;
using Unity.Collections;
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

        public readonly NetworkVariable<LifeState> CurrentLifeState = new(
            LifeState.Alive, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<FixedString128Bytes> CosmeticDataNV = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        bool _hasAcceptedCosmetic;

        public static event Action<Transform, Vector3, byte> OnEmoteRequested;

        readonly ActionQueue _actionQueue = new();
        PlayerInventory _inventory;
        PlayerIdentity? _cachedIdentity;
        ITurnContext _turnContext;

        ActionIntent? _pendingIntent;

        const float MINIGAME_GRACE_SEC = 0.5f;
        int _pendingMiniGameSlot = -1;
        byte _pendingMiniGameTarget = ActionIntent.NoTarget;
        double _pendingMiniGameDeadline;
        int _readyServerTick;

        public event System.Action<byte, MiniGameType, float, int> OnMiniGameStart;

        public int PlayerIndex => SyncedPlayerIndex.Value;
        public ActionQueue GetActionQueue() => _actionQueue;
        public ActionIntent? PendingIntent => _pendingIntent;

        public ActionIntent BuildActionIntent()
        {
            int idx = SyncedPlayerIndex.Value;
            if (idx < 0 || idx > 254)
                return ActionIntent.Empty;

            var q = _actionQueue;
            byte seat = (byte)idx;
            short itemId = -1;
            byte slotIndex = 0;
            byte targetSeat = ActionIntent.NoTarget;

            if (q.selectedAction.HasValue)
            {
                var sel = q.selectedAction.Value;
                slotIndex = sel.SlotIndex;
                targetSeat = sel.TargetSeat;
                if (sel.ItemData != null)
                {
                    var inv = GetInventory();
                    if (inv != null && sel.SlotIndex < inv.SlotStates.Count)
                        itemId = inv.SlotStates[sel.SlotIndex].ItemId;
                }
            }

            var intent = new ActionIntent(seat, slotIndex, itemId, targetSeat, _readyServerTick);
            _pendingIntent = intent;
            return intent;
        }

        public void ClearPendingIntent() => _pendingIntent = null;

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

            CosmeticDataNV.OnValueChanged += OnCosmeticNVChanged;

            if (IsOwner)
            {
                var cosmeticService = CosmeticProfileService.Instance;
                if (cosmeticService != null)
                    SubmitCosmeticRpc(cosmeticService.GetCompactDto());
                else
                    Debug.LogWarning("[PlayerState] CosmeticProfileService not found — cosmetic data skipped");
            }

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
            CosmeticDataNV.OnValueChanged -= OnCosmeticNVChanged;
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
            _pendingMiniGameTarget = ActionIntent.NoTarget;
            _readyServerTick = 0;
            _pendingIntent = null;
        }

        ItemContext BuildContext(byte targetSeat = ActionIntent.NoTarget)
        {
            int myIndex = SyncedPlayerIndex.Value;

            PlayerState opponent = null;
            var mcr = MatchCompositionRoot.Instance;
            if (mcr != null)
            {
                if (targetSeat != ActionIntent.NoTarget
                    && mcr.Registry.TryGetByPlayerIndex(targetSeat, out var targetEntry))
                {
                    opponent = targetEntry.State;
                }
                else
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
            }
            opponent ??= _turnContext.GetPlayer(myIndex == 0 ? 1 : 0);

            if (opponent == null)
            {
                Debug.LogError($"[PlayerState P{myIndex}] BuildContext: no opponent found");
                return new ItemContext
                {
                    User = this,
                    UserIndex = myIndex,
                    UserInventory = _inventory,
                    AllModifiers = _turnContext?.GetModifiers(),
                    TempSystem = _turnContext?.GetTempSystem(),
                    BuffSystem = _turnContext?.GetBuffSystem(),
                    DropTable = _turnContext?.GetDropTable(),
                };
            }

            return new ItemContext
            {
                User = this,
                Target = opponent,
                UserIndex = myIndex,
                TargetIndex = opponent.PlayerIndex,
                UserInventory = _inventory,
                TargetInventory = opponent.GetInventory(),
                AllModifiers = _turnContext?.GetModifiers(),
                TempSystem = _turnContext?.GetTempSystem(),
                BuffSystem = _turnContext?.GetBuffSystem(),
                DropTable = _turnContext?.GetDropTable(),
            };
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SelectItemServerRpc(byte slotIndex, byte targetSeat = ActionIntent.NoTarget, RpcParams rpcParams = default)
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

            if (!TryResolveTargetSeat(itemData, targetSeat, out targetSeat))
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] SelectItem rejected: invalid target");
                return;
            }

            var ctx = BuildContext(targetSeat);
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
                _pendingMiniGameTarget = targetSeat;
                _pendingMiniGameDeadline = System.Math.Min(now + itemData.MiniGameTimeLimit, prepEnd) + MINIGAME_GRACE_SEC;

                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Mini-game START: {itemData.ItemName} " +
                          $"({itemData.MiniGameType}, {itemData.MiniGameTimeLimit}s, goal={itemData.MiniGameGoal})");

                StartMiniGameClientRpc(slotIndex, (byte)itemData.MiniGameType,
                                       itemData.MiniGameTimeLimit, itemData.MiniGameGoal);
                return;
            }

            ServerQueueItem(slotIndex, itemData, targetSeat);
        }

        bool TryResolveTargetSeat(ItemDataSO itemData, byte clientTarget, out byte resolvedTarget)
        {
            resolvedTarget = ActionIntent.NoTarget;
            if (!IsSpawned || CurrentLifeState.Value != LifeState.Alive
                || NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(OwnerClientId))
                return false;
            var mode = itemData.GetTargetMode();

            if (mode == TargetMode.Self)
                return true;

            byte mySeat = (byte)Mathf.Max(0, SyncedPlayerIndex.Value);
            var mcr = MatchCompositionRoot.Instance;

            if (clientTarget != ActionIntent.NoTarget)
            {
                if (clientTarget == mySeat)
                {
                    Debug.Log($"[PlayerState P{mySeat}] Target rejected: cannot target self");
                    return false;
                }

                if (mcr == null || !mcr.Registry.TryGetByPlayerIndex(clientTarget, out var target)
                    || !IsEligibleItemTarget(target))
                {
                    Debug.Log($"[PlayerState P{mySeat}] Target rejected: seat {clientTarget} not registered");
                    return false;
                }

                resolvedTarget = clientTarget;
                return true;
            }

            if (mcr != null && mcr.ActiveConfig.RequiredPlayerCount <= 2)
            {
                foreach (var p in mcr.Registry.Players)
                {
                    if (p.Identity.PlayerIndex != mySeat && IsEligibleItemTarget(p))
                    {
                        resolvedTarget = p.Identity.PlayerIndex;
                        return true;
                    }
                }
            }

            Debug.Log($"[PlayerState P{mySeat}] Target rejected: explicit target required for {mcr?.ActiveConfig.RequiredPlayerCount ?? 0}-player mode");
            return false;
        }

        bool IsEligibleItemTarget(PlayerBinding target)
        {
            return target != null && target.IsValid
                && target.State.CurrentLifeState.Value == LifeState.Alive
                && NetworkManager.ConnectedClients.ContainsKey(target.State.OwnerClientId);
        }

        void ServerQueueItem(byte slotIndex, ItemDataSO itemData, byte targetSeat = ActionIntent.NoTarget)
        {
            if (HasSelectedItem.Value)
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Queue rejected: already selected an item this turn");
                return;
            }

            _actionQueue.SetSelected(slotIndex, itemData, targetSeat);
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
            byte savedTarget = _pendingMiniGameTarget;
            _pendingMiniGameTarget = ActionIntent.NoTarget;

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

            if (slotIndex >= _inventory.SlotStates.Count) return;
            var slot = _inventory.SlotStates[slotIndex];
            if (!slot.IsUsable) return;
            var itemData = _inventory.GetItemData(slotIndex);
            if (itemData == null) return;
            if (!TryResolveTargetSeat(itemData, savedTarget, out byte resolvedTarget))
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Mini-game result rejected: actor or target no longer eligible");
                return;
            }

            if (!success)
            {
                Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Mini-game FAILED: slot {slotIndex} — consuming 1 use");
                _pendingMiniGameTarget = ActionIntent.NoTarget;
                _inventory.ConsumeItem(slotIndex);
                _inventory.CompactSlots();
                return;
            }

            var ctx = BuildContext(resolvedTarget);
            ctx.UserSlot = slot;
            ctx.SlotIndex = slotIndex;
            if (!itemData.CanUse(ctx)) return;
            _pendingMiniGameTarget = ActionIntent.NoTarget;

            Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Mini-game SUCCESS: {itemData.ItemName} → queueing");
            ServerQueueItem(slotIndex, itemData, resolvedTarget);
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
            if (CurrentLifeState.Value != LifeState.Alive) return;

            _pendingMiniGameSlot = -1;
            _pendingMiniGameTarget = ActionIntent.NoTarget;
            _readyServerTick = NetworkManager.ServerTime.Tick;
            _actionQueue.SetReady(Time.time);
            IsReady.Value = true;
            IsFanActive.Value = false;

            Debug.Log($"[PlayerState P{SyncedPlayerIndex.Value}] Ready pressed (hasItem={HasSelectedItem.Value}, tick={_readyServerTick})");
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

        // ─── Cosmetic Sync ──────────────────────────────────────

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SubmitCosmeticRpc(string dto, RpcParams rpcParams = default)
        {
            if (!IsServer) return;
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

            if (_hasAcceptedCosmetic)
            {
                Debug.LogWarning($"[PlayerState P{SyncedPlayerIndex.Value}] SubmitCosmetic rejected: already accepted");
                return;
            }

            var service = CosmeticProfileService.Instance;
            if (service == null)
            {
                Debug.LogWarning("[PlayerState] CosmeticProfileService not found — rejecting cosmetic");
                return;
            }

            if (service.TryValidateAndCanonicalizeDto(dto, out var canonical))
            {
                CosmeticDataNV.Value = canonical;
                _hasAcceptedCosmetic = true;
            }
            else
            {
                Debug.LogWarning($"[PlayerState P{SyncedPlayerIndex.Value}] SubmitCosmetic rejected: validation failed");
            }
        }

        void OnCosmeticNVChanged(FixedString128Bytes prev, FixedString128Bytes cur)
        {
            if (IsOwner) return;
            var visual = GetComponent<AZPlayerVisual>();
            if (visual != null)
                visual.ApplyRemoteCosmetic(cur.ToString());
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
