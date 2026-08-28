using System.Collections;
using AbsoluteZero.Core.Audio;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.UI.Game.Bridge;
using AbsoluteZero.UI.Game.Build;
using AbsoluteZero.UI.MiniGame;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.Game.Presenters
{
    public sealed class MatchHudPresenter : MonoBehaviour
    {
        IGameDataBridge _bridge;
        ILocalPlayerCommands _commands;

        TextMeshProUGUI _phaseText;
        TextMeshProUGUI _timerText;
        TextMeshProUGUI _statusText;
        TextMeshProUGUI _scoreText;

        Image _timerFillImage;
        Image _timerSliderImage;
        RectTransform _clockHandRT;
        GameObject _timeAlarmObj;
        Image _timeAlarmImage;
        bool _alarmShaking;
        Coroutine _alarmCoroutine;

        Image _p1NameBox;
        Image _p2NameBox;
        TextMeshProUGUI _crownText;
        static readonly Color BOX_NORMAL = new(0.15f, 0.15f, 0.2f, 0.8f);
        static readonly Color BOX_ACTIVE = new(0.35f, 0.35f, 0.55f, 0.9f);

        Canvas _readyCanvas;
        Button _readyButton;
        Image _readyButtonImage;

        GameObject _envPanel;
        TextMeshProUGUI _envText;

        RectTransform _timerContainerRT;
        Vector2 _timerContainerBasePos;
        bool _summerVacShaking;
        Coroutine _summerVacShakeCoroutine;

        bool _hasSelectedItem;

        static readonly WaitForSeconds _waitAlarmShake = new(0.05f);
        static readonly WaitForSeconds _waitSummerShake = new(0.016f);
        static readonly WaitForSeconds _waitEnvHide = new(3.5f);

        const int ALARM_TIME_THRESHOLD = 5;

        bool _initialized;

        public void Initialize(IGameDataBridge bridge, ILocalPlayerCommands commands, GameHudRefs refs)
        {
            _bridge = bridge;
            _commands = commands;

            _phaseText = refs.PhaseText;
            _timerText = refs.TimerText;
            _statusText = refs.StatusText;
            _scoreText = refs.ScoreText;
            _timerFillImage = refs.TimerFillImage;
            _timerSliderImage = refs.TimerSliderImage;
            _clockHandRT = refs.ClockHandRT;
            _timeAlarmObj = refs.TimeAlarmObj;
            _timeAlarmImage = refs.TimeAlarmImage;
            _timerContainerRT = refs.TimerContainerRT;
            _timerContainerBasePos = _timerContainerRT.anchoredPosition;
            _readyCanvas = refs.ReadyCanvas;
            _readyButton = refs.ReadyButton;
            _readyButtonImage = refs.ReadyButtonImage;
            _envPanel = refs.EnvPanel;
            _envText = refs.EnvText;
            _p1NameBox = refs.P1NameBox;
            _p2NameBox = refs.P2NameBox;
            _crownText = refs.CrownText;

            _readyButton.onClick.AddListener(OnReadyClicked);

            _bridge.OnPhaseChanged += HandlePhaseChanged;
            _bridge.OnMatchSnapshotChanged += HandleMatchSnapshotChanged;
            _bridge.OnEnvironmentAnnounced += HandleEnvironmentAnnounced;
            _bridge.OnLocalHasSelectedItemChanged += HandleHasSelectedItemChanged;
            _bridge.OnOpponentRevealed += HandleOpponentRevealed;
            _bridge.OnCurrentAttackerChanged += HandleAttackerChanged;

            var match = _bridge.CurrentMatch;
            HandlePhaseChanged(TurnPhase.WaitingForPlayers, match.CurrentPhase);

            _initialized = true;
        }

        public void SetStatusText(string text)
        {
            if (_statusText != null)
                _statusText.text = text;
        }

        void HandlePhaseChanged(TurnPhase oldPhase, TurnPhase newPhase)
        {
            _phaseText.text = newPhase switch
            {
                TurnPhase.WaitingForPlayers => "WAITING",
                TurnPhase.PrepPhase => "PREP PHASE",
                TurnPhase.AttackPhase => "ATTACK",
                TurnPhase.ResolutionPhase => "RESOLUTION",
                TurnPhase.RoundOver => "ROUND OVER",
                _ => ""
            };

            if (_readyCanvas != null)
            {
                _readyCanvas.gameObject.SetActive(newPhase == TurnPhase.PrepPhase);
                if (newPhase == TurnPhase.PrepPhase)
                {
                    var defaultSprite = GameSprites.Get(GameSprites.BTN_DEFAULT);
                    if (_readyButtonImage != null && defaultSprite != null)
                        _readyButtonImage.sprite = defaultSprite;
                    if (_readyButton != null)
                        _readyButton.interactable = true;
                }
            }

            if (newPhase == TurnPhase.PrepPhase)
            {
                _hasSelectedItem = false;
                _statusText.text = "Select an item!";
                GameAudioManager.Instance?.StartFanLoop();
            }
            else if (newPhase == TurnPhase.AttackPhase)
            {
                _statusText.text = "Resolving...";
                GameAudioManager.Instance?.StopFanLoop();
            }
            else if (newPhase == TurnPhase.RoundOver)
            {
                GameAudioManager.Instance?.StopFanLoop();
                GameAudioManager.Instance?.StopEnvironment();
            }

            if (newPhase != TurnPhase.AttackPhase)
            {
                if (_crownText != null) _crownText.gameObject.SetActive(false);
                HandleAttackerChanged(-1);
            }
        }

        void HandleMatchSnapshotChanged(MatchSnapshot match)
        {
            UpdateTimerDisplay(match);
            UpdateScoreDisplay(match);
            UpdateCrown(match);
        }

        void UpdateTimerDisplay(MatchSnapshot match)
        {
            if (match.CurrentPhase != TurnPhase.PrepPhase)
            {
                _timerText.text = "";
                if (_timerSliderImage != null) _timerSliderImage.fillAmount = 0f;
                if (_clockHandRT != null) _clockHandRT.localRotation = Quaternion.identity;
                SetAlarmActive(false);
                return;
            }

            int remaining = match.RemainingTime;
            float total = match.PrepDuration;
            float ratio = total > 0f ? Mathf.Clamp01(remaining / total) : 1f;

            _timerText.text = $"{remaining}";

            if (_timerSliderImage != null)
                _timerSliderImage.fillAmount = 1f - ratio;
            if (_clockHandRT != null)
                _clockHandRT.localRotation = Quaternion.Euler(0f, 0f, ratio * 360f);

            bool shouldAlarm = remaining > 0 && remaining <= ALARM_TIME_THRESHOLD;
            SetAlarmActive(shouldAlarm);

            _timerText.color = remaining <= ALARM_TIME_THRESHOLD ? Color.red : Color.white;
        }

        void SetAlarmActive(bool active)
        {
            if (_timeAlarmObj == null) return;
            if (active && !_alarmShaking)
            {
                _timeAlarmObj.SetActive(true);
                _alarmShaking = true;
                _alarmCoroutine = StartCoroutine(AlarmShakeRoutine());
                GameAudioManager.Instance?.PlayClockTick();
            }
            else if (!active && _alarmShaking)
            {
                _alarmShaking = false;
                if (_alarmCoroutine != null) StopCoroutine(_alarmCoroutine);
                _timeAlarmObj.SetActive(false);
                GameAudioManager.Instance?.StopClockTick();
            }
        }

        IEnumerator AlarmShakeRoutine()
        {
            var rt = _timeAlarmObj.GetComponent<RectTransform>();
            var basePos = rt.anchoredPosition;
            while (_alarmShaking)
            {
                float ox = Random.Range(-8f, 8f);
                float oy = Random.Range(-4f, 4f);
                rt.anchoredPosition = basePos + new Vector2(ox, oy);
                yield return _waitAlarmShake;
            }
            rt.anchoredPosition = basePos;
        }

        void UpdateScoreDisplay(MatchSnapshot match)
        {
            _scoreText.text = $"{match.P1RoundWins} : {match.P2RoundWins}";
        }

        void UpdateCrown(MatchSnapshot match)
        {
            if (_crownText == null) return;
            bool showCrown = match.CurrentPhase == TurnPhase.AttackPhase
                             && match.FirstReadySeat != byte.MaxValue;
            _crownText.gameObject.SetActive(showCrown);
            if (showCrown)
            {
                var rt = _crownText.GetComponent<RectTransform>();
                rt.anchoredPosition = match.FirstReadySeat == 0
                    ? new Vector2(-80, 0)
                    : new Vector2(80, 0);
            }
        }

        void HandleAttackerChanged(int seatIdx)
        {
            if (_p1NameBox != null)
                _p1NameBox.color = seatIdx == 0 ? BOX_ACTIVE : BOX_NORMAL;
            if (_p2NameBox != null)
                _p2NameBox.color = seatIdx == 1 ? BOX_ACTIVE : BOX_NORMAL;
        }

        void HandleHasSelectedItemChanged(bool hasItem)
        {
            _hasSelectedItem = hasItem;
            if (hasItem)
                _statusText.text = "Item selected!";
        }

        void OnReadyClicked()
        {
            if (HoverRaycaster.Instance != null && HoverRaycaster.Instance.CurrentHovered != null)
                return;

            GameAudioManager.Instance?.PlayButtonClick();
            if (MiniGameHub.IsRunning) return;

            _commands.PressReady();
            _statusText.text = _hasSelectedItem ? "Ready!" : "Ready! (No action)";

            var pressedSprite = GameSprites.Get(GameSprites.BTN_PRESSED);
            if (_readyButtonImage != null && pressedSprite != null)
                _readyButtonImage.sprite = pressedSprite;

            if (_readyButton != null)
                _readyButton.interactable = false;
        }

        void HandleEnvironmentAnnounced(EnvironmentType env)
        {
            StopSummerVacShake();

            if (_envPanel == null || _envText == null) return;

            string name = env switch
            {
                EnvironmentType.SunnyDay => "햇살이 더 쩍쩍해집니다.",
                EnvironmentType.CoolBreeze => "시원한 바람이 불어옵니다.",
                EnvironmentType.CicadaSong => "매미 소리가 들려옵니다.",
                EnvironmentType.Kids => "근처에 어린 친구들이 서성거립니다.",
                EnvironmentType.Ambulance => "근처에 응급구조원이 대기중입니다.",
                EnvironmentType.SummerVacation => "여름방학이 얼마 남지 않았습니다.",
                EnvironmentType.HeatWaveWarning => "폭염경보가 발생했습니다.",
                _ => ""
            };

            _envText.text = name;
            _envPanel.SetActive(true);
            StartCoroutine(HideEnvPanelAfterDelay());

            GameAudioManager.Instance?.PlayEnvironment(env);

            if (env == EnvironmentType.SummerVacation)
            {
                if (_timerFillImage != null)
                    _timerFillImage.color = new Color(1f, 0.35f, 0.25f);
                _summerVacShaking = true;
                _summerVacShakeCoroutine = StartCoroutine(SummerVacShakeRoutine());
            }
        }

        void StopSummerVacShake()
        {
            if (!_summerVacShaking) return;
            _summerVacShaking = false;
            if (_summerVacShakeCoroutine != null) StopCoroutine(_summerVacShakeCoroutine);
            _summerVacShakeCoroutine = null;
            if (_timerContainerRT != null)
                _timerContainerRT.anchoredPosition = _timerContainerBasePos;
            if (_timerFillImage != null)
                _timerFillImage.color = Color.white;
        }

        IEnumerator SummerVacShakeRoutine()
        {
            const float amplitude = 3f;
            const float frequency = 6f;
            while (_summerVacShaking && _timerContainerRT != null)
            {
                float t = Time.time * frequency * Mathf.PI * 2f;
                float offsetX = Mathf.Sin(t) * amplitude;
                float offsetY = Mathf.Cos(t * 1.3f) * amplitude;
                _timerContainerRT.anchoredPosition = _timerContainerBasePos + new Vector2(offsetX, offsetY);
                yield return _waitSummerShake;
            }
        }

        IEnumerator HideEnvPanelAfterDelay()
        {
            yield return _waitEnvHide;
            if (_envPanel != null)
                _envPanel.SetActive(false);
        }

        void HandleOpponentRevealed(byte forSeatIndex, short opponentItemId)
        {
            if (forSeatIndex != _bridge.LocalSeatIndex) return;

            string itemName = opponentItemId >= 0
                ? (ItemManager.Instance?.GetItemData(opponentItemId)?.ItemName ?? "???")
                : "Not Selected";

            _statusText.text = $"Revealed: {itemName}";
            Debug.Log($"[UI] Tarot reveal: opponent selected '{itemName}' (id={opponentItemId})");
        }

        void OnDestroy()
        {
            if (_readyButton != null)
                _readyButton.onClick.RemoveListener(OnReadyClicked);

            if (_bridge != null)
            {
                _bridge.OnPhaseChanged -= HandlePhaseChanged;
                _bridge.OnMatchSnapshotChanged -= HandleMatchSnapshotChanged;
                _bridge.OnEnvironmentAnnounced -= HandleEnvironmentAnnounced;
                _bridge.OnLocalHasSelectedItemChanged -= HandleHasSelectedItemChanged;
                _bridge.OnOpponentRevealed -= HandleOpponentRevealed;
                _bridge.OnCurrentAttackerChanged -= HandleAttackerChanged;
            }
            StopSummerVacShake();
            SetAlarmActive(false);

            if (GameAudioManager.Instance != null)
            {
                GameAudioManager.Instance.StopFanLoop();
                GameAudioManager.Instance.StopClockTick();
                GameAudioManager.Instance.StopEnvironment();
            }
        }
    }
}
