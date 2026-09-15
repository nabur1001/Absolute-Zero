using System;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.LobbyUI
{
    public class LobbyModeSelectView
    {
        readonly GameObject _root;

        public event Action OnOneVsOneClicked;
        public event Action<int> OnMultiClicked;
        public event Action OnBackClicked;

        public GameObject Root => _root;

        public LobbyModeSelectView(GameObject root)
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
                dimBtn.onClick.AddListener(() => OnBackClicked?.Invoke());

            var oneVsOneBtn = panel.Find("OneVsOneBtn")?.GetComponent<Button>();
            if (oneVsOneBtn != null)
                oneVsOneBtn.onClick.AddListener(() => OnOneVsOneClicked?.Invoke());

            var multi3Btn = panel.Find("Multi3Btn")?.GetComponent<Button>();
            if (multi3Btn != null)
                multi3Btn.onClick.AddListener(() => OnMultiClicked?.Invoke(3));

            var multi4Btn = panel.Find("Multi4Btn")?.GetComponent<Button>();
            if (multi4Btn != null)
                multi4Btn.onClick.AddListener(() => OnMultiClicked?.Invoke(4));

            var backBtn = panel.Find("BackLink")?.GetComponent<Button>();
            if (backBtn != null)
                backBtn.onClick.AddListener(() => OnBackClicked?.Invoke());
        }

        public void SetVisible(bool visible) => _root.SetActive(visible);
    }
}
