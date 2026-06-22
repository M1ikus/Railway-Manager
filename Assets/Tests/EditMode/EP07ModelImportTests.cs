using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Models: kontrakt importu testowego modelu EP07 (Pociąg.fbx).
    /// Waliduje że dostarczony model spełnia wymagania silnika: istnieje, ma geometrię,
    /// importuje się w METRACH (nie cm/mm), oraz raportuje części + materiały + tri-budget.
    /// Headless (EditMode + AssetDatabase). Wzorzec do walidacji KAŻDEGO modelu z M-Models.
    /// Ładowanie po GUID (z Pociąg.fbx.meta) — pewniejsze niż ścieżka z polskim "ą".
    /// </summary>
    public class EP07ModelImportTests
    {
        const string Guid = "f0623020697a233489fdd3cb416916a6";

        // Realny EP07 (PKP) — pasmo sanity dla skali (m).
        const float RealLengthM = 15.916f;

        static GameObject LoadModelAsset()
        {
            string path = AssetDatabase.GUIDToAssetPath(Guid);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>Łączny AABB w world-space liczony z sharedMesh.bounds + localToWorld (działa w EditMode).</summary>
        static bool TryComputeWorldBounds(GameObject root, out Bounds bounds, out int totalTris, out int meshParts)
        {
            bounds = default;
            totalTris = 0;
            meshParts = 0;
            bool has = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                meshParts++;
                totalTris += mesh.triangles.Length / 3;
                var ltw = mf.transform.localToWorldMatrix;
                Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + new Vector3(
                        (i & 1) == 0 ? -e.x : e.x,
                        (i & 2) == 0 ? -e.y : e.y,
                        (i & 4) == 0 ? -e.z : e.z);
                    var w = ltw.MultiplyPoint3x4(corner);
                    if (!has) { bounds = new Bounds(w, Vector3.zero); has = true; }
                    else bounds.Encapsulate(w);
                }
            }
            return has;
        }

        [Test]
        public void Ep07_AssetExists()
        {
            Assert.That(LoadModelAsset(), Is.Not.Null,
                $"FBX o GUID {Guid} nie znaleziony/zaimportowany w projekcie.");
        }

        [Test]
        public void Ep07_HasRenderableGeometry()
        {
            var model = LoadModelAsset();
            Assert.That(model, Is.Not.Null);
            var go = Object.Instantiate(model);
            try
            {
                bool ok = TryComputeWorldBounds(go, out _, out int tris, out int parts);
                Assert.That(ok, Is.True, "Brak jakiegokolwiek mesha w modelu.");
                Assert.That(tris, Is.GreaterThan(0), "Model ma 0 trójkątów.");
                TestContext.WriteLine($"[EP07] meshParts={parts}, trójkąty={tris} " +
                                      $"({(tris < 8000 ? "lowpoly" : tris < 30000 ? "mid" : "high")})");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Ep07_ImportsAtMeterScale()
        {
            var model = LoadModelAsset();
            Assert.That(model, Is.Not.Null);
            var go = Object.Instantiate(model);
            try
            {
                Assert.That(TryComputeWorldBounds(go, out var b, out _, out _), Is.True);
                Vector3 s = b.size;
                float longest = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                TestContext.WriteLine(
                    $"[EP07] WYMIARY (m): X={s.x:F2} Y={s.y:F2} Z={s.z:F2} | najdłuższy={longest:F2} " +
                    $"| real EP07 dł≈{RealLengthM} → ~{longest / RealLengthM:F2}× real");
                Assert.That(longest, Is.InRange(5f, 25f),
                    $"Najdłuższy bok {longest:F2} m poza pasmem 5–25 m → zła skala importu " +
                    "(model w cm/mm? popraw globalScale lub re-export w metrach).");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Ep07_ReportPartsAndMaterials()
        {
            var model = LoadModelAsset();
            Assert.That(model, Is.Not.Null);
            var go = Object.Instantiate(model);
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[EP07] CZĘŚCI z meshem/rendererem:");
                int total = 0, missing = 0;
                var matNames = new HashSet<string>();
                foreach (var t in go.GetComponentsInChildren<Transform>())
                {
                    var mf = t.GetComponent<MeshFilter>();
                    int tris = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.triangles.Length / 3 : 0;
                    var mr = t.GetComponent<MeshRenderer>();
                    if (tris == 0 && mr == null) continue;
                    string matLabel = "—";
                    if (mr != null)
                    {
                        foreach (var m in mr.sharedMaterials)
                        {
                            total++;
                            if (m == null || m.name.StartsWith("Default")) missing++;
                            else matNames.Add(m.name);
                        }
                        matLabel = mr.sharedMaterial != null ? mr.sharedMaterial.name : "<brak>";
                    }
                    sb.AppendLine($"  • {t.name}{(tris > 0 ? $" [{tris} tri]" : "")} mat={matLabel}");
                }
                sb.AppendLine($"MATERIAŁY: {total} slotów, {missing} domyślnych/brakujących, {matNames.Count} własnych.");
                if (missing > 0)
                    sb.AppendLine("  → trzeba Extract Materials + przypisać BaseMapy (Pociąg_*_BaseMap.png).");
                TestContext.WriteLine(sb.ToString());

                Assert.That(go.GetComponentsInChildren<MeshFilter>().Length, Is.GreaterThan(0),
                    "Model powinien mieć przynajmniej jeden MeshFilter.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// Read-only recon pod animację: hierarchia (parent chain), offset pivota
        /// (mesh.bounds.center w local space — ~0 = pivot w środku części = czysty obrót),
        /// najcieńsza oś (kandydat na oś obrotu koła). Nie buduje animacji — tylko diagnoza
        /// czy ruchome części (koła/wózki) da się obracać w miejscu czy trzeba poprawić pivoty.
        /// </summary>
        [Test]
        public void Ep07_ReportHierarchyAndPivots()
        {
            var model = LoadModelAsset();
            Assert.That(model, Is.Not.Null);
            var go = Object.Instantiate(model);
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[EP07] HIERARCHIA + PIVOTY:");
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    var t = mf.transform;

                    string path = t.name;
                    for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;

                    Vector3 c = mf.sharedMesh.bounds.center;   // local space (= offset pivota)
                    Vector3 size = mf.sharedMesh.bounds.size;
                    float off = c.magnitude;
                    float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                    bool centered = off < 0.10f * Mathf.Max(maxDim, 0.001f);
                    string thin = (size.x <= size.y && size.x <= size.z) ? "X"
                                : (size.y <= size.x && size.y <= size.z) ? "Y" : "Z";
                    Vector3 lp = t.localPosition;

                    sb.AppendLine($"  • {path}");
                    sb.AppendLine($"      pivot offset={off:F3} m (bounds.center local={c.x:F2},{c.y:F2},{c.z:F2}) " +
                                  $"→ {(centered ? "✓ w środku (czysty obrót)" : "✗ PRZESUNIĘTY (obrót orbituje)")}");
                    sb.AppendLine($"      size={size.x:F2}×{size.y:F2}×{size.z:F2} m | najcieńsza oś={thin} | " +
                                  $"localPos={lp.x:F2},{lp.y:F2},{lp.z:F2}");
                }
                TestContext.WriteLine(sb.ToString());

                Assert.That(go.GetComponentsInChildren<MeshFilter>().Length, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// Read-only world-space recon: lossyScale (ile skali siedzi w transformach + czy uniform —
        /// non-uniform = obrót shearuje), realny world-AABB każdej części (prawdziwe wymiary w metrach),
        /// najcieńsza oś w WORLD space = oś obrotu koła. Definitywnie rozstrzyga skalę i którędy kręcić.
        /// </summary>
        [Test]
        public void Ep07_ReportWorldSpaceDimensions()
        {
            var model = LoadModelAsset();
            Assert.That(model, Is.Not.Null);
            var go = Object.Instantiate(model);
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[EP07] WORLD-SPACE (lossyScale + realne wymiary + oś obrotu):");
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    var t = mf.transform;
                    string path = t.name;
                    for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;

                    Vector3 ls = t.lossyScale;
                    float mx = Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                    bool uniform = mx > 0.0001f
                        && Mathf.Abs(ls.x - ls.y) < 0.02f * mx
                        && Mathf.Abs(ls.y - ls.z) < 0.02f * mx;

                    // world AABB części z 8 narożników mesh.bounds przez localToWorld (uwzględnia rotację+skalę)
                    var ltw = t.localToWorldMatrix;
                    Vector3 c = mf.sharedMesh.bounds.center, e = mf.sharedMesh.bounds.extents;
                    bool has = false; Bounds wb = default;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = c + new Vector3(
                            (i & 1) == 0 ? -e.x : e.x,
                            (i & 2) == 0 ? -e.y : e.y,
                            (i & 4) == 0 ? -e.z : e.z);
                        var w = ltw.MultiplyPoint3x4(corner);
                        if (!has) { wb = new Bounds(w, Vector3.zero); has = true; } else wb.Encapsulate(w);
                    }
                    Vector3 ws = wb.size;
                    string thin = (ws.x <= ws.y && ws.x <= ws.z) ? "X"
                                : (ws.y <= ws.x && ws.y <= ws.z) ? "Y" : "Z";

                    sb.AppendLine($"  • {path}");
                    sb.AppendLine($"      lossyScale={ls.x:F2},{ls.y:F2},{ls.z:F2} " +
                                  $"({(uniform ? "uniform ✓" : "NON-UNIFORM ✗ — obrót shearuje")})");
                    sb.AppendLine($"      world size = {ws.x:F2}×{ws.y:F2}×{ws.z:F2} m | najcieńsza (oś obrotu) = {thin}");
                }
                TestContext.WriteLine(sb.ToString());

                Assert.That(go.GetComponentsInChildren<MeshFilter>().Length, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
