using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.Timetable;

namespace RailwayManager.Timetable.Simulation
{
    public partial class DepotLocationPickerUI
    {
        // Station selection + search + confirm

        void SelectStation(RailwayStation s)
        {
            _selected = s;
            UpdateSelectedInfo();
            ClearResultRows();
            if (_resultsPanel != null) _resultsPanel.SetActive(false);
        }

        static RailwayStation FindStationByProximity(Vector3 worldPos)
        {
            var init = TimetableInitializer.Instance;
            if (init == null || init.Stations == null) return null;

            var pos2d = new Vector2(worldPos.x, worldPos.z);
            RailwayStation best = null;
            float bestDist = 500f;
            foreach (var s in init.Stations)
            {
                float d = Vector2.Distance(s.position, pos2d);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s;
                }
            }

            return best;
        }

        /// <summary>
        /// Home depot candidate rules:
        /// - station is snapped to the rail graph
        /// - station is a major station
        /// - country is enabled in active DLC set
        /// </summary>
        static bool IsEligible(RailwayStation s)
        {
            if (s.pathNodeId < 0 || !s.isMajorStation) return false;
            string country = string.IsNullOrEmpty(s.countryCode) ? "PL" : s.countryCode;
            return GameState.IsCountryActive(country);
        }

        void OnSearchChanged(string query)
        {
            ClearResultRows();

            if (string.IsNullOrWhiteSpace(query))
            {
                _resultsPanel.SetActive(false);
                return;
            }

            _resultsPanel.SetActive(true);

            var init = TimetableInitializer.Instance;
            if (init == null || init.Stations == null) return;

            string q = query.Trim().ToLowerInvariant();
            int maxResults = 30;
            int count = 0;

            foreach (var s in init.Stations)
            {
                if (count >= maxResults) break;
                if (!IsEligible(s)) continue;
                // 2026-05-17: IsNullOrWhiteSpace zamiast IsNullOrEmpty — łapie też " ", "\t" itd.
                // (OSM ma czasem stacje z whitespace-only nazwą po sanitize'rze).
                if (string.IsNullOrWhiteSpace(s.name)) continue;
                if (!s.name.ToLowerInvariant().Contains(q)) continue;

                BuildResultRow(s);
                count++;
            }

            if (count == 0)
                BuildNoResultsRow();
        }

        void BuildResultRow(RailwayStation station)
        {
            var rowGo = new GameObject($"Row_{station.stationId}", typeof(RectTransform));
            rowGo.transform.SetParent(_resultsContent, false);

            // 2026-05-17: explicit row anchor full-width żeby VLG poprawnie roz­ciągnął.
            // Wcześniej domyślny anchor (0.5,0.5)/(0.5,0.5) + sizeDelta (100,100) z `new GameObject`
            // mogło dawać dramatic clip od lewej w pewnych warunkach.
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.sizeDelta = new Vector2(0f, 54f);

            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 54f;
            le.minHeight = 54f;

            var bg = rowGo.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);

            var btn = rowGo.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f),
                UITheme.RaisedSurface,
                UITheme.WithAlpha(UITheme.PrimaryAccent, 0.24f),
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f),
                UITheme.WithAlpha(UITheme.Border, 0.55f));

            var captured = station;
            btn.onClick.AddListener(() => OnResultClicked(captured));

            // Name: górne 55% wysokości row'a, MiddleLeft alignment (tekst wycentrowany w slot, nie clipuje).
            // Bug fix 2026-05-17: poprzedni split 0.5/0.5 + offset.y -2/-6 dla Name i 6/2 dla Meta
            // dawał 4px overlap w Y i clip text na granicy. Teraz czysty 55/45 split bez offset Y.
            var nameGo = new GameObject("Name", typeof(RectTransform));
            nameGo.transform.SetParent(rowGo.transform, false);
            var nRt = nameGo.GetComponent<RectTransform>();
            nRt.anchorMin = new Vector2(0f, 0.55f);
            nRt.anchorMax = new Vector2(0.78f, 1f);
            nRt.offsetMin = new Vector2(14f, 0f);
            nRt.offsetMax = new Vector2(-8f, 0f);
            var nameTxt = nameGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(nameTxt, UIThemeTextRole.Primary);
            nameTxt.fontSize = 14;
            nameTxt.alignment = TextAlignmentOptions.MidlineLeft;
            nameTxt.richText = true;
            nameTxt.raycastTarget = false;
            nameTxt.textWrappingMode = TextWrappingModes.NoWrap;  // 2026-05-17: bez wrap (długie nazwy nie zawijają)
            nameTxt.overflowMode = TextOverflowModes.Truncate;
            nameTxt.text = $"<b>{station.name}</b>";

            // Meta: dolne 45% wysokości row'a, MiddleLeft alignment. Gap 10% w środku separuje od Name.
            var metaGo = new GameObject("Meta", typeof(RectTransform));
            metaGo.transform.SetParent(rowGo.transform, false);
            var mRt = metaGo.GetComponent<RectTransform>();
            mRt.anchorMin = new Vector2(0f, 0f);
            mRt.anchorMax = new Vector2(0.78f, 0.45f);
            mRt.offsetMin = new Vector2(14f, 0f);
            mRt.offsetMax = new Vector2(-8f, 0f);
            var metaTxt = metaGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(metaTxt, UIThemeTextRole.Secondary);
            metaTxt.fontSize = 11;
            metaTxt.alignment = TextAlignmentOptions.MidlineLeft;
            metaTxt.richText = true;
            metaTxt.raycastTarget = false;
            metaTxt.textWrappingMode = TextWrappingModes.NoWrap;
            metaTxt.overflowMode = TextOverflowModes.Truncate;
            string city = string.IsNullOrEmpty(station.cityName) ? station.name : station.cityName;
            string region = string.IsNullOrEmpty(station.voivodeship) ? "stacja glowna" : station.voivodeship;
            // 2026-05-17: "•" (U+2022) zmienione na " — " bo Legacy Text font nie miał glyph,
            // wynikiem był pusty znak (city  region wyglądało jak overlap).
            metaTxt.text = $"{city}  —  {region}";

            var actionGo = new GameObject("Action", typeof(RectTransform));
            actionGo.transform.SetParent(rowGo.transform, false);
            var aRt = actionGo.GetComponent<RectTransform>();
            aRt.anchorMin = new Vector2(0.78f, 0f);
            aRt.anchorMax = new Vector2(1f, 1f);
            aRt.offsetMin = new Vector2(4f, 8f);
            aRt.offsetMax = new Vector2(-12f, -8f);
            var actionTxt = actionGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(actionTxt, UIThemeTextRole.Accent);
            actionTxt.fontSize = 12;
            actionTxt.alignment = TextAlignmentOptions.MidlineRight;
            actionTxt.richText = true;
            actionTxt.raycastTarget = false;
            actionTxt.textWrappingMode = TextWrappingModes.NoWrap;
            actionTxt.overflowMode = TextOverflowModes.Truncate;
            actionTxt.text = "<b>Wybierz</b>";

            _resultRows.Add(rowGo);
        }

        void BuildNoResultsRow()
        {
            var rowGo = new GameObject("NoResults", typeof(RectTransform));
            rowGo.transform.SetParent(_resultsContent, false);

            // 2026-05-17: explicit anchor full-width (analogicznie do BuildResultRow).
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.sizeDelta = new Vector2(0f, 56f);

            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 56f;
            le.minHeight = 56f;

            var bg = rowGo.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.5f), UIShapePreset.Inset);

            // Text na osobnym GO (Image + Text na jednym GO → NRE, patrz commit 2a3907e)
            var txtObj = new GameObject("Text", typeof(RectTransform));
            txtObj.transform.SetParent(rowGo.transform, false);
            var txtRt = (RectTransform)txtObj.transform;
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(8, 4);
            txtRt.offsetMax = new Vector2(-8, -4);
            var txt = txtObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, UIThemeTextRole.Secondary);
            txt.fontSize = 13;
            txt.alignment = TextAlignmentOptions.Center;
            txt.richText = true;
            txt.fontStyle = FontStyles.Italic;
            txt.raycastTarget = false;
            txt.text = "<b>Brak pasujacych stacji</b>\n<size=11>Sprobuj krotszej nazwy albo wybierz stacje z mapy.</size>";

            _resultRows.Add(rowGo);
        }

        void OnResultClicked(RailwayStation s)
        {
            SelectStation(s);
            if (_searchInput != null) _searchInput.SetTextWithoutNotify(s.name);
        }

        void ClearResultRows()
        {
            foreach (var go in _resultRows)
                if (go != null) Destroy(go);
            _resultRows.Clear();
        }

        void UpdateSelectedInfo()
        {
            if (_selectedInfoText == null || _confirmButton == null) return;

            if (_selected == null)
            {
                _selectedInfoText.text =
                    "<b>Nie wybrano jeszcze stacji</b>\n<size=12>Wyszukaj stacje z listy albo uzyj przycisku wyboru z mapy.</size>";
                _selectedInfoText.color = UITheme.SecondaryText;
                _confirmButton.interactable = false;
                _confirmButtonLabel.text = "Potwierdz wybor";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b><size=18>{_selected.name}</size></b>");
            sb.AppendLine($"<size=12><color={ToHtmlColor(UITheme.PrimaryAccent)}>Stacja bazowa gracza</color></size>");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(_selected.cityName))
                sb.AppendLine($"<b>Miasto:</b> {_selected.cityName}");
            if (!string.IsNullOrEmpty(_selected.voivodeship))
                sb.AppendLine($"<b>Wojewodztwo:</b> {_selected.voivodeship}");
            sb.AppendLine("<b>Status:</b> stacja glowna, dostepna w grafie");

            var init = TimetableInitializer.Instance;
            if (init != null && init.Platforms != null && _selected.pathNodeId >= 0)
            {
                int platformCount = 0;
                foreach (var p in init.Platforms)
                    if (p.stationNodeId == _selected.pathNodeId) platformCount++;
                sb.AppendLine($"<b>Peronow:</b> {platformCount}");
            }

            _selectedInfoText.text = sb.ToString();
            _selectedInfoText.color = UITheme.PrimaryText;
            _confirmButton.interactable = true;
            _confirmButtonLabel.text = $"Potwierdz: {_selected.name}";
        }

        void OnConfirmClicked()
        {
            if (_selected == null) return;

            // HomeDepotStationId musi być w przestrzeni stationNodeId (PathfindingGraph node),
            // bo wszyscy konsumenci (CirculationValidator, DispatchService, RescueService,
            // WorkshopManager, DepotMapHandshakeService) porównują go z route.stations[].stationNodeId.
            // RailwayStation.stationId to sekwencyjny ID kolekcji, NIE node grafu — patrz StationLoader.
            GameState.HomeDepotStationId = _selected.pathNodeId;
            Log.Info($"[DepotLocationPicker] CONFIRMED home depot: '{_selected.name}' (pathNode={_selected.pathNodeId}, stationId={_selected.stationId})");
            Hide();
        }

        static string ToHtmlColor(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(color)}";
        }
    }
}
