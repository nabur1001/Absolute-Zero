using System;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Player;

namespace AbsoluteZero.Core.Combat
{
    public class MultiCombatResolution
    {
        public const int MaxEvents = 16;

        public CombatEvent[] OrderedEvents;
        public int EventCount;
        public float[] TemperatureDeltas;
        public DamageSource[] LastDamageSources;
        public byte DeadMask;
        public byte DefenseReactionMask;
        public short[] DefenseItemIds;
        public int[] ActionOrder;
        public short[] MainItemIds;
        public short[] SubItemIds;
        public InventoryDelta[] InventoryChanges;
        public int InventoryChangeCount;
        public PlayerStateDelta[] PlayerStateChanges;
        public ModifierDelta[] ModifierChanges;
        public ScheduledEffectDelta[] NewScheduled;
        public int ScheduledCount;

        public static MultiCombatResolution Create(int seatCount)
        {
            if (seatCount < 1) seatCount = 1;
            if (seatCount > 8) seatCount = 8;
            return new MultiCombatResolution
            {
                OrderedEvents = new CombatEvent[MaxEvents],
                EventCount = 0,
                TemperatureDeltas = new float[seatCount],
                LastDamageSources = InitDamageSources(seatCount),
                DeadMask = 0,
                DefenseReactionMask = 0,
                DefenseItemIds = InitItemIds(seatCount),
                ActionOrder = new int[seatCount],
                MainItemIds = InitItemIds(seatCount),
                SubItemIds = InitItemIds(seatCount),
                InventoryChanges = new InventoryDelta[seatCount * 2],
                InventoryChangeCount = 0,
                PlayerStateChanges = new PlayerStateDelta[seatCount],
                ModifierChanges = new ModifierDelta[seatCount],
                NewScheduled = new ScheduledEffectDelta[seatCount * 2],
                ScheduledCount = 0
            };
        }

        public bool IsDead(byte seat) => (DeadMask & (1 << seat)) != 0;

        public void MarkDead(byte seat) => DeadMask |= (byte)(1 << seat);

        public void AddEvent(CombatEvent evt)
        {
            if (EventCount >= MaxEvents)
                throw new InvalidOperationException($"Multi combat event capacity exceeded ({MaxEvents}).");
            OrderedEvents[EventCount++] = evt;
        }

        public void AddInventoryChange(InventoryDelta delta)
        {
            if (InventoryChangeCount >= InventoryChanges.Length)
                throw new InvalidOperationException(
                    $"Multi inventory delta capacity exceeded ({InventoryChanges.Length}).");
            InventoryChanges[InventoryChangeCount++] = delta;
        }

        public void AddScheduled(ScheduledEffectDelta delta)
        {
            if (ScheduledCount >= NewScheduled.Length)
                throw new InvalidOperationException(
                    $"Multi scheduled-effect capacity exceeded ({NewScheduled.Length}).");
            NewScheduled[ScheduledCount++] = delta;
        }

        static DamageSource[] InitDamageSources(int count)
        {
            var arr = new DamageSource[count];
            for (int i = 0; i < count; i++)
                arr[i] = DamageSource.None;
            return arr;
        }

        static short[] InitItemIds(int count)
        {
            var arr = new short[count];
            for (int i = 0; i < count; i++) arr[i] = -1;
            return arr;
        }
    }

    public struct PlayerStateDelta
    {
        public byte SeatIndex;
        public float? NewFanSpeed;
        public bool? IsFanUpgraded;
        public bool? IsBasicBlocked;
    }

    public struct InventoryDelta
    {
        public byte SeatIndex;
        public InventoryMutationType MutationType;
        public byte TargetSeat;
        public byte SlotIndex;
        public bool Consumed;
        public short ItemId;
    }

    public struct ModifierDelta
    {
        public byte SeatIndex;
        public DefenseInfo? ActiveDefense;
        public bool? ActionNeutralized;
        public bool? HasExtraAction;
        public bool? OpponentRevealed;
    }

    public struct ScheduledEffectDelta
    {
        public byte TargetSeat;
        public byte SourceSeat;
        public EffectType Type;
        public float Value;
        public int DelayTurns;
    }
}
