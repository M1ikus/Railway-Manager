using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using MapSystem;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable;

namespace RailwayManager.Timetable.Simulation
{
    public partial class DepotLocationPickerUI
    {
        // ═══════════════════════════════════════════
        //  MAP PICKING FLOW — switch to Map → click station → switch back
        // ═══════════════════════════════════════════

        void OnPickFromMapClicked()
        {
            // Hide overlay, switch do Map, wyłącz Map UI, pokaż mini banner
            _root.SetActive(false);
            _isMapPicking = true;
            SceneController.FullscreenOverlayOpen = false; // map camera musi działać (pan/zoom)
            StartCoroutine(SwitchToMapWhenReady());
            Log.Info($"[DepotLocationPicker] ★ OnPickFromMapClicked — _isMapPicking SET to TRUE, " +
                     $"activeScene={SceneController.ActiveScene}");
        }

        IEnumerator SwitchToMapWhenReady()
        {
            float timeout = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < timeout)
            {
                var mapScene = SceneManager.GetSceneByName("MapScene");
                if (mapScene.IsValid() && mapScene.isLoaded)
                {
                    if (SceneController.ActiveScene != SceneController.GameScene.Map)
                        SceneController.SwitchToMap();

                    // Poczekaj 2 frame'y — SceneController.SwitchToMap ma delayed coroutine
                    // do enable Map UI canvasów (żeby uniknąć ping-pongu click). Po tym my
                    // je disablujemy na czas pickingu.
                    yield return null;
                    yield return null;

                    SuppressMapSceneUI();
                    _mapPickingBanner.SetActive(true);
                    Log.Info("[DepotLocationPicker] MapScene active, picker UI disabled, banner shown");
                    yield break;
                }
                yield return null;
            }
            Log.Warn("[DepotLocationPicker] Timeout czekając na MapScene (10s)");
            _isMapPicking = false;
            Show();
        }

        void SuppressMapSceneUI()
        {
            _suppressedMapCanvases.Clear();
            var mapScene = SceneManager.GetSceneByName("MapScene");
            if (!mapScene.IsValid() || !mapScene.isLoaded) return;

            var roots = mapScene.GetRootGameObjects();
            foreach (var root in roots)
            {
                var canvases = root.GetComponentsInChildren<Canvas>(includeInactive: false);
                foreach (var c in canvases)
                {
                    if (c == null || !c.enabled) continue;
                    if (c == _canvas) continue; // nasz własny canvas jest w DontDestroyOnLoad, nigdy nie będzie w Map
                    c.enabled = false;
                    _suppressedMapCanvases.Add(c);
                }
            }
        }

        void RestoreMapSceneUI()
        {
            foreach (var c in _suppressedMapCanvases)
                if (c != null) c.enabled = true;
            _suppressedMapCanvases.Clear();

            // Zamknij station popup który mógł się otworzyć podczas pickingu (halt click etc.)
            // żeby przy kolejnym wejściu na mapę user nie zobaczył sztucznie otwartego popup'u
            if (StationPopupUI.Instance != null) StationPopupUI.Instance.Hide();
        }

        void CancelMapPicking()
        {
            if (!_isMapPicking) return;
            _isMapPicking = false;
            _mapPickingBanner.SetActive(false);
            Log.Info("[DepotLocationPicker] Map picking anulowany (ESC)");
            StartCoroutine(SwitchBackToDepotAndShow(null));
        }

        void OnStationClicked(StationMarker marker)
        {
            Log.Info($"[DepotLocationPicker] ★ OnStationClicked fire'uje się (isMapPicking={_isMapPicking}, " +
                     $"marker='{marker?.stationName}', activeScene={SceneController.ActiveScene})");
            if (!_isMapPicking)
            {
                Log.Info("[DepotLocationPicker] _isMapPicking=false — pomijam (klik stacji poza trybem pickingu)");
                return;
            }

            var station = FindStationByProximity(marker.transform.position);
            if (station == null)
            {
                Log.Warn($"[DepotLocationPicker] Kliknięta stacja '{marker.stationName}' nie znaleziona w RailwayStation DB");
                return;
            }

            if (!IsEligible(station))
            {
                Log.Info($"[DepotLocationPicker] Stacja '{station.name}' nie kwalifikuje się");
                return;
            }

            // Ustawiamy flagę tu żeby kolejne kliki/raycasty nie retriggerowały
            _isMapPicking = false;
            _mapPickingBanner.SetActive(false);
            Log.Info($"[DepotLocationPicker] Picked from map: {station.name} — switching back to Depot");

            // Switch do Depot może być zablokowany przez SceneController cooldown (0.3s) —
            // coroutine retry'uje aż się powiedzie
            StartCoroutine(SwitchBackToDepotAndShow(station));
        }

        IEnumerator SwitchBackToDepotAndShow(RailwayStation station)
        {
            RestoreMapSceneUI();

            // Retry SwitchToDepot aż cooldown minie (max 2s)
            float timeout = Time.realtimeSinceStartup + 2f;
            while (SceneController.ActiveScene != SceneController.GameScene.Depot
                   && Time.realtimeSinceStartup < timeout)
            {
                SceneController.SwitchToDepot();
                yield return null;
            }

            if (SceneController.ActiveScene != SceneController.GameScene.Depot)
            {
                Log.Warn("[DepotLocationPicker] Nie udało się wrócić do Depot w ciągu 2s — " +
                         "pokazuję overlay mimo to");
            }

            // Krótkie wait żeby scene switch się w pełni dokonał
            yield return null;

            Show();
            if (station != null)
            {
                SelectStation(station);
                if (_searchInput != null) _searchInput.SetTextWithoutNotify(station.name);
                Log.Info($"[DepotLocationPicker] Back on Depot, overlay showing with selection: {station.name}");
            }
            else
            {
                Log.Info("[DepotLocationPicker] Back on Depot, overlay showing (ESC cancel — brak selection)");
            }
        }
    }
}
