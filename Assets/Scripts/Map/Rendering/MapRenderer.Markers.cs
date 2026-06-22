using System.Collections.Generic;
using UnityEngine;
using TMPro;
using formap;
using RailwayManager.SharedUI;
using RailwayManager.Core.Rendering;

namespace MapSystem
{
    public partial class MapRenderer
    {
        // ═══════════════════════════════════════════
        //  POI / MARKERS — places, stations, signals
        // ═══════════════════════════════════════════

        /// <summary>
        /// Renders POI layer with different visualization based on POI type:
        /// - Places (city/town/village): Text labels
        /// - Stations (railway=station/halt): Icon + text (clickable)
        /// - Signals (railway=signal): Hidden (data only for logic)
        /// </summary>
        private (int meshCount, int vertexCount, int triangleCount) RenderPOILayer(
            GameObject layerObj, Material material, List<MeshGeometry> features, float height)
        {
            int placeCount = 0;
            int stationCount = 0;
            int hiddenCount = 0;

            // Create sub-containers for organization
            var placesContainer = new GameObject("Places");
            placesContainer.transform.SetParent(layerObj.transform);
            placesContainer.transform.localPosition = Vector3.zero;

            var stationsContainer = new GameObject("Stations");
            stationsContainer.transform.SetParent(layerObj.transform);
            stationsContainer.transform.localPosition = Vector3.zero;

            var signalsContainer = new GameObject("Signals_Hidden");
            signalsContainer.transform.SetParent(layerObj.transform);
            signalsContainer.transform.localPosition = Vector3.zero;
            signalsContainer.SetActive(false); // Hidden but data preserved

            // Debug: log first few POIs to see what metadata they have
            // int debugCount = 0; // was used for POI debug logging
            foreach (var feature in features)
            {
                if (feature.Vertices.Count == 0)
                    continue;

                var vertex = feature.Vertices[0];
                var position = new Vector3(vertex.x, height, vertex.y);

                // Get POI type from metadata
                feature.Metadata.TryGetValue("place", out var placeType);
                feature.Metadata.TryGetValue("railway", out var railwayType);
                feature.Metadata.TryGetValue("name", out var name);

                // Debug POIs — wyłączone

                if (!string.IsNullOrEmpty(placeType))
                {
                    // Place (city, town, village) - render as text
                    CreatePlaceLabel(placesContainer, position, name ?? "???", placeType);
                    placeCount++;
                }
                else if (railwayType == "station" || railwayType == "halt")
                {
                    // Skip tram/subway/light_rail/narrow_gauge/monorail stations — niegrywalne.
                    bool isTransit = false;
                    if (feature.Metadata.TryGetValue("station", out var stKind))
                        isTransit = stKind == "subway" || stKind == "tram" || stKind == "light_rail"
                                  || stKind == "monorail" || stKind == "narrow_gauge";
                    if (!isTransit && feature.Metadata.TryGetValue("tram", out var tt) && tt == "yes") isTransit = true;
                    if (!isTransit && feature.Metadata.TryGetValue("subway", out var st) && st == "yes") isTransit = true;
                    if (!isTransit && feature.Metadata.TryGetValue("light_rail", out var lr) && lr == "yes") isTransit = true;
                    if (!isTransit && feature.Metadata.TryGetValue("narrow_gauge", out var ng) && ng == "yes") isTransit = true;
                    if (isTransit) { hiddenCount++; continue; }

                    // Station - render as icon + text
                    CreateStationMarker(stationsContainer, position, name ?? "Stacja", railwayType);
                    stationCount++;
                }
                else if (railwayType == "signal")
                {
                    // Signal - hidden, just store reference for later logic
                    CreateHiddenSignal(signalsContainer, position, feature.Metadata);
                    hiddenCount++;
                }
            }

            // Log per-tile POI summary wyłączony (spamuje przy wielu tile'ach)

            int totalVisible = placeCount + stationCount;
            return (totalVisible, totalVisible, 0);
        }

        /// <summary>
        /// Creates a text label for a place (city, town, village)
        /// </summary>
        private void CreatePlaceLabel(GameObject parent, Vector3 position, string name, string placeType)
        {
            var labelObj = new GameObject($"Place_{name}");
            labelObj.transform.SetParent(parent.transform);
            labelObj.transform.position = position;

            // Determine scale based on place type
            float scale = placeType switch
            {
                "city" => cityFontSize,
                "town" => townFontSize,
                "village" => villageFontSize,
                _ => villageFontSize
            };

            // TMP always — legacy TextMesh fallback usunięty (deprecated od Unity 2017, gorszy renderpath,
            // ~50k labels na pełnej Polsce). UITheme.TmpFont ma 3-poziomowy fallback chain (runtime SDF
            // z polskimi znakami → TMP_Settings.defaultFontAsset → LiberationSans SDF builtin), nigdy null.
            var textMesh = labelObj.AddComponent<TextMeshPro>();
            textMesh.text = name;
            textMesh.alignment = TextAlignmentOptions.Center;
            textMesh.color = placeNameColor;
            textMesh.font = placeNameFont != null ? placeNameFont : UITheme.TmpFont;
            textMesh.fontSize = 56; // Base size — TMP SDF ma inny scale niż TextMesh, 56 daje wizualny parytet
            // No-wrap — bez tego TMP łamie długie nazwy (np. "Szczeci\nnek") gdy mieszczą się w default RectTransform.
            textMesh.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            textMesh.overflowMode = TextOverflowModes.Overflow;
            // Shared outline material — czytelność na czarnym bg mapy (TMP SDF blendowałby się bez outline).
            var mapLabelMat = GetMapLabelMaterial(textMesh.font);
            if (mapLabelMat != null) textMesh.fontSharedMaterial = mapLabelMat;

            // Make text face up (readable from top-down camera)
            labelObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            labelObj.transform.localScale = Vector3.one * scale;

            // Register for zoom-based scaling + per-type LOD visibility + force apply current zoom multiplier.
            // Bez force-apply nowe labels post-pierwszy UpdatePlaceNameScales zostaną z initial scale
            // (cache _lastPlaceNameScaleMult skipuje pełną pętlę przy unchanged zoom).
            placeLabels.Add(new LabelEntry { t = labelObj.transform, baseScale = scale, labelType = placeType });
            ApplyCurrentZoomScale(labelObj.transform, scale);
            // Apply current LOD visibility natychmiast (cities zawsze, villages tylko close zoom etc.).
            labelObj.SetActive(IsLabelVisibleAtLOD(placeType, lastLODLevel >= 0 ? lastLODLevel : 0));
        }

        /// <summary>
        /// Creates a station marker with icon and text label
        /// </summary>
        private void CreateStationMarker(GameObject parent, Vector3 position, string name, string stationType)
        {
            var stationObj = new GameObject($"Station_{name}");
            stationObj.transform.SetParent(parent.transform);
            stationObj.transform.position = position;

            // Create icon (quad with sprite or colored cube as fallback)
            GameObject iconObj;
            if (stationIconSprite != null)
            {
                iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(stationObj.transform);
                iconObj.transform.localPosition = Vector3.zero;

                var spriteRenderer = iconObj.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = stationIconSprite;
                spriteRenderer.color = stationNameColor;

                // Scale and rotate to face up
                float scale = stationIconSize / 100f;
                iconObj.transform.localScale = new Vector3(scale, scale, scale);
                iconObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                // Fallback: cube z cached shared mesh + materiałem. Wcześniej każda stacja
                // tworzyła new Material + Shader.Find lookup + CreatePrimitive z natychmiast
                // destroy'owanym Colliderem — ~3000× per session waste. Manualne MeshFilter/Renderer
                // pozwala dzielić zasoby + pomija create-then-destroy Collider.
                iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(stationObj.transform);
                iconObj.transform.localPosition = Vector3.zero;
                iconObj.transform.localScale = Vector3.one * stationIconSize;

                var mf = iconObj.AddComponent<MeshFilter>();
                mf.sharedMesh = GetFallbackCubeMesh();
                var mr = iconObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = GetFallbackStationMaterial();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            // Add collider for clicking
            var collider = stationObj.AddComponent<BoxCollider>();
            collider.size = Vector3.one * stationIconSize * 1.5f;
            collider.center = Vector3.zero;

            // Add station data component for click handling
            var stationData = stationObj.AddComponent<StationMarker>();
            stationData.stationName = name;
            stationData.stationType = stationType;

            // Create text label offset from icon
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(stationObj.transform);
            labelObj.transform.localPosition = new Vector3(0, 0, stationIconSize * 1.5f); // Offset in Z

            // TMP always — patrz komentarz w CreatePlaceLabel.
            var tmpText = labelObj.AddComponent<TextMeshPro>();
            tmpText.text = name;
            tmpText.alignment = TextAlignmentOptions.Center;
            tmpText.fontSize = 56; // parytet z CreatePlaceLabel
            tmpText.color = stationNameColor;
            tmpText.font = placeNameFont != null ? placeNameFont : UITheme.TmpFont;
            tmpText.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tmpText.overflowMode = TextOverflowModes.Overflow;
            // Shared outline material — patrz GetMapLabelMaterial.
            var mapLabelMat2 = GetMapLabelMaterial(tmpText.font);
            if (mapLabelMat2 != null) tmpText.fontSharedMaterial = mapLabelMat2;
            labelObj.transform.localScale = Vector3.one * stationFontSize;

            // Rotate text to face up
            labelObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // Register dla zoom-based scaling + per-type LOD visibility. labelType = stationType:
            // "station" (main, visible LOD 0-2) lub "halt" (przystanek, visible LOD 0-1).
            placeLabels.Add(new LabelEntry { t = labelObj.transform, baseScale = stationFontSize, labelType = stationType });
            ApplyCurrentZoomScale(labelObj.transform, stationFontSize);
            labelObj.SetActive(IsLabelVisibleAtLOD(stationType, lastLODLevel >= 0 ? lastLODLevel : 0));
        }

        /// <summary>
        /// Shared TMP material dla place + station labels — clone z font.material z dodanym
        /// białym outline. Bez outline ciemnoszary tekst (placeNameColor) jest niewidoczny
        /// na czarnym bg mapy (TMP SDF alpha blending fade-out edges). Jedna instancja
        /// shared dla wszystkich ~50k labels — bez per-text material instance.
        /// </summary>
        private Material GetMapLabelMaterial(TMP_FontAsset font)
        {
            if (_cachedMapLabelMaterial != null) return _cachedMapLabelMaterial;
            if (font == null || font.material == null) return null;

            _cachedMapLabelMaterial = new Material(font.material);
            // TMP SDF shader property IDs (z TMPro.ShaderUtilities)
            if (_cachedMapLabelMaterial.HasProperty(ShaderUtilities.ID_OutlineWidth))
                _cachedMapLabelMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
            if (_cachedMapLabelMaterial.HasProperty(ShaderUtilities.ID_OutlineColor))
                _cachedMapLabelMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.white);
            return _cachedMapLabelMaterial;
        }

        /// <summary>
        /// Cube mesh dla fallback station icon — Unity builtin "Cube.fbx" cached lazy.
        /// Pozwala wszystkim stacjom dzielić jedną referencję do mesh zamiast tworzyć
        /// CreatePrimitive per stacja.
        /// </summary>
        private Mesh GetFallbackCubeMesh()
        {
            if (_cachedFallbackCubeMesh == null)
                _cachedFallbackCubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            return _cachedFallbackCubeMesh;
        }

        /// <summary>
        /// Shared material dla fallback station icon — kolor stacji + Unlit shader cached lazy.
        /// stationNameColor jest [SerializeField] (designer-time) — wszystkie stacje używają
        /// tego samego koloru, więc shared material jest poprawny.
        /// </summary>
        private Material GetFallbackStationMaterial()
        {
            if (_cachedFallbackStationMaterial == null)
            {
                _cachedFallbackStationMaterial = MaterialFactory.CreateUnlit();
                MaterialFactory.SetBaseColor(_cachedFallbackStationMaterial, stationNameColor);
            }
            return _cachedFallbackStationMaterial;
        }

        /// <summary>
        /// Creates a hidden signal point (for logic, not visible)
        /// </summary>
        private void CreateHiddenSignal(GameObject parent, Vector3 position, Dictionary<string, string> metadata)
        {
            var signalObj = new GameObject("Signal");
            signalObj.transform.SetParent(parent.transform);
            signalObj.transform.position = position;

            // Share reference do feature.Metadata — SignalMarker używa go tylko read-only
            // (TryGetValue w SignalType/SignalName/SignalRef). Defensive copy był waste —
            // ~5000 signals na pełnej Polsce × deep-copy każdego OSM tag dict = wasted GC.
            // POIs są logic layer (NIE czyszczone przez ClearMeshGeometryFully), reference żyje.
            var signalData = signalObj.AddComponent<SignalMarker>();
            signalData.metadata = metadata;
        }
    }
}
