using UnityEngine;

namespace CamLinkPro.Preferences
{
    /// Lazy-load-from-PlayerPrefs, write-through-on-set cache. Every preference in
    /// the app (calibration, HUD visibility, app settings) is one of these so the
    /// "hide only, never disable function" semantics stay uniform everywhere.
    public sealed class PrefBool
    {
        readonly string _key;
        readonly bool _default;
        bool? _cache;

        public PrefBool(string key, bool defaultValue)
        {
            _key = key;
            _default = defaultValue;
        }

        public bool Value
        {
            get => _cache ??= PlayerPrefs.GetInt(_key, _default ? 1 : 0) != 0;
            set
            {
                _cache = value;
                PlayerPrefs.SetInt(_key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }

    public sealed class PrefFloat
    {
        readonly string _key;
        readonly float _default;
        float? _cache;

        public PrefFloat(string key, float defaultValue)
        {
            _key = key;
            _default = defaultValue;
        }

        public float Value
        {
            get => _cache ??= PlayerPrefs.GetFloat(_key, _default);
            set
            {
                _cache = value;
                PlayerPrefs.SetFloat(_key, value);
                PlayerPrefs.Save();
            }
        }
    }

    public sealed class PrefString
    {
        readonly string _key;
        readonly string _default;
        string _cache;

        public PrefString(string key, string defaultValue)
        {
            _key = key;
            _default = defaultValue;
        }

        public string Value
        {
            get => _cache ??= PlayerPrefs.GetString(_key, _default);
            set
            {
                _cache = value;
                PlayerPrefs.SetString(_key, value);
                PlayerPrefs.Save();
            }
        }
    }
}
