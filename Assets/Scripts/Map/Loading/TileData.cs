using System.Collections.Generic;
using UnityEngine;

namespace formap
{
    /// <summary>
    /// Data structure representing a loaded tile
    /// </summary>
    public class TileData
    {
        public long TileID;
        public int GridX, GridY;
        public BBox Bounds;
        public Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> Layers;
        public GameObject RootObject; // Parent GameObject for all meshes in this tile
        public bool IsLoaded;
        public bool IsVisible;
        public float LastAccessTime; // For LRU cache

        public TileData(long tileID, int gridX, int gridY, BBox bounds)
        {
            TileID = tileID;
            GridX = gridX;
            GridY = gridY;
            Bounds = bounds;
            Layers = new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>();
            IsLoaded = false;
            IsVisible = false;
            LastAccessTime = Time.realtimeSinceStartup;
        }
    }
}
