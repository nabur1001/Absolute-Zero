using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Player;

namespace AbsoluteZero.UI.Game.Bridge
{
    public struct MatchSnapshot
    {
        public TurnPhase CurrentPhase;
        public int TurnNumber;
        public int RemainingTime;
        public float PrepDuration;
        public EnvironmentType ActiveEnvironment;
        public int RoundNumber;
        public int P1RoundWins;
        public int P2RoundWins;
        public MatchState MatchState;
        public int LastRoundWinner;
        public byte FirstReadySeat;
        public byte RematchDecisionMask;
        public double RematchDeadlineServerTime;
        public uint RematchVoteEpoch;

        public GameMode Mode;
        public int RequiredPlayerCount;
        public int[] KillScores;
        public LifeState[] LifeStates;
        public MultiMatchOutcome MultiOutcome;
        public byte MultiWinnerMask;
        public uint MultiDecidingSequence;
        public bool MultiResultReleased;
    }
}
