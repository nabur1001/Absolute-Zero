using System.Collections.Generic;
using AbsoluteZero.Core.Player;
using AbsoluteZero.UI.Game.Bridge;
using AbsoluteZero.UI.Game.Build;
using UnityEngine;

namespace AbsoluteZero.UI.Game.Presenters
{
    public sealed class OpponentBarPresenter : MonoBehaviour
    {
        IGameDataBridge _bridge;
        readonly List<OppBarEntry> _bars = new();
        readonly Dictionary<byte, int> _seatToBar = new();
        readonly Dictionary<int, Transform> _barTargets = new();
        int _lastRemoteCount;
        byte _lastLocalSeat = byte.MaxValue;
        Transform _barRoot;

        bool _initialized;

        static readonly Vector3 WORLD_OFFSET = new(0f, 2.2f, 0f);

        public void Initialize(IGameDataBridge bridge, GameHudRefs refs)
        {
            _bridge = bridge;
            _bars.Clear();
            _bars.AddRange(refs.OppBars);
            _seatToBar.Clear();
            _barTargets.Clear();
            _lastRemoteCount = 0;
            _initialized = true;

            _barRoot = new GameObject("OppBarRoot").transform;
            _barRoot.SetParent(transform, false);
            foreach (var bar in _bars)
            {
                if (bar.Canvas != null)
                    bar.Canvas.transform.SetParent(_barRoot, false);
            }
        }

        void Update()
        {
            if (!_initialized) return;

            var cam = Camera.main;
            if (cam == null) return;

            byte localSeat = _bridge.LocalSeatIndex;
            var players = FindObjectsByType<PlayerState>(FindObjectsSortMode.None);

            int remoteCount = 0;
            foreach (var ps in players)
                if (ps.PlayerIndex >= 0 && ps.PlayerIndex != localSeat)
                    remoteCount++;

            if (remoteCount != _lastRemoteCount || localSeat != _lastLocalSeat)
            {
                RebuildMapping(localSeat, players);
                _lastRemoteCount = remoteCount;
                _lastLocalSeat = localSeat;
            }

            for (int i = 0; i < _bars.Count; i++)
            {
                var bar = _bars[i];
                if (bar.Canvas == null) continue;

                if (_barTargets.TryGetValue(i, out var target))
                {
                    if (target == null)
                    {
                        _barTargets.Remove(i);
                    }
                    else
                    {
                        bar.Canvas.gameObject.SetActive(true);
                        bar.Canvas.worldCamera = cam;
                        bar.Canvas.transform.position = target.position + WORLD_OFFSET;
                        bar.Canvas.transform.rotation = cam.transform.rotation;
                        continue;
                    }
                }

                byte targetSeat = 0;
                bool hasSeat = false;
                foreach (var kvp in _seatToBar)
                {
                    if (kvp.Value == i) { hasSeat = true; targetSeat = kvp.Key; break; }
                }

                if (hasSeat)
                {
                    TryResolveTarget(i, targetSeat, players);
                    bar.Canvas.gameObject.SetActive(_barTargets.ContainsKey(i) && _barTargets[i] != null);
                }
                else
                {
                    bar.Canvas.gameObject.SetActive(false);
                }
            }
        }

        void RebuildMapping(byte localSeat, PlayerState[] players)
        {
            _seatToBar.Clear();
            _barTargets.Clear();

            var remoteSeats = new List<byte>();
            foreach (var ps in players)
            {
                if (ps.PlayerIndex >= 0 && ps.PlayerIndex != localSeat)
                    remoteSeats.Add((byte)ps.PlayerIndex);
            }
            remoteSeats.Sort();

            for (int i = 0; i < remoteSeats.Count && i < _bars.Count; i++)
            {
                int slot = AZPlayerVisual.GetRemoteVisualSlot(remoteSeats[i], localSeat);
                if (slot < 0 || slot >= _bars.Count) continue;
                _seatToBar[remoteSeats[i]] = slot;
                TryResolveTarget(slot, remoteSeats[i], players);
            }
        }

        void TryResolveTarget(int barIndex, byte targetSeat, PlayerState[] players)
        {
            foreach (var ps in players)
            {
                if (ps.PlayerIndex == targetSeat)
                {
                    var visual = ps.GetComponent<AZPlayerVisual>();
                    if (visual != null)
                    {
                        var vRoot = visual.GetVisualRoot();
                        if (vRoot != null)
                        {
                            _barTargets[barIndex] = vRoot;
                            return;
                        }
                    }
                    break;
                }
            }

            // Wait for the seat's actual visual binding; an unclaimed slot is not a target.
        }
    }
}
