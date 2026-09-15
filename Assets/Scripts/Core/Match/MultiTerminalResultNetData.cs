using System;
using Unity.Netcode;

namespace AbsoluteZero.Core.Match
{
    public static class MultiVictoryRules
    {
        public static byte FindThresholdCrossings(int[] scoresBefore, int[] scoresAfter,
            int killsToWin)
        {
            if (scoresBefore == null || scoresAfter == null || killsToWin <= 0)
                return 0;

            int count = Math.Min(Math.Min(scoresBefore.Length, scoresAfter.Length), 8);
            byte winnerMask = 0;
            for (int seat = 0; seat < count; seat++)
            {
                if (scoresBefore[seat] < killsToWin && scoresAfter[seat] >= killsToWin)
                    winnerMask |= (byte)(1 << seat);
            }
            return winnerMask;
        }
    }

    public struct MultiTerminalResultNetData : INetworkSerializable, IEquatable<MultiTerminalResultNetData>
    {
        public uint DecidingSequence;
        public MultiMatchOutcome Outcome;
        public byte WinnerMask;
        public bool Released;

        public bool IsValid => DecidingSequence != 0
            && Outcome != MultiMatchOutcome.InProgress
            && WinnerMask != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref DecidingSequence);
            serializer.SerializeValue(ref Outcome);
            serializer.SerializeValue(ref WinnerMask);
            serializer.SerializeValue(ref Released);
        }

        public bool Equals(MultiTerminalResultNetData other)
            => DecidingSequence == other.DecidingSequence
                && Outcome == other.Outcome
                && WinnerMask == other.WinnerMask
                && Released == other.Released;

        public override bool Equals(object obj)
            => obj is MultiTerminalResultNetData other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(DecidingSequence, (byte)Outcome, WinnerMask, Released);
    }
}
