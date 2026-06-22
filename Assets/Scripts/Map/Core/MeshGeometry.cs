using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RailwayManager.Core;

namespace formap
{
/// <summary>
/// Represents a geometric feature with vertices and indices.
/// For lines: vertices form the polyline, indices are consecutive pairs (0-1, 1-2, ...)
/// For polygons: vertices form the polygon outline, indices form triangles
/// All coordinates are in meters (X, Y) on Z=0 plane
/// </summary>
public class MeshGeometry
{
    public BBox BoundingBox { get; set; }
    public List<Vector2> Vertices { get; set; } = new();
    public List<int> Indices { get; set; } = new();
    public List<int> HoleStarts { get; set; } = new();
    public List<int> SegmentIds { get; set; } = new();
    public List<int> JunctionIndices { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    /// <summary>
    /// Computes bounding box from vertices
    /// </summary>
    public void ComputeBoundingBox()
    {
        if (Vertices.Count == 0)
        {
            BoundingBox = BBox.Empty;
            return;
        }
        
        // Initialize with first vertex
        var first = Vertices[0];
        BoundingBox = new BBox
        {
            MinX = first.x,
            MinY = first.y,
            MaxX = first.x,
            MaxY = first.y
        };
        
        // Expand with remaining vertices
        for (int i = 1; i < Vertices.Count; i++)
        {
            var v = Vertices[i];
            BoundingBox.Expand(v.x, v.y);
        }
    }
    
    /// <summary>
    /// Validates and fixes indices to ensure they're within valid range [0, Vertices.Count-1]
    /// Returns true if indices are valid, false if geometry should be rejected
    /// </summary>
    public bool ValidateAndFixIndices()
    {
        if (Vertices.Count == 0)
        {
            Indices.Clear();
            return false;
        }
        
        int maxIndex = Vertices.Count - 1;
        bool hasInvalidIndices = false;
        
        // Check all indices
        for (int i = 0; i < Indices.Count; i++)
        {
            if (Indices[i] < 0 || Indices[i] > maxIndex)
            {
                hasInvalidIndices = true;
                break;
            }
        }
        
        if (hasInvalidIndices)
        {
            // Remove invalid indices or clamp them (safer to reject)
            // For now, reject geometry with invalid indices
            Indices.Clear();
            return false;
        }
        
        // Validate HoleStarts
        for (int i = 0; i < HoleStarts.Count; i++)
        {
            if (HoleStarts[i] < 0 || HoleStarts[i] > maxIndex)
            {
                // Remove invalid hole starts
                HoleStarts.RemoveAt(i);
                i--;
            }
        }
        
        // Validate JunctionIndices
        for (int i = 0; i < JunctionIndices.Count; i++)
        {
            if (JunctionIndices[i] < 0 || JunctionIndices[i] > maxIndex)
            {
                // Remove invalid junction indices
                JunctionIndices.RemoveAt(i);
                i--;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Writes geometry to binary format
    /// </summary>
    public void Write(BinaryWriter writer)
    {
        // Write bounding box first (for fast culling!)
        writer.Write(BoundingBox.MinX);
        writer.Write(BoundingBox.MinY);
        writer.Write(BoundingBox.MaxX);
        writer.Write(BoundingBox.MaxY);
        
        // Default uncompressed body write (used by some readers)
        WriteBody(writer);
    }

    public void WriteBody(BinaryWriter writer)
    {
        ValidateAndFixIndices();
        // NOTE: Even if Indices.Count == 0, we MUST write complete structure
        // This ensures Read() can always read the format without errors
        
        // Write vertices
        writer.Write(Vertices.Count);
        foreach (var v in Vertices)
        {
            writer.Write(v.x);
            writer.Write(v.y);
        }
        
        // Write indices
        writer.Write(Indices.Count);
        foreach (var idx in Indices)
        {
            writer.Write(idx);
        }
        
        // Write hole starts (for multipolygons with holes)
        writer.Write(HoleStarts.Count);
        foreach (var hs in HoleStarts)
        {
            writer.Write(hs);
        }
        
        // Write segment ids (for railways), empty for others
        writer.Write(SegmentIds.Count);
        foreach (var sid in SegmentIds)
        {
            writer.Write(sid);
        }
        
        // Write railway junction vertex indices (for railways), empty for others
        writer.Write(JunctionIndices.Count);
        foreach (var j in JunctionIndices)
        {
            writer.Write(j);
        }
        
        // Write metadata
        writer.Write(Metadata.Count);
        foreach (var kvp in Metadata)
        {
            WriteString(writer, kvp.Key);
            WriteString(writer, kvp.Value);
        }
    }
    
    /// <summary>
    /// Helper method to check if stream has enough data remaining
    /// </summary>
    private static bool HasEnoughData(BinaryReader reader, long requiredBytes)
    {
        try
        {
            var stream = reader.BaseStream;
            if (!stream.CanSeek || !stream.CanRead)
                return true; // Can't check, assume yes and let exceptions handle it
            
            return stream.Position + requiredBytes <= stream.Length;
        }
        catch
        {
            return true; // Can't check, assume yes and let exceptions handle it
        }
    }
    
    /// <summary>
    /// Reads geometry from binary format
    /// </summary>
    public static MeshGeometry Read(BinaryReader reader)
    {
        var geom = new MeshGeometry();
        
        try
        {
            // Read bounding box first
            geom.BoundingBox = new BBox
            {
                MinX = reader.ReadSingle(),
                MinY = reader.ReadSingle(),
                MaxX = reader.ReadSingle(),
                MaxY = reader.ReadSingle()
            };
            
            // Check if we have enough data remaining (at least one int32 for vertex count)
            if (!HasEnoughData(reader, sizeof(int)))
            {
                Log.Error("[MeshGeometry] Stream ended unexpectedly after reading bounding box");
                return geom; // Return empty geometry
            }
            
            // Read vertices
            int vertexCount = reader.ReadInt32();
            if (!HasEnoughData(reader, vertexCount * 2 * sizeof(float)))
            {
                Log.Error($"[MeshGeometry] Stream too short for {vertexCount} vertices");
                return geom;
            }
            for (int i = 0; i < vertexCount; i++)
            {
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                geom.Vertices.Add(new Vector2(x, y));
            }
            
            // Check if we have enough data for index count
            if (!HasEnoughData(reader, sizeof(int)))
            {
                Log.Error("[MeshGeometry] Stream ended unexpectedly after reading vertices");
                return geom;
            }
            
            // Read indices
            int indexCount = reader.ReadInt32();
            if (!HasEnoughData(reader, indexCount * sizeof(int)))
            {
                Log.Error($"[MeshGeometry] Stream too short for {indexCount} indices");
                return geom;
            }
            for (int i = 0; i < indexCount; i++)
            {
                geom.Indices.Add(reader.ReadInt32());
            }
            
            // Check if we have enough data for hole count
            if (!HasEnoughData(reader, sizeof(int)))
            {
                Log.Error("[MeshGeometry] Stream ended unexpectedly after reading indices");
                return geom;
            }
            
            // Read hole starts
            int holeCount = reader.ReadInt32();
            if (!HasEnoughData(reader, holeCount * sizeof(int)))
            {
                Log.Error($"[MeshGeometry] Stream too short for {holeCount} hole starts");
                return geom;
            }
            for (int i = 0; i < holeCount; i++)
            {
                geom.HoleStarts.Add(reader.ReadInt32());
            }
            
            // Check if we have enough data for segment id count
            if (!HasEnoughData(reader, sizeof(int)))
            {
                Log.Error("[MeshGeometry] Stream ended unexpectedly after reading hole starts");
                return geom;
            }
            
            // Read segment ids
            int sidCount = reader.ReadInt32();
            if (!HasEnoughData(reader, sidCount * sizeof(int)))
            {
                Log.Error($"[MeshGeometry] Stream too short for {sidCount} segment ids");
                return geom;
            }
            for (int i = 0; i < sidCount; i++)
            {
                geom.SegmentIds.Add(reader.ReadInt32());
            }
            
            // Check if we have enough data for junction count
            if (!HasEnoughData(reader, sizeof(int)))
            {
                Log.Error("[MeshGeometry] Stream ended unexpectedly after reading segment ids");
                return geom;
            }
            
            // Read junction indices
            int jCount = reader.ReadInt32();
            if (!HasEnoughData(reader, jCount * sizeof(int)))
            {
                Log.Error($"[MeshGeometry] Stream too short for {jCount} junction indices");
                return geom;
            }
            for (int i = 0; i < jCount; i++)
            {
                geom.JunctionIndices.Add(reader.ReadInt32());
            }
            
            // Check if we have enough data for metadata count
            if (!HasEnoughData(reader, sizeof(int)))
            {
                Log.Error("[MeshGeometry] Stream ended unexpectedly after reading junction indices");
                return geom;
            }
            
            // Read metadata
            int metadataCount = reader.ReadInt32();
            for (int i = 0; i < metadataCount; i++)
            {
                // Check before reading each string
                if (!HasEnoughData(reader, sizeof(int)))
                {
                    Log.Error($"[MeshGeometry] Stream too short for metadata entry {i + 1}/{metadataCount}");
                    break;
                }
                
                string key = ReadString(reader);
                
                if (!HasEnoughData(reader, sizeof(int)))
                {
                    Log.Error($"[MeshGeometry] Stream too short for metadata value after key '{key}'");
                    break;
                }
                
                string value = ReadString(reader);
                geom.Metadata[key] = value;
            }
        }
        catch (EndOfStreamException ex)
        {
            Log.Error($"[MeshGeometry] End of stream reached unexpectedly: {ex.Message}");
        }
        catch (System.IO.IOException ex)
        {
            Log.Error($"[MeshGeometry] IO error while reading geometry: {ex.Message}");
        }
        
        return geom;
    }
    
    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value ?? "");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
    
    private static string ReadString(BinaryReader reader)
    {
        try
        {
            int length = reader.ReadInt32();
            
            // Safety check: reject negative or suspiciously large lengths
            if (length < 0 || length > 1024 * 1024) // Max 1MB per string
            {
                Log.Error($"[MeshGeometry] Invalid string length: {length}");
                return "";
            }
            
            byte[] bytes = reader.ReadBytes(length);
            
            // If we didn't get enough bytes, the stream ended prematurely
            if (bytes.Length < length)
            {
                Log.Error($"[MeshGeometry] Stream too short for string of length {length}, got only {bytes.Length} bytes");
                return System.Text.Encoding.UTF8.GetString(bytes); // Return partial string
            }
            
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (EndOfStreamException ex)
        {
            Log.Error($"[MeshGeometry] End of stream while reading string: {ex.Message}");
            return "";
        }
        catch (System.IO.IOException ex)
        {
            Log.Error($"[MeshGeometry] IO error while reading string: {ex.Message}");
            return "";
        }
    }
}
}