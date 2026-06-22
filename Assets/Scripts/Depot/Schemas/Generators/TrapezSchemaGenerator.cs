using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Generators
{
    /// <summary>
    /// Generator geometrii trapezu — klasyczny PKP układ 2 sekwencyjnych crossoverów
    /// z 3m wstawką prostą na środkowym torze (NIE krzyżują się jak Scissors X).
    ///
    /// Topologia trapezowa:
    /// <code>
    /// Tor B: ─R1────────────────────────────R4──
    ///          ╲                          ╱
    ///           ╲ wstawka_1     wstawka_2
    ///            ╲                      ╱
    ///             R2════3m_gap════R3
    /// Tor A: ────────────────────────────────
    /// </code>
    ///
    /// - Crossover 1 (Tor B → Tor A): R1 (na Tor B, flip=false, lewo) + wstawka skośna
    ///   + R2 (rozjazd odwrotny na Tor A, flip=true, lewo).
    /// - 3m gap między R2 i R3 na Tor A (Pre3 = 3m straight segment).
    /// - Crossover 2 (Tor A → Tor B z powrotem): R3 (na Tor A, flip=false, prawo) + wstawka skośna
    ///   + R4 (rozjazd odwrotny na Tor B, flip=true, prawo).
    ///
    /// W X order: R1 (low X, Tor B) → R2 (middle-low X, Tor A) → 3m gap → R3 (middle-high X, Tor A)
    ///   → R4 (high X, Tor B).
    ///
    /// Pociąg jadący +X na Tor B: dojeżdża R1 → switch crossover 1 → wstawka 1 → R2 →
    ///   3m straight → R3 → switch crossover 2 → wstawka 2 → R4 → kontynuuje +X na Tor B.
    ///
    /// Vs Scissors: Scissors ma wstawki KRZYŻUJĄCE SIĘ w środku X (= compact 4-rozjazdy X).
    /// Trapez ma wstawki SEPARATE w X (= rozłożysty układ z 3m gap między R2 i R3 na Tor A).
    ///
    /// Endpointy: 4 końce torów (Tor A start = wjazd, Tor A end, Tor B start, Tor B end).
    ///
    /// Limity: trackCount fixed = 2. Tylko spacing wybieralny.
    /// turnoutType ignorowany (zawsze R190). mirror nieistotny (symetryczny).
    /// </summary>
    public class TrapezSchemaGenerator : ITurnoutSchemaGenerator
    {
        public TurnoutSchemaCategory Category => TurnoutSchemaCategory.Trapez;

        /// <summary>Bufor na końcach torów (wjazd + wyjazd przez ~15m straight).</summary>
        private const float TrackEndBuffer = 15.0f;

        /// <summary>3m wstawka prosta na Tor A między R2 (= koniec crossover 1) a R3 (= początek crossover 2).</summary>
        private const float MiddleStraightGap = 3.0f;

        public int ComputeTurnoutCount(int trackCount)
        {
            // Trapez zawsze 4 rozjazdy (2 crossovery × 2 rozjazdy każdy)
            return 4;
        }

        public bool ValidateParameters(SchemaParameters parameters, out string error)
        {
            if (parameters == null)
            {
                error = "parameters is null";
                return false;
            }
            if (parameters.trackCount != 2)
            {
                Log.Warn($"[TrapezSchemaGenerator] trackCount must be 2 for Trapez, got {parameters.trackCount}, forcing 2");
                parameters.trackCount = 2;
            }
            error = null;
            return true;
        }

        public SchemaParameters DefaultParameters()
        {
            return new SchemaParameters
            {
                trackCount = 2,
                trackSpacing = SchemaParameters.DefaultSpacing,
                turnoutType = SchemaTurnoutType.R190,
                mirror = false,
            };
        }

        public SchemaGeometry Generate(SchemaParameters p)
        {
            var geom = new SchemaGeometry();

            ValidateParameters(p, out _);

            var def = TurnoutData.R190_1_9;
            float spacing = p.GetSpacingAt(0);

            float hMain = TurnoutData.ComputeLateralOffset(Vector3.right, def);
            float sinAlpha = Mathf.Sin(def.FrogAngle);
            float cosAlpha = Mathf.Cos(def.FrogAngle);

            // === Geometria crossover (= rozjazd + wstawka skośna + rozjazd odwrotny) ===
            // Wstawka łączy R1.divEnd (lateral spacing - hMain) i R2.divEnd (lateral hMain).
            // Wstawka lateral component = (spacing - hMain) - hMain = spacing - 2*hMain.
            float wstawkaLateral = spacing - 2f * hMain;
            if (wstawkaLateral < 0)
            {
                Log.Warn($"[TrapezSchemaGenerator] spacing={spacing}m za małe (< 2*hMain={2f * hMain:F2}m), używam minimum");
                wstawkaLateral = 0.1f;
            }
            float wstawkaLength = wstawkaLateral / sinAlpha;
            float wstawkaXAdvance = wstawkaLength * cosAlpha;

            // X advance per crossover (= R1.body + wstawka + R2.body przeciwny):
            float hMainX = def.Radius * sinAlpha + def.StraightExtension * cosAlpha;
            float crossoverXLen = 2f * hMainX + wstawkaXAdvance;

            // X positions:
            float xR1 = TrackEndBuffer;                         // R1 na Tor B (low X)
            float xR2 = xR1 + crossoverXLen;                    // R2 na Tor A (middle-low X)
            float xR3 = xR2 + MiddleStraightGap;                // R3 na Tor A (middle-high X), 3m za R2
            float xR4 = xR3 + crossoverXLen;                    // R4 na Tor B (high X)
            float trackLength = xR4 + TrackEndBuffer;

            Log.Info($"[TrapezSchemaGenerator] === Generate trapez spacing={spacing}m ===");
            Log.Info($"[TrapezSchemaGenerator] hMain={hMain:F2}m, hMainX={hMainX:F2}m, wstawkaLength={wstawkaLength:F2}m, crossoverXLen={crossoverXLen:F2}m, MiddleGap={MiddleStraightGap}m, trackLength={trackLength:F2}m");
            Log.Info($"[TrapezSchemaGenerator] X positions: R1={xR1:F2}, R2={xR2:F2}, R3={xR3:F2}, R4={xR4:F2}");

            // === Tor A (Z=0) — single segment (split przez R2+R3 w PHASE 2) ===
            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3> {
                    new Vector3(0, 0, 0),
                    new Vector3(trackLength, 0, 0)
                },
                trackTypeName = "Parking",
                name = "Trapez.TorA"
            });

            // === Tor B (Z=spacing) — single segment (split przez R1+R4 w PHASE 2) ===
            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3> {
                    new Vector3(0, 0, spacing),
                    new Vector3(trackLength, 0, spacing)
                },
                trackTypeName = "Parking",
                name = "Trapez.TorB"
            });

            // === 4 rozjazdy ===
            // CONVENTION: Cross(+Y, +X) = -Z. divergeLeft=true → perp=-Z, false → +Z.

            // R1 (Tor B, low X). flip=false, divergeLeft=true → perp -Z → odgałęzienie +X-Z.
            Vector3 r1Origin = new Vector3(xR1, 0, spacing);
            BuildTurnoutAndDivergingLeg(geom, r1Origin, Vector3.right, def,
                divergeLeft: true, flipDirection: false,
                turnoutName: "Trapez.R1.Turnout (Tor B → wstawka 1)",
                arcStartName: "Trapez.R1.ArcStart");

            // R2 (Tor A, middle-low X). flip=true, divergeLeft=true → effectiveDir=-X, perp +Z
            //   (Cross(+Y,-X)=+Z, no negate) → odgałęzienie -X+Z (powraca z wstawki 1).
            Vector3 r2Origin = new Vector3(xR2, 0, 0);
            BuildTurnoutAndDivergingLeg(geom, r2Origin, Vector3.right, def,
                divergeLeft: true, flipDirection: true,
                turnoutName: "Trapez.R2.Turnout (wstawka 1 → Tor A)",
                arcStartName: "Trapez.R2.ArcStart");

            // R3 (Tor A, middle-high X = R2 + 3m). flip=false, divergeLeft=false → perp +Z
            //   (Cross(+Y,+X)=-Z, negate) → odgałęzienie +X+Z (do wstawki 2).
            Vector3 r3Origin = new Vector3(xR3, 0, 0);
            BuildTurnoutAndDivergingLeg(geom, r3Origin, Vector3.right, def,
                divergeLeft: false, flipDirection: false,
                turnoutName: "Trapez.R3.Turnout (Tor A → wstawka 2)",
                arcStartName: "Trapez.R3.ArcStart");

            // R4 (Tor B, high X). flip=true, divergeLeft=false → effectiveDir=-X, perp -Z
            //   (Cross(+Y,-X)=+Z, negate) → odgałęzienie -X-Z (powraca z wstawki 2).
            Vector3 r4Origin = new Vector3(xR4, 0, spacing);
            BuildTurnoutAndDivergingLeg(geom, r4Origin, Vector3.right, def,
                divergeLeft: false, flipDirection: true,
                turnoutName: "Trapez.R4.Turnout (wstawka 2 → Tor B)",
                arcStartName: "Trapez.R4.ArcStart");

            // === Wstawka 1 (R1.divEnd → R2.divEnd) ===
            Vector3 r1DivEnd = ComputeDivergingEndpoint(r1Origin, Vector3.right, def, divergeLeft: true, flipDirection: false);
            Vector3 r2DivEnd = ComputeDivergingEndpoint(r2Origin, Vector3.right, def, divergeLeft: true, flipDirection: true);
            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3> { r1DivEnd, r2DivEnd },
                trackTypeName = "Maneuver",
                name = "Trapez.Wstawka1 (R1↔R2)"
            });
            Log.Info($"[TrapezSchemaGenerator] Wstawka 1: R1.divEnd={r1DivEnd} → R2.divEnd={r2DivEnd}, length={Vector3.Distance(r1DivEnd, r2DivEnd):F2}m");

            // === Wstawka 2 (R3.divEnd → R4.divEnd) ===
            Vector3 r3DivEnd = ComputeDivergingEndpoint(r3Origin, Vector3.right, def, divergeLeft: false, flipDirection: false);
            Vector3 r4DivEnd = ComputeDivergingEndpoint(r4Origin, Vector3.right, def, divergeLeft: false, flipDirection: true);
            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3> { r3DivEnd, r4DivEnd },
                trackTypeName = "Maneuver",
                name = "Trapez.Wstawka2 (R3↔R4)"
            });
            Log.Info($"[TrapezSchemaGenerator] Wstawka 2: R3.divEnd={r3DivEnd} → R4.divEnd={r4DivEnd}, length={Vector3.Distance(r3DivEnd, r4DivEnd):F2}m");

            // === Endpointy: 4 końce torów ===
            geom.endpoints.Add(new Vector3(0, 0, 0));                       // Tor A start (wjazd)
            geom.endpoints.Add(new Vector3(trackLength, 0, 0));             // Tor A end
            geom.endpoints.Add(new Vector3(0, 0, spacing));                 // Tor B start
            geom.endpoints.Add(new Vector3(trackLength, 0, spacing));       // Tor B end

            Log.Info($"[TrapezSchemaGenerator] Generated complete: tracks={geom.tracks.Count}, turnouts={geom.turnouts.Count}, endpoints={geom.endpoints.Count}");

            geom.ComputeCentroid();
            geom.ComputeBounds();
            return geom;
        }

        /// <summary>
        /// Buduje SchemaTurnoutEntry + ArcStart polyline (placeAsTrack=false → R_x w PHASE 2 tworzy).
        /// </summary>
        private void BuildTurnoutAndDivergingLeg(
            SchemaGeometry geom,
            Vector3 origin,
            Vector3 trackDir,
            TurnoutData.TurnoutDefinition def,
            bool divergeLeft,
            bool flipDirection,
            string turnoutName,
            string arcStartName)
        {
            geom.turnouts.Add(new SchemaTurnoutEntry
            {
                turnoutTypeName = SchemaTurnoutType.R190,
                origin = origin,
                direction = trackDir,
                divergeLeft = divergeLeft,
                flipDirection = flipDirection,
                name = turnoutName
            });

            Vector3 effectiveDir = flipDirection ? -trackDir : trackDir;
            var (_, divergingLeg) = TurnoutData.GenerateTurnoutGeometry(origin, effectiveDir, def, divergeLeft);

            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3>(divergingLeg),
                trackTypeName = "Maneuver",
                name = arcStartName,
                placeAsTrack = false  // R_x.PlaceTurnoutOnChain w PHASE 2 utworzy
            });
            Vector3 divEnd = ComputeDivergingEndpoint(origin, trackDir, def, divergeLeft, flipDirection);
            Log.Info($"[TrapezSchemaGenerator] {turnoutName}: origin={origin}, flip={flipDirection}, divergeLeft={divergeLeft}, divEnd={divEnd}");
        }

        /// <summary>
        /// Oblicza endpoint divergingLeg uwzględniając flip.
        /// </summary>
        private Vector3 ComputeDivergingEndpoint(Vector3 origin, Vector3 trackDir, TurnoutData.TurnoutDefinition def, bool divergeLeft, bool flipDirection)
        {
            Vector3 effectiveDir = flipDirection ? -trackDir : trackDir;
            return TurnoutData.GetDivergingEndpoint(origin, effectiveDir, def, divergeLeft);
        }
    }
}
