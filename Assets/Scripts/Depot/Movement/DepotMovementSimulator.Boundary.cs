using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public partial class DepotMovementSimulator
    {
        // ── Boundary detection (BuildableArea / despawn margin) ──────

        GroundGenerator _groundGenerator;

        /// <summary>
        /// Margines za bramą po którym despawnujemy visual (m). Liczony od granicy BuildableArea.
        /// Pozwala pociągowi kawałek przejechać za bramę zanim zniknie — bardziej naturalnie
        /// niż znikanie dokładnie na linii płotu.
        /// </summary>
        const float DespawnMarginM = 35f; // fallback dla starszego flow

        /// <summary>
        /// Sprawdza czy pozycja 3D jest POZA obszarem budowlanym depot (za ogrodzeniem).
        /// Granica: GroundGenerator.BuildableArea.
        /// </summary>
        bool IsOutsideDepot(Vector3 worldPos)
        {
            if (_groundGenerator == null)
                _groundGenerator = DepotServices.Get<GroundGenerator>();
            if (_groundGenerator == null) return false;

            var bounds = _groundGenerator.BuildableArea;
            return worldPos.x < bounds.min.x || worldPos.x > bounds.max.x
                || worldPos.z < bounds.min.z || worldPos.z > bounds.max.z;
        }

        /// <summary>
        /// Znajduje pozycję 3D gdzie polyline toru przechodzi z outside (poza BuildableArea)
        /// do inside (wewnątrz) — efektywnie "linia bramy" dla danego toru. Używane przez
        /// SpawnConsistAtEntry żeby zatrzymać wjeżdżający consist przy bramie.
        /// Null gdy tor nie przechodzi przez granicę (cały wewnątrz lub cały na zewnątrz).
        /// </summary>
        Vector3? FindGateCrossingOnTrack(DepotTrackData track)
        {
            if (track?.EdgeIds == null || track.EdgeIds.Count == 0) return null;

            // Zbierz pełną polyline toru (konkatenacja edge.Polyline z uwzględnieniem kierunku).
            // Dla permanentnego toru zazwyczaj to pojedyncza krawędź — ale obsłużmy ogólnie.
            var polyline = _graph.GetTrackPolyline(track.TrackId);
            if (polyline == null || polyline.Count < 2) return null;

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                bool outThis = IsOutsideDepot(polyline[i]);
                bool outNext = IsOutsideDepot(polyline[i + 1]);
                if (outThis != outNext)
                {
                    // Transition — interpoluj na granicę BuildableArea dla dokładnego punktu
                    var bounds = _groundGenerator != null ? _groundGenerator.BuildableArea : new Bounds();
                    Vector3 a = polyline[i];
                    Vector3 b = polyline[i + 1];

                    // Dla każdej osi: jeśli segment przecina granicę, znajdź t
                    float bestT = outThis ? 1f : 0f; // fallback: weź inside point
                    float bestDist = float.MaxValue;

                    void TryAxis(float aVal, float bVal, float boundary)
                    {
                        if (Mathf.Approximately(aVal, bVal)) return;
                        float t = (boundary - aVal) / (bVal - aVal);
                        if (t < 0f || t > 1f) return;
                        Vector3 cross = Vector3.Lerp(a, b, t);
                        // Dodatkowa weryfikacja: punkt musi być na granicy bounds
                        if (cross.x < bounds.min.x - 0.01f || cross.x > bounds.max.x + 0.01f) return;
                        if (cross.z < bounds.min.z - 0.01f || cross.z > bounds.max.z + 0.01f) return;
                        float d = Vector3.Distance(a, cross);
                        if (d < bestDist) { bestDist = d; bestT = t; }
                    }

                    TryAxis(a.x, b.x, bounds.min.x);
                    TryAxis(a.x, b.x, bounds.max.x);
                    TryAxis(a.z, b.z, bounds.min.z);
                    TryAxis(a.z, b.z, bounds.max.z);

                    return Vector3.Lerp(a, b, bestT);
                }
            }

            return null;
        }

        /// <summary>
        /// Sprawdza czy visual consist'u jest wystarczająco daleko za bramą żeby despawnować.
        /// Używane dla exit flow (czeka aż cały skład + bufor są za ogrodzeniem).
        /// </summary>
        bool IsBeyondDespawnMargin(Vector3 worldPos) => IsBeyondDespawnMargin(worldPos, DespawnMarginM);

        bool IsBeyondDespawnMargin(Vector3 worldPos, float margin)
        {
            if (_groundGenerator == null)
                _groundGenerator = DepotServices.Get<GroundGenerator>();
            if (_groundGenerator == null) return false;

            var bounds = _groundGenerator.BuildableArea;
            return worldPos.x < bounds.min.x - margin
                || worldPos.x > bounds.max.x + margin
                || worldPos.z < bounds.min.z - margin
                || worldPos.z > bounds.max.z + margin;
        }

        /// <summary>
        /// Zwraca listę "zewnętrznych" node'ów — tych których pozycja jest poza BuildableArea.
        /// Consist jadący do takiego node'u = consist wyjeżdżający z depot.
        /// </summary>
        List<int> GetOutsideNodes()
        {
            var result = new List<int>();
            if (_graph == null) return result;
            foreach (var kvp in _graph.Nodes)
            {
                if (IsOutsideDepot(kvp.Value.Position))
                    result.Add(kvp.Key);
            }
            return result;
        }
    }
}
