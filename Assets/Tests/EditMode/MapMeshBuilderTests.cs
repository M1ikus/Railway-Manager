using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MapSystem;
using formap;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-041: MapMeshBuilder.BuildMesh — weryfikacja windingu fillów. Główna mapa odwraca trójkąty
    /// fillów (żeby były front-facing dla top-down kamery); podgląd musi robić to samo, inaczej fille
    /// (lasy/woda/budynki) znikają przy single-sided materiale. Linie nie są odwracane. Y = height.
    /// </summary>
    public class MapMeshBuilderTests
    {
        private static List<MeshGeometry> OneTriangle()
        {
            var g = new MeshGeometry();
            g.Vertices.Add(new Vector2(0f, 0f));
            g.Vertices.Add(new Vector2(1f, 0f));
            g.Vertices.Add(new Vector2(0f, 1f));
            g.Indices.Add(0); g.Indices.Add(1); g.Indices.Add(2);
            return new List<MeshGeometry> { g };
        }

        [Test]
        public void Fill_NoReverse_KeepsWinding()
        {
            var mesh = MapMeshBuilder.BuildMesh(OneTriangle(), 0f, isLine: false, reverseWinding: false);
            Assert.IsNotNull(mesh);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, mesh.triangles);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Fill_Reverse_SwapsFirstAndThird()
        {
            var mesh = MapMeshBuilder.BuildMesh(OneTriangle(), 0f, isLine: false, reverseWinding: true);
            Assert.IsNotNull(mesh);
            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, mesh.triangles);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Line_IgnoresReverse()
        {
            var mesh = MapMeshBuilder.BuildMesh(OneTriangle(), 0f, isLine: true, reverseWinding: true);
            Assert.IsNotNull(mesh);
            Assert.AreEqual(MeshTopology.Lines, mesh.GetTopology(0));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, mesh.GetIndices(0)); // linie nie odwracane
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Height_MapsToY()
        {
            var mesh = MapMeshBuilder.BuildMesh(OneTriangle(), 5f, isLine: false, reverseWinding: false);
            Assert.IsNotNull(mesh);
            foreach (var v in mesh.vertices) Assert.AreEqual(5f, v.y, 1e-4f);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Empty_ReturnsNull()
        {
            Assert.IsNull(MapMeshBuilder.BuildMesh(null, 0f, false));
            Assert.IsNull(MapMeshBuilder.BuildMesh(new List<MeshGeometry>(), 0f, false));
        }
    }
}
