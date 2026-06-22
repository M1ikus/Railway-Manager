using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    public partial class TrainRunSimulator
    {
        // ── Wizualizacja (Etap 2) ───────────────────────────────────

        void CreateVisual(SimulatedTrain st)
        {
            // CreatePrimitive gwarantuje widoczność — domyślny materiał działa w każdym pipeline
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Train_{st.trainRun.trainNumberSnapshot}";
            go.layer = 31; // MapLayer — widoczny przez Map Camera
            go.transform.SetParent(_trainContainer);

            // Spłaszcz sześcian do kwadratu widocznego z góry
            go.transform.localScale = new Vector3(TrainVisualSize, 2f, TrainVisualSize);

            // Zielony kolor (URP-safe: próbujemy _BaseColor i _Color)
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && _trainMaterial != null)
                mr.sharedMaterial = _trainMaterial;

            // Zatrzymujemy BoxCollider (z CreatePrimitive) — potrzebny do OnMouseDown (popup)
            // Ale isTrigger = true żeby nie interferował z fizyką
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            // Marker do obsługi klików (popup info)
            var marker = go.AddComponent<TrainMarker>();
            marker.SimulatedTrain = st;

            st.visual = go;
            st.visualTransform = go.transform;

            // Pozycja startowa
            UpdateVisualPosition(st);

            Log.Info($"[TrainRunSimulator] Visual created: layer={go.layer}, " +
                     $"scale={go.transform.localScale}, pos={go.transform.position}");
        }

        void UpdateVisualPosition(SimulatedTrain st)
        {
            if (st.visualTransform == null) return;

            float distM = st.trainRun.currentPositionOnRouteM;

            // Interpoluj pozycję na szczegółowej polyline (edge geometry)
            var pos = GetPositionOnPolyline(st, distM);
            st.visualTransform.position = new Vector3(pos.x, TrainYHeight, pos.y);

            // Rotacja wg kierunku jazdy (tylko Y — kwadrat leży płasko, kamera patrzy z góry)
            var dir = GetDirectionOnPolyline(st, distM);
            if (dir.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                st.visualTransform.rotation = Quaternion.Euler(0f, angle, 0f);
            }

            // M9c-4: propagate pozycję do VehicleLocationService (dla UI idle vehicles,
            // debug overlay, cross-scene queries). Wywoływane per tick — dlatego
            // UpdateRoutePosition nie emituje OnLocationChanged (tylko .worldMapPosition update).
            var locSvc = VehicleLocationService.Instance;
            if (locSvc != null && st.trainRun.runningVehicleIds != null)
            {
                foreach (int vid in st.trainRun.runningVehicleIds)
                    locSvc.UpdateRoutePosition(vid, pos);
            }

            // M6-4: delta kilometrowa → koszt operacyjny (paliwo/energia + track access)
            float deltaM = distM - st.lastCostDistanceM;
            if (deltaM > 0f && st.trainRun.runningVehicleIds != null
                && st.trainRun.runningVehicleIds.Count > 0)
            {
                int costPerKm = RailwayManager.Timetable.Economy.CostCalculator
                    .GetConsistOperationalCostPerKm(st.trainRun.runningVehicleIds);
                if (costPerKm > 0)
                {
                    int costGroszy = Mathf.RoundToInt(costPerKm * (deltaM / 1000f));
                    if (costGroszy > 0)
                    {
                        var econ = RailwayManager.Timetable.Economy.EconomyManager.Instance;
                        econ?.AddCost(st.trainRun.circulationId, costGroszy, "operational", "per_km");
                    }
                }

                // M7-2: degradacja komponentów per-km
                RailwayManager.Fleet.DegradationService.ApplyDegradation(
                    st.trainRun.runningVehicleIds, deltaM / 1000f);

                // M7-3: check awarii — tylko gdy pociąg faktycznie jedzie (Running)
                if (st.state == TrainState.Running)
                    CheckForBreakdown(st, deltaM);
            }
            st.lastCostDistanceM = distM;
        }
    }
}
