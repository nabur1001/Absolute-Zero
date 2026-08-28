using System;
using System.Collections;
using AbsoluteZero.Core.Audio;
using AbsoluteZero.Core.Common;
using AbsoluteZero.UI.Game.Bridge;
using AbsoluteZero.UI.Game.Build;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.Game.Presenters
{
    public sealed class RoundResultPresenter
    {
        const float FADE_OUT_DURATION = 1f;
        const float TEXT_RISE_DURATION = 0.8f;
        const float ROUND_HOLD_DURATION = 1.5f;
        const float FADE_IN_DURATION = 1f;
        static readonly WaitForSeconds _waitHold = new(ROUND_HOLD_DURATION);

        readonly IGameDataBridge _bridge;
        readonly ILocalPlayerCommands _commands;
        readonly MonoBehaviour _host;
        readonly Image _overlay;
        readonly TextMeshProUGUI _text;
        readonly RectTransform _textRT;
        readonly Vector2 _textBasePos;
        readonly Button _lobbyButton;

        Coroutine _cinematic;
        bool _leaveInProgress;

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

            _lobbyButton.onClick.AddListener(OnBackToLobbyClicked);

            _bridge.OnRoundResult += HandleRoundResult;
            _bridge.OnMatchEnd += HandleMatchEnd;
            _bridge.OnPhaseChanged += HandlePhaseChanged;

            _overlay.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            _bridge.OnRoundResult -= HandleRoundResult;
            _bridge.OnMatchEnd -= HandleMatchEnd;
            _bridge.OnPhaseChanged -= HandlePhaseChanged;
            if (_lobbyButton != null)
                _lobbyButton.onClick.RemoveListener(OnBackToLobbyClicked);
            CancelCinematic();
        }

        void HandlePhaseChanged(TurnPhase oldPhase, TurnPhase newPhase)
        {
            if (newPhase == TurnPhase.PrepPhase)
                CancelCinematic();
        }

        void HandleRoundResult(MatchSnapshot match)
        {
            byte localSeat = _bridge.LocalSeatIndex;
            int winner = match.LastRoundWinner;

            string text;
            Color color;
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

            StartCinematic(text, color, false);
        }

        void HandleMatchEnd(MatchSnapshot match)
        {
            byte localSeat = _bridge.LocalSeatIndex;
            int winner = match.LastRoundWinner;

            bool iAmWinner = winner >= 0 && localSeat == winner;
            string text = iAmWinner ? "MATCH WIN!" : "MATCH LOSE...";
            Color color = iAmWinner
                ? new Color(1f, 0.85f, 0.2f)
                : new Color(0.5f, 0.5f, 0.6f);

            StartCinematic(text, color, true);
        }

        void StartCinematic(string text, Color textColor, bool isMatchEnd)
        {
            CancelCinematic();
            _cinematic = _host.StartCoroutine(CinematicRoutine(text, textColor, isMatchEnd));
        }

        void CancelCinematic()
        {
            if (_cinematic != null)
            {
                _host.StopCoroutine(_cinematic);
                _cinematic = null;
            }
            _overlay.gameObject.SetActive(false);
            _lobbyButton.gameObject.SetActive(false);
            _textRT.anchoredPosition = _textBasePos;
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
                _lobbyButton.gameObject.SetActive(true);
                _lobbyButton.interactable = true;
                _leaveInProgress = false;
                _cinematic = null;
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

        async void OnBackToLobbyClicked()
        {
            if (_leaveInProgress) return;
            _leaveInProgress = true;
            _lobbyButton.interactable = false;

            GameAudioManager.Instance?.PlayButtonClick();

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
    }
}
