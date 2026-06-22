using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Generators
{
    /// <summary>
    /// Generator geometrii wachlarza rozjazdów (Throat) — klasyczny PKP wachlarz rozjazdów.
    ///
    /// Topologia:
    /// - Tor przewodni (długi, prosta wzdłuż +X) — kontynuuje przez wszystkie rozjazdy + buffer.
    /// - N-1 rozjazdów NA TORZE PRZEWODNIM, sąsiadujące bezpośrednio (origin następnego
    ///   = origin poprzedniego + def.Length).
    /// - Per rozjazd: ŁUK ODGAŁĘZIENIA + WSTAWKA SKOŚNA + ŁUK POWROTNY + TOR POSTOJOWY.
    ///   Każdy tor postojowy ma SWÓJ NIEZALEŻNY skos i łuk powrotny (NIE wspólny skos jak Ladder).
    /// - Wszystkie tory postojowe równoległe do toru przewodniego (po łuku powrotnym).
    /// - Lateral target dla tor postojowy i = i × spacing (cumulative).
    ///
    /// Vs Ladder: Ladder ma WSPÓLNY SKOS między rozjazdami (zwężona głowica = bardziej
    /// zwarty układ). Throat ma NIEZALEŻNE skosy per tor (klasyczny "rozłożony" wachlarz =
    /// bardziej rozłożysty, każdy tor swój łuk od początku do końca).
    ///
    /// Endpointy: 1 wjazd + 1 koniec toru przewodniego + N-1 końców torów postojowych = N+1.
    /// endpoint[0] = wjazd (anchor convention dla snap).
    ///
    /// Limity: trackCount 3-6.
    /// </summary>
    public class ThroatSchemaGenerator : ITurnoutSchemaGenerator
    {
        public TurnoutSchemaCategory Category => TurnoutSchemaCategory.Throat;

        public const int MinTrackCount = 3;
        public const int MaxTrackCount = 6;

        /// <summary>Tor wjazdowy przed pierwszym rozjazdem (m) — daje miejsce na snap.</summary>
        private const float EntryTrackLength = 25.0f;

        /// <summary>Bufor toru przewodniego za ostatnim rozjazdem (m).</summary>
        private const float MainTrackEndBuffer = 20.0f;

        /// <summary>Standardowa długość toru postojowego za łukiem powrotnym (m).</summary>
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
                error = $"Throat trackCount must be {MinTrackCount}-{MaxTrackCount}, got {parameters.trackCount}";
                return false;
            }
            error = null;
            return true;
        }

        public SchemaParameters DefaultParameters()
        {
            // Reverse mapping: turnoutIdx=0 (pierwszy na torze przewodnim) → tor NAJDALSZY,
            // turnoutIdx=N-2 (ostatni) → tor NAJBLIŻSZY.
            //
            // Wszystkie R190 — geometria "odgałęzienie po U" (rozjazd + insert + łuk powrotny
            // tego samego promienia co rozjazd) działa dla każdego toru bez względu na docelowy
            // lateral. Dla najdalszego toru (T3, lateral=15m) insert jest po prostu DŁUŻSZY
            // (~109m dla spacing=5m), nie wymaga większego promienia rozjazdu.
            //
            // R300 użyteczne tylko gdy chcesz mniej ostrych łuków powrotnych (większy promień
            // = łagodniejszy łuk = większa prędkość przejazdowa). Default zostawia R190.
            return new SchemaParameters
            {
                trackCount = 4,
                trackSpacings = new[] {
                    SchemaParameters.DefaultSpacing,
                    SchemaParameters.DefaultSpacing,
                    SchemaParameters.DefaultSpacing
                },
                turnoutTypes = new[] {
                    SchemaTurnoutType.R190,  // turnoutIdx=0 → tor 3 (najdalszy, lateral=15m, insert ~109m)
                    SchemaTurnoutType.R190,  // turnoutIdx=1 → tor 2 (lateral=10m, insert ~64m)
                    SchemaTurnoutType.R190,  // turnoutIdx=2 → tor 1 (najbliższy, lateral=5m, insert ~18m)
                },
                mirror = false,
            };
        }

        public SchemaGeometry Generate(SchemaParameters p)
        {
            var geom = new SchemaGeometry();

            if (!ValidateParameters(p, out var err))
            {
                Log.Warn($"[ThroatSchemaGenerator] {err} — clamping to valid range");
                p.trackCount = Mathf.Clamp(p.trackCount, MinTrackCount, MaxTrackCount);
            }

            int N = p.trackCount;
            int turnoutCount = ComputeTurnoutCount(N);

            var defs = new TurnoutData.TurnoutDefinition[turnoutCount];
            for (int i = 0; i < turnoutCount; i++)
            {
                var resolved = SchemaTurnoutType.Resolve(p.GetTurnoutTypeAt(i));
                if (!resolved.HasValue)
                {
                    Log.Error($"[ThroatSchemaGenerator] Unknown turnout type '{p.GetTurnoutTypeAt(i)}' at index {i}");
                    return geom;
                }
                defs[i] = resolved.Value;
            }

            bool divergeLeft = !p.mirror;

            // STAGGER między rozjazdami — każdy kolejny rozjazd na torze przewodnim ma odsunięty
            // origin o max(def.Length, spacing/sin(α)). Bez tego sąsiednie inserty (= odgałęzienia)
            // wychodzą blisko siebie w +X i są <spacing apart w obszarze przejściowym.
            // Z tym staggerem: na X = R_(i+1).origin, lateral T_obsługiwany_przez_R_i wynosi
            // dokładnie hMain + spacing → ≥ spacing apart od T_obsługiwany_przez_R_(i+1).
            //
            // Trade-off: tor przewodni jest dłuższy (~+45m × (N-2) dla R190 spacing=5m), ale
            // wachlarz wygląda jak klasyczny PKP (tory wyraźnie rozdzielone od początku, nie
            // zbiegające się tuż przed parkingiem).
            float[] turnoutStaggers = new float[turnoutCount];
            for (int i = 0; i < turnoutCount; i++)
            {
                float spacingForPair = p.GetSpacingAt(i);
                float sinAlpha_i = Mathf.Sin(defs[i].FrogAngle);
                float minStagger = sinAlpha_i > 0.001f ? spacingForPair / sinAlpha_i : defs[i].Length;
                turnoutStaggers[i] = Mathf.Max(defs[i].Length, minStagger);
            }

            // Tor przewodni — długi, mieści wszystkie rozjazdy + staggery + buffer.
            float mainTrackLength = EntryTrackLength;
            for (int i = 0; i < turnoutCount; i++) mainTrackLength += turnoutStaggers[i];
            mainTrackLength += MainTrackEndBuffer;

            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3> {
                    Vector3.zero,
                    new Vector3(mainTrackLength, 0, 0)
                },
                trackTypeName = "Entry",
                name = "Throat.MainTrack"
            });
            geom.endpoints.Add(Vector3.zero);
            geom.endpoints.Add(new Vector3(mainTrackLength, 0, 0));
            Log.Info($"[ThroatSchemaGenerator] === Generate throat N={N}, spacing={p.GetSpacingAt(0)}m, mirror={p.mirror} ===");
            Log.Info($"[ThroatSchemaGenerator] MainTrack: 0..{mainTrackLength:F2}m, staggers=[{string.Join(", ", System.Array.ConvertAll(turnoutStaggers, s => s.ToString("F2") + "m"))}]");

            // Klasyczny wachlarz PKP — per tor postojowy: rozjazd + odgałęzienie (łuk + extension)
            // + wstawka skośna + łuk powrotny + tor postojowy. Każdy tor NIEZALEŻNY (nie wspólny
            // skos jak Ladder).
            //
            // REVERSE MAPPING: pierwszy rozjazd (turnoutIdx=0, najwcześniej na torze przewodnim
            // = najbliżej wjazdu) obsługuje tor NAJDALSZY (trackIdx=N-1). Ostatni rozjazd
            // (turnoutIdx=N-2, najpóźniej na torze przewodnim) → tor najbliższy (trackIdx=1).
            //
            // Powód: tor najdalszy potrzebuje NAJDŁUŻSZEGO insertu skośnego (musi pokonać większą
            // lateral). Stawiając jego rozjazd jako PIERWSZY, insert ma dużo miejsca w +X żeby
            // skośnie dojechać do swojej cumulativeLateral. Tor najbliższy potrzebuje krótkiego
            // insertu → jego rozjazd może być na końcu wachlarza.
            //
            // Geometria per turnoutIdx (= sequence on main track):
            // - Rozjazd R_(turnoutIdx+1) na torze przewodnim, origin = poprzedni + def.Length.
            // - T{trackIdx}.ArcStart = divergingLeg (placeAsTrack=false → PHASE 2 tworzy).
            // - T{trackIdx}.Insert = wstawka skośna o length aby osiągnąć cumulativeLateral.
            // - T{trackIdx}.ReturnArc = łuk powrotny (free arc, placeAsTrack=true).
            // - T{trackIdx}.ParkExtension = prosta wzdłuż +X o ParkingTrackLength.
            //
            // Lateral target tor i = i × spacing.
            // insert_i = (i*spacing - hMain - hReturn) / sinAlpha.

            Vector3 turnoutOrigin = new Vector3(EntryTrackLength, 0, 0);

            for (int turnoutIdx = 0; turnoutIdx < turnoutCount; turnoutIdx++)
            {
                int trackIdx = N - 1 - turnoutIdx;  // reverse: pierwszy rozjazd → najdalszy tor
                var def = defs[turnoutIdx];
                float cumulativeLateral = p.GetCumulativeOffsetTo(trackIdx);

                float hMain = TurnoutData.ComputeLateralOffset(Vector3.right, def);
                float hReturn = def.Radius * (1f - Mathf.Cos(def.FrogAngle));
                float sinAlpha = Mathf.Sin(def.FrogAngle);

                // Insert length to reach cumulativeLateral
                float insertLength = (cumulativeLateral - hMain - hReturn) / sinAlpha;
                if (insertLength < 0)
                {
                    Log.Warn($"[ThroatSchemaGenerator] T{trackIdx}: cumulativeLateral={cumulativeLateral}m za małe (< hMain+hReturn={hMain + hReturn:F2}m), używam minimum 0.1m");
                    insertLength = 0.1f;
                }

                // === Rozjazd R_(turnoutIdx+1) na torze przewodnim, obsługuje tor postojowy {trackIdx} ===
                geom.turnouts.Add(new SchemaTurnoutEntry
                {
                    turnoutTypeName = p.GetTurnoutTypeAt(turnoutIdx),
                    origin = turnoutOrigin,
                    direction = Vector3.right,
                    divergeLeft = divergeLeft,
                    flipDirection = false,
                    name = $"Throat.R{turnoutIdx + 1}.Turnout (→T{trackIdx})"
                });
                Log.Info($"[ThroatSchemaGenerator] R{turnoutIdx + 1}: origin={turnoutOrigin}, type={p.GetTurnoutTypeAt(turnoutIdx)}, obsługuje tor postojowy T{trackIdx} (lateral={cumulativeLateral:F2}m)");

                // === T{trackIdx}.ArcStart = divergingLeg R_(turnoutIdx+1) (łuk + extension) ===
                // placeAsTrack=false — R_(turnoutIdx+1).PlaceTurnoutOnChain w PHASE 2 utworzy ten łuk.
                // Bez tego dwa segmenty w tym samym miejscu = duplikat ("rozerwane" tory).
                var (_, divergingLeg) = TurnoutData.GenerateTurnoutGeometry(turnoutOrigin, Vector3.right, def, divergeLeft);
                Vector3 arcStartEnd = TurnoutData.GetDivergingEndpoint(turnoutOrigin, Vector3.right, def, divergeLeft);
                Vector3 skewDir = TurnoutData.GetDivergingEndDirection(Vector3.right, def, divergeLeft);

                geom.tracks.Add(new SchemaTrackEntry
                {
                    polyline = new List<Vector3>(divergingLeg),
                    trackTypeName = "Parking",
                    name = $"Throat.T{trackIdx}.ArcStart",
                    placeAsTrack = false
                });
                Log.Info($"[ThroatSchemaGenerator] T{trackIdx}.ArcStart: origin={turnoutOrigin}, end={arcStartEnd}, skewDir={skewDir} (placeAsTrack=false, R{turnoutIdx + 1} tworzy)");

                // === T{i}.Insert = wstawka skośna ===
                Vector3 insertEnd = arcStartEnd + skewDir * insertLength;
                if (insertLength > 0.1f)
                {
                    geom.tracks.Add(new SchemaTrackEntry
                    {
                        polyline = new List<Vector3> { arcStartEnd, insertEnd },
                        trackTypeName = "Maneuver",
                        name = $"Throat.T{trackIdx}.Insert"
                    });
                    Log.Info($"[ThroatSchemaGenerator] T{trackIdx}.Insert: from={arcStartEnd}, to={insertEnd}, length={insertLength:F2}m");
                }

                // === T{i}.ReturnArc = łuk powrotny ===
                // Free arc — brak rozjazdu R_(N) kończącego, więc placeAsTrack=true (manualnie).
                // Promień łuku powrotnego = def.Radius (= TEN SAM PROMIEŃ CO ROZJAZD ROZGAŁĘZIAJĄCY).
                // Daje "odgałęzienie po U" symmetric — łuk wyjściowy z rozjazdu i łuk powrotny
                // mają taką samą krzywiznę. Bez tego łuk powrotny mógłby być ostrzejszy/łagodniejszy
                // niż wyjściowy = niezgodne z PKP standardem dla głowic rozjazdowych.
                var returnArc = TurnoutData.GenerateReturnArc(insertEnd, skewDir, def.Radius, def.FrogAngle, turnLeft: !divergeLeft);
                Vector3 returnArcEnd = returnArc != null && returnArc.Count > 0
                    ? returnArc[returnArc.Count - 1]
                    : insertEnd;

                if (returnArc != null && returnArc.Count >= 2)
                {
                    geom.tracks.Add(new SchemaTrackEntry
                    {
                        polyline = new List<Vector3>(returnArc),
                        trackTypeName = "Maneuver",
                        name = $"Throat.T{trackIdx}.ReturnArc"
                    });
                    Log.Info($"[ThroatSchemaGenerator] T{trackIdx}.ReturnArc: from={insertEnd}, to={returnArcEnd}, alpha={def.FrogAngle * Mathf.Rad2Deg:F2}°");
                }

                // === T{i}.ParkExtension ===
                // Brak rozjazdu kończącego (free return arc) → start = returnArcEnd (= koniec łuku).
                Vector3 trackEnd = returnArcEnd + Vector3.right * ParkingTrackLength;
                geom.tracks.Add(new SchemaTrackEntry
                {
                    polyline = new List<Vector3> { returnArcEnd, trackEnd },
                    trackTypeName = "Parking",
                    name = $"Throat.T{trackIdx}.ParkExtension"
                });
                Log.Info($"[ThroatSchemaGenerator] T{trackIdx}.ParkExtension: from={returnArcEnd}, to={trackEnd}, lateral_target={cumulativeLateral:F2}m, lateral_actual={Mathf.Abs(trackEnd.z):F2}m");

                geom.endpoints.Add(trackEnd);

                // Update turnoutOrigin — kolejny rozjazd o stagger dalej (= max(def.Length, spacing/sin α))
                // by inserty były od początku ≥ spacing apart w lateral.
                turnoutOrigin = new Vector3(turnoutOrigin.x + turnoutStaggers[turnoutIdx], 0, 0);
            }

            Log.Info($"[ThroatSchemaGenerator] Generated complete: tracks={geom.tracks.Count}, turnouts={geom.turnouts.Count}, endpoints={geom.endpoints.Count}");

            geom.ComputeCentroid();
            geom.ComputeBounds();
            return geom;
        }
    }
}
