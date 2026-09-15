using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.LobbyUI
{
    public static class UIHelper
    {
        public static TextMeshProUGUI CreateText(Transform parent, string name,
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

        public static Image CreatePanel(Transform parent, string name,
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

        public static Button CreateButton(Transform parent, string name,
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
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.2f;
            colors.pressedColor = bgColor * 0.7f;
            colors.disabledColor = new Color(0.78f, 0.75f, 0.71f, 0.6f);
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

        public static TMP_InputField CreateInputField(Transform parent, string name,
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
            phText.color = new Color(0.61f, 0.56f, 0.50f, 0.7f);
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

        public static GameObject CreatePlayerSlot(Transform parent, string label, Color bgColor)
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
    }
}
