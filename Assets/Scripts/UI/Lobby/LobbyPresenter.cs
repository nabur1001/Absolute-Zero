using System;
using AbsoluteZero.Core.Cosmetic;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Session;
using Unity.Services.Lobbies.Models;
using UnityEngine;

using LobbyModel = Unity.Services.Lobbies.Models.Lobby;

namespace AbsoluteZero.UI.LobbyUI
{
    public class LobbyPresenter : IDisposable
    {
        readonly LobbyMainView _mainView;
        readonly LobbyModeSelectView _modeSelectView;
        readonly LobbyRoomView _roomView;
        readonly LobbySettingsView _settingsView;
        readonly ClosetView _closetView;

        LobbyManager _lobbyManager;
        NetworkSessionCoordinator _coordinator;

        LobbyViewState _currentState;
        bool _disposed;
        string _myPlayerId;

        public LobbyPresenter(
            LobbyMainView mainView,
            LobbyModeSelectView modeSelectView,
            LobbyRoomView roomView,
            LobbySettingsView settingsView,
            ClosetView closetView)
        {
            _mainView = mainView;
            _modeSelectView = modeSelectView;
            _roomView = roomView;
            _settingsView = settingsView;
            _closetView = closetView;
        }

        public void Initialize(LobbyManager lobbyManager, NetworkSessionCoordinator coordinator)
        {
            _lobbyManager = lobbyManager;
            _coordinator = coordinator;

            _lobbyManager.OnLobbyCreated += OnLobbyEntered;
            _lobbyManager.OnLobbyJoined += OnLobbyEntered;
            _lobbyManager.OnLobbyUpdated += OnLobbyUpdated;
            _lobbyManager.OnLobbyLeft += OnLobbyLeft;
            _lobbyManager.OnError += OnLobbyError;

            _coordinator.OnError += OnCoordinatorError;
            _coordinator.OnStateChanged += OnCoordinatorStateChanged;

            _mainView.OnArenaClicked += HandleArenaClicked;
            _mainView.OnClosetClicked += HandleClosetClicked;
            _mainView.OnSettingsClicked += HandleSettingsClicked;
            _mainView.OnNicknameEndEdit += HandleNicknameEndEdit;

            var profileService = CosmeticProfileService.Instance;
            if (profileService != null && !string.IsNullOrEmpty(profileService.Nickname))
                _mainView.SetNicknameText(profileService.Nickname);

            _modeSelectView.OnOneVsOneClicked += HandleOneVsOneClicked;
            _modeSelectView.OnMultiClicked += HandleMultiClicked;
            _modeSelectView.OnBackClicked += HandleModeSelectBack;

            _roomView.OnCreateClicked += HandleCreateClicked;
            _roomView.OnJoinClicked += HandleJoinClicked;
            _roomView.OnStartClicked += HandleStartClicked;
            _roomView.OnLeaveClicked += HandleLeaveClicked;
            _roomView.OnBackClicked += HandleRoomBack;

            _settingsView.OnCloseClicked += HandleSettingsClose;

            _closetView.OnTabChanged += HandleClosetTabChanged;
            _closetView.OnEquipClicked += HandleClosetEquip;
            _closetView.OnUnequipClicked += HandleClosetUnequip;
            _closetView.OnCloseClicked += HandleClosetClose;

            SetState(LobbyViewState.Main);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_lobbyManager != null)
            {
                _lobbyManager.OnLobbyCreated -= OnLobbyEntered;
                _lobbyManager.OnLobbyJoined -= OnLobbyEntered;
                _lobbyManager.OnLobbyUpdated -= OnLobbyUpdated;
                _lobbyManager.OnLobbyLeft -= OnLobbyLeft;
                _lobbyManager.OnError -= OnLobbyError;
            }

            if (_coordinator != null)
            {
                _coordinator.OnError -= OnCoordinatorError;
                _coordinator.OnStateChanged -= OnCoordinatorStateChanged;
            }

            _mainView.OnArenaClicked -= HandleArenaClicked;
            _mainView.OnClosetClicked -= HandleClosetClicked;
            _mainView.OnSettingsClicked -= HandleSettingsClicked;
            _mainView.OnNicknameEndEdit -= HandleNicknameEndEdit;

            _modeSelectView.OnOneVsOneClicked -= HandleOneVsOneClicked;
            _modeSelectView.OnMultiClicked -= HandleMultiClicked;
            _modeSelectView.OnBackClicked -= HandleModeSelectBack;

            _roomView.OnCreateClicked -= HandleCreateClicked;
            _roomView.OnJoinClicked -= HandleJoinClicked;
            _roomView.OnStartClicked -= HandleStartClicked;
            _roomView.OnLeaveClicked -= HandleLeaveClicked;
            _roomView.OnBackClicked -= HandleRoomBack;

            _settingsView.OnCloseClicked -= HandleSettingsClose;

            _closetView.OnTabChanged -= HandleClosetTabChanged;
            _closetView.OnEquipClicked -= HandleClosetEquip;
            _closetView.OnUnequipClicked -= HandleClosetUnequip;
            _closetView.OnCloseClicked -= HandleClosetClose;
        }

        #region State Machine

        public void SetState(LobbyViewState newState)
        {
            if (_disposed) return;

            _mainView.SetVisible(newState == LobbyViewState.Main);
            _modeSelectView.SetVisible(newState == LobbyViewState.ModeSelect);
            _roomView.SetVisible(newState == LobbyViewState.Room);
            _settingsView.SetVisible(newState == LobbyViewState.Settings);
            _closetView.SetVisible(newState == LobbyViewState.Closet);

            _currentState = newState;

            if (newState == LobbyViewState.Closet)
                RefreshClosetItems(_closetView.CurrentTab);

            if (newState == LobbyViewState.Room)
            {
                bool inLobby = _lobbyManager != null && _lobbyManager.IsInLobby;
                _roomView.ApplyMembership(inLobby);
            }
        }

        #endregion

        #region View Action Handlers

        void HandleArenaClicked()
        {
            if (_disposed) return;
            SetState(LobbyViewState.ModeSelect);
        }

        void HandleClosetClicked()
        {
            if (_disposed) return;
            SetState(LobbyViewState.Closet);
        }

        void HandleSettingsClicked()
        {
            if (_disposed) return;
            SetState(LobbyViewState.Settings);
        }

        void HandleOneVsOneClicked()
        {
            if (_disposed) return;
            _coordinator?.SetMatchParameters(GameMode.OneVsOne, 2);
            SetState(LobbyViewState.Room);
        }

        void HandleMultiClicked(int playerCount)
        {
            if (_disposed) return;
            _coordinator?.SetMatchParameters(GameMode.Multi, playerCount);
            SetState(LobbyViewState.Room);
        }

        void HandleModeSelectBack()
        {
            if (_disposed) return;
            SetState(LobbyViewState.Main);
        }

        async void HandleCreateClicked()
        {
            if (_disposed || _coordinator == null) return;

            _roomView.SetCreateInteractable(false);
            _roomView.SetStatus("로비 생성 중...");

            var result = await _coordinator.CreateLobbyAsync();

            if (_disposed) return;
            if (result.IsFailure)
            {
                _roomView.SetCreateInteractable(true);
                _roomView.SetStatus("로비 생성 실패");
            }
        }

        async void HandleJoinClicked(string code)
        {
            if (_disposed || _coordinator == null) return;

            if (_lobbyManager != null && _lobbyManager.IsInLobby)
            {
                _roomView.SetStatus("이미 로비에 있습니다");
                return;
            }

            if (string.IsNullOrEmpty(code))
            {
                _roomView.SetStatus("코드를 입력하세요");
                return;
            }

            _roomView.SetJoinInteractable(false);
            _roomView.SetStatus($"로비 참가 중 ({code})...");

            var result = await _coordinator.JoinGameAsync(code);

            if (_disposed) return;
            if (result.IsFailure)
            {
                _roomView.SetStatus($"참가 실패: {result.ErrorMessage}");
                _roomView.ResetJoinInput();
                _roomView.SetJoinInteractable(true);
            }
        }

        async void HandleStartClicked()
        {
            if (_disposed || _coordinator == null || !_coordinator.IsHostRole) return;
            if (_coordinator.State != SessionState.Ready) return;

            var lobby = _coordinator.CurrentLobby;
            int required = _coordinator.SelectedPlayerCount;
            if (lobby == null || lobby.Players.Count < required)
            {
                _roomView.SetStatus($"인원이 부족합니다 ({lobby?.Players.Count ?? 0}/{required})");
                return;
            }

            _roomView.SetStartInteractable(false);
            _roomView.SetStatus("게임 시작 중...");

            var result = await _coordinator.StartMatchAsHostAsync();

            if (_disposed) return;
            if (result.IsFailure)
            {
                _roomView.SetStatus($"시작 실패: {result.ErrorMessage}");
                _roomView.SetStartInteractable(true);
            }
        }

        async void HandleLeaveClicked()
        {
            if (_disposed || _coordinator == null) return;
            _roomView.SetStatus("로비 퇴장 중...");
            await _coordinator.LeaveAsync();
        }

        async void HandleRoomBack()
        {
            if (_disposed) return;

            if (_lobbyManager != null && _lobbyManager.IsInLobby)
            {
                _roomView.SetStatus("로비 퇴장 중...");
                if (_coordinator != null)
                    await _coordinator.LeaveAsync();
            }

            if (_disposed) return;
            SetState(LobbyViewState.ModeSelect);
        }

        void HandleSettingsClose()
        {
            if (_disposed) return;
            SetState(LobbyViewState.Main);
        }

        void HandleClosetTabChanged(CosmeticPart part)
        {
            if (_disposed) return;
            RefreshClosetItems(part);
        }

        void HandleClosetEquip(CosmeticItemSO item)
        {
            if (_disposed) return;
            var service = CosmeticProfileService.Instance;
            if (service == null) return;
            service.EquipState.Equip(item);
            RefreshClosetItems(_closetView.CurrentTab);
        }

        void HandleClosetUnequip(CosmeticPart part)
        {
            if (_disposed) return;
            var service = CosmeticProfileService.Instance;
            if (service == null) return;
            service.EquipState.Unequip(part);
            RefreshClosetItems(_closetView.CurrentTab);
        }

        async void HandleClosetClose()
        {
            if (_disposed) return;

            var service = CosmeticProfileService.Instance;
            if (service != null)
                service.EquipState.Save();

            if (_lobbyManager != null && _lobbyManager.IsInLobby && service != null)
            {
                try { await _lobbyManager.SetPlayerCosmeticDataAsync(service.GetCompactDto()); }
                catch (Exception e) { Debug.LogWarning($"[LobbyPresenter] SetPlayerCosmeticData failed: {e.Message}"); }
            }

            if (_disposed) return;
            SetState(LobbyViewState.Main);
        }

        void RefreshClosetItems(CosmeticPart part)
        {
            var service = CosmeticProfileService.Instance;
            if (service == null || service.Registry == null) return;
            var items = service.Registry.GetByPart(part);
            _closetView.RenderItems(items, service.EquipState);
        }

        async void HandleNicknameEndEdit(string raw)
        {
            if (_disposed) return;

            var profileService = CosmeticProfileService.Instance;
            if (profileService == null) return;

            if (!profileService.TryValidateNickname(raw, out string validated))
            {
                _mainView.SetNicknameText(profileService.Nickname);
                return;
            }

            profileService.SetNickname(validated);
            _mainView.SetNicknameText(validated);

            if (_lobbyManager != null && _lobbyManager.IsInLobby)
            {
                try { await _lobbyManager.SetPlayerNameAsync(validated); }
                catch (Exception e) { Debug.LogWarning($"[LobbyPresenter] SetPlayerName failed: {e.Message}"); }
            }
        }

        #endregion

        #region Service Event Handlers

        void OnLobbyEntered(LobbyModel lobby)
        {
            if (_disposed) return;

            _myPlayerId = _lobbyManager != null ? _lobbyManager.PlayerId : null;

            bool isHost = _coordinator != null && _coordinator.IsHostRole;
            _roomView.SetStartVisible(isHost);

            _roomView.RenderPlayerList(lobby, _myPlayerId);
            SetState(LobbyViewState.Room);

            _roomView.ApplyMembership(true);
            _roomView.SetLobbyCode(lobby.LobbyCode);

            string hostTag = isHost ? " (호스트)" : "";
            _roomView.SetStatus($"로비 참가{hostTag} — 코드: {lobby.LobbyCode}");
        }

        void OnLobbyUpdated(LobbyModel lobby)
        {
            if (_disposed) return;
            _roomView.RenderPlayerList(lobby, _myPlayerId);

            if (_coordinator != null && _coordinator.IsHostRole && _coordinator.State == SessionState.Ready)
                _roomView.SetStartInteractable(lobby.Players.Count >= _coordinator.SelectedPlayerCount);
        }

        void OnLobbyLeft()
        {
            if (_disposed) return;
            _roomView.ResetAll();
            SetState(LobbyViewState.Main);
            _mainView.SetStatus("준비 완료");
        }

        void OnLobbyError(string error)
        {
            if (_disposed) return;

            if (_currentState == LobbyViewState.Room)
                _roomView.SetStatus($"오류: {error}");
            else
                _mainView.SetStatus($"오류: {error}");

            _roomView.SetCreateInteractable(true);
            _roomView.SetJoinInteractable(true);
        }

        void OnCoordinatorError(string error)
        {
            if (_disposed) return;
            _roomView.SetStatus($"오류: {error}");
        }

        void OnCoordinatorStateChanged(SessionState state, SessionOperation operation)
        {
            if (_disposed) return;

            switch (state)
            {
                case SessionState.Ready:
                    _roomView.SetStatus(_coordinator.LastError ?? "연결 준비 완료");
                    _roomView.SetJoinInteractable(true);
                    _roomView.SetStartInteractable(_coordinator.IsHostRole);
                    break;
                case SessionState.Failed:
                    _roomView.SetStatus(_coordinator.LastError ?? "연결에 실패했습니다.");
                    _roomView.SetJoinInteractable(true);
                    break;
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
                    _roomView.SetStatus(opLabel);
                    break;
                case SessionState.LoadingGame:
                    _roomView.SetStatus("게임 로딩 중...");
                    break;
                case SessionState.InGame:
                    _roomView.SetStatus("게임 시작!");
                    break;
            }
        }

        #endregion
    }
}
