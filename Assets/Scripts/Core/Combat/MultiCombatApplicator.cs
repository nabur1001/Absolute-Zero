using System;
using AbsoluteZero.Core.Buff;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;

namespace AbsoluteZero.Core.Combat
{
    public class MultiCombatApplicator
    {
        readonly ISeatStateAccessor _accessor;
        readonly AuthoritativeDeathService _deathService;
        readonly BuffDebuffSystem _buffSystem;
        readonly ISeatInventoryMutator _inventoryMutator;

        public MultiCombatApplicator(
            ISeatStateAccessor accessor,
            AuthoritativeDeathService deathService,
            BuffDebuffSystem buffSystem,
            ISeatInventoryMutator inventoryMutator = null)
        {
            _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
            _deathService = deathService ?? throw new ArgumentNullException(nameof(deathService));
            _buffSystem = buffSystem ?? throw new ArgumentNullException(nameof(buffSystem));
            _inventoryMutator = inventoryMutator;
        }

        public bool Apply(MultiCombatResolution resolution, MatchCombatSnapshot snapshot)
        {
            if (resolution == null) return false;
            int seatCount = snapshot.SeatCount;
            if (seatCount == 0
                || resolution.TemperatureDeltas == null || resolution.TemperatureDeltas.Length < seatCount
                || resolution.PlayerStateChanges == null || resolution.PlayerStateChanges.Length < seatCount
                || resolution.ModifierChanges == null || resolution.ModifierChanges.Length < seatCount
                || resolution.LastDamageSources == null || resolution.LastDamageSources.Length < seatCount
                || resolution.InventoryChanges == null
                || resolution.NewScheduled == null)
                return false;

            int invCount = Math.Min(resolution.InventoryChangeCount, resolution.InventoryChanges.Length);
            var preparedMutations = new InventoryMutationPlan[invCount];
            for (int i = 0; i < invCount; i++)
            {
                var inv = resolution.InventoryChanges[i];
                if (inv.Consumed)
                {
                    bool valid = _inventoryMutator != null
                        ? _inventoryMutator.CanConsume(inv.SeatIndex, inv.SlotIndex, inv.ItemId)
                        : CanConsumeFallback(inv, seatCount);
                    if (!valid) return false;
                }

                if (inv.MutationType != InventoryMutationType.None)
                {
                    if (_inventoryMutator == null
                        || !_inventoryMutator.TryPrepareMutation(
                            inv.MutationType, inv.SeatIndex, inv.TargetSeat,
                            out preparedMutations[i]))
                        return false;
                }
            }

            // Commit every prepared random choice before other state writes. The plans
            // validate their captured slots again and never roll a second time.
            for (int i = 0; i < preparedMutations.Length; i++)
            {
                var plan = preparedMutations[i];
                if (plan != null && !plan.TryApply())
                    return false;
            }

            for (int seat = 0; seat < seatCount; seat++)
            {
                if (!UnityEngine.Mathf.Approximately(resolution.TemperatureDeltas[seat], 0f))
                {
                    float newTemp = snapshot.CurrentTemperatures[seat] + resolution.TemperatureDeltas[seat];
                    _accessor.SetTemperature((byte)seat, newTemp);
                }
            }

            for (int seat = 0; seat < seatCount; seat++)
            {
                var delta = resolution.PlayerStateChanges[seat];
                if (delta.NewFanSpeed.HasValue)
                    _accessor.SetFanSpeed((byte)seat, delta.NewFanSpeed.Value);
                if (delta.IsFanUpgraded.HasValue)
                    _accessor.SetIsFanUpgraded((byte)seat, delta.IsFanUpgraded.Value);
                if (delta.IsBasicBlocked.HasValue)
                    _accessor.SetIsBasicBlocked((byte)seat, delta.IsBasicBlocked.Value);
            }

            for (int seat = 0; seat < seatCount; seat++)
            {
                var mod = resolution.ModifierChanges[seat];
                var current = _accessor.GetModifiers((byte)seat);
                bool changed = false;

                if (mod.ActiveDefense.HasValue)
                {
                    current.ActiveDefense = mod.ActiveDefense;
                    changed = true;
                }
                if (mod.ActionNeutralized.HasValue)
                {
                    current.ActionNeutralized = mod.ActionNeutralized.Value;
                    changed = true;
                }
                if (mod.HasExtraAction.HasValue)
                {
                    current.HasExtraAction = mod.HasExtraAction.Value;
                    changed = true;
                }
                if (mod.OpponentRevealed.HasValue)
                {
                    current.OpponentRevealed = mod.OpponentRevealed.Value;
                    changed = true;
                }

                if (changed)
                    _accessor.SetModifiers((byte)seat, current);
            }

            for (int i = 0; i < invCount; i++)
            {
                var inv = resolution.InventoryChanges[i];
                if (inv.SeatIndex >= seatCount) continue;

                if (inv.Consumed)
                {
                    if (_inventoryMutator != null)
                    {
                        if (!_inventoryMutator.TryConsume(inv.SeatIndex, inv.SlotIndex, inv.ItemId))
                            throw new InvalidOperationException($"Inventory changed during atomic action: seat={inv.SeatIndex}, slot={inv.SlotIndex}, item={inv.ItemId}");
                    }
                    else if (_accessor.IsConnected(inv.SeatIndex))
                    {
                        var slots = _accessor.GetInventorySlots(inv.SeatIndex);
                        if (slots != null && inv.SlotIndex < slots.Length
                            && !slots[inv.SlotIndex].IsEmpty
                            && slots[inv.SlotIndex].ItemId == inv.ItemId)
                        {
                            if (!slots[inv.SlotIndex].IsUnlimited)
                            {
                                slots[inv.SlotIndex].RemainingUses--;
                                if (slots[inv.SlotIndex].RemainingUses <= 0)
                                    slots[inv.SlotIndex] = ItemSlotNetData.Empty;
                            }
                            _accessor.SetInventorySlots(inv.SeatIndex, slots);
                        }
                    }
                }

            }

            int schedCount = Math.Min(resolution.ScheduledCount, resolution.NewScheduled.Length);
            for (int i = 0; i < schedCount; i++)
            {
                var sched = resolution.NewScheduled[i];
                if (sched.TargetSeat >= seatCount) continue;
                _buffSystem.Schedule(sched.TargetSeat, sched.Type, sched.Value, sched.DelayTurns, sched.SourceSeat);
            }

            for (int seat = 0; seat < seatCount; seat++)
            {
                if (resolution.IsDead((byte)seat))
                    _deathService.TryKill((byte)seat, resolution.LastDamageSources[seat]);
            }

            _deathService.FlushDeathQueue();
            return true;
        }

        bool CanConsumeFallback(InventoryDelta inv, int seatCount)
        {
            if (inv.SeatIndex >= seatCount || !_accessor.IsConnected(inv.SeatIndex)) return false;
            var slots = _accessor.GetInventorySlots(inv.SeatIndex);
            return slots != null && inv.SlotIndex < slots.Length
                && slots[inv.SlotIndex].IsUsable
                && slots[inv.SlotIndex].ItemId == inv.ItemId;
        }
    }
}
