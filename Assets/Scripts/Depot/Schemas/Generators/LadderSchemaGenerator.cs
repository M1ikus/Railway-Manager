using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Generators
{
    /// <summary>
    /// Generator geometrii drabinki rozjazdów (Ladder) — klasyczny układ "po jednej odnodze".
    ///
    /// Topologia:
    /// - Tor przewodni prosty (od X=0 = wjazd, do X = mainTrackLength)
    /// - Sekwencja N-1 rozjazdów na TORZE PRZEWODNIM (sąsiednie rozjazdy bezpośrednio jeden za
    ///   drugim, origin następnego = origin poprzedniego + def.Length)
    /// - Każdy rozjazd ma odgałęzienie pod kątem frogAngle = 6.34° (R190) w jedną stronę
    ///   (divergeLeft=true gdy mirror=false; divergeLeft=false gdy mirror=true)
    /// - Każde odgałęzienie kontynuuje jako tor postojowy o długości ParkingTrackLength
    /// - Wszystkie tory postojowe są pod TYM SAMYM kątem względem toru przewodniego
    ///   (wszystkie równoległe do siebie, tylko zaczynają się w różnych X)
    ///
    /// Endpointy: 1 wjazd (X=0) + 1 koniec toru przewodniego + N-1 końców torów postojowych
    ///   = N+1 endpointów. endpoint[0] = wjazd (anchor convention dla snap).
    ///
    /// Limity: trackCount 2-8.
    /// </summary>
    public class LadderSchemaGenerator : ITurnoutSchemaGenerator
    {
        public TurnoutSchemaCategory Category => TurnoutSchemaCategory.Ladder;

        public const int MinTrackCount = 2;
        public const int MaxTrackCount = 8;

        /// <summary>Tor wjazdowy przed pierwszym rozjazdem (m) — daje miejsce na snap.</summary>
        private const float EntryTrackLength = 25.0f;

        /// <summary>Bufor toru przewodniego za ostatnim rozjazdem (m).</summary>
        private const float MainTrackEndBuffer = 20.0f;

        /// <summary>Standardowa długość toru postojowego za rozjazdem (m).</summary>
        private const float ParkingTrackLength = 80.0f;

        public int ComputeTurnoutCount(int trackCount)
        {
            return Mathf.Max(0, trackCount - 1);
        }

        public bool ValidateParameters(SchemaParameters parameters, out string error)
        {
            if (parameters == null)
            {
                error = "parameters is null";
                return false;
            }
            if (parameters.trackCount < MinTrackCount || parameters.trackCount > MaxTrackCount)
            {
                error = $"Ladder trackCount must be {MinTrackCount}-{MaxTrackCount}, got {parameters.trackCount}";
                return false;
            }
            error = null;
            return true;
        }

        public SchemaParameters DefaultParameters()
        {
            return new SchemaParameters
            {
                trackCount = 4,
                trackSpacing = SchemaParameters.DefaultSpacing,
                turnoutType = SchemaTurnoutType.R190,
                mirror = false,
            };
        }

        public SchemaGeometry Generate(SchemaParameters p)
        {
            var geom = new SchemaGeometry();

            if (!ValidateParameters(p, out var err))
            {
                Log.Warn($"[LadderSchemaGenerator] {err} — clamping to valid range");
                p.trackCount = Mathf.Clamp(p.trackCount, MinTrackCount, MaxTrackCount);
            }

            int N = p.trackCount;
            int turnoutCount = ComputeTurnoutCount(N);

            // Resolve definitions per rozjazd
            var defs = new TurnoutData.TurnoutDefinition[turnoutCount];
            for (int i = 0; i < turnoutCount; i++)
            {
                var resolved = SchemaTurnoutType.Resolve(p.GetTurnoutTypeAt(i));
                if (!resolved.HasValue)
                {
                    Log.Error($"[LadderSchemaGenerator] Unknown turnout type '{p.GetTurnoutTypeAt(i)}' at index {i}");
                    return geom;
                }
                defs[i] = resolved.Value;
            }

            bool divergeLeft = !p.mirror;

            // Tor przewodni długi — kontynuuje przez wszystkie rozjazdy + buffer.
            // Aby obliczyć długość, zsumuj body wszystkich rozjazdów (każdy kolejny rozjazd
            // jest na ODGAŁĘZIENIU poprzedniego, więc body sumują się wzdłuż "trunk" pod
            // narastającym kątem; tor PRZEWODNI sam pozostaje prosty +X).
            // Dla prostoty: tor przewodni jest długi enough żeby pierwszy rozjazd miał miejsce.
            float mainTrackLength = EntryTrackLength + defs[0].Length + MainTrackEndBuffer;
            for (int i = 1; i < turnoutCount; i++) mainTrackLength += defs[i].Length;

            // Tor przewodni
            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3> {
                    Vector3.zero,
                    new Vector3(mainTrackLength, 0, 0)
                },
                trackTypeName = "Entry",
                name = "Ladder.MainTrack"
            });
            geom.endpoints.Add(Vector3.zero);
            geom.endpoints.Add(new Vector3(mainTrackLength, 0, 0));
            Log.Info($"[LadderSchemaGenerator] === Generate ladder N={N}, spacing={p.GetSpacingAt(0)}m, mirror={p.mirror} ===");
            Log.Info($"[LadderSchemaGenerator] MainTrack: 0..{mainTrackLength:F2}m");

            // Wspólny skos algorytm — JEDEN ArcStart inicjujący skos (= odgałęzienie R1),
            // jedna ciągła linia skośna pod frogAngle przez całą długość, KAŻDY tor postojowy
            // ma własny ReturnArc + ParkExtension wychodzące ze wspólnego skosu w różnych
            // punktach (= rozjazdy powrotne na skosie).
            //
            // Geometria:
            // - T1.ArcStart = łuk R1 (= odgałęzienie pierwszego rozjazdu, początek skosu)
            // - T{i}.SkewInsert = fragment wspólnego skosu między poprzednim a bieżącym ReturnArc
            // - T{i}.ReturnArc = łuk powrotny do +X (= odgałęzienie kolejnego rozjazdu powrotnego
            //   lub łuk swobodny dla ostatniego toru), alpha = frogAngle (NIE skumulowany — bo
            //   skos jest pod stałym kątem frogAngle)
            // - T{i}.ParkExtension = prosta postojowa pod +X
            // - Lateral tor i = i * spacing (cumulative)

            float hMain = TurnoutData.ComputeLateralOffset(Vector3.right, defs[0]);
            float hReturn = defs[0].Radius * (1f - Mathf.Cos(defs[0].FrogAngle));
            float sinAlpha = Mathf.Sin(defs[0].FrogAngle);

            // R1 — pierwszy rozjazd na torze przewodnim, inicjuje skos
            Vector3 arcStartOrigin = new Vector3(EntryTrackLength, 0, 0);
            geom.turnouts.Add(new SchemaTurnoutEntry
            {
                turnoutTypeName = p.GetTurnoutTypeAt(0),
                origin = arcStartOrigin,
                direction = Vector3.right,
                divergeLeft = divergeLeft,
                flipDirection = false,
                name = "Ladder.R1.Turnout"
            });

            var (_, arcStartLeg) = TurnoutData.GenerateTurnoutGeometry(arcStartOrigin, Vector3.right, defs[0], divergeLeft);
            Vector3 arcStartEnd = TurnoutData.GetDivergingEndpoint(arcStartOrigin, Vector3.right, defs[0], divergeLeft);
            Vector3 skewDir = TurnoutData.GetDivergingEndDirection(Vector3.right, defs[0], divergeLeft);

            // T1.ArcStart = łuk R1 — TWORZONY przez TurnoutPlacer.PlaceTurnoutOnChain w PHASE 2
            // (diverging leg R1). Pomijamy w PHASE 1 PrefabTrackBuilder żeby uniknąć duplikatu
            // (= "rozerwane" tory: R1.Diverging i ten polyline w tym samym miejscu = 2 segmenty
            // = 2 endpointy = wizualne kuleczki).
            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3>(arcStartLeg),
                trackTypeName = "Parking",
                name = "Ladder.T1.ArcStart",
                placeAsTrack = false
            });
            Log.Info($"[LadderSchemaGenerator] T1.ArcStart: origin={arcStartOrigin}, end={arcStartEnd}, skewDir={skewDir}, length={ComputePolylineLength(arcStartLeg):F2}m (placeAsTrack=false, R1 tworzy)");

            // Per tor postojowy: SkewInsert + ReturnArc + ParkExtension
            // Wszystkie SkewInsert są fragmentami JEDNEGO wspólnego skosu (od arcStartEnd dalej)
            float prevSkewDist = 0f;
            Vector3 prevSkewPoint = arcStartEnd;

            for (int trackIdx = 1; trackIdx < N; trackIdx++)
            {
                int turnoutIdx = trackIdx - 1;
                var def = defs[turnoutIdx];

                // Cumulative lateral target dla tor postojowy i
                float cumulativeLateral = p.GetCumulativeOffsetTo(trackIdx);

                // Skew distance ABSOLUTE od arcStartEnd dla osiągnięcia cumulative lateral
                // Formula: cumulativeLateral = hMain + skewDist * sinAlpha + hReturn
                // → skewDist = (cumulativeLateral - hMain - hReturn) / sinAlpha
                float skewDistAbs = (cumulativeLateral - hMain - hReturn) / sinAlpha;
                if (skewDistAbs < 0)
                {
                    Log.Warn($"[LadderSchemaGenerator] T{trackIdx}: cumulativeLateral={cumulativeLateral}m za małe (< hMain+hReturn={hMain + hReturn:F2}m), używam minimum");
                    skewDistAbs = 0.1f;
                }

                Vector3 skewEnd = arcStartEnd + skewDir * skewDistAbs;

                // T{i}.SkewInsert = fragment skosu od poprzedniego rozjazdu/arcStartEnd do tej pozycji
                if (skewDistAbs > prevSkewDist + 0.1f)
                {
                    geom.tracks.Add(new SchemaTrackEntry
                    {
                        polyline = new List<Vector3> { prevSkewPoint, skewEnd },
                        trackTypeName = "Maneuver",
                        name = $"Ladder.T{trackIdx}.SkewInsert"
                    });
                    Log.Info($"[LadderSchemaGenerator] T{trackIdx}.SkewInsert: from={prevSkewPoint} (skewDist={prevSkewDist:F2}m), to={skewEnd} (skewDist={skewDistAbs:F2}m), fragmentLength={(skewDistAbs - prevSkewDist):F2}m");
                }

                // R{i+1} — rozjazd powrotny na skosie (oprócz ostatniego toru — wtedy łuk swobodny)
                bool hasReturnTurnout = trackIdx < N - 1;
                if (hasReturnTurnout)
                {
                    geom.turnouts.Add(new SchemaTurnoutEntry
                    {
                        turnoutTypeName = p.GetTurnoutTypeAt(turnoutIdx),
                        origin = skewEnd,
                        direction = skewDir,
                        divergeLeft = !divergeLeft,  // PRZECIWNY (wraca do +X)
                        flipDirection = false,
                        name = $"Ladder.R{trackIdx + 1}.Turnout"
                    });
                }

                // T{i}.ReturnArc = łuk powrotny do +X (alpha = frogAngle, bo skos jest pod frogAngle)
                var returnArc = TurnoutData.GenerateReturnArc(skewEnd, skewDir, def.Radius, def.FrogAngle, turnLeft: !divergeLeft);
                Vector3 returnArcEnd = returnArc != null && returnArc.Count > 0
                    ? returnArc[returnArc.Count - 1]
                    : skewEnd;

                if (returnArc != null && returnArc.Count >= 2)
                {
                    // ReturnArc tworzony przez TurnoutPlacer (= R(i+1) odgałęzienie) JEŚLI hasReturnTurnout.
                    // Dla ostatniego toru (hasReturnTurnout=false) nie ma żadnego rozjazdu generującego
                    // ten łuk → musimy go postawić ręcznie jako "free arc".
                    bool placeManually = !hasReturnTurnout;
                    geom.tracks.Add(new SchemaTrackEntry
                    {
                        polyline = new List<Vector3>(returnArc),
                        trackTypeName = "Maneuver",
                        name = $"Ladder.T{trackIdx}.ReturnArc",
                        placeAsTrack = placeManually
                    });
                    Log.Info($"[LadderSchemaGenerator] T{trackIdx}.ReturnArc: from={skewEnd}, to={returnArcEnd}, alpha={def.FrogAngle * Mathf.Rad2Deg:F2}°, hasReturnTurnout={hasReturnTurnout}, placeAsTrack={placeManually}");
                }

                // T{i}.ParkExtension
                // UWAGA na duplikat z R(i+1).Diverging.StraightExtension:
                //  - GenerateReturnArc() zwraca TYLKO łuk (bez extension), więc returnArcEnd
                //    = koniec łuku (BEZ extension PKP-owskich ~6m za łukiem).
                //  - W PHASE 2 R(i+1).Diverging zawiera StraightExtension wzdłuż +X (6.09m dla R190).
                //  - Jeśli ParkExtension zaczyna się w returnArcEnd, jego pierwsze ~6m nakłada się
                //    z R(i+1).Diverging.StraightExtension = DUPLIKAT.
                // Naprawa: dla hasReturnTurnout=true użyj GetDivergingEndpoint (= po extension).
                // Dla last track (no R(N)) → manualny T(N-1).ReturnArc + ParkExtension od końca łuku.
                Vector3 parkExtensionStart = hasReturnTurnout
                    ? TurnoutData.GetDivergingEndpoint(skewEnd, skewDir, def, divergeLeft: !divergeLeft)
                    : returnArcEnd;

                Vector3 trackEnd = parkExtensionStart + Vector3.right * ParkingTrackLength;
                geom.tracks.Add(new SchemaTrackEntry
                {
                    polyline = new List<Vector3> { parkExtensionStart, trackEnd },
                    trackTypeName = "Parking",
                    name = $"Ladder.T{trackIdx}.ParkExtension"
                });
                Log.Info($"[LadderSchemaGenerator] T{trackIdx}.ParkExtension: from={parkExtensionStart}, to={trackEnd}, lateral_target={cumulativeLateral:F2}m, lateral_actual={Mathf.Abs(trackEnd.z):F2}m, startShift={Vector3.Distance(returnArcEnd, parkExtensionStart):F2}m (= StraightExtension R(i+1))");

                geom.endpoints.Add(trackEnd);

                // Update prev dla kolejnego SkewInsert
                prevSkewDist = skewDistAbs;
                prevSkewPoint = skewEnd;
            }

            Log.Info($"[LadderSchemaGenerator] Generated complete: tracks={geom.tracks.Count}, turnouts={geom.turnouts.Count}, endpoints={geom.endpoints.Count}");

            geom.ComputeCentroid();
            geom.ComputeBounds();
            return geom;
        }

        private static float ComputePolylineLength(System.Collections.Generic.List<Vector3> polyline)
        {
            if (polyline == null || polyline.Count < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < polyline.Count; i++)
                total += Vector3.Distance(polyline[i - 1], polyline[i]);
            return total;
        }
    }
}
