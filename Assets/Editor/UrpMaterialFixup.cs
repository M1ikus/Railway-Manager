#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RailwayManager.EditorTools
{
    /// <summary>
    /// M-URP / URP-4 fixup: konwertuje pozostałe materiały Built-in na URP — zarówno pliki .mat,
    /// jak i materiały WBUDOWANE w sceny (czego GUI Render Pipeline Converter nie rusza).
    ///   Standard / Legacy Diffuse / null / error  -> URP/Lit   (color/metallic/smoothness/normal/emission)
    ///   Unlit/Color / Unlit/Texture               -> URP/Unlit (color + base map)
    /// Headless: Unity -batchmode -quit -executeMethod RailwayManager.EditorTools.UrpMaterialFixup.Run
    /// </summary>
    public static class UrpMaterialFixup
    {
        static readonly string[] Scenes =
        {
            "Assets/Scenes/Depot.unity",
            "Assets/Scenes/MapScene.unity",
            "Assets/Scenes/MainMenu.unity",
            "Assets/Scenes/GameCreator.unity",
        };

        [MenuItem("Tools/URP Migration/Fixup remaining materials")]
        public static void Run()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (lit == null || unlit == null) { Debug.LogError("[UrpMaterialFixup] Brak shadera URP/Lit lub URP/Unlit — czy URP aktywny?"); return; }

            // 1. Pliki .mat (Application.dataPath — niezalezne od CWD; FindAssets bywa pusty w batchmode)
            int assetCount = 0;
            var assetsRoot = Application.dataPath.Replace('\\', '/');
            foreach (var raw in System.IO.Directory.GetFiles(assetsRoot, "*.mat", System.IO.SearchOption.AllDirectories))
            {
                var path = "Assets" + raw.Replace('\\', '/').Substring(assetsRoot.Length);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                if (TryConvert(m, lit, unlit)) { EditorUtility.SetDirty(m); assetCount++; Debug.Log($"[UrpMaterialFixup] .mat -> {m.shader.name}: {path}"); }
            }
            AssetDatabase.SaveAssets();

            // 2. Materialy wbudowane w sceny (embedded — nie sa .mat assetami)
            int sceneCount = 0;
            foreach (var sc in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(sc, OpenSceneMode.Single);
                int local = 0;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        bool changed = false;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var m = mats[i];
                            if (m != null && !AssetDatabase.Contains(m) && TryConvert(m, lit, unlit)) { changed = true; local++; }
                        }
                        if (changed) r.sharedMaterials = mats;
                    }
                if (local > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
                sceneCount += local;
                Debug.Log($"[UrpMaterialFixup] {sc}: {local} embedded skonwertowanych");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[UrpMaterialFixup] GOTOWE. .mat={assetCount}, embedded scen={sceneCount}");
        }

        /// <summary>Zwraca true gdy materiał był na shaderze Built-in i został przekonwertowany.</summary>
        static bool TryConvert(Material m, Shader lit, Shader unlit)
        {
            string n = m.shader != null ? m.shader.name : null;
            bool toUnlit = n == "Unlit/Color" || n == "Unlit/Texture";
            bool toLit = n == null || n == "Standard" || n == "Legacy Shaders/Diffuse" || n == "Diffuse" || n == "Hidden/InternalErrorShader";
            if (!toUnlit && !toLit) return false;

            // czytaj property PRZED zmiana shadera
            Color color    = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Texture mainTex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            float gloss    = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.5f;
            Texture bump   = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            Color emis     = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            bool emisOn    = m.IsKeywordEnabled("_EMISSION");

            m.shader = toUnlit ? unlit : lit;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (mainTex != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", mainTex);
            if (toLit)
            {
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", gloss);
                if (bump != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
                if (emisOn) { m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emis); }
            }
            return true;
        }
    }
}
#endif
