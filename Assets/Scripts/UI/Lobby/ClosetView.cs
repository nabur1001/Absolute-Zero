using System;
using System.Collections.Generic;
using AbsoluteZero.Core.Cosmetic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.LobbyUI
{
    public class ClosetView
    {
        readonly GameObject _root;
        Transform _listContent;
        readonly List<GameObject> _itemSlots = new();
        CosmeticPart _currentTab = CosmeticPart.Head;
        readonly Button[] _tabButtons = new Button[5];

        static readonly Color TabActive = new(0.24f, 0.48f, 0.50f);
        static readonly Color TabInactive = new(0.40f, 0.38f, 0.35f);
        static readonly Color EquippedColor = new(0.18f, 0.55f, 0.34f);
        static readonly Color UnequippedColor = new(0.55f, 0.45f, 0.33f);

        public event Action<CosmeticPart> OnTabChanged;
        public event Action<CosmeticItemSO> OnEquipClicked;
        public event Action<CosmeticPart> OnUnequipClicked;
        public event Action OnCloseClicked;

        public GameObject Root => _root;

        public ClosetView(GameObject root)
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

            BindTabs(panel);

            var scroll = panel.Find("Scroll");
            if (scroll != null)
            {
                var content = scroll.Find("Content");
                if (content != null)
                    _listContent = content;
            }

            var closeBtn = panel.Find("CloseBtn")?.GetComponent<Button>();
            if (closeBtn != null)
                closeBtn.onClick.AddListener(() => OnCloseClicked?.Invoke());
        }

        void BindTabs(Transform panel)
        {
            var tabs = panel.Find("Tabs");
            if (tabs == null) return;

            string[] tabNames = { "Tab_Head", "Tab_Top", "Tab_Back", "Tab_Bottom", "Tab_Tail" };
            CosmeticPart[] parts = { CosmeticPart.Head, CosmeticPart.Top, CosmeticPart.Back, CosmeticPart.Bottom, CosmeticPart.Tail };

            for (int i = 0; i < 5; i++)
            {
                var btn = tabs.Find(tabNames[i])?.GetComponent<Button>();
                if (btn == null) continue;

                _tabButtons[i] = btn;
                var part = parts[i];
                btn.onClick.AddListener(() =>
                {
                    _currentTab = part;
                    UpdateTabVisuals();
                    OnTabChanged?.Invoke(part);
                });
            }
        }

        public void RenderItems(List<CosmeticItemSO> items, CosmeticEquipState equipState)
        {
            foreach (var slot in _itemSlots)
                UnityEngine.Object.Destroy(slot);
            _itemSlots.Clear();

            if (items == null || _listContent == null) return;

            foreach (var item in items)
            {
                bool isEquipped = equipState != null && equipState.GetEquipped(item.Part) == item;
                var slot = CreateItemSlot(item, isEquipped);
                _itemSlots.Add(slot);
            }
        }

        GameObject CreateItemSlot(CosmeticItemSO item, bool isEquipped)
        {
            var go = new GameObject($"Item_{item.Id}");
            go.transform.SetParent(_listContent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 50);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 50;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.96f, 0.93f, 0.88f);

            var label = UIHelper.CreateText(go.transform, "Name",
                new Vector2(-60, 0), new Vector2(280, 40),
                item.DisplayName, 20, new Color(0.17f, 0.09f, 0.06f));
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var labelRT = label.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(0.6f, 1);
            labelRT.offsetMin = new Vector2(16, 4);
            labelRT.offsetMax = new Vector2(0, -4);

            string btnLabel = isEquipped ? "해제" : "장착";
            Color btnColor = isEquipped ? EquippedColor : UnequippedColor;
            var btn = UIHelper.CreateButton(go.transform, "EquipBtn",
                Vector2.zero, new Vector2(80, 36), btnLabel, btnColor, 18);
            var btnRT = btn.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(1, 0.5f);
            btnRT.anchorMax = new Vector2(1, 0.5f);
            btnRT.anchoredPosition = new Vector2(-56, 0);

            if (isEquipped)
                btn.onClick.AddListener(() => OnUnequipClicked?.Invoke(item.Part));
            else
                btn.onClick.AddListener(() => OnEquipClicked?.Invoke(item));

            return go;
        }

        void UpdateTabVisuals()
        {
            CosmeticPart[] parts = { CosmeticPart.Head, CosmeticPart.Top, CosmeticPart.Back, CosmeticPart.Bottom, CosmeticPart.Tail };
            for (int i = 0; i < 5; i++)
            {
                if (_tabButtons[i] == null) continue;
                var img = _tabButtons[i].GetComponent<Image>();
                if (img != null)
                    img.color = parts[i] == _currentTab ? TabActive : TabInactive;
            }
        }

        public void SetVisible(bool visible)
        {
            _root.SetActive(visible);
            if (visible)
            {
                _currentTab = CosmeticPart.Head;
                UpdateTabVisuals();
            }
        }

        public CosmeticPart CurrentTab => _currentTab;
    }
}
