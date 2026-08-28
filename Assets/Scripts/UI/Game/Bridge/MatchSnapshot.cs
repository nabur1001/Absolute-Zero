using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Network;

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
    }
}
