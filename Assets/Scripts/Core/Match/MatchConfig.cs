using AbsoluteZero.Core.Network;

namespace AbsoluteZero.Core.Match
{
    public sealed class MatchConfig
    {
        public IGameModeRule Rule { get; }
        public int RequiredPlayerCount { get; }
        public GameMode Mode { get; }

        public MatchConfig(IGameModeRule rule, int requiredPlayerCount, GameMode mode)
        {
            Rule = rule;
            RequiredPlayerCount = requiredPlayerCount;
            Mode = mode;
        }
    }
}
