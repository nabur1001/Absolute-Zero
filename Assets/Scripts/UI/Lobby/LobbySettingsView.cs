using System;
using AbsoluteZero.Core.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.LobbyUI
{
    public class LobbySettingsView
    {
        readonly GameObject _root;
        Slider _bgmSlider;
        Slider _sfxSlider;

        public event Action OnCloseClicked;

        public GameObject Root => _root;

        public LobbySettingsView(GameObject root)
        {
            _root = root;
            Bind();
        }

        void Bind()
        {
            var panel = _root.transform.Find("PanelBG");
            if (panel == null) return;

            var dimBtn = _root.transform.Find("Dim")?.GetComponent<Button>();
            if (dimBtn != null)
                dimBtn.onClick.AddListener(() => OnCloseClicked?.Invoke());

            _bgmSlider = panel.Find("BGMSlider/Slider")?.GetComponent<Slider>();
            _sfxSlider = panel.Find("SFXSlider/Slider")?.GetComponent<Slider>();

            var audioMgr = GameAudioManager.Instance;
            if (audioMgr != null)
            {
                if (_bgmSlider != null) _bgmSlider.value = audioMgr.BGMVolume;
                if (_sfxSlider != null) _sfxSlider.value = audioMgr.SFXVolume;
            }

            if (_bgmSlider != null)
                _bgmSlider.onValueChanged.AddListener(val =>
                {
                    var mgr = GameAudioManager.Instance;
                    if (mgr != null) mgr.SetBGMVolume(val);
                });

            if (_sfxSlider != null)
                _sfxSlider.onValueChanged.AddListener(val =>
                {
                    var mgr = GameAudioManager.Instance;
                    if (mgr != null) mgr.SetSFXVolume(val);
                });

            var closeBtn = panel.Find("CloseBtn")?.GetComponent<Button>();
            if (closeBtn != null)
                closeBtn.onClick.AddListener(() => OnCloseClicked?.Invoke());
        }

        public void SetVisible(bool visible)
        {
            _root.SetActive(visible);

            if (visible)
            {
                var audioMgr = GameAudioManager.Instance;
                if (audioMgr != null)
                {
                    if (_bgmSlider != null) _bgmSlider.SetValueWithoutNotify(audioMgr.BGMVolume);
                    if (_sfxSlider != null) _sfxSlider.SetValueWithoutNotify(audioMgr.SFXVolume);
                }
            }
        }
    }
}
