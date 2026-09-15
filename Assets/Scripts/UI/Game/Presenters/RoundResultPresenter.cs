using System;
using System.Collections;
using AbsoluteZero.Core.Audio;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Network;
using AbsoluteZero.UI.Game.Bridge;
using AbsoluteZero.UI.Game.Build;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using GameMode = AbsoluteZero.Core.Network.GameMode;

namespace AbsoluteZero.UI.Game.Presenters
{
    public sealed class RoundResultPresenter
    {
        const float FADE_OUT_DURATION = 1f;
        const float TEXT_RISE_DURATION = 0.8f;
        const float ROUND_HOLD_DURATION = 1.5f;
        const float FADE_IN_DURATION = 1f;
        static readonly WaitForSeconds _waitHold = new(ROUND_HOLD_DURATION);
        static readonly WaitForSeconds _waitTimerTick = new(1f);
        static readonly WaitForSeconds _waitLobbyFallback = new(2f);
        static readonly WaitForSeconds _waitDeclinedLeave = new(3f);

        readonly IGameDataBridge _bridge;
        readonly ILocalPlayerCommands _commands;
        readonly MonoBehaviour _host;
        readonly Image _overlay;
        readonly TextMeshProUGUI _text;
        readonly RectTransform _textRT;
        readonly Vector2 _textBasePos;
        readonly Button _lobbyButton;
        readonly Button _rematchButton;
        readonly TextMeshProUGUI _rematchStatusText;

        Coroutine _cinematic;
        Coroutine _autoLeaveHandle;
        Coroutine _timerHandle;
        bool _leaveInProgress;

        bool _matchCompleteCinematicFinished;
        uint _lastProcessedEpoch;
        uint _activeVoteEpoch;
        bool _rematchDecisionSent;
        bool _lobbyClickPending;
        bool _declinedHandled;

        public RoundResultPresenter(IGameDataBridge bridge, ILocalPlayerCommands commands,
                                     GameHudRefs refs, MonoBehaviour host)
        {
            _bridge = bridge;
            _commands = commands;
            _host = host;
            _overlay = refs.CinematicOverlay;
            _text = refs.CinematicText;
            _textRT = _text.GetComponent<RectTransform>();
            _textBasePos = _textRT.anchoredPosition;
            _lobbyButton = refs.LobbyButton;
            _rematchButton = refs.RematchButton;
            _rematchStatusText = refs.RematchStatusText;

            _lobbyButton.onClick.AddListener(OnBackToLobbyClicked);
            _rematchButton.onClick.AddListener(OnRematchClicked);

            _bridge.OnRoundResult += HandleRoundResult;
            _bridge.OnMatchEnd += HandleMatchEnd;
            _bridge.OnPhaseChanged += HandlePhaseChanged;
            _bridge.OnMatchSnapshotChanged += HandleMatchSnapshotChanged;
            _bridge.OnRematchDecisionChanged += HandleRematchDecisionChanged;

            _overlay.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            _bridge.OnRoundResult -= HandleRoundResult;
            _bridge.OnMatchEnd -= HandleMatchEnd;
            _bridge.OnPhaseChanged -= HandlePhaseChanged;
            _bridge.OnMatchSnapshotChanged -= HandleMatchSnapshotChanged;
            _bridge.OnRematchDecisionChanged -= HandleRematchDecisionChanged;

            if (_lobbyButton != null)
                _lobbyButton.onClick.RemoveListener(OnBackToLobbyClicked);
            if (_rematchButton != null)
                _rematchButton.onClick.RemoveListener(OnRematchClicked);

            CancelCinematic();
            StopAutoLeave();
            StopTimer();
        }

        // ─── Phase / State handlers ──────────────────────────────

        void HandlePhaseChanged(TurnPhase oldPhase, TurnPhase newPhase)
        {
            if (newPhase == TurnPhase.PrepPhase)
            {
                CancelCinematic();
                ResetRematchState();
            }
        }

        void HandleMatchSnapshotChanged(MatchSnapshot match)
        {
            ReconcileRematchVoteUI();
        }

        void HandleRematchDecisionChanged(byte mask)
        {
            UpdateOpponentDecisionDisplay(mask);
        }

        // ─── Round / Match result ─────────────────────────────────

        void HandleRoundResult(MatchSnapshot match)
        {
            byte localSeat = _bridge.LocalSeatIndex;
            int winner = match.LastRoundWinner;

            string text;
            Color color;

            if (match.Mode == GameMode.Multi)
            {
                if (winner < 0)
                {
                    text = "ALL OUT!";
                    color = Color.yellow;
                }
                else if (localSeat == winner)
                {
                    text = "SURVIVED!";
                    color = new Color(0.3f, 1f, 0.4f);
                }
                else
                {
                    text = "FROZEN!";
                    color = new Color(0.5f, 0.8f, 1f);
                }
            }
            else
            {
                if (winner < 0)
                {
                    text = "DRAW!";
                    color = Color.yellow;
                }
                else
                {
                    bool iAmWinner = localSeat == winner;
                    text = iAmWinner ? "ROUND WIN!" : "ROUND LOSE...";
                    color = iAmWinner ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f);
                }
            }

            StartCinematic(text, color, false);
        }

        void HandleMatchEnd(MatchSnapshot match)
        {
            _matchCompleteCinematicFinished = false;

            byte localSeat = _bridge.LocalSeatIndex;

            if (match.Mode == GameMode.Multi)
            {
                bool iAmWinner = (match.MultiWinnerMask & (1 << localSeat)) != 0;
                string text;
                Color color;

                if (match.MultiOutcome == MultiMatchOutcome.JointVictory && iAmWinner)
                {
                    text = "JOINT VICTORY!";
                    color = new Color(1f, 0.85f, 0.2f);
                }
                else if (iAmWinner)
                {
                    text = "VICTORY!";
                    color = new Color(1f, 0.85f, 0.2f);
                }
                else
                {
                    text = "DEFEAT...";
                    color = new Color(0.5f, 0.5f, 0.6f);
                }

                StartCinematic(text, color, true);
            }
            else
            {
                int winner = match.LastRoundWinner;
                bool iAmWinner = winner >= 0 && localSeat == winner;
                string text = iAmWinner ? "MATCH WIN!" : "MATCH LOSE...";
                Color color = iAmWinner
                    ? new Color(1f, 0.85f, 0.2f)
                    : new Color(0.5f, 0.5f, 0.6f);
                StartCinematic(text, color, true);
            }
        }

        // ─── Rematch Reconciliation (v7 idempotent) ───────────────

        void ReconcileRematchVoteUI()
        {
            var match = _bridge.CurrentMatch;

            if (match.MatchState == MatchState.RematchDeclined)
            {
                if (_declinedHandled) return;
                _declinedHandled = true;

                if (_cinematic != null)
                {
                    _host.StopCoroutine(_cinematic);
                    _cinematic = null;
                }
                StopTimer();

                if (_leaveInProgress) return;

                _overlay.gameObject.SetActive(true);
                _overlay.color = Color.black;
                _overlay.raycastTarget = true;
                _text.text = "";
                _rematchButton.gameObject.SetActive(false);
                _lobbyButton.gameObject.SetActive(false);
                _rematchStatusText.gameObject.SetActive(true);
                _matchCompleteCinematicFinished = true;

                if (_lobbyClickPending)
                {
                    _rematchStatusText.text = "로비로 돌아갑니다...";
                    StopAutoLeave();
                    _leaveInProgress = true;
                    FireAndForgetLeave();
                }
                else
                {
                    _rematchStatusText.text = "재대결이 성사되지 않았습니다";
                    StopAutoLeave();
                    _autoLeaveHandle = _host.StartCoroutine(AutoLeaveRoutine(_waitDeclinedLeave));
                }
                return;
            }

            if (match.MatchState != MatchState.RematchVote) return;

            // Already open for this epoch — just refresh mask display
            if (match.RematchVoteEpoch == _activeVoteEpoch && match.RematchVoteEpoch <= _lastProcessedEpoch)
            {
                UpdateOpponentDecisionDisplay();
                return;
            }

            // 4-condition gate for initial UI activation
            if (!_matchCompleteCinematicFinished) return;

            double serverTime = GetServerTime();
            if (match.RematchVoteEpoch <= _lastProcessedEpoch) return;
            if (match.RematchDeadlineServerTime <= serverTime) return;

            _lastProcessedEpoch = match.RematchVoteEpoch;
            _activeVoteEpoch = match.RematchVoteEpoch;
            _rematchDecisionSent = false;
            _lobbyClickPending = false;

            _rematchButton.gameObject.SetActive(true);
            _rematchButton.interactable = true;
            _lobbyButton.gameObject.SetActive(true);
            _lobbyButton.interactable = true;
            _rematchStatusText.gameObject.SetActive(true);
            _rematchStatusText.text = "";

            StartTimer();
            UpdateOpponentDecisionDisplay();
        }

        void UpdateOpponentDecisionDisplay(byte? overrideMask = null)
        {
            var match = _bridge.CurrentMatch;
            if (match.MatchState != MatchState.RematchVote) return;
            if (match.RematchVoteEpoch != _activeVoteEpoch) return;

            byte activeMask = overrideMask ?? match.RematchDecisionMask;
            byte localSeat = _bridge.LocalSeatIndex;
            byte opponentBit = (byte)(1 << (1 - localSeat));

            if ((activeMask & opponentBit) != 0)
            {
                if (_rematchDecisionSent && !_lobbyClickPending)
                    _rematchStatusText.text = "상대가 선택 완료";
            }
            else if (_rematchDecisionSent && !_lobbyClickPending)
            {
                _rematchStatusText.text = "수락함 — 상대 대기 중...";
            }
        }

        void ResetRematchState()
        {
            _rematchDecisionSent = false;
            _matchCompleteCinematicFinished = false;
            _lobbyClickPending = false;
            _declinedHandled = false;
            StopAutoLeave();
            StopTimer();
        }

        // ─── Timer ────────────────────────────────────────────────

        void StartTimer()
        {
            StopTimer();
            _timerHandle = _host.StartCoroutine(TimerRoutine());
        }

        void StopTimer()
        {
            if (_timerHandle != null)
            {
                if (_host != null) _host.StopCoroutine(_timerHandle);
                _timerHandle = null;
            }
        }

        IEnumerator TimerRoutine()
        {
            while (true)
            {
                var match = _bridge.CurrentMatch;
                if (match.MatchState != MatchState.RematchVote) yield break;

                if (!_rematchDecisionSent)
                {
                    double remaining = match.RematchDeadlineServerTime - GetServerTime();
                    int seconds = Mathf.CeilToInt(Mathf.Max(0f, (float)remaining));
                    _rematchStatusText.text = $"재대결? ({seconds}초)";
                }

                yield return _waitTimerTick;
            }
        }

        // ─── Button handlers ──────────────────────────────────────

        void OnRematchClicked()
        {
            if (_rematchDecisionSent) return;
            _rematchDecisionSent = true;

            if (GameAudioManager.Instance != null)
                GameAudioManager.Instance.PlayButtonClick();

            _commands.SubmitRematchDecision(true, _activeVoteEpoch);
            _rematchButton.interactable = false;
            _lobbyButton.interactable = false;
            StopTimer();
            _rematchStatusText.text = "수락함 — 상대 대기 중...";
        }

        async void OnBackToLobbyClicked()
        {
            if (_leaveInProgress) return;

            if (GameAudioManager.Instance != null)
                GameAudioManager.Instance.PlayButtonClick();

            var match = _bridge.CurrentMatch;
            if (match.MatchState == MatchState.RematchVote && !_rematchDecisionSent)
            {
                _rematchDecisionSent = true;
                _lobbyClickPending = true;
                _commands.SubmitRematchDecision(false, _activeVoteEpoch);
                _rematchButton.interactable = false;
                _lobbyButton.interactable = false;
                StopTimer();
                _rematchStatusText.text = "로비로 돌아갑니다...";
                _autoLeaveHandle = _host.StartCoroutine(AutoLeaveRoutine(_waitLobbyFallback));
                return;
            }

            _leaveInProgress = true;
            _lobbyButton.interactable = false;

            try
            {
                await _commands.LeaveMatchAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RoundResultPresenter] Leave failed: {e.Message}");
                _lobbyButton.interactable = true;
                _leaveInProgress = false;
            }
        }

        // ─── Cinematic ───────────────────────────────────────────

        void StartCinematic(string text, Color textColor, bool isMatchEnd)
        {
            CancelCinematic();
            if (isMatchEnd)
                _matchCompleteCinematicFinished = false;
            _cinematic = _host.StartCoroutine(CinematicRoutine(text, textColor, isMatchEnd));
        }

        void CancelCinematic()
        {
            if (_cinematic != null)
            {
                if (_host != null) _host.StopCoroutine(_cinematic);
                _cinematic = null;
            }
            // Scene unload may destroy the runtime Canvas before this presenter.
            if (_overlay != null) _overlay.gameObject.SetActive(false);
            if (_lobbyButton != null) _lobbyButton.gameObject.SetActive(false);
            if (_rematchButton != null) _rematchButton.gameObject.SetActive(false);
            if (_rematchStatusText != null) _rematchStatusText.gameObject.SetActive(false);
            if (_textRT != null) _textRT.anchoredPosition = _textBasePos;
            _matchCompleteCinematicFinished = true;
        }

        IEnumerator CinematicRoutine(string text, Color textColor, bool isMatchEnd)
        {
            _overlay.gameObject.SetActive(true);
            _overlay.raycastTarget = true;
            _overlay.color = new Color(0f, 0f, 0f, 0f);
            _text.text = text;
            _text.color = new Color(textColor.r, textColor.g, textColor.b, 0f);
            _textRT.anchoredPosition = _textBasePos;
            _lobbyButton.gameObject.SetActive(false);
            _rematchButton.gameObject.SetActive(false);
            _rematchStatusText.gameObject.SetActive(false);

            float t = 0f;
            while (t < FADE_OUT_DURATION)
            {
                t += Time.deltaTime;
                float a = Mathf.SmoothStep(0f, 1f, t / FADE_OUT_DURATION);
                _overlay.color = new Color(0f, 0f, 0f, a);
                yield return null;
            }
            _overlay.color = Color.black;

            float startY = _textBasePos.y - 20f;
            t = 0f;
            while (t < TEXT_RISE_DURATION)
            {
                t += Time.deltaTime;
                float p = Mathf.SmoothStep(0f, 1f, t / TEXT_RISE_DURATION);
                _text.color = new Color(textColor.r, textColor.g, textColor.b, p);
                _textRT.anchoredPosition = new Vector2(_textBasePos.x, Mathf.Lerp(startY, _textBasePos.y, p));
                yield return null;
            }
            _text.color = textColor;
            _textRT.anchoredPosition = _textBasePos;

            if (isMatchEnd)
            {
                _matchCompleteCinematicFinished = true;
                _cinematic = null;

                var match = _bridge.CurrentMatch;
                if (match.Mode == GameMode.Multi)
                {
                    Debug.Log($"[MatchResult] Visible seq={match.MultiDecidingSequence} local={_bridge.LocalSeatIndex} winners={match.MultiWinnerMask}");
                    _lobbyButton.gameObject.SetActive(true);
                    _lobbyButton.interactable = true;
                    _rematchButton.gameObject.SetActive(false);
                    _rematchStatusText.gameObject.SetActive(true);
                    _rematchStatusText.text = "매치 종료";
                    StopAutoLeave();
                    _autoLeaveHandle = _host.StartCoroutine(AutoLeaveRoutine(_waitDeclinedLeave));
                }
                else
                {
                    ReconcileRematchVoteUI();
                }
                yield break;
            }

            yield return _waitHold;

            t = 0f;
            while (t < FADE_IN_DURATION)
            {
                t += Time.deltaTime;
                float a = Mathf.SmoothStep(1f, 0f, t / FADE_IN_DURATION);
                _overlay.color = new Color(0f, 0f, 0f, a);
                _text.color = new Color(textColor.r, textColor.g, textColor.b, a);
                yield return null;
            }

            _overlay.color = new Color(0f, 0f, 0f, 0f);
            _text.color = new Color(textColor.r, textColor.g, textColor.b, 0f);
            _overlay.raycastTarget = false;
            _overlay.gameObject.SetActive(false);
            _cinematic = null;
        }

        // ─── Auto-leave ──────────────────────────────────────────

        IEnumerator AutoLeaveRoutine(WaitForSeconds delay)
        {
            yield return delay;
            _autoLeaveHandle = null;
            if (!_leaveInProgress)
            {
                _leaveInProgress = true;
                FireAndForgetLeave();
            }
        }

        async void FireAndForgetLeave()
        {
            try
            {
                await _commands.LeaveMatchAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RoundResultPresenter] Auto-leave failed: {e.Message}");
                _leaveInProgress = false;
            }
        }

        void StopAutoLeave()
        {
            if (_autoLeaveHandle != null)
            {
                if (_host != null) _host.StopCoroutine(_autoLeaveHandle);
                _autoLeaveHandle = null;
            }
        }

        static double GetServerTime()
        {
            var nm = NetworkManager.Singleton;
            return nm != null ? nm.ServerTime.Time : 0;
        }
    }
}
