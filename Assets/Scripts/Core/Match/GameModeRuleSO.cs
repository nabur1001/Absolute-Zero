using AbsoluteZero.Core.Network;
using UnityEngine;

namespace AbsoluteZero.Core.Match
{
    [CreateAssetMenu(fileName = "NewGameModeRule", menuName = "AbsoluteZero/GameModeRule")]
    public class GameModeRuleSO : ScriptableObject, IGameModeRule
    {
        [Header("Mode")]
        [SerializeField] GameMode targetMode;
        public GameMode TargetMode => targetMode;

        [Header("Win Condition")]
        [SerializeField] WinConditionType winCondition;
        [SerializeField] int winsRequired = 2;
        [SerializeField] int killsToWin = 5;
        [SerializeField] int maxRounds = 5;

        [Header("Timing")]
        [SerializeField] float prepPhaseDuration = 20f;

        [Header("Items")]
        [SerializeField] int initialRandomItems;
        [SerializeField] int maxRandomItems = 8;
        [SerializeField] int deathmatchGrantCount;
        [SerializeField] bool isWindbreakerUnlimited = true;
        [SerializeField] bool isTarotAllowed = true;

        [Header("Systems")]
        [SerializeField] bool enableGhostSystem;
        [SerializeField] bool enableReconnect;
        [SerializeField] float graceTimerSeconds;

        public WinConditionType WinCondition => winCondition;
        public int WinsRequired => winsRequired;
        public int KillsToWin => killsToWin;
        public int MaxRounds => maxRounds;
        public float PrepPhaseDuration => prepPhaseDuration;
        public int InitialRandomItems => initialRandomItems;
        public int MaxRandomItems => maxRandomItems;
        public int DeathmatchGrantCount => deathmatchGrantCount;
        public bool IsWindbreakerUnlimited => isWindbreakerUnlimited;
        public bool IsTarotAllowed => isTarotAllowed;
        public bool EnableGhostSystem => enableGhostSystem;
        public bool EnableReconnect => enableReconnect;
        public float GraceTimerSeconds => graceTimerSeconds;
    }
}
