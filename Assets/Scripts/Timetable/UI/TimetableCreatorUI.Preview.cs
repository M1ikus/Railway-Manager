using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;
using RailwayManager.Timetable.UI;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Osadzony podgląd trasy na mini-mapie OSM w kreatorze rozkładów (RMP-F).
    /// Dokuje <see cref="RouteMapPreview"/> w prawej części panelu kreatora i odświeża go
    /// live przy każdej (re)generacji trasy. Widget jest reuzywalny — tu wpinamy tylko
    /// bieżącą trasę (linia + markery stacji); obiegi/tabor dodadzą własne providery.
    /// </summary>
    public partial class TimetableCreatorUI
    {
        private const float PreviewDockWidth = 440f;
        private const float PreviewDockHeight = 460f;
        private const float PreviewDockMargin = 14f;
        private const float PreviewDockTopOffset = 90f; // pod paskiem MaintenanceAlertsUI (Y=-44..-80)

        private RouteMapPreview _routePreview;
        private GameObject _previewPlaceholder;
        private bool _previewDockBuilt;
        private bool _miniPickActive; // czy aktualnie wybieramy stację klikiem na mini-mapie

        private void EnsureRoutePreview()
        {
            if (_previewDockBuilt || _panel == null) return;

            // Zarezerwuj miejsce po prawej w VLG kreatora (treść reflowuje się węziej).
            var vlg = _panel.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                var p = vlg.padding;
                vlg.padding = new RectOffset(p.left, Mathf.RoundToInt(PreviewDockWidth + PreviewDockMargin * 2f), p.top, p.bottom);
            }

            // Kontener dokowany w prawym-górnym rogu panelu (poza flow VLG).
            var container = new GameObject("RoutePreviewDock", typeof(RectTransform));
            container.transform.SetParent(_panel.transform, false);
            var crt = (RectTransform)container.transform;
            crt.anchorMin = new Vector2(1f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-PreviewDockMargin, -PreviewDockTopOffset);
            crt.sizeDelta = new Vector2(PreviewDockWidth, PreviewDockHeight);
            container.AddComponent<LayoutElement>().ignoreLayout = true;

            var bg = container.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.Panel);
            var outline = container.AddComponent<Outline>();
            outline.effectColor = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.55f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var cvlg = container.AddComponent<VerticalLayoutGroup>();
            cvlg.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Sm);
            cvlg.spacing = UITheme.Spacing.Xs;
            cvlg.childForceExpandWidth = true;
            cvlg.childForceExpandHeight = false;

            var label = Lbl(container.transform, "PODGLAD TRASY (OSM)", 11, UITheme.PrimaryAccent);
            if (label != null)
            {
                var le = label.gameObject.GetComponent<LayoutElement>() ?? label.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 16;
                le.flexibleHeight = 0;
            }

            var hostGo = new GameObject("PreviewHost", typeof(RectTransform));
            hostGo.transform.SetParent(container.transform, false);
            var hle = hostGo.AddComponent<LayoutElement>();
            hle.flexibleHeight = 1;
            hle.flexibleWidth = 1;

            _routePreview = RouteMapPreview.Create((RectTransform)hostGo.transform, "creator-route");
            _routePreview.OnMapClicked += HandleMiniMapClicked;
            _routePreview.OnViewChanged += HandleMiniViewChanged;

            // Placeholder gdy brak trasy (nad RawImage, nie blokuje pan/zoom).
            _previewPlaceholder = new GameObject("PreviewPlaceholder", typeof(RectTransform));
            _previewPlaceholder.transform.SetParent(hostGo.transform, false);
            var phrt = (RectTransform)_previewPlaceholder.transform;
            phrt.anchorMin = Vector2.zero; phrt.anchorMax = Vector2.one;
            phrt.offsetMin = new Vector2(8f, 8f); phrt.offsetMax = new Vector2(-8f, -8f);
            var phtmp = _previewPlaceholder.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(phtmp, UIThemeTextRole.Secondary);
            phtmp.text = "Kliknij „Na mapie” przy A/B, potem wskaż stację na podglądzie";
            phtmp.fontSize = 12;
            phtmp.alignment = TextAlignmentOptions.Center;
            phtmp.textWrappingMode = TextWrappingModes.Normal;
            phtmp.raycastTarget = false;

            _previewDockBuilt = true;
        }

        /// <summary>Live-refresh: wpina bieżącą trasę (linia + markery stacji) i dopasowuje widok.</summary>
        private void RefreshRoutePreview()
        {
            EnsureRoutePreview();
            if (_routePreview == null) return;
            if (_miniPickActive) return; // w trybie wyboru kropki/widok zarządzane osobno

            if (_currentRoute == null || _currentRoute.nodeIds == null || _currentRoute.nodeIds.Count < 2)
            {
                if (_previewPlaceholder != null) _previewPlaceholder.SetActive(true);
                _routePreview.ClearOverlays();
                _routePreview.SyncToMainMapView(); // pokaż bieżący widok dużej mapy (gotowy do wyboru)
                return;
            }

            var init = TimetableInitializer.Instance;
            if (init == null || init.Graph == null)
            {
                if (_previewPlaceholder != null) _previewPlaceholder.SetActive(true);
                _routePreview.ClearOverlays();
                _routePreview.SyncToMainMapView(); // pokaż bieżący widok dużej mapy (gotowy do wyboru)
                return;
            }

            var poly = init.Graph.BuildRoutePolyline(_currentRoute.nodeIds, out _);
            var lines = new List<RouteMapPreview.PreviewPolyline>
            {
                new RouteMapPreview.PreviewPolyline
                {
                    points = poly,
                    color = new Color(0.2f, 0.9f, 1f, 0.95f), // cyan jak overlay dużej mapy
                    widthM = 0f                                // adaptacyjna do zoomu podglądu
                }
            };
            _routePreview.SetPolylines(lines);

            var markers = new List<RouteMapPreview.PreviewMarker>();
            var stations = _currentRoute.stations;
            if (stations != null)
            {
                for (int i = 0; i < stations.Count; i++)
                {
                    Color c = i == 0
                        ? new Color(0.30f, 0.90f, 0.40f)               // start = zielony
                        : (i == stations.Count - 1
                            ? new Color(0.95f, 0.40f, 0.35f)           // koniec = czerwony
                            : new Color(0.95f, 0.85f, 0.30f));         // pośrednie = żółte
                    markers.Add(new RouteMapPreview.PreviewMarker
                    {
                        worldPos = stations[i].position,
                        color = c,
                        sizeM = 0f // adaptacyjny rozmiar
                    });
                }
            }
            _routePreview.SetMarkers(markers);

            if (_previewPlaceholder != null) _previewPlaceholder.SetActive(false);
            _routePreview.FitToContent();
        }

        // ── Wybór stacji klikiem na mini-mapie (zastępuje pełnoekranowy picking) ──

        /// <summary>
        /// Uzbraja tryb wyboru na mini-mapie. Cel zakodowany w istniejących polach:
        /// _pickingWaypointIndex == -1 → endpoint wg _pickingStart; -2 → nowy waypoint; ≥0 → waypoint[idx].
        /// </summary>
        private void ArmMiniPick()
        {
            EnsureRoutePreview();
            if (_routePreview == null) return;
            _miniPickActive = true;
            if (_previewPlaceholder != null) _previewPlaceholder.SetActive(false);
            ShowPickStationDots();
        }

        private void ShowPickStationDots()
        {
            if (_routePreview == null || !_routePreview.TryGetViewBounds(out var b)) return;
            var init = TimetableInitializer.Instance;
            if (init?.Stations == null) return;

            var dots = new List<RouteMapPreview.PreviewMarker>();
            foreach (var st in init.Stations)
            {
                if (st == null || st.pathNodeId < 0) continue;
                var p = st.position;
                if (p.x < b.MinX || p.x > b.MaxX || p.y < b.MinY || p.y > b.MaxY) continue;
                dots.Add(new RouteMapPreview.PreviewMarker
                {
                    worldPos = p,
                    color = new Color(0.95f, 0.85f, 0.30f),
                    sizeM = 0f
                });
                if (dots.Count >= 300) break; // cap przy mocnym oddaleniu
            }
            _routePreview.SetPolylines(System.Array.Empty<RouteMapPreview.PreviewPolyline>());
            _routePreview.SetMarkers(dots);
            _routePreview.Redraw();
        }

        private void HandleMiniViewChanged()
        {
            if (_miniPickActive) ShowPickStationDots();
        }

        private void HandleMiniMapClicked(Vector2 world)
        {
            if (!_miniPickActive) return;
            var st = FindNearestStationForPick(world);
            if (st == null) return; // klik w pustkę — zostań w trybie

            _miniPickActive = false;
            if (_pickingWaypointIndex == -1)
            {
                SelectSuggestion(st, _pickingStart);
            }
            else if (_pickingWaypointIndex == -2)
            {
                _waypoints.Add(st);
                _waypointTracks.Add("");
                RefreshWaypointsUI();
                Refresh();
            }
            else
            {
                SetWaypoint(_pickingWaypointIndex, st);
            }
            _pickingWaypointIndex = -1;

            // Zdejmij kropki, zostaw widok; linia trasy pojawi się dopiero po „Wygeneruj trasę".
            _routePreview.SetPolylines(System.Array.Empty<RouteMapPreview.PreviewPolyline>());
            _routePreview.SetMarkers(System.Array.Empty<RouteMapPreview.PreviewMarker>());
            _routePreview.Redraw();
        }

        private RailwayStation FindNearestStationForPick(Vector2 world)
        {
            var init = TimetableInitializer.Instance;
            if (init?.Stations == null) return null;
            RailwayStation best = null;
            float bestSq = float.MaxValue;
            foreach (var st in init.Stations)
            {
                if (st == null || st.pathNodeId < 0) continue;
                float d = (st.position - world).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = st; }
            }
            return best;
        }
    }
}
