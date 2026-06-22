using System;
using RailwayManager.Core;

namespace formap
{
    /// <summary>
    /// Tile grid utilities for spatial partitioning
    /// </summary>
    public static class TileGrid
    {
        public const float TILE_SIZE = 10000f; // 10km x 10km tiles

        /// <summary>
        /// Offset to ensure grid coordinates are always positive for tile ID calculation.
        /// Supports grid coordinates from -100000 to +100000 in both X and Y.
        /// </summary>
        private const long GRID_COORDINATE_OFFSET = 100000L;

        /// <summary>
        /// BUG-077: Maksymalny dozwolony grid coordinate w obie strony (±100000 tiles × 10km
        /// = ±1,000,000 km). Polska mieści się w ~±70 grid units, więc to 1400× zapas.
        /// Cantor pairing JEST iniekcją tylko gdy obie współrzędne są w nieujemnym
        /// zakresie po offset'cie — przekroczenie może dać nieprawidłowy tileID lub kolizję.
        /// </summary>
        private const int MAX_GRID_COORDINATE = 100000;

        /// <summary>
        /// Calculates deterministic tile ID from grid coordinates
        /// Uses Cantor pairing function for unique mapping
        /// </summary>
        public static long GetTileID(int gridX, int gridY)
        {
            // BUG-077: Defensive range check — Cantor pairing requires bounded inputs
            if (gridX < -MAX_GRID_COORDINATE || gridX > MAX_GRID_COORDINATE ||
                gridY < -MAX_GRID_COORDINATE || gridY > MAX_GRID_COORDINATE)
            {
                Log.Warn($"[TileGrid] grid coord poza zakresem ±{MAX_GRID_COORDINATE}: ({gridX},{gridY}) — możliwa kolizja Cantor pairing");
            }

            // Handle negative coordinates by offsetting to positive space
            long x = (long)gridX + GRID_COORDINATE_OFFSET;
            long y = (long)gridY + GRID_COORDINATE_OFFSET;

            // Cantor pairing function: unique bijection from N×N to N
            return (x + y) * (x + y + 1) / 2 + y;
        }

        /// <summary>
        /// Calculates grid coordinates from world position
        /// </summary>
        public static (int gridX, int gridY) WorldToGrid(float worldX, float worldY)
        {
            return (
                (int)Math.Floor(worldX / TILE_SIZE),
                (int)Math.Floor(worldY / TILE_SIZE)
            );
        }

        /// <summary>
        /// Calculates tile bounds from grid coordinates
        /// </summary>
        public static BBox GetTileBounds(int gridX, int gridY)
        {
            float minX = gridX * TILE_SIZE;
            float minY = gridY * TILE_SIZE;

            return new BBox
            {
                MinX = minX,
                MinY = minY,
                MaxX = minX + TILE_SIZE,
                MaxY = minY + TILE_SIZE
            };
        }
    }
}
