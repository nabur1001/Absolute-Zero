namespace AbsoluteZero.Core.Match
{
    public readonly struct GameModeRuleSnapshot
    {
        public readonly WinConditionType WinCondition;
        public readonly int WinsRequired;
        public readonly int KillsToWin;
        public readonly int MaxRounds;
        public readonly float PrepPhaseDuration;
        public readonly int InitialRandomItems;
        public readonly int MaxRandomItems;
        public readonly int DeathmatchGrantCount;
        public readonly bool IsWindbreakerUnlimited;
        public readonly bool IsTarotAllowed;
        public readonly bool EnableGhostSystem;
        public readonly bool EnableReconnect;
        public readonly float GraceTimerSeconds;

        GameModeRuleSnapshot(IGameModeRule rule)
        {
            WinCondition = rule.WinCondition;
            WinsRequired = rule.WinsRequired;
            KillsToWin = rule.KillsToWin;
            MaxRounds = rule.MaxRounds;
            PrepPhaseDuration = rule.PrepPhaseDuration;
            InitialRandomItems = rule.InitialRandomItems;
            MaxRandomItems = rule.MaxRandomItems;
            DeathmatchGrantCount = rule.DeathmatchGrantCount;
            IsWindbreakerUnlimited = rule.IsWindbreakerUnlimited;
            IsTarotAllowed = rule.IsTarotAllowed;
            EnableGhostSystem = rule.EnableGhostSystem;
            EnableReconnect = rule.EnableReconnect;
            GraceTimerSeconds = rule.GraceTimerSeconds;
        }

        public static GameModeRuleSnapshot From(IGameModeRule rule) => new(rule);
    }
}
