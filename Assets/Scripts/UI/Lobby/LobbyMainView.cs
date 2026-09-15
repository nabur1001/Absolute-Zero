using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.LobbyUI
{
    public class LobbyMainView
    {
        readonly GameObject _root;
        TextMeshProUGUI _statusText;
        TMP_InputField _nicknameInput;

        public event Action OnArenaClicked;
        public event Action OnClosetClicked;
        public event Action OnSettingsClicked;
        public event Action<string> OnNicknameEndEdit;

        public GameObject Root => _root;

        public LobbyMainView(GameObject root)
        {
            _root = root;
            Bind();
        }

        void Bind()
        {
            var t = _root.transform;

            _nicknameInput = t.Find("NicknameBar")?.GetComponent<TMP_InputField>();
            if (_nicknameInput != null)
                _nicknameInput.onEndEdit.AddListener(text => OnNicknameEndEdit?.Invoke(text));

            var arenaBtn = t.Find("ArenaBtn")?.GetComponent<Button>();
            if (arenaBtn != null)
                arenaBtn.onClick.AddListener(() => OnArenaClicked?.Invoke());

            var closetBtn = t.Find("ClosetBtn")?.GetComponent<Button>();
            if (closetBtn != null)
                closetBtn.onClick.AddListener(() => OnClosetClicked?.Invoke());

            var settingsBtn = t.Find("SettingsBtn")?.GetComponent<Button>();
            if (settingsBtn != null)
                settingsBtn.onClick.AddListener(() => OnSettingsClicked?.Invoke());

            _statusText = t.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
        }

        public void SetVisible(bool visible) => _root.SetActive(visible);

        public void SetStatus(string msg)
        {
            if (_statusText != null) _statusText.text = msg;
        }

        public void SetNicknameText(string text)
        {
            if (_nicknameInput != null) _nicknameInput.text = text;
        }
    }
}
