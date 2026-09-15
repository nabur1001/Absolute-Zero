namespace AbsoluteZero.Core.Match
{
    public enum WinConditionType : byte
    {
        RoundWins = 0,
        KillScore = 1
    }

    public interface IGameModeRule
    {
        WinConditionType WinCondition { get; }
        int WinsRequired { get; }
        int KillsToWin { get; }
        int MaxRounds { get; }
        float PrepPhaseDuration { get; }
        int InitialRandomItems { get; }
        int MaxRandomItems { get; }
        int DeathmatchGrantCount { get; }
        bool IsWindbreakerUnlimited { get; }
        bool IsTarotAllowed { get; }
        bool EnableGhostSystem { get; }
        bool EnableReconnect { get; }
        float GraceTimerSeconds { get; }
    }
}
