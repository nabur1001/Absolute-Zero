using System;
using System.Collections.Generic;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Item.Data;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using UnityEngine;

namespace AbsoluteZero.Core.Combat
{
    public sealed class SeatInventoryMutator : ISeatInventoryMutator
    {
        const int MaxInventorySlots = 12;

        readonly struct PreparedSlotChange
        {
            public readonly int Index;
            public readonly ItemSlotNetData Before;
            public readonly ItemSlotNetData After;

            public PreparedSlotChange(int index, ItemSlotNetData before, ItemSlotNetData after)
            {
                Index = index;
                Before = before;
                After = after;
            }
        }

        readonly PlayerInventory[] _inventories;
        readonly ISeatStateAccessor _accessor;
        readonly ItemDropTable _dropTable;
        readonly int _seatCount;

        public SeatInventoryMutator(
            PlayerState[] players,
            ISeatStateAccessor accessor,
            ItemDropTable dropTable,
            int seatCount)
        {
            _accessor = accessor;
            _dropTable = dropTable;
            _seatCount = seatCount;
            _inventories = new PlayerInventory[seatCount];
            for (int i = 0; i < seatCount; i++)
            {
                if (players != null && i < players.Length && players[i] != null)
                    _inventories[i] = players[i].GetInventory();
            }
        }

        public bool TryConsume(byte seat, byte slot, short itemId)
        {
            if (!CanConsume(seat, slot, itemId)) return false;
            var slots = _accessor.GetInventorySlots(seat);
            if (slots[slot].IsUnlimited) return true;

            slots[slot].RemainingUses--;
            if (slots[slot].RemainingUses <= 0)
                slots[slot] = ItemSlotNetData.Empty;

            _accessor.SetInventorySlots(seat, slots);
            return true;
        }

        public bool CanConsume(byte seat, byte slot, short itemId)
        {
            if (seat >= _seatCount || !_accessor.IsConnected(seat)) return false;
            var slots = _accessor.GetInventorySlots(seat);
            return slots != null && slot < slots.Length
                && !slots[slot].IsEmpty && slots[slot].ItemId == itemId
                && slots[slot].IsUsable;
        }

        public bool TryPrepareMutation(InventoryMutationType type, byte actorSeat, byte targetSeat,
            out InventoryMutationPlan plan)
        {
            plan = null;
            return type switch
            {
                InventoryMutationType.RerollTarget => TryPrepareReroll(targetSeat, out plan),
                InventoryMutationType.StealFromTarget => TryPrepareSteal(actorSeat, targetSeat, out plan),
                InventoryMutationType.None => true,
                _ => false
            };
        }

        bool TryPrepareReroll(byte targetSeat, out InventoryMutationPlan plan)
        {
            plan = null;
            if (targetSeat >= _seatCount || !_accessor.IsConnected(targetSeat)
                || _dropTable == null || _dropTable.IsEmpty)
                return false;

            var inv = _inventories[targetSeat];
            if (inv == null) return false;

            var changes = new List<PreparedSlotChange>();
            for (int i = 0; i < inv.SlotStates.Count; i++)
            {
                var before = inv.SlotStates[i];
                if (before.IsEmpty || before.IsUnlimited) continue;
                var existing = inv.GetItemData(i);
                if (existing == null || existing.SlotType != ItemSlotType.Sub) continue;

                var replacement = _dropTable.Roll();
                short replacementId = FindItemId(replacement);
                if (replacement == null || replacementId < 0) return false;
                changes.Add(new PreparedSlotChange(i, before, MakeSlot(replacementId, replacement)));
            }

            plan = new InventoryMutationPlan(InventoryMutationType.RerollTarget,
                changes.Count == 0, () =>
                {
                    for (int i = 0; i < changes.Count; i++)
                    {
                        var change = changes[i];
                        if (change.Index >= inv.SlotStates.Count
                            || !inv.SlotStates[change.Index].Equals(change.Before))
                            return false;
                    }

                    var queue = inv.GetComponent<PlayerState>()?.GetActionQueue();
                    for (int i = 0; i < changes.Count; i++)
                    {
                        var change = changes[i];
                        queue?.InvalidateSlot((byte)change.Index);
                        inv.SlotStates[change.Index] = change.After;
                    }
                    Debug.Log($"[SeatInventoryMutator] Applied prepared reroll: target={targetSeat}, slots={changes.Count}");
                    return true;
                });
            return true;
        }

        bool TryPrepareSteal(byte actorSeat, byte targetSeat, out InventoryMutationPlan plan)
        {
            plan = null;
            if (actorSeat >= _seatCount || targetSeat >= _seatCount) return false;
            if (!_accessor.IsConnected(actorSeat) || !_accessor.IsConnected(targetSeat)) return false;

            var actorInv = _inventories[actorSeat];
            var targetInv = _inventories[targetSeat];
            if (actorInv == null || targetInv == null) return false;

            var occupied = new List<int>();
            for (int i = 0; i < targetInv.SlotStates.Count; i++)
            {
                var slot = targetInv.SlotStates[i];
                if (!slot.IsEmpty && !slot.IsUnlimited)
                    occupied.Add(i);
            }

            if (occupied.Count == 0)
            {
                plan = NoOpPlan(InventoryMutationType.StealFromTarget,
                    $"[SeatInventoryMutator] Prepared steal has no eligible target: target={targetSeat}");
                return true;
            }

            int targetSlot = occupied[UnityEngine.Random.Range(0, occupied.Count)];
            var targetBefore = targetInv.SlotStates[targetSlot];
            int actorSlot = FindSlot(actorInv, targetBefore.ItemId);

            if (actorSlot >= 0 && !actorInv.SlotStates[actorSlot].IsUnlimited)
            {
                var actorBefore = actorInv.SlotStates[actorSlot];
                var actorAfter = actorBefore;
                actorAfter.RemainingUses = (byte)Mathf.Min(
                    actorBefore.RemainingUses + targetBefore.RemainingUses, 254);
                plan = new InventoryMutationPlan(InventoryMutationType.StealFromTarget, false, () =>
                {
                    if (actorSlot >= actorInv.SlotStates.Count
                        || targetSlot >= targetInv.SlotStates.Count
                        || !actorInv.SlotStates[actorSlot].Equals(actorBefore)
                        || !targetInv.SlotStates[targetSlot].Equals(targetBefore))
                        return false;

                    actorInv.SlotStates[actorSlot] = actorAfter;
                    targetInv.GetComponent<PlayerState>()?.GetActionQueue()?.InvalidateSlot((byte)targetSlot);
                    targetInv.SlotStates[targetSlot] = ItemSlotNetData.Empty;
                    Debug.Log($"[SeatInventoryMutator] Applied prepared steal: actor={actorSeat}, target={targetSeat}, slot={targetSlot}, stacked={actorSlot}");
                    return true;
                });
                return true;
            }

            if (actorInv.SlotStates.Count >= MaxInventorySlots)
            {
                plan = NoOpPlan(InventoryMutationType.StealFromTarget,
                    $"[SeatInventoryMutator] Prepared steal cannot fit target item: actor={actorSeat}");
                return true;
            }

            int expectedActorCount = actorInv.SlotStates.Count;
            plan = new InventoryMutationPlan(InventoryMutationType.StealFromTarget, false, () =>
            {
                if (actorInv.SlotStates.Count != expectedActorCount
                    || targetSlot >= targetInv.SlotStates.Count
                    || !targetInv.SlotStates[targetSlot].Equals(targetBefore))
                    return false;

                actorInv.SlotStates.Add(targetBefore);
                targetInv.GetComponent<PlayerState>()?.GetActionQueue()?.InvalidateSlot((byte)targetSlot);
                targetInv.SlotStates[targetSlot] = ItemSlotNetData.Empty;
                Debug.Log($"[SeatInventoryMutator] Applied prepared steal: actor={actorSeat}, target={targetSeat}, slot={targetSlot}, appended={expectedActorCount}");
                return true;
            });
            return true;
        }

        static InventoryMutationPlan NoOpPlan(InventoryMutationType type, string message)
        {
            return new InventoryMutationPlan(type, true, () =>
            {
                Debug.Log(message);
                return true;
            });
        }

        short FindItemId(ItemDataSO item)
        {
            if (item == null) return -1;
            var registry = ItemManager.Instance?.GetAllItems();
            if (registry == null) return -1;
            for (short i = 0; i < registry.Length; i++)
                if (registry[i] == item) return i;
            return -1;
        }

        static int FindSlot(PlayerInventory inventory, short itemId)
        {
            for (int i = 0; i < inventory.SlotStates.Count; i++)
                if (!inventory.SlotStates[i].IsEmpty && inventory.SlotStates[i].ItemId == itemId)
                    return i;
            return -1;
        }

        static ItemSlotNetData MakeSlot(short itemId, ItemDataSO item)
        {
            byte uses = item.MaxUses <= 0 ? (byte)255 : (byte)Mathf.Min(item.MaxUses, 254);
            return new ItemSlotNetData
            {
                ItemId = itemId,
                RemainingUses = uses,
                Flags = (byte)(item.SlotType == ItemSlotType.Sub ? 0b10 : 0)
            };
        }
    }
}
