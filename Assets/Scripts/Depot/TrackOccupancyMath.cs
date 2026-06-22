using System.Collections.Generic;

namespace DepotSystem
{
    /// <summary>
    /// TD-031: czysta algebra interwałów zajętości toru — bez MonoBehaviour / sceny Unity, więc
    /// w pełni testowalna w EditMode. Wszystkie współrzędne to track-local metry [0, Length]
    /// (oś toru wzdłuż <see cref="TrackGraph.GetTrackPolyline"/>, Start→End).
    /// </summary>
    public static class TrackOccupancyMath
    {
        /// <summary>
        /// Czy dwa interwały [aLo,aHi] i [bLo,bHi] nachodzą z marginesem styku <paramref name="gap"/>.
        /// Styk dokładnie na granicy (odstęp == 0) przy gap==0 traktujemy jako WOLNE (półotwarte).
        /// </summary>
        public static bool RangeOverlaps(float aLo, float aHi, float bLo, float bHi, float gap)
        {
            return aLo < bHi + gap && bLo < aHi + gap;
        }

        /// <summary>
        /// Czy zakres [fromM,toM] jest wolny od WSZYSTKICH occupantów o ConsistId != ignoreConsistId
        /// (z marginesem <paramref name="gap"/>). Kolejność from/to dowolna.
        /// </summary>
        public static bool IsRangeFree(IReadOnlyList<TrackOccupant> occupants, float fromM, float toM,
                                       int ignoreConsistId, float gap)
        {
            if (occupants == null) return true;
            float lo = fromM <= toM ? fromM : toM;
            float hi = fromM <= toM ? toM : fromM;
            for (int i = 0; i < occupants.Count; i++)
            {
                var o = occupants[i];
                if (o == null || o.ConsistId == ignoreConsistId) continue;
                if (RangeOverlaps(lo, hi, o.FrontDistM, o.RearDistM, gap)) return false;
            }
            return true;
        }

        /// <summary>
        /// Szuka pierwszej wolnej luki o długości <paramref name="requiredLength"/> w [0, trackLength],
        /// zachowując <paramref name="gap"/> od sąsiednich occupantów (granice toru bez gap).
        /// Occupanci sortowani po FrontDistM, potem ConsistId (determinizm). Zwraca true + lewy
        /// koniec footprintu w <paramref name="gapStartM"/>.
        /// </summary>
        public static bool TryFindFreeGap(IReadOnlyList<TrackOccupant> occupants, float trackLength,
                                          float requiredLength, float gap, out float gapStartM)
        {
            gapStartM = 0f;
            if (requiredLength <= 0f) return true;
            if (requiredLength > trackLength) return false;
            if (occupants == null || occupants.Count == 0) return true;

            var sorted = new List<TrackOccupant>(occupants);
            sorted.Sort((x, y) =>
            {
                int c = x.FrontDistM.CompareTo(y.FrontDistM);
                return c != 0 ? c : x.ConsistId.CompareTo(y.ConsistId);
            });

            float cursor = 0f; // najwcześniejsza wolna pozycja
            for (int i = 0; i < sorted.Count; i++)
            {
                float windowEnd = sorted[i].FrontDistM - gap; // gap przed occupantem
                if (windowEnd - cursor >= requiredLength)
                {
                    gapStartM = cursor;
                    return true;
                }
                float after = sorted[i].RearDistM + gap; // gap za occupantem
                if (after > cursor) cursor = after;
            }

            if (trackLength - cursor >= requiredLength)
            {
                gapStartM = cursor;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Najbliższy occupant ściśle PRZED nosem na pozycji <paramref name="fromDistM"/> w kierunku
        /// <paramref name="dirSign"/> (+1 = rosnące dist → najmniejszy FrontDistM &gt; from;
        /// -1 = malejące → największy RearDistM &lt; from), pomijając <paramref name="ignoreConsistId"/>.
        /// Zwraca ConsistId i krawędź którą nos pierwszą napotka (<paramref name="nearEdgeDistM"/>),
        /// lub -1 gdy nikogo nie ma z przodu.
        /// </summary>
        public static int FindNearestOccupantAhead(IReadOnlyList<TrackOccupant> occupants, float fromDistM,
                                                   int dirSign, int ignoreConsistId, out float nearEdgeDistM)
        {
            nearEdgeDistM = 0f;
            if (occupants == null) return -1;

            int bestId = -1;
            float bestEdge = 0f;
            for (int i = 0; i < occupants.Count; i++)
            {
                var o = occupants[i];
                if (o == null || o.ConsistId == ignoreConsistId) continue;

                if (dirSign >= 0)
                {
                    if (o.FrontDistM > fromDistM && (bestId < 0 || o.FrontDistM < bestEdge))
                    {
                        bestId = o.ConsistId;
                        bestEdge = o.FrontDistM;
                    }
                }
                else
                {
                    if (o.RearDistM < fromDistM && (bestId < 0 || o.RearDistM > bestEdge))
                    {
                        bestId = o.ConsistId;
                        bestEdge = o.RearDistM;
                    }
                }
            }

            nearEdgeDistM = bestEdge;
            return bestId;
        }

        // ── TD-031 Etap B: konwersje dystans-na-polyline ↔ track-local ──

        /// <summary>
        /// Dystans na polyline taska → track-local [0, trackLenM] dla danego segmentu (z clampem).
        /// </summary>
        public static float TaskDistToTrackLocal(TaskTrackSegment seg, float taskDistM)
        {
            float local = seg.reversedVsTrack ? (seg.polyEndM - taskDistM) : (taskDistM - seg.polyStartM);
            if (local < 0f) local = 0f;
            else if (local > seg.trackLenM) local = seg.trackLenM;
            return local;
        }

        /// <summary>
        /// Track-local dystans → dystans na polyline taska (odwrotność <see cref="TaskDistToTrackLocal"/>,
        /// bez clampu — zakładamy poprawny localM w [0, trackLenM]).
        /// </summary>
        public static float TrackLocalToTaskDist(TaskTrackSegment seg, float localM)
        {
            return seg.reversedVsTrack ? (seg.polyEndM - localM) : (seg.polyStartM + localM);
        }

        // ── TD-031 backward-compat (save bez bump SchemaVersion / migratora — pre-EA) ──

        /// <summary>
        /// TD-031: jeśli tor ma starą binarną zajętość (IsOccupied + OccupyingConsistId, ale pustą listę
        /// Occupants — np. save sprzed pozycyjnej zajętości) → syntetyzuj footprint = CAŁY tor [0, Length]
        /// (placement = środek, dokładnie jak w modelu binarnym). No-op gdy tor ma już Occupants lub jest
        /// wolny. Zwraca true gdy coś dodał. Wołane w <see cref="TrackGraph.RestoreFromSave"/>.
        /// </summary>
        public static bool SynthesizeLegacyOccupant(DepotTrackData track)
        {
            if (track == null) return false;
            if (track.Occupants == null) track.Occupants = new List<TrackOccupant>();
            if (track.Occupants.Count > 0) return false;

            int cid = track.OccupyingConsistId >= 0 ? track.OccupyingConsistId : track.OccupyingTrainId;
            if (!track.IsOccupied || cid < 0) return false;

            track.Occupants.Add(new TrackOccupant
            {
                ConsistId = cid,
                VehicleIds = track.OccupyingVehicleIds != null
                    ? new List<int>(track.OccupyingVehicleIds)
                    : new List<int>(),
                FrontDistM = 0f,
                RearDistM = track.Length,
                DirSign = 1
            });
            return true;
        }
    }
}
