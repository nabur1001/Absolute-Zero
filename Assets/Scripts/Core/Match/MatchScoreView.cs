namespace AbsoluteZero.Core.Match
{
    public static class MatchScoreView
    {
        public static int GetRoundWins(MatchManager mm, int playerIndex)
            => playerIndex == 0 ? mm.P1RoundWins.Value : mm.P2RoundWins.Value;
    }
}
