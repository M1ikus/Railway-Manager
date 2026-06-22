using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// MB-1 / Phase A: Rozszerzenie <see cref="TMP_Dropdown"/> o **2-liniowe items**
    /// (główna nazwa + subtitle / opis). Caption (zamknięty dropdown) pokazuje
    /// tylko nazwę, items (otwarty dropdown) pokazują nazwę + subtitle.
    ///
    /// Użycie:
    /// <code>
    /// var dd = ddGO.AddComponent&lt;MultiLineDropdown&gt;();
    /// dd.ClearAllOptions();
    /// dd.AddOptionWithSubtitle("Łatwy",     "dla początkujących");
    /// dd.AddOptionWithSubtitle("Normalny",  "polecany na pierwszy run");
    /// dd.AddOptionWithSubtitle("Trudny",    "dla doświadczonych zarządców");
    /// </code>
    ///
    /// Implementacja: po <c>base.Show()</c> (gdy items są zinstancjowane), wstrzykujemy
    /// rich-text subtitle w pole label każdego item'a (`label\n&lt;size=14&gt;subtitle&lt;/size&gt;`).
    /// Pierwszy frame opóźnienia przez coroutine — TMP_Dropdown internally instancjonuje items
    /// w `Show()` ale niektóre layout/canvas updates lecą w następnym frame'ie.
    ///
    /// Caption nie jest modyfikowany — zostaje single-line z samą nazwą (zamknięty
    /// dropdown ma 36px wysokości, nie zmieści dwóch linii).
    /// </summary>
    public class MultiLineDropdown : TMP_Dropdown
    {
        [Header("MultiLine settings")]
        [Tooltip("Wysokość item'a w otwartym dropdown'ie (musi pomieścić 2 linie).")]
        public float itemHeight = 50f;

        [Tooltip("Rozmiar fontu subtitle (mniejszy niż główny label).")]
        public float subtitleFontSize = 14f;

        [Tooltip("Hex color subtitle (bez '#'). Default = miękki szary.")]
        public string subtitleColorHex = "9CA3AF";

        // Parallel list of subtitles, indexed by option order.
        private readonly List<string> _subtitles = new List<string>();

        // ── Public API ──────────────────────────────

        /// <summary>Dodaje opcję z subtitle. Zachowuje synchroniczne indeksy z options/subtitles.</summary>
        public void AddOptionWithSubtitle(string mainLabel, string subtitle)
        {
            options.Add(new OptionData(mainLabel ?? ""));
            _subtitles.Add(subtitle ?? "");
            RefreshShownValue();
        }

        /// <summary>Czyści wszystkie opcje + subtitles. Idempotentne.</summary>
        public void ClearAllOptions()
        {
            ClearOptions();
            _subtitles.Clear();
        }

        /// <summary>Zwraca subtitle dla indexu (lub pusty string jeśli brak).</summary>
        public string GetSubtitle(int index)
        {
            return (index >= 0 && index < _subtitles.Count) ? _subtitles[index] : "";
        }

        // ── Show hooks ──────────────────────────────
        // TMP_Dropdown.Show() jest public ale NIE virtual w tej wersji TMP — nie można override.
        // Hook'ujemy przez Selectable.OnPointerClick/OnSubmit (virtual) — oba wywołują Show()
        // wewnętrznie. Po base.* mamy dropdown otwarty, w następnym frame'ie items są
        // zinstancjowane i można wstrzyknąć subtitle.

        public override void OnPointerClick(PointerEventData eventData)
        {
            base.OnPointerClick(eventData);
            if (isActiveAndEnabled) StartCoroutine(InjectSubtitlesNextFrame());
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            base.OnSubmit(eventData);
            if (isActiveAndEnabled) StartCoroutine(InjectSubtitlesNextFrame());
        }

        private IEnumerator InjectSubtitlesNextFrame()
        {
            yield return null; // Wait one frame

            var content = FindContentParent();
            if (content == null) yield break;

            int subtitleIdx = 0;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (child == null || !child.gameObject.activeSelf) continue;

                if (subtitleIdx >= _subtitles.Count) break;
                var sub = _subtitles[subtitleIdx];
                if (!string.IsNullOrEmpty(sub))
                    InjectSubtitleIntoItem(child, sub);

                subtitleIdx++;
            }
        }

        private Transform FindContentParent()
        {
            // TMP_Dropdown instancjonuje "Dropdown List" jako sibling component.
            // ScrollRect.content trzyma items.
            foreach (Transform child in transform)
            {
                var sr = child.GetComponentInChildren<ScrollRect>();
                if (sr != null && sr.content != null) return sr.content;
            }
            return null;
        }

        private void InjectSubtitleIntoItem(Transform item, string subtitle)
        {
            var label = item.GetComponentInChildren<TextMeshProUGUI>();
            if (label == null) return;

            // Idempotent: marker komponent zamiast text scanning. Wcześniej `Contains("\n<size=")`
            // dawał false-positive jeśli user data faktycznie zawiera literal `\n<size=`.
            if (label.GetComponent<SubtitleInjectedMark>() != null) return;
            label.gameObject.AddComponent<SubtitleInjectedMark>();

            string mainText = label.text ?? "";
            label.text = $"{mainText}\n<size={subtitleFontSize}><color=#{subtitleColorHex}>{subtitle}</color></size>";

            // Increase item height żeby pomieścić 2 linie
            var rt = item.GetComponent<RectTransform>();
            if (rt != null && rt.sizeDelta.y < itemHeight)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, itemHeight);

            // Vertical alignment center żeby tekst nie był ucięty
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
        }

        /// <summary>Marker komponent — sygnalizuje że dany TMP label już dostał wstrzyknięty subtitle (idempotent re-show).</summary>
        private class SubtitleInjectedMark : MonoBehaviour { }
    }
}
