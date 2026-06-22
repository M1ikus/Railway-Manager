using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Generators
{
    /// <summary>
    /// Generator geometrii rozjazdu nożycowego (Scissors crossing) — klasyczny PKP X z 4 rozjazdami.
    ///
    /// Topologia X-pattern (krzyżujące się diagonale w środku):
    /// <code>
    ///   LT (R2) ─────────────────── RT (R4)
    ///        \                     /
    ///         \  D2     D1        /
    ///          \  \    /         /
    ///           \  \  /         /
    ///            \  \/         /
    ///             \ /\        /
    ///              X  \      /
    ///             / \  \    /
    ///            /   \  \  /
    ///           /     \  \/
    ///          /       \ /\
    ///         /         X  \
    ///        /           \  \
    ///       /             \  \
    ///   LB (R1) ─────────────────── RB (R3)
    /// </code>
    ///
    /// - 4 rozjazdy R190 w 4 rogach X-pattern (na końcach Tor A i Tor B).
    /// - 2 krótkie diagonale (inserty) łączące divEnd'y w środku X — KRZYŻUJĄ się geometrycznie.
    /// - D1 (R1↔R4, "/"): od R1 (LB) do R4 (RT) skośnie.
    /// - D2 (R2↔R3, "\"): od R2 (LT) do R3 (RB) skośnie.
    ///
    /// W realu PKP w środku X jest krzyżownica (diamond crossing). MVP: tylko visual X
    /// (polylines D1+D2 przecinają się geometrycznie, brak fizycznego węzła grafu).
    ///
    /// Endpointy: 4 końce torów (Tor A start = wjazd, Tor A end, Tor B start, Tor B end).
    ///
    /// Limity: trackCount fixed = 2. Tylko spacing wybieralny.
    /// turnoutType ignorowany (zawsze R190). mirror nieistotny (symetryczny).
    ///
    /// CONVENTION: Cross(+Y, +X) = -Z. Więc divergeLeft=true → perp=-Z, false → +Z.
    /// </summary>
    public class ScissorsSchemaGenerator : ITurnoutSchemaGenerator
    {
        public TurnoutSchemaCategory Category => TurnoutSchemaCategory.Scissors;

        /// <summary>Bufor na końcach torów (wjazd przed pierwszym rozjazdem + wyjazd za ostatnim).</summary>
        private const float TrackEndBuffer = 15.0f;

        /// <summary>
        /// Minimalny dystans (m) między body końcami sąsiednich rozjazdów (na tym samym torze).
        /// Zapewnia widoczny straight segment między body R1 i R3 (Tor A) oraz R2 i R4 (Tor B).
        /// </summary>
        private const float MinBodyToBodyGap = 3.0f;

        public int ComputeTurnoutCount(int trackCount)
        {
            // Scissors zawsze 4 rozjazdy (po 2 na każdym torze, w 4 rogach X)
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
                Log.Warn($"[ScissorsSchemaGenerator] trackCount must be 2 for Scissors, got {parameters.trackCount}, forcing 2");
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
            float hReturn = def.Radius * (1f - Mathf.Cos(def.FrogAngle));
            float sinAlpha = Mathf.Sin(def.FrogAngle);
            float cosAlpha = Mathf.Cos(def.FrogAngle);

            // === Geometria X-pattern z 4 rozjazdów ===
            // Każda diagonal = R_left.divergingLeg + Insert + R_right.divergingLeg(mirror).
            //
            // R1 (LB, +X+Z) divEnd lateral = +hMain. R4 (RT, -X-Z) divEnd lateral = spacing - hMain.
            // Insert lateral: (spacing - hMain) - hMain = spacing - 2*hMain.
            // Insert length: (spacing - 2*hMain) / sin(α).
            float insertLength = (spacing - 2f * hMain) / sinAlpha;
            if (insertLength < 0)
            {
                Log.Warn($"[ScissorsSchemaGenerator] spacing={spacing}m za małe dla 4-rozjazdy X (potrzebny ≥ 2*hMain={2f * hMain:F2}m), używam minimum 0.1m insert");
                insertLength = 0.1f;
            }

            float hMainX = def.Radius * sinAlpha + def.StraightExtension * cosAlpha;
            float crossLenGeometric = 2f * hMainX + insertLength * cosAlpha;

            // Sprawdź gap między body R1 i R3 (na Tor A). Gap = crossLen - 2*def.Length.
            float geometricGap = crossLenGeometric - 2f * def.Length;
            float crossLen;
            if (geometricGap < MinBodyToBodyGap)
            {
                crossLen = 2f * def.Length + MinBodyToBodyGap;
                Log.Info($"[ScissorsSchemaGenerator] Rozszerzam crossLen z {crossLenGeometric:F2}m do {crossLen:F2}m (geometric gap {geometricGap:F2}m < min {MinBodyToBodyGap}m)");
            }
            else
            {
                crossLen = crossLenGeometric;
            }
            float trackLength = TrackEndBuffer + crossLen + TrackEndBuffer;

            Log.Info($"[ScissorsSchemaGenerator] === Generate scissors X spacing={spacing}m ===");
            Log.Info($"[ScissorsSchemaGenerator] hMain={hMain:F2}m, insertLength={insertLength:F2}m, hMainX={hMainX:F2}m, crossLen={crossLen:F2}m, trackLength={trackLength:F2}m");

            // === Tor A (Z=0) ===
            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3> {
                    new Vector3(0, 0, 0),
                    new Vector3(trackLength, 0, 0)
                },
                trackTypeName = "Parking",
                name = "Scissors.TorA"
            });

            // === Tor B (Z=spacing) ===
            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3> {
                    new Vector3(0, 0, spacing),
                    new Vector3(trackLength, 0, spacing)
                },
                trackTypeName = "Parking",
                name = "Scissors.TorB"
            });

            // === 4 rozjazdy w 4 rogach X-pattern ===
            // ORDER: grouped by track (R1+R3 razem na Tor A, potem R2+R4 razem na Tor B).

            // R1 (LB): origin (BUFFER, 0, 0), flip=false, divergeLeft=false → odgałęzienie +X+Z
            Vector3 r1Origin = new Vector3(TrackEndBuffer, 0, 0);
            BuildTurnoutAndDivergingLeg(geom, r1Origin, Vector3.right, def,
                divergeLeft: false, flipDirection: false,
                turnoutName: "Scissors.R1.Turnout (LB)", arcStartName: "Scissors.D1.LB.ArcStart");

            // R3 (RB): origin (BUFFER+crossLen, 0, 0), flip=true, divergeLeft=true → odgałęzienie -X+Z
            Vector3 r3Origin = new Vector3(TrackEndBuffer + crossLen, 0, 0);
            BuildTurnoutAndDivergingLeg(geom, r3Origin, Vector3.right, def,
                divergeLeft: true, flipDirection: true,
                turnoutName: "Scissors.R3.Turnout (RB)", arcStartName: "Scissors.D2.RB.ArcStart");

            // R2 (LT): origin (BUFFER, 0, spacing), flip=false, divergeLeft=true → odgałęzienie +X-Z
            Vector3 r2Origin = new Vector3(TrackEndBuffer, 0, spacing);
            BuildTurnoutAndDivergingLeg(geom, r2Origin, Vector3.right, def,
                divergeLeft: true, flipDirection: false,
                turnoutName: "Scissors.R2.Turnout (LT)", arcStartName: "Scissors.D2.LT.ArcStart");

            // R4 (RT): origin (BUFFER+crossLen, 0, spacing), flip=true, divergeLeft=false → odgałęzienie -X-Z
            Vector3 r4Origin = new Vector3(TrackEndBuffer + crossLen, 0, spacing);
            BuildTurnoutAndDivergingLeg(geom, r4Origin, Vector3.right, def,
                divergeLeft: false, flipDirection: true,
                turnoutName: "Scissors.R4.Turnout (RT)", arcStartName: "Scissors.D1.RT.ArcStart");

            // === 2 diagonale (krótkie inserty łączące divEnd'y w środku X) ===

            // D1 (R1 ↔ R4): R1.divEnd → R4.divEnd
            Vector3 r1DivEnd = ComputeDivergingEndpoint(r1Origin, Vector3.right, def, divergeLeft: false, flipDirection: false);
            Vector3 r4DivEnd = ComputeDivergingEndpoint(r4Origin, Vector3.right, def, divergeLeft: false, flipDirection: true);
            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3> { r1DivEnd, r4DivEnd },
                trackTypeName = "Maneuver",
                name = "Scissors.D1.Insert (R1↔R4)"
            });
            Log.Info($"[ScissorsSchemaGenerator] D1.Insert: R1.divEnd={r1DivEnd} → R4.divEnd={r4DivEnd}, length={Vector3.Distance(r1DivEnd, r4DivEnd):F2}m");

            // D2 (R2 ↔ R3): R2.divEnd → R3.divEnd
            Vector3 r2DivEnd = ComputeDivergingEndpoint(r2Origin, Vector3.right, def, divergeLeft: true, flipDirection: false);
            Vector3 r3DivEnd = ComputeDivergingEndpoint(r3Origin, Vector3.right, def, divergeLeft: true, flipDirection: true);
            geom.tracks.Add(new SchemaTrackEntry
            {
                polyline = new List<Vector3> { r2DivEnd, r3DivEnd },
                trackTypeName = "Maneuver",
                name = "Scissors.D2.Insert (R2↔R3)"
            });
            Log.Info($"[ScissorsSchemaGenerator] D2.Insert: R2.divEnd={r2DivEnd} → R3.divEnd={r3DivEnd}, length={Vector3.Distance(r2DivEnd, r3DivEnd):F2}m");

            // === Endpointy: 4 końce torów ===
            geom.endpoints.Add(new Vector3(0, 0, 0));                       // Tor A start (wjazd)
            geom.endpoints.Add(new Vector3(trackLength, 0, 0));             // Tor A end
            geom.endpoints.Add(new Vector3(0, 0, spacing));                 // Tor B start
            geom.endpoints.Add(new Vector3(trackLength, 0, spacing));       // Tor B end

            Log.Info($"[ScissorsSchemaGenerator] Generated complete: tracks={geom.tracks.Count}, turnouts={geom.turnouts.Count}, endpoints={geom.endpoints.Count}");

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
            Log.Info($"[ScissorsSchemaGenerator] {turnoutName}: origin={origin}, flip={flipDirection}, divergeLeft={divergeLeft}, divEnd={divEnd}");
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
