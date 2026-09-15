using System.Collections.Generic;
using UnityEngine;

namespace AbsoluteZero.Core.Audio
{
    public class GameAudioManager : MonoBehaviour
    {
        public static GameAudioManager Instance { get; private set; }

        AudioSource _bgmSource;
        AudioSource _sfxSource;
        AudioSource _uiSource;
        AudioSource _envSource;
        AudioSource _fanLoopSource;
        AudioSource _clockSource;

        readonly Dictionary<string, AudioClip> _clipCache = new();

        float _hoverCooldown;
        const float HOVER_COOLDOWN_SEC = 0.1f;

        const float BgmBase = 0.35f;
        const float SfxBase = 0.7f;
        const float UiBase = 0.5f;
        const float EnvBase = 0.6f;
        const float FanBase = 0.15f;
        const float ClockBase = 0.5f;

        float _bgmMaster = 1f;
        float _sfxMaster = 1f;

        static readonly Dictionary<string, string> TriggerToSfx = new()
        {
            { "swing", "SFX_swing" },
            { "fan", "SFX_miniFan" },
            { "gun", "SFX_watergun" },
            { "drink", "SFX_drink" },
            { "eat", "SFX_eat" },
            { "feed", "SFX_feed" },
            { "hug", "SFX_hug" },
            { "defence", "SFX_clothZiper" },
            { "mask", "SFX_wear" },
            { "tape", "SFX_boxtape" },
            { "card", "SFX_redcard" },
            { "heal", "SFX_heal" },
        };

        static readonly Dictionary<string, string> ItemNameToSfx = new()
        {
            { "Screwdriver", "SFX_driver" },
            { "Cat", "SFX_cat" },
            { "Warm Tea", "SFX_drink" },
            { "Hot Americano", "SFX_drink" },
            { "Soda", "SFX_drink" },
            { "Claw Machine", "SFX_steal" },
            { "Tarot Card", "SFX_heal" },
        };

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _bgmSource = CreateSource("BGM", true, BgmBase);
            _sfxSource = CreateSource("SFX", false, SfxBase);
            _uiSource = CreateSource("UI", false, UiBase);
            _envSource = CreateSource("ENV", false, EnvBase);
            _fanLoopSource = CreateSource("FanLoop", true, FanBase);
            _clockSource = CreateSource("Clock", true, ClockBase);

            _bgmMaster = PlayerPrefs.GetFloat("bgm_volume", 1f);
            _sfxMaster = PlayerPrefs.GetFloat("sfx_volume", 1f);
            ApplyBGMVolume();
            ApplySFXVolume();
        }

        public float BGMVolume => _bgmMaster;
        public float SFXVolume => _sfxMaster;

        public void SetBGMVolume(float master)
        {
            _bgmMaster = Mathf.Clamp01(master);
            PlayerPrefs.SetFloat("bgm_volume", _bgmMaster);
            PlayerPrefs.Save();
            ApplyBGMVolume();
        }

        public void SetSFXVolume(float master)
        {
            _sfxMaster = Mathf.Clamp01(master);
            PlayerPrefs.SetFloat("sfx_volume", _sfxMaster);
            PlayerPrefs.Save();
            ApplySFXVolume();
        }

        void ApplyBGMVolume()
        {
            if (_bgmSource != null) _bgmSource.volume = BgmBase * _bgmMaster;
        }

        void ApplySFXVolume()
        {
            if (_sfxSource != null) _sfxSource.volume = SfxBase * _sfxMaster;
            if (_uiSource != null) _uiSource.volume = UiBase * _sfxMaster;
            if (_envSource != null) _envSource.volume = EnvBase * _sfxMaster;
            if (_fanLoopSource != null) _fanLoopSource.volume = FanBase * _sfxMaster;
            if (_clockSource != null) _clockSource.volume = ClockBase * _sfxMaster;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (_hoverCooldown > 0f)
                _hoverCooldown -= Time.unscaledDeltaTime;
        }

        AudioSource CreateSource(string name, bool loop, float volume)
        {
            var go = new GameObject($"Audio_{name}");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.loop = loop;
            src.volume = volume;
            src.playOnAwake = false;
            return src;
        }

        AudioClip LoadClip(string clipName)
        {
            if (_clipCache.TryGetValue(clipName, out var cached))
                return cached;

            var clip = Resources.Load<AudioClip>($"Audio/{clipName}");
            if (clip != null)
                _clipCache[clipName] = clip;
            else
                Debug.LogWarning($"[Audio] Clip not found: Audio/{clipName}");
            return clip;
        }

        // ═══ BGM ═══

        public void PlayBGM()
        {
            if (_bgmSource.isPlaying) return;
            var clip = LoadClip("BGM");
            if (clip == null) return;
            _bgmSource.clip = clip;
            _bgmSource.Play();
        }

        public void StopBGM()
        {
            _bgmSource.Stop();
        }

        // ═══ Item Animation SFX ═══

        public void PlayItemSfx(string animTrigger, string itemName)
        {
            if (ItemNameToSfx.TryGetValue(itemName, out var specialClip))
            {
                PlaySfx(specialClip);
                Debug.Log($"[Audio] PlayItemSfx: '{itemName}' → {specialClip} (by itemName)");
                return;
            }

            if (!string.IsNullOrEmpty(animTrigger) && TriggerToSfx.TryGetValue(animTrigger, out var triggerClip))
            {
                PlaySfx(triggerClip);
                Debug.Log($"[Audio] PlayItemSfx: '{itemName}' trigger='{animTrigger}' → {triggerClip}");
                return;
            }

            Debug.Log($"[Audio] PlayItemSfx: no mapping for '{itemName}' trigger='{animTrigger}'");
        }

        public void PlayClawSteal()
        {
            PlaySfx("SFX_steal");
        }

        // ═══ Combat SFX ═══

        public void PlayDamaged()
        {
            string clip = Random.value > 0.5f ? "SFX_damaged" : "SFX_damaged2";
            PlaySfx(clip);
        }

        public void PlayFreeze()
        {
            PlaySfx("SFX_freeze");
        }

        public void PlayIceBreak()
        {
            PlaySfx("SFX_iceBreak");
        }

        // ═══ Environment SFX ═══

        public void PlayEnvironment(Item.EnvironmentType env)
        {
            StopEnvironment();

            switch (env)
            {
                case Item.EnvironmentType.CicadaSong:
                    PlayEnvLoop("SFX_cicada");
                    break;
                case Item.EnvironmentType.SunnyDay:
                    PlayEnvLoop("SFX_cicada");
                    break;
                case Item.EnvironmentType.HeatWaveWarning:
                    PlayEnvLoop("SFX_cicada");
                    break;
                case Item.EnvironmentType.CoolBreeze:
                    PlayEnvLoop("SFX_wind");
                    break;
                case Item.EnvironmentType.SummerVacation:
                    PlayEnvOneShot("SFX_clock");
                    break;
                case Item.EnvironmentType.Kids:
                    PlayEnvOneShot("SFX_kidWhistle");
                    break;
                case Item.EnvironmentType.Ambulance:
                    PlayEnvOneShot("SFX_siren");
                    break;
            }
        }

        public void PlayKidSteal()
        {
            PlaySfx("SFX_kidSteal");
        }

        public void StopEnvironment()
        {
            _envSource.Stop();
            _envSource.loop = false;
        }

        // ═══ UI SFX ═══

        public void PlayButtonClick()
        {
            PlayUI("button1");
        }

        public void PlayHover()
        {
            if (_hoverCooldown > 0f) return;
            _hoverCooldown = HOVER_COOLDOWN_SEC;
            PlayUI("button1", 0.3f);
        }

        public void PlayBoxOpen()
        {
            PlaySfx("SFX_boxOpen");
        }

        public void PlayClockTick()
        {
            if (_clockSource.isPlaying) return;
            var clip = LoadClip("SFX_clock");
            if (clip == null) return;
            _clockSource.clip = clip;
            _clockSource.Play();
        }

        public void StopClockTick()
        {
            _clockSource.Stop();
        }

        // ═══ Fan Stay Loop ═══

        public void StartFanLoop()
        {
            var clip = LoadClip("SFX_fan_loop");
            if (clip == null) return;
            _fanLoopSource.clip = clip;
            _fanLoopSource.Play();
        }

        public void StopFanLoop()
        {
            _fanLoopSource.Stop();
        }

        // ═══ Internal ═══

        void PlaySfx(string clipName)
        {
            var clip = LoadClip(clipName);
            if (clip != null)
                _sfxSource.PlayOneShot(clip);
        }

        void PlayUI(string clipName, float volumeScale = 1f)
        {
            var clip = LoadClip(clipName);
            if (clip != null)
                _uiSource.PlayOneShot(clip, volumeScale);
        }

        void PlayEnvLoop(string clipName)
        {
            var clip = LoadClip(clipName);
            if (clip == null) return;
            _envSource.clip = clip;
            _envSource.loop = true;
            _envSource.Play();
        }

        void PlayEnvOneShot(string clipName)
        {
            var clip = LoadClip(clipName);
            if (clip != null)
                _envSource.PlayOneShot(clip);
        }
    }
}
