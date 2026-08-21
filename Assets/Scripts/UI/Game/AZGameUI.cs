using System;
using System.Collections;
using AbsoluteZero.Core.Audio;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Inventory;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Match;
using AbsoluteZero.Core.Player;
using AbsoluteZero.Core.Turn;
using AbsoluteZero.UI.Emote;
using AbsoluteZero.UI.MiniGame;
using AbsoluteZero.Core.Session;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AbsoluteZero.UI.Game
{
    public class AZGameUI : MonoBehaviour
    {
        public static AZGameUI Instance { get; private set; }

        TurnManager _tm;
        MatchManager _mm;
        PlayerState _localPlayer;
        PlayerState _p1Cached;
        PlayerState _p2Cached;

        Canvas _overlayCanvas;
        TextMeshProUGUI _phaseText;
        TextMeshProUGUI _timerText;
        TextMeshProUGUI _statusText;
        TextMeshProUGUI _scoreText;
        TextMeshProUGUI _myTempText;
        Slider _myHpSlider;

        Image _timerFillImage;
        Image _timerSliderImage;
        RectTransform _clockHandRT;
        GameObject _timeAlarmObj;
        Image _timeAlarmImage;
        bool _alarmShaking;
        Coroutine _alarmCoroutine;

        Canvas _oppBarCanvas;
        TextMeshProUGUI _oppTempText;
        Slider _oppHpSlider;
        Image _myHpFillImage;
        Image _oppHpFillImage;

        Canvas _readyCanvas;
        Button _readyButton;
        Image _readyButtonImage;

        GameObject _gameOverPanel;
        TextMeshProUGUI _gameOverText;

        InventoryPresenter _presenter;

        GameObject _envPanel;
        TextMeshProUGUI _envText;

        RectTransform _timerContainerRT;
        Vector2 _timerContainerBasePos;
        bool _summerVacShaking;
        Coroutine _summerVacShakeCoroutine;

        bool _oppBarAttached;

        bool _uiBuilt;

        float _displayedMyTemp = 37f;
        float _displayedOppTemp = 37f;
        float? _myTempOverride;
        float? _oppTempOverride;
        const float HP_LERP_SPEED = 6f;

        static readonly WaitForSeconds _waitEnvHide = new(3.5f);
        static readonly WaitForSeconds _waitAlarmShake = new(0.05f);
        static readonly WaitForSeconds _waitSummerShake = new(0.016f);

        static readonly Color HP_COLOR_GREEN = new(0.39f, 0.78f, 0.31f, 1f);
        static readonly Color HP_COLOR_PINK = new(0.90f, 0.47f, 0.59f, 1f);
        static readonly Color HP_COLOR_SKY = new(0.39f, 0.71f, 0.92f, 1f);
        static readonly Color HP_COLOR_BLUE = new(0.20f, 0.39f, 0.86f, 1f);

        static readonly Vector3 OPP_BAR_LOCAL_OFFSET = new(0f, 2.2f, 0f);
        static readonly Vector3 READY_BTN_POS = new(0f, 0.35f, 1.2f);
        const float WORLD_CANVAS_SCALE = 0.005f;
        const float OPP_BAR_SCALE = 0.007f;
        const int ALARM_TIME_THRESHOLD = 5;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            EnsureEventSystem();
            BuildOverlayUI();
            BuildOppBarWorldUI();
            BuildReadyWorldUI();
            _uiBuilt = true;

            var presenterGO = new GameObject("InventoryPresenter");
            var spawnRoot = GameObject.Find("MyItemSpawnRoot");
            if (spawnRoot != null)
                presenterGO.transform.position = spawnRoot.transform.position;
            _presenter = presenterGO.AddComponent<InventoryPresenter>();
            _presenter.OnWorldItemClicked += OnItemClicked;

            var hubGO = new GameObject("MiniGameHub");
            hubGO.AddComponent<MiniGameHub>();
            MiniGameHub.OnFinishedLocal += OnMiniGameFinished;

            SpawnStayItemFans();
            EnsureAudioManager();
        }

        [Header("=== Fan Tuning (live-adjustable in Inspector during play) ===")]
        [SerializeField] float playerFanScale = 0.54f;
        [SerializeField] float enemyFanScale = 0.95f;
        [SerializeField] float fanLiftBase = 1.4f;
        [SerializeField] Vector3 fanHeadCenter = new(-0.16f, 0.60f, 0f);
        [SerializeField] float fanBladeScale = 1.2f;
        [SerializeField] float fanBladeSquashX = 0.82f;
        [SerializeField] Vector2 fanGrilleScale = new(0.92f, 1.02f);
        [SerializeField] float fanBladesZ = -0.015f;
        [SerializeField] float fanGrilleZ = -0.03f;

        void SpawnStayItemFans()
        {
            var bodySprite = Resources.Load<Sprite>("Fan/fan_body");
            var bladesSprite = Resources.Load<Sprite>("Fan/fan_blades");
            var grilleSprite = Resources.Load<Sprite>("Fan/fan_grille");
            var fallback = GameSprites.GetStayItemSprite();

            SpawnFanAt("PlayerStayItem", true, bodySprite, bladesSprite, grilleSprite, fallback);
            SpawnFanAt("EnemyStayItem", false, bodySprite, bladesSprite, grilleSprite, fallback);
        }

        void SpawnFanAt(string markerName, bool isPlayer,
            Sprite bodySprite, Sprite bladesSprite, Sprite grilleSprite, Sprite fallback)
        {
            var marker = GameObject.Find(markerName);
            if (marker == null) return;

            float s = isPlayer ? playerFanScale : enemyFanScale;
            int playerIndex = isPlayer ? -1 : -2;

            var go = new GameObject($"{markerName}_Fan");
            go.transform.SetParent(marker.transform, false);
            go.transform.localPosition = new Vector3(0f, fanLiftBase * s, 0f);
            go.transform.localScale = isPlayer ? Vector3.one * s : new Vector3(-s, s, s);

            var bodySr = go.AddComponent<SpriteRenderer>();
            bodySr.sprite = bodySprite != null ? bodySprite : fallback;
            bodySr.sortingOrder = 3;

            if (bodySprite == null || bladesSprite == null) return;

            var head = new GameObject("Head");
            head.transform.SetParent(go.transform, false);
            head.transform.localPosition = fanHeadCenter;

            if (grilleSprite != null)
            {
                var grille = new GameObject("Grille");
                grille.transform.SetParent(head.transform, false);
                grille.transform.localPosition = new Vector3(0f, 0f, fanGrilleZ);
                grille.transform.localScale = new Vector3(fanGrilleScale.x, fanGrilleScale.y, 1f);
                var grilleSr = grille.AddComponent<SpriteRenderer>();
                grilleSr.sprite = grilleSprite;
                grilleSr.sortingOrder = 5;
            }

            var pivot = new GameObject("BladePivot");
            pivot.transform.SetParent(head.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, fanBladesZ);
            pivot.transform.localScale = new Vector3(fanBladeSquashX, 1f, 1f);

            var blades = new GameObject("Blades");
            blades.transform.SetParent(pivot.transform, false);
            blades.transform.localScale = Vector3.one * fanBladeScale;
            var bladeSr = blades.AddComponent<SpriteRenderer>();
            bladeSr.sprite = bladesSprite;
            bladeSr.sortingOrder = 4;

            var spinner = go.AddComponent<FanBladeSpinner>();
            spinner.Bind(blades.transform, playerIndex);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying) ReapplyFanTuning();
        }

        void ReapplyFanTuning()
        {
            ReapplyFanOne("PlayerStayItem_Fan", true);
            ReapplyFanOne("EnemyStayItem_Fan", false);
        }

        void ReapplyFanOne(string fanName, bool isPlayer)
        {
            var go = GameObject.Find(fanName);
            if (go == null) return;

            float s = isPlayer ? playerFanScale : enemyFanScale;
            go.transform.localPosition = new Vector3(0f, fanLiftBase * s, 0f);
            go.transform.localScale = isPlayer ? Vector3.one * s : new Vector3(-s, s, s);

            var head = go.transform.Find("Head");
            if (head == null) return;
            head.localPosition = fanHeadCenter;

            var grille = head.Find("Grille");
            if (grille != null)
            {
                grille.localPosition = new Vector3(0f, 0f, fanGrilleZ);
                grille.localScale = new Vector3(fanGrilleScale.x, fanGrilleScale.y, 1f);
            }

            var pivot = head.Find("BladePivot");
            if (pivot != null)
            {
                pivot.localPosition = new Vector3(0f, 0f, fanBladesZ);
                pivot.localScale = new Vector3(fanBladeSquashX, 1f, 1f);
                var bladesTf = pivot.Find("Blades");
                if (bladesTf != null) bladesTf.localScale = Vector3.one * fanBladeScale;
            }
        }
#endif

        void EnsureAudioManager()
        {
            if (GameAudioManager.Instance == null)
            {
                var go = new GameObject("GameAudioManager");
                go.AddComponent<GameAudioManager>();
                DontDestroyOnLoad(go);
            }
            GameAudioManager.Instance?.PlayBGM();
        }

        void Update()
        {
            if (!_uiBuilt) return;

            if (_tm == null)
            {
                _tm = TurnManager.Instance;
                if (_tm == null) return;
                _mm = FindAnyObjectByType<MatchManager>();
                SubscribeToEvents();
            }

            if (_localPlayer == null)
                FindLocalPlayer();

            if (_p1Cached == null || _p2Cached == null)
                FindAllPlayers();

            UpdateTimerDisplay();
            UpdateTempDisplay();
            UpdateScoreDisplay();
            UpdateOppBarTransform();

        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            TurnManager.OnOpponentRevealed -= OnOpponentRevealed;
            TurnManager.OnEnvironmentAnnounced -= OnEnvironmentAnnounced;
            CombatVFXManager.OnTempOverridesClear -= ClearTempOverrides;
            CombatVFXManager.OnTempTargetsOverride -= OverrideTempTargets;
            CombatVFXManager.OnPlayerTempOverride -= OverridePlayerTemp;
            if (_tm != null)
            {
                _tm.CurrentPhase.OnValueChanged -= OnPhaseChanged;
                _tm.LastRoundWinner.OnValueChanged -= OnWinnerChanged;
            }
            if (_localPlayer != null)
                _localPlayer.HasSelectedItem.OnValueChanged -= OnHasSelectedItemChanged;
            MiniGameHub.OnFinishedLocal -= OnMiniGameFinished;
            StopSummerVacShake();

            if (GameAudioManager.Instance != null)
            {
                GameAudioManager.Instance.StopFanLoop();
                GameAudioManager.Instance.StopClockTick();
                GameAudioManager.Instance.StopEnvironment();
                GameAudioManager.Instance.StopBGM();
            }
            if (_presenter != null)
                _presenter.OnWorldItemClicked -= OnItemClicked;
            CancelInvoke();
        }

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
        }

        void FindLocalPlayer()
        {
            if (NetworkManager.Singleton == null) return;

            var localObj = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
            if (localObj == null) return;

            var ps = localObj.GetComponent<PlayerState>();
            if (ps == null) return;

            _localPlayer = ps;
            _localPlayer.HasSelectedItem.OnValueChanged += OnHasSelectedItemChanged;
        }

        void FindAllPlayers()
        {
            var players = FindObjectsByType<PlayerState>(FindObjectsSortMode.None);
            if (players.Length < 2) return;

            if (players[0].OwnerClientId <= players[1].OwnerClientId)
            {
                _p1Cached = players[0];
                _p2Cached = players[1];
            }
            else
            {
                _p1Cached = players[1];
                _p2Cached = players[0];
            }
        }

        PlayerState GetOpponentPlayer()
        {
            if (_localPlayer == null || _p1Cached == null || _p2Cached == null) return null;
            return _p1Cached.OwnerClientId == _localPlayer.OwnerClientId ? _p2Cached : _p1Cached;
        }

        void SubscribeToEvents()
        {
            _tm.CurrentPhase.OnValueChanged += OnPhaseChanged;
            _tm.LastRoundWinner.OnValueChanged += OnWinnerChanged;
            TurnManager.OnOpponentRevealed += OnOpponentRevealed;
            TurnManager.OnEnvironmentAnnounced += OnEnvironmentAnnounced;
            CombatVFXManager.OnTempOverridesClear += ClearTempOverrides;
            CombatVFXManager.OnTempTargetsOverride += OverrideTempTargets;
            CombatVFXManager.OnPlayerTempOverride += OverridePlayerTemp;
            OnPhaseChanged(TurnPhase.WaitingForPlayers, _tm.CurrentPhase.Value);
        }

        void OnPhaseChanged(TurnPhase oldPhase, TurnPhase newPhase)
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
                SnapTempDisplay();
                _statusText.text = "Select an item!";
                _gameOverPanel.SetActive(false);
                GameAudioManager.Instance?.StartFanLoop();
            }
            else if (newPhase == TurnPhase.AttackPhase)
            {
                _statusText.text = "Resolving...";
                _gameOverPanel.SetActive(false);
                GameAudioManager.Instance?.StopFanLoop();
            }
            else if (newPhase == TurnPhase.RoundOver)
            {
                GameAudioManager.Instance?.StopFanLoop();
                GameAudioManager.Instance?.StopEnvironment();
                Invoke(nameof(ShowRoundResult), 0.15f);
            }
        }

        void OnWinnerChanged(int oldVal, int newVal)
        {
            if (_tm.CurrentPhase.Value == TurnPhase.RoundOver)
                ShowRoundResult();
        }

        void OnHasSelectedItemChanged(bool oldVal, bool newVal)
        {
            if (newVal)
                _statusText.text = "Item selected!";
        }

        void ShowRoundResult()
        {
            _gameOverPanel.SetActive(true);

            int winner = _tm.LastRoundWinner.Value;
            bool isMatchDone = _mm != null && _mm.IsMatchComplete();

            if (winner < 0)
            {
                _gameOverText.text = "DRAW!";
                _gameOverText.color = Color.yellow;
            }
            else
            {
                bool iAmWinner = _localPlayer != null && _localPlayer.SyncedPlayerIndex.Value == winner;
                if (isMatchDone)
                {
                    _gameOverText.text = iAmWinner ? "MATCH WIN!" : "MATCH LOSE...";
                    _gameOverText.color = iAmWinner ? new Color(1f, 0.85f, 0.2f) : new Color(0.5f, 0.5f, 0.6f);
                }
                else
                {
                    _gameOverText.text = iAmWinner ? "ROUND WIN!" : "ROUND LOSE...";
                    _gameOverText.color = iAmWinner ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f);
                    _statusText.text = "Next round starting...";
                }
            }
        }

        void UpdateTimerDisplay()
        {
            if (_tm == null || _tm.CurrentPhase.Value != TurnPhase.PrepPhase)
            {
                _timerText.text = "";
                if (_timerSliderImage != null) _timerSliderImage.fillAmount = 0f;
                if (_clockHandRT != null) _clockHandRT.localRotation = Quaternion.identity;
                SetAlarmActive(false);
                return;
            }

            int remaining = _tm.RemainingTime.Value;
            float total = _tm.PrepDuration.Value;
            float ratio = total > 0f ? Mathf.Clamp01(remaining / total) : 1f;

            _timerText.text = $"{remaining}";

            if (_timerSliderImage != null)
                _timerSliderImage.fillAmount = 1f - ratio;
            if (_clockHandRT != null)
                _clockHandRT.localRotation = Quaternion.Euler(0f, 0f, ratio * 360f);

            bool shouldAlarm = remaining > 0 && remaining <= ALARM_TIME_THRESHOLD;
            SetAlarmActive(shouldAlarm);

            if (remaining <= ALARM_TIME_THRESHOLD)
                _timerText.color = Color.red;
            else
                _timerText.color = Color.white;
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
                float ox = UnityEngine.Random.Range(-8f, 8f);
                float oy = UnityEngine.Random.Range(-4f, 4f);
                rt.anchoredPosition = basePos + new Vector2(ox, oy);
                yield return _waitAlarmShake;
            }
            rt.anchoredPosition = basePos;
        }

        void UpdateTempDisplay()
        {
            float dt = Time.deltaTime;

            if (_localPlayer != null)
            {
                float myTarget = _myTempOverride ?? _localPlayer.Temperature.Value;
                _displayedMyTemp = Mathf.MoveTowards(_displayedMyTemp, myTarget, HP_LERP_SPEED * dt);
                _myTempText.text = $"{_displayedMyTemp:F0}°";
                if (_myHpSlider != null)
                    _myHpSlider.value = Mathf.Clamp01(_displayedMyTemp / 37f);
                if (_myHpFillImage != null)
                    _myHpFillImage.color = GetTempColor(_displayedMyTemp);
            }

            var opp = GetOpponentPlayer();
            if (opp != null)
            {
                float oppTarget = _oppTempOverride ?? opp.Temperature.Value;
                _displayedOppTemp = Mathf.MoveTowards(_displayedOppTemp, oppTarget, HP_LERP_SPEED * dt);
                _oppTempText.text = $"{_displayedOppTemp:F0}°";
                if (_oppHpSlider != null)
                    _oppHpSlider.value = Mathf.Clamp01(_displayedOppTemp / 37f);
                if (_oppHpFillImage != null)
                    _oppHpFillImage.color = GetTempColor(_displayedOppTemp);
            }
        }

        public void OverrideTempTargets(float p0Temp, float p1Temp)
        {
            if (_localPlayer == null) return;
            int myIdx = _localPlayer.PlayerIndex;
            _myTempOverride = myIdx == 0 ? p0Temp : p1Temp;
            _oppTempOverride = myIdx == 0 ? p1Temp : p0Temp;
        }

        public void OverridePlayerTemp(int playerIndex, float temp)
        {
            if (_localPlayer == null) return;
            if (playerIndex == _localPlayer.PlayerIndex)
                _myTempOverride = temp;
            else
                _oppTempOverride = temp;
        }

        public void SnapTempDisplay()
        {
            if (_localPlayer != null)
                _displayedMyTemp = _localPlayer.Temperature.Value;
            var opp = GetOpponentPlayer();
            if (opp != null)
                _displayedOppTemp = opp.Temperature.Value;
        }

        public void ClearTempOverrides()
        {
            _myTempOverride = null;
            _oppTempOverride = null;
        }

        static Color GetTempColor(float temp)
        {
            if (temp >= 30f)
            {
                float t = Mathf.InverseLerp(37f, 30f, temp);
                return Color.Lerp(HP_COLOR_GREEN, HP_COLOR_PINK, t);
            }
            if (temp >= 20f)
            {
                float t = Mathf.InverseLerp(30f, 20f, temp);
                return Color.Lerp(HP_COLOR_PINK, HP_COLOR_SKY, t);
            }
            if (temp >= 10f)
            {
                float t = Mathf.InverseLerp(20f, 10f, temp);
                return Color.Lerp(HP_COLOR_SKY, HP_COLOR_BLUE, t);
            }
            return HP_COLOR_BLUE;
        }

        void UpdateScoreDisplay()
        {
            if (_mm == null) return;
            _scoreText.text = $"P1  {MatchScoreView.GetRoundWins(_mm, 0)} : {MatchScoreView.GetRoundWins(_mm, 1)}  P2";
        }

        void UpdateOppBarTransform()
        {
            if (_oppBarCanvas == null) return;

            if (!_oppBarAttached)
            {
                Transform target = null;

                var opp = GetOpponentPlayer();
                if (opp != null)
                    target = opp.transform;

                if (target == null)
                {
                    var enemyGO = GameObject.Find("EnemyPlayer");
                    if (enemyGO != null)
                        target = enemyGO.transform;
                }

                if (target != null)
                {
                    _oppBarCanvas.transform.SetParent(target, false);
                    _oppBarCanvas.transform.localPosition = OPP_BAR_LOCAL_OFFSET;
                    _oppBarAttached = true;
                }
            }

            var cam = Camera.main;
            if (cam == null) return;

            _oppBarCanvas.worldCamera = cam;
            _oppBarCanvas.transform.rotation = cam.transform.rotation;
        }

        void OnMiniGameFinished(byte slotIndex, bool success)
        {
            if (_statusText != null)
                _statusText.text = success ? "Mini-game clear!" : "Mini-game failed...";
        }

        void OnItemClicked(int slotIndex)
        {
            if (_localPlayer == null) return;
            if (_localPlayer.IsReady.Value) return;
            if (MiniGameHub.IsRunning) return;
            if (_presenter == null) return;

            var itemData = _presenter.GetLocalItemData(slotIndex);
            if (itemData == null) return;

            if (_localPlayer.HasSelectedItem.Value)
                return;

            GameAudioManager.Instance?.PlayButtonClick();
            _localPlayer.SelectItemServerRpc((byte)slotIndex);
            _presenter.NotifyItemConfirmed(slotIndex);

            _statusText.text = $"Selected: {itemData.ItemName}";
        }

        void OnReadyClicked()
        {
            if (HoverRaycaster.Instance != null && HoverRaycaster.Instance.CurrentHovered != null)
                return;

            GameAudioManager.Instance?.PlayButtonClick();
            if (_localPlayer == null) return;
            if (MiniGameHub.IsRunning) return;

            _localPlayer.PressReadyServerRpc();
            _statusText.text = _localPlayer.HasSelectedItem.Value
                ? "Ready!"
                : "Ready! (No action)";

            var pressedSprite = GameSprites.Get(GameSprites.BTN_PRESSED);
            if (_readyButtonImage != null && pressedSprite != null)
                _readyButtonImage.sprite = pressedSprite;

            if (_readyButton != null)
                _readyButton.interactable = false;
        }

        async void OnBackToLobbyClicked()
        {
            GameAudioManager.Instance?.PlayButtonClick();
            try
            {
                var coordinator = NetworkSessionCoordinator.Instance;
                if (coordinator != null)
                    await coordinator.LeaveAsync();
                else
                {
                    var sessionManager = Core.Network.SessionManager.Instance;
                    if (sessionManager != null)
                        sessionManager.Disconnect();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AZGameUI] Leave failed: {e.Message}");
            }
        }


        #region UI Construction

        void BuildOverlayUI()
        {
            var canvasGO = new GameObject("OverlayCanvas");
            _overlayCanvas = canvasGO.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 0;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f;

            canvasGO.AddComponent<GraphicRaycaster>();

            Transform root = canvasGO.transform;

            // === TOP-LEFT: My HP Bar (sprite) ===
            BuildMyHpBar(root);

            // === TOP-RIGHT: Clock + Timer ===
            BuildClockTimer(root);

            // === TOP-CENTER: Phase + Score ===
            _phaseText = CreateText(root, "PhaseText",
                new Vector2(0, -30), new Vector2(400, 50), "WAITING", 28);
            AnchorTopCenter(_phaseText.GetComponent<RectTransform>());

            _scoreText = CreateText(root, "ScoreText",
                new Vector2(0, -75), new Vector2(300, 30), "P1  0 : 0  P2", 20);
            _scoreText.color = new Color(0.9f, 0.9f, 0.6f);
            AnchorTopCenter(_scoreText.GetComponent<RectTransform>());

            // === BOTTOM: Status ===
            _statusText = CreateText(root, "StatusText",
                new Vector2(0, 30), new Vector2(600, 35), "Waiting for players...", 20);
            _statusText.color = new Color(0.8f, 0.8f, 0.8f);
            AnchorBottomCenter(_statusText.GetComponent<RectTransform>());

            _gameOverPanel = new GameObject("GameOverPanel");
            _gameOverPanel.transform.SetParent(root, false);
            var goRect = _gameOverPanel.AddComponent<RectTransform>();
            goRect.anchoredPosition = new Vector2(0, -10);
            goRect.sizeDelta = new Vector2(500, 150);

            CreatePanel(_gameOverPanel.transform, "GOBg", Vector2.zero,
                new Vector2(500, 150), new Color(0.05f, 0.05f, 0.1f, 0.95f));

            _gameOverText = CreateText(_gameOverPanel.transform, "GOText",
                new Vector2(0, 25), new Vector2(400, 60), "", 42);

            var lobbyBtn = CreateButton(_gameOverPanel.transform, "LobbyBtn",
                new Vector2(0, -40), new Vector2(200, 45), "BACK TO LOBBY",
                new Color(0.5f, 0.3f, 0.3f));
            lobbyBtn.onClick.AddListener(OnBackToLobbyClicked);

            _gameOverPanel.SetActive(false);

            BuildEnvironmentPanel(root);
        }

        void BuildMyHpBar(Transform root)
        {
            var container = new GameObject("MyHpBar");
            container.transform.SetParent(root, false);
            var cRect = container.AddComponent<RectTransform>();
            cRect.anchoredPosition = new Vector2(395.8f, -81.9f);
            cRect.sizeDelta = new Vector2(600, 100);
            cRect.anchorMin = new Vector2(0f, 1f);
            cRect.anchorMax = new Vector2(0f, 1f);
            cRect.pivot = new Vector2(0.5f, 0.5f);

            _myHpSlider = container.AddComponent<Slider>();
            _myHpSlider.minValue = 0f;
            _myHpSlider.maxValue = 1f;
            _myHpSlider.value = 1f;
            _myHpSlider.interactable = false;
            _myHpSlider.direction = Slider.Direction.LeftToRight;

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(container.transform, false);
            var bgRect = bgGO.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.25f);
            bgRect.anchorMax = new Vector2(1f, 0.75f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.sprite = GameSprites.Get(GameSprites.UI_BAR_BG);
            bgImg.color = Color.white;

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(container.transform, false);
            var faRect = fillArea.AddComponent<RectTransform>();
            faRect.anchorMin = new Vector2(0f, 0.25f);
            faRect.anchorMax = new Vector2(1f, 0.75f);
            faRect.offsetMin = Vector2.zero;
            faRect.offsetMax = Vector2.zero;

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillArea.transform, false);
            var fillRect = fillGO.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.sprite = GameSprites.Get(GameSprites.UI_BAR_FILL);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.color = HP_COLOR_GREEN;
            _myHpFillImage = fillImg;

            _myHpSlider.fillRect = fillRect;

            var outlineGO = new GameObject("Outline");
            outlineGO.transform.SetParent(container.transform, false);
            var olRect = outlineGO.AddComponent<RectTransform>();
            olRect.anchoredPosition = new Vector2(3.6f, 0f);
            olRect.sizeDelta = new Vector2(620, 80);
            var olImg = outlineGO.AddComponent<Image>();
            olImg.sprite = GameSprites.Get(GameSprites.UI_BAR_OUTLINE);
            olImg.raycastTarget = false;

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(container.transform, false);
            var iconRect = iconGO.AddComponent<RectTransform>();
            iconRect.anchoredPosition = new Vector2(-330.79f, -25.85f);
            iconRect.sizeDelta = new Vector2(100, 200);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = GameSprites.Get(GameSprites.UI_THERMO_ICON);
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            BuildGiftLines(container.transform);

            _myTempText = CreateText(container.transform, "MyTemp",
                new Vector2(0, -55), new Vector2(200, 24), "37°", 20);
            _myTempText.alignment = TextAlignmentOptions.Center;
        }

        void BuildGiftLines(Transform parent)
        {
            var giftRoot = new GameObject("GiftLine");
            giftRoot.transform.SetParent(parent, false);
            var grRect = giftRoot.AddComponent<RectTransform>();
            grRect.anchorMin = Vector2.zero;
            grRect.anchorMax = Vector2.one;
            grRect.offsetMin = Vector2.zero;
            grRect.offsetMax = Vector2.zero;

            float[] xPositions = { 159.1f, 324.6f, 485.8f };
            string[] iconNames = { GameSprites.UI_GIFT_ICON_A, GameSprites.UI_GIFT_ICON_B, GameSprites.UI_GIFT_ICON_C };
            Vector2[] iconSizes = { new(70, 70), new(70, 35), new(35, 35) };
            float[] iconYOffsets = { -50f, -30f, -30f };

            for (int i = 0; i < 3; i++)
            {
                var lineGO = new GameObject($"line_{(i + 1) * 10}");
                lineGO.transform.SetParent(giftRoot.transform, false);
                var lineRect = lineGO.AddComponent<RectTransform>();
                lineRect.anchorMin = new Vector2(0f, 0.5f);
                lineRect.anchorMax = new Vector2(0f, 0.5f);
                lineRect.pivot = new Vector2(0.5f, 0.5f);
                lineRect.anchoredPosition = new Vector2(xPositions[i], 0f);
                lineRect.sizeDelta = new Vector2(7.45f, 70f);
                var lineImg = lineGO.AddComponent<Image>();
                lineImg.sprite = GameSprites.Get(GameSprites.UI_GIFT_LINE);
                lineImg.raycastTarget = false;

                var giftGO = new GameObject("Icon_Gift");
                giftGO.transform.SetParent(lineGO.transform, false);
                var giftRect = giftGO.AddComponent<RectTransform>();
                giftRect.anchoredPosition = new Vector2(0f, iconYOffsets[i]);
                giftRect.sizeDelta = iconSizes[i];
                var giftImg = giftGO.AddComponent<Image>();
                giftImg.sprite = GameSprites.Get(iconNames[i]);
                giftImg.raycastTarget = false;
            }
        }

        void BuildClockTimer(Transform root)
        {
            var container = new GameObject("Timer");
            container.transform.SetParent(root, false);
            var cRect = container.AddComponent<RectTransform>();
            cRect.anchoredPosition = new Vector2(-108, -108);
            cRect.sizeDelta = new Vector2(204, 206);
            cRect.localScale = new Vector3(0.7f, 0.7f, 1f);
            AnchorTopRight(cRect);
            _timerContainerRT = cRect;
            _timerContainerBasePos = cRect.anchoredPosition;

            var timerBg = new GameObject("TimerBg");
            timerBg.transform.SetParent(container.transform, false);
            var tbRect = timerBg.AddComponent<RectTransform>();
            tbRect.anchoredPosition = Vector2.zero;
            tbRect.sizeDelta = new Vector2(204, 206);
            _timerFillImage = timerBg.AddComponent<Image>();
            _timerFillImage.sprite = GameSprites.Get(GameSprites.UI_TIMER_BG);
            _timerFillImage.type = Image.Type.Simple;
            _timerFillImage.preserveAspect = true;
            _timerFillImage.color = Color.white;
            _timerFillImage.raycastTarget = false;

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(container.transform, false);
            var slRect = sliderGO.AddComponent<RectTransform>();
            slRect.anchoredPosition = Vector2.zero;
            slRect.sizeDelta = new Vector2(204, 206);
            _timerSliderImage = sliderGO.AddComponent<Image>();
            _timerSliderImage.sprite = GameSprites.Get(GameSprites.UI_TIMER_BG);
            _timerSliderImage.type = Image.Type.Filled;
            _timerSliderImage.fillMethod = Image.FillMethod.Radial360;
            _timerSliderImage.fillOrigin = 2;
            _timerSliderImage.fillClockwise = true;
            _timerSliderImage.fillAmount = 0f;
            _timerSliderImage.color = new Color(0.18f, 0.18f, 0.18f, 1f);
            _timerSliderImage.raycastTarget = false;

            var outlineGO = new GameObject("Outline");
            outlineGO.transform.SetParent(container.transform, false);
            var olRect = outlineGO.AddComponent<RectTransform>();
            olRect.anchoredPosition = Vector2.zero;
            olRect.sizeDelta = new Vector2(261, 261);
            var olImg = outlineGO.AddComponent<Image>();
            olImg.sprite = GameSprites.Get(GameSprites.UI_TIMER_OUTLINE);
            olImg.raycastTarget = false;

            var lineGO = new GameObject("Line");
            lineGO.transform.SetParent(container.transform, false);
            var liRect = lineGO.AddComponent<RectTransform>();
            liRect.anchoredPosition = Vector2.zero;
            liRect.sizeDelta = new Vector2(191, 193);
            var liImg = lineGO.AddComponent<Image>();
            liImg.sprite = GameSprites.Get(GameSprites.UI_TIMER_LINE);
            liImg.raycastTarget = false;

            var handGO = new GameObject("ClockHand");
            handGO.transform.SetParent(container.transform, false);
            _clockHandRT = handGO.AddComponent<RectTransform>();
            _clockHandRT.anchoredPosition = Vector2.zero;
            _clockHandRT.sizeDelta = new Vector2(33, 114);
            _clockHandRT.pivot = new Vector2(0.5f, 0.15f);
            var handImg = handGO.AddComponent<Image>();
            handImg.sprite = GameSprites.Get(GameSprites.UI_TIMER_HAND);
            handImg.raycastTarget = false;

            _timerText = CreateText(container.transform, "TimerText",
                new Vector2(0, -10), new Vector2(120, 60), "", 36);
            _timerText.color = Color.white;
            _timerText.fontStyle = FontStyles.Bold;

            _timeAlarmObj = new GameObject("TimeAlarm");
            _timeAlarmObj.transform.SetParent(root, false);
            var alarmRect = _timeAlarmObj.AddComponent<RectTransform>();
            alarmRect.anchoredPosition = new Vector2(0, 386);
            alarmRect.sizeDelta = new Vector2(483, 198);
            alarmRect.anchorMin = new Vector2(0.5f, 0.5f);
            alarmRect.anchorMax = new Vector2(0.5f, 0.5f);
            alarmRect.pivot = new Vector2(0.5f, 0.5f);
            _timeAlarmImage = _timeAlarmObj.AddComponent<Image>();
            _timeAlarmImage.sprite = GameSprites.Get(GameSprites.ALARM);
            _timeAlarmImage.preserveAspect = true;
            _timeAlarmImage.raycastTarget = false;
            _timeAlarmObj.SetActive(false);
        }

        void BuildOppBarWorldUI()
        {
            var canvasGO = new GameObject("EnemyCanvas");
            _oppBarCanvas = canvasGO.AddComponent<Canvas>();
            _oppBarCanvas.renderMode = RenderMode.WorldSpace;

            var rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100, 100);
            canvasGO.transform.localScale = Vector3.one * OPP_BAR_SCALE;

            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var hpBar = new GameObject("HPBar");
            hpBar.transform.SetParent(canvasGO.transform, false);
            var hpRect = hpBar.AddComponent<RectTransform>();
            hpRect.anchoredPosition = Vector2.zero;
            hpRect.sizeDelta = new Vector2(600, 100);

            _oppHpSlider = hpBar.AddComponent<Slider>();
            _oppHpSlider.minValue = 0f;
            _oppHpSlider.maxValue = 1f;
            _oppHpSlider.value = 1f;
            _oppHpSlider.interactable = false;
            _oppHpSlider.direction = Slider.Direction.LeftToRight;

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(hpBar.transform, false);
            var bgRect = bgGO.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.sprite = GameSprites.Get(GameSprites.UI_BAR_BG);
            bgImg.color = Color.white;

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(hpBar.transform, false);
            var faRect = fillArea.AddComponent<RectTransform>();
            faRect.anchorMin = new Vector2(0f, 0.25f);
            faRect.anchorMax = new Vector2(1f, 0.75f);
            faRect.offsetMin = Vector2.zero;
            faRect.offsetMax = Vector2.zero;

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillArea.transform, false);
            var fillRect = fillGO.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.sprite = GameSprites.Get(GameSprites.UI_BAR_FILL);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.color = HP_COLOR_GREEN;
            _oppHpFillImage = fillImg;

            _oppHpSlider.fillRect = fillRect;

            var outlineGO = new GameObject("Outline");
            outlineGO.transform.SetParent(hpBar.transform, false);
            var olRect = outlineGO.AddComponent<RectTransform>();
            olRect.anchoredPosition = new Vector2(3.6f, 0f);
            olRect.sizeDelta = new Vector2(620, 80);
            var olImg = outlineGO.AddComponent<Image>();
            olImg.sprite = GameSprites.Get(GameSprites.UI_BAR_OUTLINE);
            olImg.raycastTarget = false;

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(hpBar.transform, false);
            var iconRect = iconGO.AddComponent<RectTransform>();
            iconRect.anchoredPosition = new Vector2(-302f, -26f);
            iconRect.sizeDelta = new Vector2(100, 200);
            iconRect.localScale = new Vector3(-1f, 1f, 1f);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = GameSprites.Get(GameSprites.UI_THERMO_ICON);
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            BuildOppDividerLines(hpBar.transform);

            _oppTempText = CreateText(canvasGO.transform, "OppTemp",
                new Vector2(0, -70), new Vector2(360, 32), "37°", 26);
        }

        void BuildOppDividerLines(Transform parent)
        {
            var root = new GameObject("DividerLines");
            root.transform.SetParent(parent, false);
            var rootRect = root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            float[] xPositions = { 159.1f, 324.6f, 485.8f };
            for (int i = 0; i < 3; i++)
            {
                var lineGO = new GameObject($"line_{(i + 1) * 10}");
                lineGO.transform.SetParent(root.transform, false);
                var lineRect = lineGO.AddComponent<RectTransform>();
                lineRect.anchorMin = new Vector2(0f, 0.5f);
                lineRect.anchorMax = new Vector2(0f, 0.5f);
                lineRect.pivot = new Vector2(0.5f, 0.5f);
                lineRect.anchoredPosition = new Vector2(xPositions[i], 0f);
                lineRect.sizeDelta = new Vector2(7.45f, 70f);
                var lineImg = lineGO.AddComponent<Image>();
                lineImg.sprite = GameSprites.Get(GameSprites.UI_GIFT_LINE);
                lineImg.raycastTarget = false;
            }
        }

        void BuildReadyWorldUI()
        {
            var canvasGO = new GameObject("ReadyWorldCanvas");
            _readyCanvas = canvasGO.AddComponent<Canvas>();
            _readyCanvas.renderMode = RenderMode.WorldSpace;

            var rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(300, 180);
            canvasGO.transform.localScale = Vector3.one * WORLD_CANVAS_SCALE;
            canvasGO.transform.position = READY_BTN_POS;
            canvasGO.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            canvasGO.AddComponent<GraphicRaycaster>();

            var readySprite = GameSprites.Get(GameSprites.BTN_DEFAULT);
            var btnGO = new GameObject("ReadyBtn");
            btnGO.transform.SetParent(canvasGO.transform, false);
            var btnRect = btnGO.AddComponent<RectTransform>();
            btnRect.anchoredPosition = Vector2.zero;
            btnRect.sizeDelta = new Vector2(560, 320);

            var btnImg = btnGO.AddComponent<Image>();
            if (readySprite != null)
            {
                btnImg.sprite = readySprite;
                btnImg.preserveAspect = true;
            }
            else
            {
                btnImg.color = new Color(0.5f, 0.5f, 0.2f);
            }

            _readyButtonImage = btnImg;
            _readyButton = btnGO.AddComponent<Button>();
            _readyButton.targetGraphic = btnImg;
            _readyButton.onClick.AddListener(OnReadyClicked);
            btnGO.AddComponent<EmoteWheel>();
            _readyCanvas.gameObject.SetActive(false);
        }

        void BuildEnvironmentPanel(Transform root)
        {
            _envPanel = new GameObject("EnvPanel");
            _envPanel.transform.SetParent(root, false);
            var rect = _envPanel.AddComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0, -140);
            rect.sizeDelta = new Vector2(600, 80);
            AnchorTopCenter(rect);

            CreatePanel(_envPanel.transform, "EnvBg",
                Vector2.zero, new Vector2(600, 80),
                new Color(0.1f, 0.05f, 0.2f, 0.9f));

            _envText = CreateText(_envPanel.transform, "EnvText",
                Vector2.zero, new Vector2(560, 60), "", 32);
            _envText.fontStyle = FontStyles.Bold;
            _envText.color = new Color(1f, 0.85f, 0.3f);

            _envPanel.SetActive(false);
        }

        void OnEnvironmentAnnounced(EnvironmentType env)
        {
            StopSummerVacShake();

            if (_envPanel == null || _envText == null) return;

            string name = env switch
            {
                EnvironmentType.SunnyDay => "햇살이 더 쨍쨍해집니다.",
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
            if (_summerVacShaking)
            {
                _summerVacShaking = false;
                if (_summerVacShakeCoroutine != null) StopCoroutine(_summerVacShakeCoroutine);
                _summerVacShakeCoroutine = null;
                if (_timerContainerRT != null)
                    _timerContainerRT.anchoredPosition = _timerContainerBasePos;
                if (_timerFillImage != null)
                    _timerFillImage.color = Color.white;
            }
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

        void OnOpponentRevealed(byte forPlayerIndex, short opponentItemId)
        {
            if (_localPlayer == null) return;
            if (_localPlayer.SyncedPlayerIndex.Value != forPlayerIndex) return;

            string itemName = opponentItemId >= 0
                ? (ItemManager.Instance?.GetItemData(opponentItemId)?.ItemName ?? "???")
                : "Not Selected";

            if (_statusText != null)
                _statusText.text = $"Revealed: {itemName}";

            Debug.Log($"[UI] Tarot reveal: opponent selected '{itemName}' (id={opponentItemId})");
        }

        static void AnchorTopCenter(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
        }

        static void AnchorTopLeft(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }

        static void AnchorTopRight(RectTransform rt)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
        }

        static void AnchorBottomLeft(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
        }

        static void AnchorBottomCenter(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
        }

        static TextMeshProUGUI CreateText(Transform parent, string name,
            Vector2 pos, Vector2 size, string text, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            return tmp;
        }

        static Image CreatePanel(Transform parent, string name,
            Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static Button CreateButton(Transform parent, string name,
            Vector2 pos, Vector2 size, string label, Color bgColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = bgColor * 1.2f;
            colors.pressedColor = bgColor * 0.7f;
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
            btn.colors = colors;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 18;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return btn;
        }

        #endregion
    }
}
