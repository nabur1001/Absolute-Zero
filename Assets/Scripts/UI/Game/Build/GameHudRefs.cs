using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.Game.Build
{
    public sealed class GameHudRefs
    {
        public Canvas OverlayCanvas;
        public TextMeshProUGUI PhaseText;
        public TextMeshProUGUI TimerText;
        public TextMeshProUGUI StatusText;
        public TextMeshProUGUI ScoreText;
        public TextMeshProUGUI MyTempText;
        public Slider MyHpSlider;
        public Image MyHpFillImage;
        public Image TimerFillImage;
        public Image TimerSliderImage;
        public RectTransform ClockHandRT;
        public GameObject TimeAlarmObj;
        public Image TimeAlarmImage;
        public RectTransform TimerContainerRT;
        public GameObject EnvPanel;
        public TextMeshProUGUI EnvText;

        public Image CinematicOverlay;
        public TextMeshProUGUI CinematicText;
        public Button LobbyButton;

        public Image P1NameBox;
        public Image P2NameBox;
        public TextMeshProUGUI P1NameText;
        public TextMeshProUGUI P2NameText;
        public TextMeshProUGUI CrownText;

        public Canvas OppBarCanvas;
        public TextMeshProUGUI OppTempText;
        public Slider OppHpSlider;
        public Image OppHpFillImage;

        public Canvas ReadyCanvas;
        public Button ReadyButton;
        public Image ReadyButtonImage;
    }
}
