using System.Collections.Generic;
using UnityEngine;
using TMPro;
using formap; // TileData
using MapSystem;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Ukrywa POI labels (Places + Stations) renderowane przez MapRenderer dla pozycji POZA Polską (M-PL-4 ulepszone).
    ///
    /// Aktualnie MapRenderer.RenderPOILayer renderuje WSZYSTKIE POI z PBF (w tym Frankfurt nad Odrą,
    /// Berlin, Drezno, Praga itd.) — zaśmieca mapę "obcymi" miastami które nie są częścią rozgrywki.
    ///
    /// Strategia: subscribe do <see cref="TileManager.OnTileLoaded"/>, po każdym tile load znajdź dzieci
    /// "Place_*" i "Station_*" w Tile.RootObject hierarchii i wyłącz te które są poza Polską
    /// (via <see cref="CountryOverlayService.IsInsidePoland"/>).
    ///
    /// Plus initial scan przy <see cref="CountryOverlayService.OnInitialized"/> — hide labels już
    /// załadowanych tile'i (gdy service przychodzi PO tile rendering).
    ///
    /// Wpinka: dodaj komponent do dowolnego GameObject'a w MapScene — autowire TileManager.
    /// </summary>
    public class OutsidePolandPOIHider : MonoBehaviour
    {
        [Header("References (autowire jeśli null)")]
        public TileManager tileManager;
        [Tooltip("MapRenderer Transform — children to Tile_X_Y GO z faktyczną hierarchią POI. " +
                 "Autowire przez FindAnyObjectByType<MapRenderer>().")]
        public Transform mapRendererRoot;

        [Header("Settings")]
        [Tooltip("Czy ukrywać Place labels (city/town/village) poza PL.")]
        public bool hidePlacesOutsidePoland = true;

        [Tooltip("Strategia dla stacji railway=station poza PL (większe stacje):\n" +
                 "• Hide — ukryj całkowicie (icon + label)\n" +
                 "• Italic — label w italic + szary, ukryj icon, niekliklne (subtle context)\n" +
                 "• Highlight — label większy + czerwony + bold, icon zachowany, klikalne (DLC marker)\n" +
                 "• Show — zostaw bez zmian (debug)")]
        public ForeignStationStyle foreignStationStrategy = ForeignStationStyle.Highlight;

        [Tooltip("Halt'y (railway=halt) poza PL — czy ukrywać. Halt'y to małe przystanki, dla " +
                 "kontekstu mapy nieprzydatne, ukrywamy domyślnie.")]
        public bool hideHaltsOutsidePoland = true;

        [Tooltip("Kolor labels dla stacji zagranicznych. Italic: szary kontekst. Highlight: czerwony DLC marker.")]
        public Color foreignStationColor = new Color(0.85f, 0.15f, 0.15f, 1f); // czerwony DLC

        [Tooltip("Mnożnik rozmiaru fontu I ikony dla zagranicznych stacji w trybie Highlight. " +
                 "1.0 = bez zmian (label scale 3, icon 30m). 5.0 = znacznie większe (label scale 15, " +
                 "icon 150m), czytelne z dużego oddalenia.")]
        [Range(0.5f, 20f)]
        public float foreignStationFontSizeMultiplier = 5.0f;

        [Tooltip("Włącz/wyłącz hider runtime. False = pokazuj wszystkie POI (debug).")]
        public bool hidingEnabled = true;

        public enum ForeignStationStyle
        {
            Hide,       // ukryj całe (icon + label)
            Italic,     // tylko label, w italic + szary, niekliklne
            Highlight,  // label większy + czerwony + bold, icon zachowany, klikalne (DLC marker)
            Show        // zostaw bez zmian (debug)
        }

        private bool isSubscribed;
        private int totalHiddenPlaces;
        private int totalHiddenStations;
        private int totalHiddenHalts;
        private int totalItalicizedStations;

        void Start()
        {
            if (tileManager == null) tileManager = FindAnyObjectByType<TileManager>();
            if (mapRendererRoot == null)
            {
                var mr = FindAnyObjectByType<MapRenderer>();
                if (mr != null) mapRendererRoot = mr.transform;
            }

            if (tileManager == null || mapRendererRoot == null)
            {
                Log.Warn($"[OutsidePolandPOIHider] Disabled — TileManager={tileManager}, MapRendererRoot={mapRendererRoot}");
                return;
            }

            tileManager.OnTileLoaded += HandleTileLoaded;
            CountryOverlayService.OnInitialized += HandleServiceInitialized;
            isSubscribed = true;

            Log.Info($"[OutsidePolandPOIHider] Started — listening on TileManager.OnTileLoaded, "
                     + $"mapRendererRoot has {mapRendererRoot.childCount} children");

            // Jeśli service już ready — initial scan już-loaded tile'i
            if (CountryOverlayService.IsInitialized)
                HandleServiceInitialized();
        }

        void OnDestroy()
        {
            if (isSubscribed && tileManager != null)
            {
                tileManager.OnTileLoaded -= HandleTileLoaded;
                CountryOverlayService.OnInitialized -= HandleServiceInitialized;
            }
        }

        private void HandleTileLoaded(long tileID, TileData tileData)
        {
            // No-op: per-frame scan w Update() jest single source of truth.
            // OnTileLoaded subscription zostaje dla edge case (initial load timing).
            // Aktualnie scan w LateUpdate i tak złapie nowe tile'e.
        }

        private int diagnosticFrameCount;
        private int diagnosticTilesSeen;
        private int diagnosticPOIsScanned;
        private int diagnosticPOIsOutsidePL;

        void LateUpdate()
        {
            if (!hidingEnabled || !CountryOverlayService.IsInitialized || mapRendererRoot == null) return;

            int newPlaces = 0, newHalts = 0, newStations = 0, newStyled = 0;
            int tilesSeen = 0;
            int poisScanned = 0;
            int poisOutsidePL = 0;
            int tilesWithoutLayerPOIs = 0;
            int tilesWithoutPlacesContainer = 0;
            int totalPlaceChildren = 0;
            int totalStationChildren = 0;

            foreach (Transform tile in mapRendererRoot)
            {
                if (tile == null || !tile.name.StartsWith("Tile_")) continue;
                tilesSeen++;

                // MapRenderer ma DWIE warstwy point-POI:
                // - Layer_POIs (LayerType.POIs=8) → railway=station/halt/signal w Stations container
                // - Layer_Places (LayerType.Places=11) → place=city/town/village w Places container
                // Oba layer'y mają wewnątrz "Places" + "Stations" + "Signals_Hidden" containery (z RenderPOILayer),
                // ale Place_* są tylko w Layer_Places/Places, Station_* są tylko w Layer_POIs/Stations.
                var layerPOIs = tile.Find("Layer_POIs");
                var layerPlaces = tile.Find("Layer_Places");

                if (layerPOIs == null && layerPlaces == null)
                {
                    tilesWithoutLayerPOIs++;
                    continue;
                }

                if (hidePlacesOutsidePoland && layerPlaces != null)
                {
                    var placesContainer = layerPlaces.Find("Places");
                    if (placesContainer == null)
                    {
                        tilesWithoutPlacesContainer++;
                    }
                    else
                    {
                        totalPlaceChildren += placesContainer.childCount;
                        ProcessPlaces(placesContainer, ref newPlaces, ref poisScanned, ref poisOutsidePL);
                    }
                }

                if (layerPOIs != null)
                {
                    var stationsContainer = layerPOIs.Find("Stations");
                    if (stationsContainer != null)
                    {
                        totalStationChildren += stationsContainer.childCount;
                        ProcessStations(stationsContainer, ref newHalts, ref newStations, ref newStyled, ref poisScanned, ref poisOutsidePL);
                    }
                }
            }

            // Diagnostyka — pierwszy + co 60 frame log o scan
            diagnosticFrameCount++;
            diagnosticTilesSeen = tilesSeen;
            diagnosticPOIsScanned += poisScanned;
            diagnosticPOIsOutsidePL += poisOutsidePL;
            // Verbose diagnostic logs removed — summary only shown when scan is meaningful change

            if (newPlaces + newHalts + newStations + newStyled > 0)
            {
                totalHiddenPlaces += newPlaces;
                totalHiddenHalts += newHalts;
                totalHiddenStations += newStations;
                totalItalicizedStations += newStyled;
            }
        }

        private int verboseLogCount;

        private void ProcessPlaces(Transform container, ref int counter, ref int scanned, ref int outsidePL)
        {
            foreach (Transform poi in container)
            {
                if (poi == null) continue;
                if (poi.GetComponent<OutsidePolandStyledMarker>() != null) continue;
                scanned++;

                var pos2D = new Vector2(poi.position.x, poi.position.z);
                bool insidePL = CountryOverlayService.IsInsidePoland(pos2D);

                if (insidePL)
                {
                    poi.gameObject.AddComponent<OutsidePolandStyledMarker>();
                    continue;
                }
                outsidePL++;

                poi.gameObject.SetActive(false);
                poi.gameObject.AddComponent<OutsidePolandStyledMarker>();
                counter++;
            }
        }

        private void ProcessStations(Transform container, ref int newHalts, ref int newStations, ref int newStyled, ref int scanned, ref int outsidePL)
        {
            foreach (Transform poi in container)
            {
                if (poi == null) continue;
                if (poi.GetComponent<OutsidePolandStyledMarker>() != null) continue;
                scanned++;

                var pos2D = new Vector2(poi.position.x, poi.position.z);
                if (CountryOverlayService.IsInsidePoland(pos2D))
                {
                    poi.gameObject.AddComponent<OutsidePolandStyledMarker>();
                    continue;
                }
                outsidePL++;

                // Outside PL — rozróżnij station vs halt
                var marker = poi.GetComponent<StationMarker>();
                bool isHalt = marker != null && marker.stationType == "halt";
                if (isHalt)
                {
                    if (hideHaltsOutsidePoland)
                    {
                        poi.gameObject.SetActive(false);
                        newHalts++;
                    }
                }
                else
                {
                    switch (foreignStationStrategy)
                    {
                        case ForeignStationStyle.Hide:
                            poi.gameObject.SetActive(false);
                            newStations++;
                            break;
                        case ForeignStationStyle.Italic:
                            ApplyItalicStyle(poi);
                            newStyled++;
                            break;
                        case ForeignStationStyle.Highlight:
                            ApplyHighlightStyle(poi);
                            newStyled++;
                            break;
                        case ForeignStationStyle.Show:
                            break;
                    }
                }

                poi.gameObject.AddComponent<OutsidePolandStyledMarker>();
            }
        }

        private void HandleServiceInitialized()
        {
            // Per-frame scan w LateUpdate i tak złapie wszystko. Tu tylko log info.
            Log.Info($"[OutsidePolandPOIHider] Service initialized. Per-frame LateUpdate scan active. "
                     + $"Strategy: places=hide:{hidePlacesOutsidePoland}, halts=hide:{hideHaltsOutsidePoland}, "
                     + $"stations={foreignStationStrategy}.");
        }

        /// <summary>
        /// Italic mode: ukryj icon, label w italic + szary, niekliklne. Subtle context.
        /// </summary>
        private void ApplyItalicStyle(Transform stationRoot)
        {
            var iconChild = stationRoot.Find("Icon");
            if (iconChild != null) iconChild.gameObject.SetActive(false);

            var labelChild = stationRoot.Find("Label");
            if (labelChild != null)
            {
                var tmp = labelChild.GetComponent<TextMeshPro>();
                if (tmp != null)
                {
                    tmp.fontStyle |= FontStyles.Italic;
                    tmp.color = foreignStationColor;
                }
                else
                {
                    var legacy = labelChild.GetComponent<TextMesh>();
                    if (legacy != null)
                    {
                        legacy.fontStyle = FontStyle.Italic;
                        legacy.color = foreignStationColor;
                    }
                }
            }

            var col = stationRoot.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        /// <summary>
        /// Highlight mode: label większy + czerwony + bold, icon zachowany przemalowany i powiększony,
        /// klikalne. DLC-style marker — widoczny z daleka, sygnalizuje że stacja istnieje
        /// ale jest poza obecną grą (przyszły DLC kraj).
        /// </summary>
        private void ApplyHighlightStyle(Transform stationRoot)
        {
            float origIconScale = 1f, origLabelScale = 1f, newIconScale = 1f, newLabelScale = 1f;

            // Icon: zachowany ale przemalowany na czerwony + powiększony
            var iconChild = stationRoot.Find("Icon");
            if (iconChild != null)
            {
                origIconScale = iconChild.localScale.x;
                var sr = iconChild.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = foreignStationColor;
                else
                {
                    var mr = iconChild.GetComponent<MeshRenderer>();
                    if (mr != null && mr.material != null) mr.material.color = foreignStationColor;
                }
                iconChild.localScale *= foreignStationFontSizeMultiplier;
                newIconScale = iconChild.localScale.x;
            }

            // Label: większy + czerwony + bold + odsunięty od powiększonej ikony
            var labelChild = stationRoot.Find("Label");
            if (labelChild != null)
            {
                origLabelScale = labelChild.localScale.x;
                var tmp = labelChild.GetComponent<TextMeshPro>();
                if (tmp != null)
                {
                    tmp.fontStyle |= FontStyles.Bold;
                    tmp.color = foreignStationColor;
                    labelChild.localScale *= foreignStationFontSizeMultiplier;
                }
                else
                {
                    var legacy = labelChild.GetComponent<TextMesh>();
                    if (legacy != null)
                    {
                        legacy.fontStyle = FontStyle.Bold;
                        legacy.color = foreignStationColor;
                        labelChild.localScale *= foreignStationFontSizeMultiplier;
                    }
                }
                newLabelScale = labelChild.localScale.x;

                // Przesuń label dalej od ikony — original offset Z=stationIconSize*1.5=45m,
                // po icon scale ×N nasza ikona ma promień 75×N. Label musi być za krawędzią.
                // Mnożymy z offset przez multiplier żeby zachować proporcję względem icon size.
                var pos = labelChild.localPosition;
                labelChild.localPosition = new Vector3(pos.x, pos.y, pos.z * foreignStationFontSizeMultiplier);
            }

            // Powiększ też collider żeby pasował do większego icon
            var col = stationRoot.GetComponent<BoxCollider>();
            if (col != null) col.size *= foreignStationFontSizeMultiplier;
        }

        [ContextMenu("DEBUG: Rescan all tiles")]
        public void DebugRescan()
        {
            // Usuń wszystkie OutsidePolandStyledMarker → następny LateUpdate przetworzy ponownie
            if (mapRendererRoot == null) return;
            int markersRemoved = 0;
            var allMarkers = mapRendererRoot.GetComponentsInChildren<OutsidePolandStyledMarker>(includeInactive: true);
            foreach (var m in allMarkers)
            {
                if (m != null) { Destroy(m); markersRemoved++; }
            }
            totalHiddenPlaces = totalHiddenStations = totalHiddenHalts = totalItalicizedStations = 0;
            Log.Info($"[OutsidePolandPOIHider] DebugRescan: removed {markersRemoved} markers — next LateUpdate re-processes all POI.");
        }

        [ContextMenu("DEBUG: Show all (disable hiding)")]
        public void DebugShowAll()
        {
            hidingEnabled = false;
            if (mapRendererRoot == null) return;
            int restored = 0;
            foreach (Transform tile in mapRendererRoot)
            {
                if (tile == null || !tile.name.StartsWith("Tile_")) continue;
                var allChildren = tile.GetComponentsInChildren<Transform>(includeInactive: true);
                foreach (var t in allChildren)
                {
                    if (t == null) continue;
                    if (t.name.StartsWith("Place_") || t.name.StartsWith("Station_"))
                    {
                        if (!t.gameObject.activeSelf)
                        {
                            t.gameObject.SetActive(true);
                            restored++;
                        }
                    }
                }
            }
            Log.Info($"[OutsidePolandPOIHider] DEBUG: restored {restored} POIs to visible.");
        }
    }
}
