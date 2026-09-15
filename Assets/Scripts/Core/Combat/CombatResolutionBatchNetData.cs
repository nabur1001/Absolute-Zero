using System;
using Unity.Netcode;

namespace AbsoluteZero.Core.Combat
{
    public struct CombatResolutionBatchNetData : INetworkSerializable
    {
        public const int MaxEvents = 16;
        public const int MaxSeats = 4;

        public byte SeatCount;
        public byte EventCount;

        public CombatEventNetData[] Events;
        public float[] TempBefore;
        public float[] TempAfter;
        public short[] MainItemIds;
        public short[] SubItemIds;
        public int[] ActionOrder;

        public byte DeadMask;
        public byte WinnerMask;
        public byte MatchWinnerMask;
        public byte FirstActionSeat;
        public uint ResultSequence;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            if (!s.IsReader)
            {
                if (Events == null || TempBefore == null || TempAfter == null ||
                    MainItemIds == null || SubItemIds == null || ActionOrder == null)
                    throw new InvalidOperationException("Writer: null array detected");
                if (SeatCount > MaxSeats)
                    throw new InvalidOperationException($"SeatCount {SeatCount} > {MaxSeats}");
                if (EventCount > MaxEvents)
                    throw new InvalidOperationException($"EventCount {EventCount} > {MaxEvents}");
                if (EventCount > Events.Length)
                    throw new InvalidOperationException("EventCount exceeds Events array");
                if (SeatCount > TempBefore.Length || SeatCount > TempAfter.Length ||
                    SeatCount > MainItemIds.Length || SeatCount > SubItemIds.Length ||
                    SeatCount > ActionOrder.Length)
                    throw new InvalidOperationException("SeatCount exceeds seat array");
            }

            s.SerializeValue(ref SeatCount);
            s.SerializeValue(ref EventCount);

            if (s.IsReader)
            {
                if (SeatCount > MaxSeats)
                    throw new InvalidOperationException($"SeatCount {SeatCount} > {MaxSeats}");
                if (EventCount > MaxEvents)
                    throw new InvalidOperationException($"EventCount {EventCount} > {MaxEvents}");

                if (Events == null || Events.Length < EventCount)
                    Events = new CombatEventNetData[MaxEvents];
                if (TempBefore == null || TempBefore.Length < SeatCount)
                    TempBefore = new float[MaxSeats];
                if (TempAfter == null || TempAfter.Length < SeatCount)
                    TempAfter = new float[MaxSeats];
                if (MainItemIds == null || MainItemIds.Length < SeatCount)
                    MainItemIds = new short[MaxSeats];
                if (SubItemIds == null || SubItemIds.Length < SeatCount)
                    SubItemIds = new short[MaxSeats];
                if (ActionOrder == null || ActionOrder.Length < SeatCount)
                    ActionOrder = new int[MaxSeats];
            }

            for (int i = 0; i < EventCount; i++)
                Events[i].NetworkSerialize(s);

            for (int i = 0; i < SeatCount; i++) s.SerializeValue(ref TempBefore[i]);
            for (int i = 0; i < SeatCount; i++) s.SerializeValue(ref TempAfter[i]);
            for (int i = 0; i < SeatCount; i++) s.SerializeValue(ref MainItemIds[i]);
            for (int i = 0; i < SeatCount; i++) s.SerializeValue(ref SubItemIds[i]);
            for (int i = 0; i < SeatCount; i++) s.SerializeValue(ref ActionOrder[i]);

            s.SerializeValue(ref DeadMask);
            s.SerializeValue(ref WinnerMask);
            s.SerializeValue(ref MatchWinnerMask);
            s.SerializeValue(ref FirstActionSeat);
            s.SerializeValue(ref ResultSequence);
        }

        public CombatResolutionBatchNetData WithWinnerMask(byte mask)
        {
            var copy = this;
            copy.WinnerMask = mask;
            return copy;
        }

        public CombatResolutionBatchNetData WithMatchWinnerMask(byte mask)
        {
            var copy = this;
            copy.MatchWinnerMask = mask;
            return copy;
        }

        public static bool TryFromResolution(
            MultiCombatResolution resolution, MatchCombatSnapshot snapshot,
            byte winnerMask, uint sequence,
            out CombatResolutionBatchNetData data)
        {
            data = default;
            if (resolution == null || snapshot.CurrentTemperatures == null)
                return false;

            int seatCount = snapshot.SeatCount;
            int eventCount = resolution.EventCount;

            if (seatCount > MaxSeats || seatCount <= 0) return false;
            if (eventCount > MaxEvents || eventCount < 0) return false;
            if (snapshot.CurrentTemperatures.Length < seatCount) return false;
            if (resolution.OrderedEvents == null || resolution.OrderedEvents.Length < eventCount) return false;
            if (resolution.TemperatureDeltas == null || resolution.TemperatureDeltas.Length < seatCount) return false;
            if (resolution.MainItemIds == null || resolution.MainItemIds.Length < seatCount) return false;
            if (resolution.SubItemIds == null || resolution.SubItemIds.Length < seatCount) return false;
            if (resolution.ActionOrder == null || resolution.ActionOrder.Length < seatCount) return false;

            byte validMask = (byte)((1 << seatCount) - 1);
            if ((resolution.DeadMask & ~validMask) != 0) return false;
            if ((winnerMask & ~validMask) != 0) return false;
            for (int i = 0; i < seatCount; i++)
            {
                if (resolution.ActionOrder[i] < 0 || resolution.ActionOrder[i] >= seatCount)
                    return false;
            }

            data = new CombatResolutionBatchNetData
            {
                SeatCount = (byte)seatCount,
                EventCount = (byte)eventCount,
                Events = new CombatEventNetData[MaxEvents],
                TempBefore = new float[MaxSeats],
                TempAfter = new float[MaxSeats],
                MainItemIds = new short[MaxSeats],
                SubItemIds = new short[MaxSeats],
                ActionOrder = new int[MaxSeats],
                DeadMask = resolution.DeadMask,
                WinnerMask = winnerMask,
                MatchWinnerMask = 0,
                FirstActionSeat = (byte)resolution.ActionOrder[0],
                ResultSequence = sequence
            };

            for (int i = 0; i < eventCount; i++)
                data.Events[i] = CombatEventNetData.FromCombatEvent(resolution.OrderedEvents[i]);

            for (int i = 0; i < seatCount; i++)
            {
                data.TempBefore[i] = snapshot.CurrentTemperatures[i];
                data.TempAfter[i] = snapshot.CurrentTemperatures[i] + resolution.TemperatureDeltas[i];
                data.MainItemIds[i] = resolution.MainItemIds[i];
                data.SubItemIds[i] = resolution.SubItemIds[i];
                data.ActionOrder[i] = resolution.ActionOrder[i];
            }

            return true;
        }
    }
}
