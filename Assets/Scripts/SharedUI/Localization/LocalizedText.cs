using UnityEngine;
using RailwayManager.Core;
using TMPro;

namespace RailwayManager.SharedUI.Localization
{
    /// <summary>
    /// MonoBehaviour wrapper dla TextMeshProUGUI / TextMeshPro który auto-aktualizuje
    /// tekst przy zmianie języka (subskrybuje <see cref="LocalizationService.OnLanguageChanged"/>).
    ///
    /// Użycie: dodaj komponent obok TMP, ustaw <see cref="key"/> w Inspector lub
    /// w kodzie przez <see cref="SetKey"/>. Tekst odświeży się przy każdym SetLanguage.
    ///
    /// Dla string interpolation z params użyj <see cref="LocalizedTextFormatted"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string key;

        /// <summary>Jeśli false — komponent nie subskrybuje OnLanguageChanged (jednorazowy refresh w Awake).</summary>
        [SerializeField] private bool autoUpdate = true;

        private TMP_Text _tmp;

        public string Key
        {
            get => key;
            set
            {
                key = value;
                Refresh();
            }
        }

        void Awake()
        {
            _tmp = GetComponent<TMP_Text>();
            if (_tmp == null)
                Log.Warn($"[LocalizedText] No TMP_Text component on {name} — disabling", this);
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

        /// <summary>Wymusza ponowne wczytanie tekstu z LocalizationService.</summary>
        public void Refresh()
        {
            if (_tmp == null) return;
            if (string.IsNullOrEmpty(key)) return;
            _tmp.text = LocalizationService.Get(key);
        }

        /// <summary>API dla code-driven setup (zamiast Inspector field).</summary>
        public void SetKey(string newKey)
        {
            key = newKey;
            Refresh();
        }
    }
}
