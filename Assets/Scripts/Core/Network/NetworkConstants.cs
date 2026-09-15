using System;

namespace AbsoluteZero.Core.Network
{
    public enum MatchState : byte
    {
        WaitingToStart = 0,
        RoundInProgress,
        RoundEnd,
        MatchComplete,
        RematchVote,
        RematchDeclined
    }

    public enum MatchEndReason : byte
    {
        None = 0,
        Player1Win = 1,
        Player2Win = 2,
        Draw = 3
    }

    public enum GameMode : byte
    {
        None = 0,
        OneVsOne = 1,
        Multi = 2,
        Solo = 3
    }

    public enum TeamId : byte
    {
        None = 0,
        Player1,
        Player2
    }

    public static class NetworkTickRate
    {
        public const int TICKS_PER_SECOND = 30;
        public const float TICK_DURATION = 1f / TICKS_PER_SECOND;
    }
}
