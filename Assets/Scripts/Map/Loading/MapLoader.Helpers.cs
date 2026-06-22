using System.Collections.Generic;
using System.IO;
using formap;

namespace MapSystem
{
    public partial class MapLoader
    {
        // ═══════════════════════════════════════════
        //  STATIC HELPERS — geometry clear, layer classification, skip-bytes
        // ═══════════════════════════════════════════

        /// <summary>
        /// Free geometry data po wygenerowaniu meshów — Unity Mesh ma własną kopię.
        /// BUT keep dla layers needed by gameplay logic:
        /// - Railways: RailwayGraph topology + pathfinding
        /// - AdminBoundaries: VoivodeshipResolver PIP test
        /// - Places: PlaceLoader (positions of cities)
        /// - POIs: StationLoader (positions of stations) + LoadSignals (Metadata railway/signal tags)
        /// - Platforms: PlatformLoader (centroid calc)
        /// </summary>
        private static void ClearNonLogicLayerGeometry(TileData tileData)
        {
            if (tileData.Layers == null) return;

            List<BinaryFormat.LayerType> nonLogicKeys = null;
            foreach (var kvp in tileData.Layers)
            {
                if (IsLogicLayer(kvp.Key)) continue;
                // Clear contents (defensive in case ktoś trzyma reference)
                foreach (var geom in kvp.Value)
                    ClearMeshGeometryFully(geom);
                nonLogicKeys ??= new List<BinaryFormat.LayerType>();
                nonLogicKeys.Add(kvp.Key);
            }

            // Usuń non-logic layer entries z dict — MeshGeometry shells (object overhead ~100B
            // × 17M Buildings = ~1.7 GB sama metadata GC) trafią do GC po out-of-scope.
            if (nonLogicKeys != null)
                foreach (var key in nonLogicKeys)
                    tileData.Layers.Remove(key);
        }

        /// <summary>
        /// Pełne wyczyszczenie wszystkich kolekcji w MeshGeometry — vertices, indices,
        /// HoleStarts, SegmentIds, JunctionIndices, **Metadata (Dictionary&lt;string,string&gt;)**.
        /// Metadata jest największym zjadaczem RAM dla Buildings (17M features × ~5-10 OSM tagów
        /// × ~50B per pair = 5-10 GB). Bez tego clear non-logic layer Buildings/Forests/Highways
        /// trzymają cały słownik tagów po RenderTile.
        /// </summary>
        private static void ClearMeshGeometryFully(MeshGeometry geom)
        {
            if (geom == null) return;
            geom.Vertices?.Clear();
            geom.Vertices?.TrimExcess();
            geom.Indices?.Clear();
            geom.Indices?.TrimExcess();
            geom.HoleStarts?.Clear();
            geom.HoleStarts?.TrimExcess();
            geom.SegmentIds?.Clear();
            geom.SegmentIds?.TrimExcess();
            geom.JunctionIndices?.Clear();
            geom.JunctionIndices?.TrimExcess();
            geom.Metadata?.Clear();  // KLUCZOWE — Buildings tagi to gros RAM
        }

        /// <summary>Warstwy których geometria jest potrzebna poza renderingiem (logika gry).</summary>
        private static bool IsLogicLayer(BinaryFormat.LayerType type)
        {
            return type == BinaryFormat.LayerType.Railways
                || type == BinaryFormat.LayerType.AdminBoundaries
                || type == BinaryFormat.LayerType.Places
                || type == BinaryFormat.LayerType.POIs
                || type == BinaryFormat.LayerType.Platforms;
        }

        // v8 (formap §8 rm-v8-integration): maski warstw do whole-block skip — gdy
        // (tileLayerMask & wantedMask) == 0, blok nie ma żadnej chcianej warstwy → pomijamy
        // File.OpenRead + LZ4 decompress + decode. Liczone Z predykatów (BuildLayerMask) → zero driftu
        // z IsLogicLayer/IsPreviewRenderLayer. Correctness: stored LayerMask == recomputed (formap --verify-logic).
        private static readonly int LogicLayerMask = BuildLayerMask(IsLogicLayer);
        private static readonly int PreviewRenderLayerMask = BuildLayerMask(IsPreviewRenderLayer);

        private static int BuildLayerMask(System.Func<BinaryFormat.LayerType, bool> predicate)
        {
            int mask = 0;
            for (int i = 0; i < BinaryFormat.LayerCount; i++)
                if (predicate((BinaryFormat.LayerType)i)) mask |= 1 << i;
            return mask;
        }

        /// <summary>
        /// Stream-seek skip pojedynczego MeshGeometry feature bez parsowania (no allocation).
        /// Format zgodny z <see cref="MeshGeometry.WriteBody"/>: bbox(16B) + vertices/indices/holes/
        /// segIds/junctions/metadata. Używane w extraction mode dla non-logic layers (Buildings/
        /// Forests/Highways) — 10-50× szybsze niż Read+discard.
        ///
        /// Wszystkie count'y mają sanity check — corrupt bytes mogłyby dać ogromne/ujemne wartości
        /// powodując infinite loop lub negative seek. Throw → catch w LoadTile → log + skip tile.
        /// </summary>
        private static void SkipFeatureBytes(BinaryReader r)
        {
            const int MaxCountSanity = 1_000_000;
            const int MaxStringLen = 1024 * 1024; // 1 MB string max
            var s = r.BaseStream;

            s.Seek(16, SeekOrigin.Current);                  // BBox: 4 floats
            int vertexCount = r.ReadInt32();
            ValidateCount(vertexCount, MaxCountSanity, "vertexCount");
            s.Seek(vertexCount * 8L, SeekOrigin.Current);    // Vector2 × n

            int indexCount = r.ReadInt32();
            ValidateCount(indexCount, MaxCountSanity, "indexCount");
            s.Seek(indexCount * 4L, SeekOrigin.Current);

            int holeCount = r.ReadInt32();
            ValidateCount(holeCount, MaxCountSanity, "holeCount");
            s.Seek(holeCount * 4L, SeekOrigin.Current);

            int segIdCount = r.ReadInt32();
            ValidateCount(segIdCount, MaxCountSanity, "segIdCount");
            s.Seek(segIdCount * 4L, SeekOrigin.Current);

            int juncCount = r.ReadInt32();
            ValidateCount(juncCount, MaxCountSanity, "juncCount");
            s.Seek(juncCount * 4L, SeekOrigin.Current);

            int metadataCount = r.ReadInt32();
            ValidateCount(metadataCount, 10_000, "metadataCount"); // realistycznie <100 tagów per feature
            for (int i = 0; i < metadataCount; i++)
            {
                int keyLength = r.ReadInt32();
                ValidateCount(keyLength, MaxStringLen, "keyLength");
                s.Seek(keyLength, SeekOrigin.Current);
                int valueLength = r.ReadInt32();
                ValidateCount(valueLength, MaxStringLen, "valueLength");
                s.Seek(valueLength, SeekOrigin.Current);
            }
        }

        private static void ValidateCount(int value, int max, string fieldName)
        {
            if (value < 0 || value > max)
                throw new System.IO.InvalidDataException(
                    $"SkipFeatureBytes: invalid {fieldName}={value} (max={max}) — corrupt tile data");
        }
    }
}
