using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DepotSystem
{
    /// <summary>
    /// Centralny lazy-cache dla MonoBehaviour-singletonów w scenie Depot.
    ///
    /// Eliminuje powtarzający się wzorzec
    /// <c>if (X == null) X = FindAnyObjectByType&lt;X&gt;()</c> rozsiany po ~27 plikach
    /// (~80 wywołań). Każdy <see cref="Object.FindAnyObjectByType{T}"/> skanuje całą
    /// scenę — dla Depot z 200+ obiektów to mierzalny koszt powtarzany niepotrzebnie.
    ///
    /// Wzorzec użycia:
    /// <code>
    /// var graph = DepotServices.Get&lt;TrackGraph&gt;();
    /// if (graph == null) return;  // jeszcze nie istnieje na scenie
    /// </code>
    ///
    /// Cache jest auto-invalidowany na każdy scene unload (Depot.unity reload →
    /// stare referencje stałyby się "Missing", więc czyścimy). Manual
    /// <see cref="Invalidate{T}"/> dla edge case'ów (np. gdy konkretny system jest
    /// świadomie zdestroy'owany w runtime i tworzony na nowo).
    ///
    /// Nie używać dla obiektów które są dynamicznie usuwane i tworzone w trakcie
    /// gry (np. spawnowane pojazdy w VehicleController) — pattern jest dla
    /// scenicznych singletonów (TrackGraph/WallBuildingSystem/CatenaryGenerator itp.).
    /// </summary>
    public static class DepotServices
    {
        private static readonly Dictionary<System.Type, Object> _cache = new();
        private static bool _hooksRegistered;

        /// <summary>Lazy lookup z cache. Zwraca null gdy obiekt jeszcze nie istnieje na scenie.</summary>
        public static T Get<T>() where T : Object
        {
            EnsureHooks();
            var key = typeof(T);
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return (T)cached;
            var fresh = Object.FindAnyObjectByType<T>();
            if (fresh != null) _cache[key] = fresh;
            return fresh;
        }

        /// <summary>Wymusza ponowny lookup typu T przy następnym Get (bez czyszczenia całej cache).</summary>
        public static void Invalidate<T>() where T : Object
        {
            _cache.Remove(typeof(T));
        }

        /// <summary>Czyści całą cache. Wywoływane automatycznie na każde scene unload.</summary>
        public static void InvalidateAll()
        {
            _cache.Clear();
        }

        private static void EnsureHooks()
        {
            if (_hooksRegistered) return;
            _hooksRegistered = true;
            SceneManager.sceneUnloaded += _ => InvalidateAll();
        }
    }
}
