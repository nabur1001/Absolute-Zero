using System;
using System.Collections.Generic;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Session;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

using LobbyPlayer = Unity.Services.Lobbies.Models.Player;

namespace AbsoluteZero.UI.LobbyUI
{
    public class AZLobbyUI : MonoBehaviour
    {
        private LobbyManager lobbyManager;
        private NetworkSessionCoordinator coordinator;

        private Canvas mainCanvas;
        private GameObject mainPanel;
        private GameObject lobbyPanel;

        private Button createBtn;
        private TMP_InputField joinCodeInput;
        private Button joinBtn;
        private TextMeshProUGUI orText;
        private TextMeshProUGUI statusText;

        private TextMeshProUGUI lobbyCodeText;
        private Transform playerListContainer;
        private Button startBtn;
        private Button leaveBtn;
        private TextMeshProUGUI lobbyStatusText;
        private TextMeshProUGUI logText;

        private string myPlayerId;
        private readonly Dictionary<string, string> lastKnownActions = new();
        private readonly List<GameObject> playerSlotObjects = new();

        private void Start()
        {
            BuildUI();
            StartCoroutine(WaitForManagers());
        }

        private System.Collections.IEnumerator WaitForManagers()
        {
            SetStatus("초기화 중...");

            while (LobbyManager.Instance == null || NetworkSessionCoordinator.Instance == null)
                yield return null;

            lobbyManager = LobbyManager.Instance;
            coordinator = NetworkSessionCoordinator.Instance;

            lobbyManager.OnLobbyCreated += OnLobbyEntered;
            lobbyManager.OnLobbyJoined += OnLobbyEntered;
            lobbyManager.OnLobbyUpdated += OnLobbyUpdated;
            lobbyManager.OnLobbyLeft += OnLobbyLeft;
            lobbyManager.OnError += OnError;

            coordinator.OnError += OnCoordinatorError;
            coordinator.OnStateChanged += OnCoordinatorStateChanged;

            while (coordinator.State != SessionState.Ready && coordinator.State != SessionState.Failed)
                yield return null;

            if (coordinator.State == SessionState.Failed)
            {
                SetStatus($"초기화 실패: {coordinator.LastError}");
                yield break;
            }

            SetStatus("준비 완료");
        }

        private void OnDestroy()
        {
            if (lobbyManager != null)
            {
                lobbyManager.OnLobbyCreated -= OnLobbyEntered;
                lobbyManager.OnLobbyJoined -= OnLobbyEntered;
                lobbyManager.OnLobbyUpdated -= OnLobbyUpdated;
                lobbyManager.OnLobbyLeft -= OnLobbyLeft;
                lobbyManager.OnError -= OnError;
            }

            if (coordinator != null)
            {
                coordinator.OnError -= OnCoordinatorError;
                coordinator.OnStateChanged -= OnCoordinatorStateChanged;
            }
        }

        #region Panel Control

        private void ShowMainPanel()
        {
            mainPanel.SetActive(true);
            lobbyPanel.SetActive(false);
        }

        private void ShowLobbyPanel()
        {
            mainPanel.SetActive(false);
            lobbyPanel.SetActive(true);
            ApplyLobbyMembershipUI(lobbyManager != null && lobbyManager.IsInLobby);
        }

        // 로비 소속 여부에 따라 UI 정리:
        // - 로비에 있으면 생성/참가 컨트롤 숨김(지저분함 제거), 없으면 표시
        // - 로비에 없으면 이전 코드 잔존 제거("------"로 리셋)
        private void ApplyLobbyMembershipUI(bool inLobby)
        {
            if (createBtn != null) createBtn.gameObject.SetActive(!inLobby);
            if (orText != null) orText.gameObject.SetActive(!inLobby);
            if (joinCodeInput != null) joinCodeInput.gameObject.SetActive(!inLobby);
            if (joinBtn != null) joinBtn.gameObject.SetActive(!inLobby);

            if (!inLobby && lobbyCodeText != null)
                lobbyCodeText.text = "------";
        }

        #endregion

        #region Button Handlers

        private void OnArenaClicked()
        {
            ShowLobbyPanel();
        }

        private async void OnCreateClicked()
        {
            if (coordinator == null) return;

            createBtn.interactable = false;
            SetStatus("로비 생성 중...");

            var result = await coordinator.CreateLobbyAsync();

            if (result.IsFailure)
            {
                createBtn.interactable = true;
                SetStatus("로비 생성 실패");
            }
        }

        private async void OnJoinClicked()
        {
            if (coordinator == null) return;

            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                SetStatus("이미 로비에 있습니다");
                return;
            }

            string code = joinCodeInput.text.ToUpper().Trim();
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("코드를 입력하세요");
                return;
            }

            joinBtn.interactable = false;
            joinCodeInput.interactable = false;
            SetStatus($"로비 참가 중 ({code})...");

            var result = await coordinator.JoinGameAsync(code);

            if (result.IsFailure)
            {
                SetStatus($"참가 실패: {result.ErrorMessage}");
                joinCodeInput.text = "";
                joinCodeInput.interactable = true;
                joinBtn.interactable = true;
            }
        }

        private async void OnLeaveClicked()
        {
            if (coordinator == null) return;
            SetLobbyStatus("로비 퇴장 중...");
            await coordinator.LeaveAsync();
        }

        private async void OnStartClicked()
        {
            if (coordinator == null || !coordinator.IsHostRole) return;
            if (coordinator.State != SessionState.Ready) return;

            var lobby = coordinator.CurrentLobby;
            if (lobby == null || lobby.Players.Count < 2)
            {
                SetLobbyStatus("상대가 없어 시작할 수 없습니다 (2명 필요)");
                return;
            }

            startBtn.interactable = false;
            SetLobbyStatus("게임 시작 중...");

            var result = await coordinator.StartMatchAsHostAsync();

            if (result.IsFailure)
            {
                SetLobbyStatus($"시작 실패: {result.ErrorMessage}");
                startBtn.interactable = true;
            }
        }

        #endregion

        #region Lobby Events

        private void OnLobbyEntered(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            myPlayerId = lobbyManager?.PlayerId;

            bool isHost = coordinator != null && coordinator.IsHostRole;
            startBtn.gameObject.SetActive(isHost);

            UpdatePlayerList(lobby);
            ShowLobbyPanel();

            ApplyLobbyMembershipUI(true);
            lobbyCodeText.text = lobby.LobbyCode;

            string hostTag = isHost ? " (호스트)" : "";
            SetLobbyStatus($"로비 참가{hostTag} — 코드: {lobby.LobbyCode}");
        }

        private void OnLobbyUpdated(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            UpdatePlayerList(lobby);
        }

        private void OnLobbyLeft()
        {
            createBtn.interactable = true;
            joinBtn.interactable = true;
            joinCodeInput.interactable = true;
            joinCodeInput.text = "";
            ClearPlayerList();
            ApplyLobbyMembershipUI(false);
            ShowMainPanel();
            SetStatus("준비 완료");
        }

        private void OnError(string error)
        {
            SetStatus($"오류: {error}");
            createBtn.interactable = true;
            joinBtn.interactable = true;
            joinCodeInput.interactable = true;
        }

        #endregion

        #region Coordinator Events

        private void OnCoordinatorError(string error)
        {
            SetLobbyStatus($"오류: {error}");
        }

        private void OnCoordinatorStateChanged(SessionState state, SessionOperation operation)
        {
            switch (state)
            {
                case SessionState.Connecting:
                    string opLabel = operation switch
                    {
                        SessionOperation.AllocatingRelay => "릴레이 할당 중...",
                        SessionOperation.JoiningRelay => "릴레이 연결 중...",
                        SessionOperation.StartingHost => "호스트 시작 중...",
                        SessionOperation.StartingClient => "클라이언트 시작 중...",
                        SessionOperation.WaitingRelayCode => "릴레이 코드 대기 중...",
                        _ => "연결 중..."
                    };
                    SetLobbyStatus(opLabel);
                    break;
                case SessionState.LoadingGame:
                    SetLobbyStatus("게임 로딩 중...");
                    break;
                case SessionState.InGame:
                    SetLobbyStatus("게임 시작!");
                    break;
            }
        }

        #endregion

        #region Player List

        private void UpdatePlayerList(Unity.Services.Lobbies.Models.Lobby lobby)
        {
            ClearPlayerList();

            foreach (var player in lobby.Players)
            {
                string playerName = GetPlayerName(player);
                bool isMe = player.Id == myPlayerId;
                bool isHost = player.Id == lobby.HostId;

                string label = playerName;
                if (isHost) label += " [호스트]";
                if (isMe) label += " (나)";

                Color bgColor = isHost
                    ? new Color(0.6f, 0.5f, 0.2f, 0.8f)
                    : isMe
                        ? new Color(0.2f, 0.6f, 0.3f, 0.8f)
                        : new Color(0.2f, 0.4f, 0.6f, 0.8f);

                var slotGO = CreatePlayerSlot(playerListContainer, label, bgColor);
                playerSlotObjects.Add(slotGO);
            }

            int emptySlots = lobby.MaxPlayers - lobby.Players.Count;
            for (int i = 0; i < emptySlots; i++)
            {
                var slotGO = CreatePlayerSlot(playerListContainer, "대기 중...",
                    new Color(0.3f, 0.3f, 0.3f, 0.5f));
                playerSlotObjects.Add(slotGO);
            }

            if (startBtn != null && coordinator != null && coordinator.IsHostRole && coordinator.State == SessionState.Ready)
                startBtn.interactable = lobby.Players.Count >= 2;
        }

        private void ClearPlayerList()
        {
            foreach (var go in playerSlotObjects)
                Destroy(go);
            playerSlotObjects.Clear();
        }

        private static string GetPlayerName(LobbyPlayer player)
        {
            if (player.Data != null && player.Data.TryGetValue("PlayerName", out var nameData))
                return nameData.Value;
            return $"Player_{player.Id[..6]}";
        }

        #endregion

        #region Status

        private void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }

        private void SetLobbyStatus(string msg)
        {
            if (lobbyStatusText != null) lobbyStatusText.text = msg;
            AppendLog(msg);
        }

        void CopyLobbyCode()
        {
            string code = lobbyCodeText != null ? lobbyCodeText.text : "";
            if (string.IsNullOrEmpty(code) || code == "------")
            {
                SetLobbyStatus("복사할 코드 없음");
                return;
            }
            GUIUtility.systemCopyBuffer = code;
            SetLobbyStatus($"코드 복사됨: {code}");
        }

        private void AppendLog(string msg)
        {
            if (logText == null) return;
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            logText.text += $"[{timestamp}] {msg}\n";

            string[] lines = logText.text.Split('\n');
            if (lines.Length > 30)
            {
                logText.text = string.Join("\n",
                    new ArraySegment<string>(lines, lines.Length - 30, 30));
            }
        }

        #endregion

        #region UI Construction

        private void BuildUI()
        {
            var mainUIGO = GameObject.Find("MainUI");
            if (mainUIGO != null)
            {
                mainCanvas = mainUIGO.GetComponent<Canvas>();
                var scaler = mainUIGO.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920, 1080);
                    scaler.matchWidthOrHeight = 0.5f;
                }
                if (mainUIGO.GetComponent<GraphicRaycaster>() == null)
                    mainUIGO.AddComponent<GraphicRaycaster>();
            }
            else
            {
                mainUIGO = new GameObject("MainUI");
                mainCanvas = mainUIGO.AddComponent<Canvas>();
                mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                mainCanvas.sortingOrder = 10;
                var scaler = mainUIGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                mainUIGO.AddComponent<GraphicRaycaster>();
            }

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<EventSystem>();
                esGO.AddComponent<InputSystemUIInputModule>();
            }

            Transform root = mainUIGO.transform;

            BuildMainPanel(root);
            BuildLobbyPanel(root);

            ShowMainPanel();
        }

        private void BuildMainPanel(Transform root)
        {
            mainPanel = new GameObject("MainPanel");
            mainPanel.transform.SetParent(root, false);
            var rt = mainPanel.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var sprites = Resources.LoadAll<Sprite>("로비화면");
            Sprite nickSprite = null, arenaSprite = null, closetSprite = null;
            foreach (var s in sprites)
            {
                if (s.name == "lobby_nickname") nickSprite = s;
                else if (s.name == "lobby_arena") arenaSprite = s;
                else if (s.name == "lobby_closet") closetSprite = s;
            }

            // --- 닉네임 바: 좌상단 (비율 507:126 ≈ 4:1) ---
            if (nickSprite != null)
            {
                var nickGO = new GameObject("NicknameBar");
                nickGO.transform.SetParent(mainPanel.transform, false);
                var nickRT = nickGO.AddComponent<RectTransform>();
                nickRT.anchorMin = new Vector2(0, 1);
                nickRT.anchorMax = new Vector2(0, 1);
                nickRT.pivot = new Vector2(0, 1);
                nickRT.anchoredPosition = new Vector2(30, -25);
                nickRT.sizeDelta = new Vector2(340, 85);

                var nickImg = nickGO.AddComponent<Image>();
                nickImg.sprite = nickSprite;
                nickImg.preserveAspect = true;

                var nickText = CreateText(nickGO.transform, "NickText",
                    Vector2.zero, Vector2.zero, "Player", 22, Color.black);
                var nickTextRT = nickText.GetComponent<RectTransform>();
                nickTextRT.anchorMin = Vector2.zero;
                nickTextRT.anchorMax = Vector2.one;
                nickTextRT.offsetMin = new Vector2(15, 0);
                nickTextRT.offsetMax = new Vector2(-55, 0);
                nickText.alignment = TextAlignmentOptions.MidlineLeft;
            }

            // --- 버튼 그룹: 좌하단 (버튼 비율 476:256 ≈ 1.86:1) ---
            // 결투장 버튼
            if (arenaSprite != null)
            {
                var arenaGO = new GameObject("ArenaBtn");
                arenaGO.transform.SetParent(mainPanel.transform, false);
                var arenaRT = arenaGO.AddComponent<RectTransform>();
                arenaRT.anchorMin = new Vector2(0, 0);
                arenaRT.anchorMax = new Vector2(0, 0);
                arenaRT.pivot = new Vector2(0, 0);
                arenaRT.anchoredPosition = new Vector2(30, 185);
                arenaRT.sizeDelta = new Vector2(340, 183);

                var arenaImg = arenaGO.AddComponent<Image>();
                arenaImg.sprite = arenaSprite;
                arenaImg.preserveAspect = true;

                var arenaBtn = arenaGO.AddComponent<Button>();
                var colors = arenaBtn.colors;
                colors.highlightedColor = new Color(0.9f, 0.95f, 1f);
                colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
                arenaBtn.colors = colors;
                arenaBtn.onClick.AddListener(OnArenaClicked);
            }

            // 옷장 버튼
            if (closetSprite != null)
            {
                var closetGO = new GameObject("ClosetBtn");
                closetGO.transform.SetParent(mainPanel.transform, false);
                var closetRT = closetGO.AddComponent<RectTransform>();
                closetRT.anchorMin = new Vector2(0, 0);
                closetRT.anchorMax = new Vector2(0, 0);
                closetRT.pivot = new Vector2(0, 0);
                closetRT.anchoredPosition = new Vector2(30, 15);
                closetRT.sizeDelta = new Vector2(340, 160);

                var closetImg = closetGO.AddComponent<Image>();
                closetImg.sprite = closetSprite;
                closetImg.preserveAspect = true;

                var closetBtn = closetGO.AddComponent<Button>();
                closetBtn.interactable = false;
                var colors = closetBtn.colors;
                colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);
                closetBtn.colors = colors;
            }

            // 상태 텍스트 (좌하단 버튼 아래)
            statusText = CreateText(mainPanel.transform, "Status",
                Vector2.zero, Vector2.zero, "준비 완료", 20,
                new Color(0.3f, 0.3f, 0.3f));
            var stRT = statusText.GetComponent<RectTransform>();
            stRT.anchorMin = new Vector2(0, 0);
            stRT.anchorMax = new Vector2(0, 0);
            stRT.pivot = new Vector2(0, 0);
            stRT.anchoredPosition = new Vector2(45, 10);
            stRT.sizeDelta = new Vector2(400, 30);
            statusText.alignment = TextAlignmentOptions.BottomLeft;
        }

        private void BuildLobbyPanel(Transform root)
        {
            lobbyPanel = new GameObject("LobbyPanel");
            lobbyPanel.transform.SetParent(root, false);
            var rt = lobbyPanel.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 반투명 배경 딤
            var dimGO = new GameObject("Dim");
            dimGO.transform.SetParent(lobbyPanel.transform, false);
            var dimRT = dimGO.AddComponent<RectTransform>();
            dimRT.anchorMin = Vector2.zero;
            dimRT.anchorMax = Vector2.one;
            dimRT.offsetMin = Vector2.zero;
            dimRT.offsetMax = Vector2.zero;
            var dimImg = dimGO.AddComponent<Image>();
            dimImg.color = new Color(0, 0, 0, 0.5f);
            dimImg.raycastTarget = true;

            // 메인 패널
            var panelBG = CreatePanel(lobbyPanel.transform, "PanelBG", Vector2.zero,
                new Vector2(720, 780), new Color(0.12f, 0.12f, 0.18f, 0.96f));

            Transform panel = panelBG.transform;

            // 타이틀
            CreateText(panel, "Title",
                new Vector2(0, 340), new Vector2(500, 60), "결투장", 48);

            // 로비 코드
            CreateText(panel, "CodeLabel",
                new Vector2(0, 285), new Vector2(400, 30), "로비 코드 (클릭하여 복사)", 18,
                new Color(0.6f, 0.6f, 0.7f));

            lobbyCodeText = CreateText(panel, "LobbyCode",
                new Vector2(0, 240), new Vector2(400, 60), "------", 48);
            lobbyCodeText.color = new Color(1f, 0.9f, 0.4f);

            var codeBtn = lobbyCodeText.gameObject.AddComponent<Button>();
            codeBtn.targetGraphic = lobbyCodeText;
            codeBtn.transition = Selectable.Transition.None;
            codeBtn.onClick.AddListener(CopyLobbyCode);

            // 플레이어 목록
            CreateText(panel, "PlayersLabel",
                new Vector2(0, 185), new Vector2(200, 30), "플레이어", 22,
                new Color(0.6f, 0.6f, 0.7f));

            var listGO = new GameObject("PlayerList");
            listGO.transform.SetParent(panel, false);
            var listRT = listGO.AddComponent<RectTransform>();
            listRT.anchoredPosition = new Vector2(0, 105);
            listRT.sizeDelta = new Vector2(500, 140);
            var vlg = listGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.padding = new RectOffset(10, 10, 5, 5);
            playerListContainer = listGO.transform;

            // 생성/참가 영역
            createBtn = CreateButton(panel, "CreateBtn",
                new Vector2(0, 10), new Vector2(400, 60), "로비 생성",
                new Color(0.2f, 0.5f, 0.8f), 24);
            createBtn.onClick.AddListener(OnCreateClicked);

            orText = CreateText(panel, "OrText",
                new Vector2(0, -40), new Vector2(100, 25), "— 또는 —", 16,
                new Color(0.5f, 0.5f, 0.5f));

            joinCodeInput = CreateInputField(panel, "JoinCodeInput",
                new Vector2(-70, -85), new Vector2(260, 55), "코드 입력...");

            joinBtn = CreateButton(panel, "JoinBtn",
                new Vector2(145, -85), new Vector2(130, 55), "참가",
                new Color(0.3f, 0.6f, 0.3f), 22);
            joinBtn.onClick.AddListener(OnJoinClicked);

            // 시작/퇴장 버튼
            startBtn = CreateButton(panel, "StartBtn",
                new Vector2(-100, -165), new Vector2(190, 55), "게임 시작",
                new Color(0.8f, 0.4f, 0.1f), 22);
            startBtn.onClick.AddListener(OnStartClicked);
            startBtn.gameObject.SetActive(false);

            leaveBtn = CreateButton(panel, "LeaveBtn",
                new Vector2(100, -165), new Vector2(190, 55), "나가기",
                new Color(0.6f, 0.2f, 0.2f), 22);
            leaveBtn.onClick.AddListener(OnLeaveClicked);

            // 상태
            lobbyStatusText = CreateText(panel, "LobbyStatus",
                new Vector2(0, -220), new Vector2(600, 30), "", 18,
                new Color(0.7f, 0.7f, 0.7f));

            // 로그
            BuildLogScroll(panel, new Vector2(0, -310), new Vector2(600, 130));
        }

        private void BuildLogScroll(Transform parent, Vector2 pos, Vector2 size)
        {
            var logScrollGO = new GameObject("LogScroll");
            logScrollGO.transform.SetParent(parent, false);
            var logScrollRT = logScrollGO.AddComponent<RectTransform>();
            logScrollRT.anchoredPosition = pos;
            logScrollRT.sizeDelta = size;

            var logBG = logScrollGO.AddComponent<Image>();
            logBG.color = new Color(0.05f, 0.05f, 0.08f, 0.8f);

            var scrollRect = logScrollGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(logScrollGO.transform, false);
            var vpRT = viewport.AddComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = new Vector2(8, 5);
            vpRT.offsetMax = new Vector2(-8, -5);
            var vpMask = viewport.AddComponent<Mask>();
            vpMask.showMaskGraphic = false;
            var vpImg = viewport.AddComponent<Image>();
            vpImg.color = Color.white;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var cRT = content.AddComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0, 1);
            cRT.anchorMax = new Vector2(1, 1);
            cRT.pivot = new Vector2(0.5f, 1);
            cRT.offsetMin = Vector2.zero;
            cRT.offsetMax = Vector2.zero;

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            logText = content.AddComponent<TextMeshProUGUI>();
            logText.fontSize = 14;
            logText.color = new Color(0.7f, 0.8f, 0.7f);
            logText.alignment = TextAlignmentOptions.TopLeft;
            logText.text = "";

            scrollRect.viewport = vpRT;
            scrollRect.content = cRT;
        }

        #endregion

        #region UI Helpers

        private static TextMeshProUGUI CreateText(Transform parent, string name,
            Vector2 pos, Vector2 size, string text, int fontSize,
            Color? color = null)
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
            tmp.color = color ?? Color.white;
            return tmp;
        }

        private static Image CreatePanel(Transform parent, string name,
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

        private static Button CreateButton(Transform parent, string name,
            Vector2 pos, Vector2 size, string label, Color bgColor, int fontSize = 20)
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
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return btn;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name,
            Vector2 pos, Vector2 size, string placeholder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);

            var textArea = new GameObject("TextArea");
            textArea.transform.SetParent(go.transform, false);
            var taRT = textArea.AddComponent<RectTransform>();
            taRT.anchorMin = Vector2.zero;
            taRT.anchorMax = Vector2.one;
            taRT.offsetMin = new Vector2(12, 5);
            taRT.offsetMax = new Vector2(-12, -5);
            textArea.AddComponent<RectMask2D>();

            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(textArea.transform, false);
            var phRT = placeholderGO.AddComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = Vector2.zero;
            phRT.offsetMax = Vector2.zero;
            var phText = placeholderGO.AddComponent<TextMeshProUGUI>();
            phText.text = placeholder;
            phText.fontSize = 20;
            phText.fontStyle = FontStyles.Italic;
            phText.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            phText.alignment = TextAlignmentOptions.MidlineLeft;

            var inputTextGO = new GameObject("Text");
            inputTextGO.transform.SetParent(textArea.transform, false);
            var itRT = inputTextGO.AddComponent<RectTransform>();
            itRT.anchorMin = Vector2.zero;
            itRT.anchorMax = Vector2.one;
            itRT.offsetMin = Vector2.zero;
            itRT.offsetMax = Vector2.zero;
            var inputTMP = inputTextGO.AddComponent<TextMeshProUGUI>();
            inputTMP.fontSize = 22;
            inputTMP.color = Color.white;
            inputTMP.alignment = TextAlignmentOptions.MidlineLeft;

            var inputField = go.AddComponent<TMP_InputField>();
            inputField.textViewport = taRT;
            inputField.textComponent = inputTMP;
            inputField.placeholder = phText;
            inputField.characterLimit = 8;
            inputField.contentType = TMP_InputField.ContentType.Alphanumeric;
            inputField.pointSize = 22;

            return inputField;
        }

        private static GameObject CreatePlayerSlot(Transform parent, string label, Color bgColor)
        {
            var go = new GameObject("Slot");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(480, 55);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 55;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var textGO = new GameObject("Label");
            textGO.transform.SetParent(go.transform, false);
            var trt = textGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(20, 0);
            trt.offsetMax = new Vector2(-20, 0);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 22;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = Color.white;

            return go;
        }

        #endregion
    }
}
