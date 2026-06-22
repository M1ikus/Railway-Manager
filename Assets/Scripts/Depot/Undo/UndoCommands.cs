using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem.Undo
{
    // ═══════════════════════════════════════════
    //  TORY — Track + Turnout commands
    // ═══════════════════════════════════════════

    /// <summary>
    /// Cofnij postawienie toru = usuń tor.
    /// </summary>
    public class TrackPlacedCommand : IUndoCommand
    {
        private readonly int trackId;
        public TrackPlacedCommand(int trackId) { this.trackId = trackId; }

        public void Undo()
        {
            var builder = DepotServices.Get<PrefabTrackBuilder>();
            builder?.RemoveTrack(trackId);
        }

        public string Description => $"Track placed (id={trackId})";
    }

    /// <summary>
    /// Cofnij usunięcie toru = odtwórz tor z zapisanej polyline.
    /// </summary>
    public class TrackRemovedCommand : IUndoCommand
    {
        private readonly List<Vector3> polyline;
        private readonly string trackName;
        private readonly DepotTrackType trackType;

        public TrackRemovedCommand(List<Vector3> polyline, string trackName, DepotTrackType trackType)
        {
            this.polyline = new List<Vector3>(polyline);
            this.trackName = trackName;
            this.trackType = trackType;
        }

        public void Undo()
        {
            var builder = DepotServices.Get<PrefabTrackBuilder>();
            builder?.PlaceTrackWithPolyline(polyline, trackName, trackType);
        }

        public string Description => $"Track removed ({trackName})";
    }

    /// <summary>
    /// Cofnij postawienie rozjazdu = usuń rozjazd (odtwarza oryginalny tor automatycznie).
    /// </summary>
    public class TurnoutPlacedCommand : IUndoCommand
    {
        private readonly int turnoutId;
        public TurnoutPlacedCommand(int turnoutId) { this.turnoutId = turnoutId; }

        public void Undo()
        {
            var builder = DepotServices.Get<PrefabTrackBuilder>();
            builder?.RemoveTurnout(turnoutId);
        }

        public string Description => $"Turnout placed (id={turnoutId})";
    }

    /// <summary>
    /// Cofnij usunięcie rozjazdu = odtwórz oryginalny chain + postaw rozjazd ponownie.
    /// </summary>
    public class TurnoutRemovedCommand : IUndoCommand
    {
        private readonly List<Vector3> originalChainPolyline;
        private readonly string chainName;
        private readonly DepotTrackType chainType;
        private readonly TurnoutData.TurnoutDefinition definition;
        private readonly bool divergeLeft;
        private readonly bool flipDirection;
        private readonly float distAlongChain;

        public TurnoutRemovedCommand(
            List<Vector3> originalChainPolyline,
            string chainName,
            DepotTrackType chainType,
            TurnoutData.TurnoutDefinition definition,
            bool divergeLeft,
            bool flipDirection,
            float distAlongChain)
        {
            this.originalChainPolyline = new List<Vector3>(originalChainPolyline);
            this.chainName = chainName;
            this.chainType = chainType;
            this.definition = definition;
            this.divergeLeft = divergeLeft;
            this.flipDirection = flipDirection;
            this.distAlongChain = distAlongChain;
        }

        public void Undo()
        {
            var builder = DepotServices.Get<PrefabTrackBuilder>();
            var placer = DepotServices.Get<TurnoutPlacer>();
            if (builder == null || placer == null) return;

            // Odtwórz oryginalny prosty tor (chain)
            var restoredTrack = builder.PlaceTrackWithPolyline(originalChainPolyline, chainName, chainType);
            if (restoredTrack == null) return;

            // Znajdź chain z tego toru i postaw rozjazd ponownie
            var chain = placer.FindStraightChain(restoredTrack);
            if (chain != null)
            {
                // TD-035: krzyżownica wraca jako krzyżownica (PlaceTurnoutOnChain z jej defem
                // postawiłby ZWYKŁY rozjazd). Uwaga: odwrócona kolejność argumentów flip/divergeLeft.
                if (definition.Name == TurnoutData.Crossover_R190.Name)
                    placer.PlaceCrossoverOnChain(chain, distAlongChain, definition, flipDirection, divergeLeft);
                else
                    placer.PlaceTurnoutOnChain(chain, distAlongChain, definition, divergeLeft, flipDirection);
            }
        }

        public string Description => $"Turnout removed ({definition.Name})";
    }

    /// <summary>
    /// TD-010: atomowe cofnięcie całego schematu (głowicy rozjazdowej) — jeden Ctrl+Z usuwa
    /// wszystkie tory + rozjazdy postawione jednym <c>ConfirmPlacement</c>, zamiast N osobnych komend.
    ///
    /// Undo jest <b>diff-based</b>, bo <see cref="PrefabTrackBuilder.RemoveTurnout"/> AUTO-ODTWARZA
    /// skonsumowany chain jako NOWY tor (z nowym id). Naiwne "usuń rozjazdy + usuń tory po id"
    /// zostawiłoby ten odtworzony chain jako osieroconą geometrię (zombie) — to był latentny bug
    /// per-element undo dla schematów z rozjazdami.
    ///
    /// Kroki: (1) snapshot żywych track-id, (2) usuń rozjazdy w odwrotnej kolejności (każdy odtwarza
    /// swój chain = nowe id), (3) usuń chainy odtworzone w kroku 2 (nowe id = różnica vs snapshot,
    /// schema-owned), (4) usuń pozostałe tory schematu (members rozjazdów już zniknęły → RemoveTrack
    /// no-op; standalone tory usunięte). Refundy lecą przez istniejące RemoveTrack/RemoveTurnout
    /// (net-zero, lustro place'a — TD-035).
    /// </summary>
    public class SchemaPlacementCommand : IUndoCommand
    {
        private readonly List<int> trackIds;
        private readonly List<int> turnoutIds;

        public SchemaPlacementCommand(List<int> trackIds, List<int> turnoutIds)
        {
            this.trackIds = trackIds != null ? new List<int>(trackIds) : new List<int>();
            this.turnoutIds = turnoutIds != null ? new List<int>(turnoutIds) : new List<int>();
        }

        public void Undo()
        {
            var builder = DepotServices.Get<PrefabTrackBuilder>();
            if (builder == null) return;

            // 1. snapshot żywych torów PRZED usunięciem rozjazdów
            var preIds = new HashSet<int>();
            foreach (var seg in builder.PlacedTracks)
                if (seg != null) preIds.Add(seg.GraphTrackId);

            // 2. usuń rozjazdy (odwrotna kolejność); każdy RemoveTurnout odtwarza chain jako nowy tor
            for (int i = turnoutIds.Count - 1; i >= 0; i--)
                builder.RemoveTurnout(turnoutIds[i]);

            // 3. usuń chainy odtworzone przez RemoveTurnout (id nieobecne w snapshot = schema-owned)
            var restored = new List<int>();
            foreach (var seg in builder.PlacedTracks)
                if (seg != null && !preIds.Contains(seg.GraphTrackId))
                    restored.Add(seg.GraphTrackId);
            foreach (int id in restored)
                builder.RemoveTrack(id);

            // 4. usuń pozostałe tory schematu (members rozjazdów już usunięte w kroku 2 → no-op)
            foreach (int id in trackIds)
                builder.RemoveTrack(id);
        }

        public string Description => $"Schema placed ({trackIds.Count} tracks, {turnoutIds.Count} turnouts)";
    }

    // ═══════════════════════════════════════════
    //  SIEĆ TRAKCYJNA — Catenary commands
    // ═══════════════════════════════════════════

    /// <summary>
    /// Cofnij toggle sieci trakcyjnej — przywraca HasCatenary na listę torów do poprzedniego stanu.
    /// </summary>
    public class CatenaryToggleCommand : IUndoCommand
    {
        private readonly List<int> trackIds;
        private readonly bool previousState;

        public CatenaryToggleCommand(List<int> trackIds, bool previousState)
        {
            this.trackIds = new List<int>(trackIds);
            this.previousState = previousState;
        }

        public void Undo()
        {
            var graph = DepotServices.Get<TrackGraph>();
            var gen = DepotServices.Get<CatenaryGenerator>();
            if (graph == null) return;

            foreach (int id in trackIds)
                graph.SetTrackCatenary(id, previousState);

            gen?.GenerateNetwork();
        }

        public string Description => $"Catenary toggle ({trackIds.Count} tracks → {previousState})";
    }

    // ═══════════════════════════════════════════
    //  ŚCIEŻKI — Path commands
    // ═══════════════════════════════════════════

    /// <summary>
    /// Cofnij postawienie komórki ścieżki = usuń komórkę.
    /// </summary>
    public class PathCellPlacedCommand : IUndoCommand
    {
        private readonly Vector2Int cell;
        public PathCellPlacedCommand(Vector2Int cell) { this.cell = cell; }

        public void Undo()
        {
            var path = DepotServices.Get<PathBuildStateMachine>();
            path?.UndoRemoveCell(cell);
        }

        public string Description => $"Path cell placed at {cell}";
    }

    /// <summary>
    /// Cofnij usunięcie komórki ścieżki = odtwórz komórkę.
    /// </summary>
    public class PathCellRemovedCommand : IUndoCommand
    {
        private readonly Vector2Int cell;
        private readonly PathBuildSubMode subMode;

        public PathCellRemovedCommand(Vector2Int cell, PathBuildSubMode subMode)
        {
            this.cell = cell;
            this.subMode = subMode;
        }

        public void Undo()
        {
            var path = DepotServices.Get<PathBuildStateMachine>();
            path?.UndoPlaceCell(cell, subMode);
        }

        public string Description => $"Path cell removed at {cell} ({subMode})";
    }

    /// <summary>
    /// Cofnij zbiorczą operację na komórkach (parking placement/demolish).
    /// </summary>
    public class PathCellsBatchCommand : IUndoCommand
    {
        private readonly List<Vector2Int> cells;
        private readonly PathBuildSubMode subMode;
        private readonly bool wasPlaced; // true = cells were placed, Undo = remove them

        public PathCellsBatchCommand(List<Vector2Int> cells, PathBuildSubMode subMode, bool wasPlaced)
        {
            this.cells = new List<Vector2Int>(cells);
            this.subMode = subMode;
            this.wasPlaced = wasPlaced;
        }

        public void Undo()
        {
            var path = DepotServices.Get<PathBuildStateMachine>();
            if (path == null) return;

            if (wasPlaced)
                foreach (var c in cells) path.UndoRemoveCell(c);
            else
                foreach (var c in cells) path.UndoPlaceCell(c, subMode);
        }

        public string Description => $"Path batch {(wasPlaced ? "placed" : "removed")}: {cells.Count} cells";
    }

    /// <summary>
    /// Cofnij postawienie parkingu = usuń parking (visual + edges).
    /// </summary>
    public class ParkingPlacedCommand : IUndoCommand
    {
        private readonly Vector3 cornerA, cornerB;
        private readonly int edgeId;
        private readonly PlacedPathSegment visual;

        public ParkingPlacedCommand(Vector3 a, Vector3 b, int edgeId, PlacedPathSegment visual)
        {
            cornerA = a; cornerB = b;
            this.edgeId = edgeId;
            this.visual = visual;
        }

        public void Undo()
        {
            var path = DepotServices.Get<PathBuildStateMachine>();
            path?.UndoDemolishParking(visual);
        }

        public string Description => $"Parking placed at ({cornerA}, {cornerB})";
    }

    // ═══════════════════════════════════════════
    //  POMIESZCZENIA — Wall/Building commands
    // ═══════════════════════════════════════════

    /// <summary>
    /// Cofnij postawienie budynku = usuń wszystkie jego ściany.
    /// </summary>
    public class BuildingPlacedCommand : IUndoCommand
    {
        private readonly int buildingId;
        public BuildingPlacedCommand(int buildingId) { this.buildingId = buildingId; }

        public void Undo()
        {
            var sys = DepotServices.Get<WallBuildingSystem>();
            sys?.UndoRemoveBuilding(buildingId);
        }

        public string Description => $"Building placed (id={buildingId})";
    }

    /// <summary>
    /// Cofnij usunięcie budynku = odtwórz wszystkie ściany.
    /// </summary>
    public class BuildingRemovedCommand : IUndoCommand
    {
        private readonly List<(Vector3 start, Vector3 end, float height)> walls;

        public BuildingRemovedCommand(List<(Vector3, Vector3, float)> walls)
        {
            this.walls = new List<(Vector3, Vector3, float)>(walls);
        }

        public void Undo()
        {
            var sys = DepotServices.Get<WallBuildingSystem>();
            sys?.UndoCreateBuilding(walls);
        }

        public string Description => $"Building removed ({walls.Count} walls)";
    }

    /// <summary>
    /// Cofnij usunięcie pojedynczej ściany.
    /// </summary>
    public class WallRemovedCommand : IUndoCommand
    {
        private readonly Vector3 start, end;
        private readonly float height;
        private readonly int buildingId;

        public WallRemovedCommand(Vector3 start, Vector3 end, float height, int buildingId)
        {
            this.start = start;
            this.end = end;
            this.height = height;
            this.buildingId = buildingId;
        }

        public void Undo()
        {
            var sys = DepotServices.Get<WallBuildingSystem>();
            sys?.UndoCreateWall(start, end, height, buildingId);
        }

        public string Description => $"Wall removed (building {buildingId})";
    }
}
