using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable.Assistant
{
    /// <summary>
    /// M11 AS-5c: tworzenie rozkładu z wybranej propozycji plannera — ta sama ścieżka co
    /// kreator (Route z PathResult → TimetableBuilder.BuildStops z realnymi czasami →
    /// AddTimetable), tylko headless. Rozkład powstaje jako zwykły Active bez numeru
    /// pociągu i bez obiegu — gracz domyka w istniejących narzędziach (numer w kreatorze,
    /// obieg przez circulation.autogen — asystent sam to potem zasugeruje, reguła ob.circulation).
    ///
    /// Mapowania archetyp→kategoria/IRJ są MVP-prostne (stałe w kodzie z TODO M-Balance):
    /// Aglomeracyjna → "os"/RA, Regionalna → "os"/RO, Międzyregionalna → "rp"/MP.
    /// </summary>
    public static class TimetableFromRelationService
    {
        /// <summary>Pierwszy kurs dnia dla taktu plannera (05:00) — okno 18 h kończy ~23:00.</summary>
        public const int FirstRunMinutesFromMidnight = 5 * 60;

        // ── Mapowania archetypu (pure, testowalne) ──

        public static string MapArchetypeToCategoryId(RelationArchetype archetype) => archetype switch
        {
            RelationArchetype.Agglomeration => "os",
            RelationArchetype.Regional => "os",
            _ => "rp"
        };

        public static IrjGroup MapArchetypeToIrjGroup(RelationArchetype archetype) => archetype switch
        {
            RelationArchetype.Agglomeration => IrjGroup.RegionalAgglomeration,
            RelationArchetype.Regional => IrjGroup.RegionalLocal,
            _ => IrjGroup.InterregionalFast
        };

        /// <summary>
        /// Wariant częstotliwości → FrequencySpec. Kontrakt: RunsPerDay() utworzonego
        /// taktu == variant.runsPerDay (last = first + (runs-1) × interval).
        /// </summary>
        public static FrequencySpec BuildFrequency(in FrequencyVariant variant, int firstRunMinutes)
        {
            if (variant.runsPerDay <= 1)
                return FrequencySpec.SingleRun(firstRunMinutes);

            int interval = UnityEngine.Mathf.Max(1, variant.taktMinutes);
            int last = firstRunMinutes + (variant.runsPerDay - 1) * interval;
            return FrequencySpec.Takt(interval, firstRunMinutes, last);
        }

        // ── Tworzenie (scena: wymaga TimetableInitializer.IsReady) ──

        /// <summary>
        /// Tworzy Route + Timetable dla zaakceptowanej propozycji. Null gdy graf niegotowy /
        /// brak ścieżki (caller pokazuje błąd zamiast tworzyć połamany rozkład).
        /// </summary>
        public static Timetable Create(RailwayStation from, RailwayStation to,
            VehicleCandidate candidate, in FrequencyVariant variant, RelationArchetype archetype)
        {
            var init = TimetableInitializer.Instance;
            if (init == null || !init.IsReady || from == null || to == null || candidate == null)
            {
                Log.Warn("[TimetableFromRelation] Create odrzucone — graf/parametry niegotowe");
                return null;
            }
            if (from.pathNodeId < 0 || to.pathNodeId < 0) return null;

            var path = RailwayPathfinder.FindPath(init.Graph, from.pathNodeId, to.pathNodeId);
            if (!path.success || path.nodeIds == null || path.nodeIds.Count < 2)
            {
                Log.Warn($"[TimetableFromRelation] Brak ścieżki {from.name} → {to.name}");
                return null;
            }

            // Route — jak kreator: nodeIds z pathfindingu + stacje końcowe.
            var route = new Route
            {
                name = $"{from.name} → {to.name}",
                nodeIds = new System.Collections.Generic.List<int>(path.nodeIds),
                totalLengthM = path.totalLengthM
            };
            route.stations.Add(MakeRouteStation(from, 0f));
            route.stations.Add(MakeRouteStation(to, path.totalLengthM));
            if (init.Resolver != null)
            {
                route.startVoivodeship = from.voivodeship;
                route.crossesVoivodeshipBorder =
                    !string.IsNullOrEmpty(from.voivodeship) && from.voivodeship != to.voivodeship;
            }
            TimetableService.AddRoute(route);

            string categoryId = MapArchetypeToCategoryId(archetype);
            var category = TimetableService.GetCommercialCategory(categoryId);
            if (category == null)
            {
                Log.Warn($"[TimetableFromRelation] Brak kategorii '{categoryId}' — rozkład bez stops, przerwane");
                return null;
            }

            bool isDmu = candidate.type == FleetVehicleType.DMU;
            var composition = new PlannedComposition
            {
                mode = CompositionMode.MultipleUnit,
                maxSpeedKmh = candidate.maxSpeedKmh,
                brakeRegime = BrakeRegime.R,
                symbolicNotation = isDmu ? "SZT" : "EZT"
            };

            var stops = TimetableBuilder.BuildStops(route, init.Graph, category,
                CompositionMode.MultipleUnit, candidate.maxSpeedKmh, FirstRunMinutesFromMidnight);

            var tt = new Timetable
            {
                name = $"{from.name}→{to.name} ({category.shortCode})",
                routeId = route.id,
                commercialCategoryId = category.id,
                stops = stops,
                frequency = BuildFrequency(variant, FirstRunMinutesFromMidnight),
                calendar = DayMask.Daily(),
                composition = composition,
                irjCategory = new IrjCategory(MapArchetypeToIrjGroup(archetype),
                    isDmu ? TractionLetter.DieselUnit : TractionLetter.ElectricUnit),
                trainNumber = "", // numer nadaje gracz w kreatorze (TrainNumberValidator wymaga kontekstu)
                notes = "Utworzony przez asystenta (planner relacji)"
            };
            TimetableService.AddTimetable(tt);

            Log.Info($"[TimetableFromRelation] Utworzono rozkład #{tt.id} '{tt.name}' "
                     + $"({variant.runsPerDay} kursów/d, takt {variant.taktMinutes} min, pojazd sugerowany: {candidate.label})");
            return tt;
        }

        static RouteStation MakeRouteStation(RailwayStation s, float distanceFromStartM) => new RouteStation
        {
            stationNodeId = s.pathNodeId,
            stationName = s.name,
            distanceFromStartM = distanceFromStartM,
            position = s.position,
            isMajorStation = s.isMajorStation,
            voivodeship = s.voivodeship,
            cityName = s.cityName
        };
    }
}
