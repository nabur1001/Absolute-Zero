using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Player;
using Unity.Netcode;
using UnityEngine;

namespace AbsoluteZero.Core.Solo
{
    public enum BotDifficulty : byte
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }

    public sealed class BotBrain : MonoBehaviour
    {
        PlayerState _playerState;
        Turn.TurnManager _turnManager;
        BotDifficulty _difficulty = BotDifficulty.Normal;
        bool _active;

        float _actionDelay;
        float _readyDelay;
        bool _actionDone;
        bool _readyDone;
        bool _phaseEntered;
        Coroutine _miniGameCoroutine;

        void Update()
        {
            if (!_active) return;

            if (_playerState == null || !_playerState.IsSpawned)
            {
                TryFindPlayerState();
                return;
            }

            if (_turnManager == null)
                _turnManager = FindAnyObjectByType<Turn.TurnManager>();
            if (_turnManager == null) return;

            if (_turnManager.CurrentPhase.Value != TurnPhase.PrepPhase)
            {
                if (_phaseEntered)
                {
                    _actionDone = false;
                    _readyDone = false;
                    _phaseEntered = false;
                }
                return;
            }

            if (!_phaseEntered)
            {
                _phaseEntered = true;
                _actionDelay = GetActionDelay();
                _actionDone = false;
                _readyDone = false;
            }

            if (!_actionDone)
            {
                _actionDelay -= Time.deltaTime;
                if (_actionDelay <= 0f)
                {
                    PickAction();
                    _actionDone = true;
                    _readyDelay = GetReadyDelay();
                }
                return;
            }

            if (!_readyDone)
            {
                _readyDelay -= Time.deltaTime;
                if (_readyDelay <= 0f)
                {
                    _playerState.PressReadyServerRpc();
                    _readyDone = true;
                }
            }
        }

        void TryFindPlayerState()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient) return;

            var players = FindObjectsByType<PlayerState>(FindObjectsSortMode.None);
            foreach (var ps in players)
            {
                if (ps.IsOwner && ps.IsSpawned)
                {
                    _playerState = ps;
                    _active = true;
                    _phaseEntered = false;
                    ps.OnMiniGameStart += OnMiniGameStart;
                    Debug.Log($"[Bot] Found local PlayerState — seat {ps.PlayerIndex}");
                    return;
                }
            }
        }

        void OnDestroy()
        {
            if (_miniGameCoroutine != null) StopCoroutine(_miniGameCoroutine);
            if (_playerState != null) _playerState.OnMiniGameStart -= OnMiniGameStart;
        }

        void OnMiniGameStart(byte slotIndex, MiniGameType type, float timeLimit, int goal)
        {
            if (_playerState == null || !_playerState.IsSpawned) return;
            float delay = _difficulty == BotDifficulty.Hard ? 0.3f : Random.Range(0.5f, timeLimit * 0.5f);
            if (_miniGameCoroutine != null) StopCoroutine(_miniGameCoroutine);
            _miniGameCoroutine = StartCoroutine(AutoSubmitMiniGame(slotIndex, delay));
        }

        System.Collections.IEnumerator AutoSubmitMiniGame(byte slotIndex, float delay)
        {
            float elapsed = 0f;
            while (elapsed < delay) { elapsed += Time.deltaTime; yield return null; }
            if (_playerState != null && _playerState.IsSpawned)
            {
                bool success = _difficulty != BotDifficulty.Easy || Random.value > 0.3f;
                _playerState.SubmitMiniGameResultServerRpc(slotIndex, success);
                Debug.Log($"[Bot] Mini-game auto-submit: slot={slotIndex}, success={success}");
            }
            _miniGameCoroutine = null;
        }

        void PickAction()
        {
            if (_playerState == null) return;
            var inventory = _playerState.GetComponent<PlayerInventory>();
            if (inventory == null) return;

            int bestSlot = -1;
            float bestScore = -1f;

            for (int i = 0; i < inventory.SlotStates.Count; i++)
            {
                var slot = inventory.SlotStates[i];
                if (!slot.IsUsable) continue;

                float score = ScoreSlot(inventory, i, slot);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSlot = i;
                }
            }

            if (bestSlot >= 0)
            {
                _playerState.SelectItemServerRpc((byte)bestSlot);
                Debug.Log($"[Bot] Selected slot {bestSlot} (score={bestScore:F1})");
            }
        }

        float ScoreSlot(PlayerInventory inventory, int slotIndex, ItemSlotNetData slot)
        {
            var itemData = inventory.GetItemData(slotIndex);
            if (itemData == null) return 0f;

            float myTemp = _playerState.Temperature.Value;
            float baseScore = Random.Range(0f, 1f);

            switch (_difficulty)
            {
                case BotDifficulty.Easy:
                    return baseScore;

                case BotDifficulty.Normal:
                    if (itemData.Category == ItemCategory.Recovery && myTemp > 30f)
                        return baseScore + 2f;
                    if (itemData.Category == ItemCategory.Attack)
                        return baseScore + 1f;
                    return baseScore;

                case BotDifficulty.Hard:
                    if (itemData.Category == ItemCategory.Recovery && myTemp > 25f)
                        return baseScore + 3f;
                    if (itemData.Category == ItemCategory.Attack && myTemp < 30f)
                        return baseScore + 2f;
                    if (itemData.Category == ItemCategory.Defense)
                        return baseScore + 1.5f;
                    return baseScore;

                default:
                    return baseScore;
            }
        }

        float GetActionDelay()
        {
            return _difficulty switch
            {
                BotDifficulty.Easy => Random.Range(3f, 6f),
                BotDifficulty.Normal => Random.Range(1.5f, 4f),
                BotDifficulty.Hard => Random.Range(0.5f, 2f),
                _ => 2f
            };
        }

        float GetReadyDelay()
        {
            return _difficulty switch
            {
                BotDifficulty.Easy => Random.Range(1f, 3f),
                BotDifficulty.Normal => Random.Range(0.5f, 2f),
                BotDifficulty.Hard => Random.Range(0.2f, 1f),
                _ => 1f
            };
        }

        public void SetDifficulty(BotDifficulty difficulty)
        {
            _difficulty = difficulty;
            Debug.Log($"[Bot] Difficulty set to {difficulty}");
        }
    }
}
