using UnityEngine;
using RailwayManager.Core;
using TMPro;

namespace RailwayManager.SharedUI.Localization
{
    /// <summary>
    /// Wariant <see cref="LocalizedText"/> z runtime args dla
    /// <c>string.Format</c>. Użycie:
    /// <code>
    /// var lt = GetComponent&lt;LocalizedTextFormatted&gt;();
    /// lt.SetKeyAndArgs("delays.format", trainNumber, delayMinutes);
    /// </code>
    ///
    /// Po SetLanguage tekst jest re-formated z ostatnio przekazanymi args.
    /// </summary>
    [DisallowMultipleComponent]
    public class LocalizedTextFormatted : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private bool autoUpdate = true;

        private TMP_Text _tmp;
        private object[] _args;

        void Awake()
        {
            _tmp = GetComponent<TMP_Text>();
            if (_tmp == null)
                Log.Warn($"[LocalizedTextFormatted] No TMP_Text on {name}", this);
        }

        void OnEnable()
        {
            Refresh();
            if (autoUpdate)
                LocalizationService.OnLanguageChanged += Refresh;
        }

        void OnDisable()
        {
            if (autoUpdate)
                LocalizationService.OnLanguageChanged -= Refresh;
        }

        /// <summary>Ustaw key + args razem (typowy use case).</summary>
        public void SetKeyAndArgs(string newKey, params object[] args)
        {
            key = newKey;
            _args = args;
            Refresh();
        }

        /// <summary>Update tylko args (key bez zmian) — np. dla licznika opóźnień co tick.</summary>
        public void SetArgs(params object[] args)
        {
            _args = args;
            Refresh();
        }

        public void Refresh()
        {
            if (_tmp == null) return;
            if (string.IsNullOrEmpty(key)) return;
            _tmp.text = _args != null && _args.Length > 0
                ? LocalizationService.Get(key, _args)
                : LocalizationService.Get(key);
        }
    }
}
