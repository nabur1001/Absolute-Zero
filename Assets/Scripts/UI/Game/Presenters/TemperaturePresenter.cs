using System.Collections.Generic;
using AbsoluteZero.Core.Player;
using AbsoluteZero.UI.Game.Bridge;
using AbsoluteZero.UI.Game.Build;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.Game.Presenters
{
    public sealed class TemperaturePresenter
    {
        readonly IGameDataBridge _bridge;

        readonly TextMeshProUGUI _myTempText;
        readonly Slider _myHpSlider;
        readonly Image _myHpFillImage;

        readonly List<OppBarEntry> _oppBars;
        readonly Dictionary<int, float> _displayedOppTemps = new();

        float _displayedMyTemp = 37f;

        readonly Dictionary<byte, float?> _tempOverrides = new();

        const float HP_LERP_SPEED = 6f;

        static readonly Color HP_COLOR_GREEN = new(0.39f, 0.78f, 0.31f, 1f);
        static readonly Color HP_COLOR_PINK = new(0.90f, 0.47f, 0.59f, 1f);
        static readonly Color HP_COLOR_SKY = new(0.39f, 0.71f, 0.92f, 1f);
        static readonly Color HP_COLOR_BLUE = new(0.20f, 0.39f, 0.86f, 1f);

        public TemperaturePresenter(IGameDataBridge bridge, GameHudRefs refs)
        {
            _bridge = bridge;
            _myTempText = refs.MyTempText;
            _myHpSlider = refs.MyHpSlider;
            _myHpFillImage = refs.MyHpFillImage;
            _oppBars = refs.OppBars;

            _bridge.OnTempOverride += HandleTempOverride;
            _bridge.OnTempOverridesClear += HandleTempOverridesClear;
        }

        public void Tick(float deltaTime)
        {
            byte localSeat = _bridge.LocalSeatIndex;

            if (_bridge.TryGetSeat(localSeat, out var mySeat))
            {
                float myTarget = GetOverrideOrActual(localSeat, mySeat.Temperature);
                _displayedMyTemp = Mathf.MoveTowards(_displayedMyTemp, myTarget, HP_LERP_SPEED * deltaTime);
                _myTempText.text = $"{_displayedMyTemp:F0}°";
                if (_myHpSlider != null)
                    _myHpSlider.value = Mathf.Clamp01(_displayedMyTemp / 37f);
                if (_myHpFillImage != null)
                    _myHpFillImage.color = GetTempColor(_displayedMyTemp);
            }

            for (byte oppSeat = 0; oppSeat < 4; oppSeat++)
            {
                int i = AZPlayerVisual.GetRemoteVisualSlot(oppSeat, localSeat);
                if (i < 0 || i >= _oppBars.Count) continue;
                var bar = _oppBars[i];
                if (!_bridge.TryGetSeat(oppSeat, out var oppSnapshot))
                    continue;

                if (!_displayedOppTemps.ContainsKey(i))
                    _displayedOppTemps[i] = 37f;

                float oppTarget = GetOverrideOrActual(oppSeat, oppSnapshot.Temperature);
                float displayed = Mathf.MoveTowards(_displayedOppTemps[i], oppTarget, HP_LERP_SPEED * deltaTime);
                _displayedOppTemps[i] = displayed;

                if (bar.TempText != null)
                    bar.TempText.text = $"{displayed:F0}°";
                if (bar.HpSlider != null)
                    bar.HpSlider.value = Mathf.Clamp01(displayed / 37f);
                if (bar.HpFillImage != null)
                    bar.HpFillImage.color = GetTempColor(displayed);
            }
        }

        public void SnapTempDisplay()
        {
            byte localSeat = _bridge.LocalSeatIndex;
            if (_bridge.TryGetSeat(localSeat, out var mySeat))
                _displayedMyTemp = mySeat.Temperature;

            for (byte seat = 0; seat < 4; seat++)
            {
                int i = AZPlayerVisual.GetRemoteVisualSlot(seat, localSeat);
                if (i < 0 || i >= _oppBars.Count) continue;
                if (_bridge.TryGetSeat(seat, out var oppSnapshot))
                    _displayedOppTemps[i] = oppSnapshot.Temperature;
            }
        }

        public void Dispose()
        {
            _bridge.OnTempOverride -= HandleTempOverride;
            _bridge.OnTempOverridesClear -= HandleTempOverridesClear;
        }

        void HandleTempOverride(byte seatIndex, float temp)
        {
            _tempOverrides[seatIndex] = temp;
        }

        void HandleTempOverridesClear()
        {
            _tempOverrides.Clear();
        }

        float GetOverrideOrActual(byte seatIndex, float actualTemp)
        {
            if (_tempOverrides.TryGetValue(seatIndex, out var ov) && ov.HasValue)
                return ov.Value;
            return actualTemp;
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
    }
}
