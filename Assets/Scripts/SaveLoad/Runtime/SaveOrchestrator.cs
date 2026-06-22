using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: Centralna koordynacja Save/Load.
    ///
    /// Pipeline Save:
    ///   1. Iteruj po SaveRegistry.All
    ///   2. Per moduł: <see cref="ISavable.Serialize"/> → JObject
    ///   3. Bundle.AddModule + Manifest.ModuleVersions
    ///   4. ComputeHmac na końcu (po wszystkich modułach)
    ///   5. Storage.SaveAsync
    ///
    /// Pipeline Load:
    ///   1. Storage.LoadAsync → bundle (lub null = NotFound)
    ///   2. Verify HMAC (mismatch = ModifiedSave)
    ///   3. Check GameVersion (newer = NewerVersion)
    ///   4. Per moduł: znajdź w Registry → migrator chain → Deserialize
    ///   5. Per-module exception → InitializeDefault + add to FailedModules
    ///   6. Wraca Success / PartialLoad
    ///
    /// SaveAsync/LoadAsync są async — używają Task-based API. Unity
    /// współpracuje przez await (Unity 2018+ obsługuje async/await
    /// na main thread).
    /// </summary>
    public class SaveOrchestrator
    {
        private readonly ISaveStorage _storage;
        private readonly string _gameVersion;

        // Gate serializujący SaveAsync/LoadAsync na poziomie orchestratora. Bez tego
        // dwie operacje (np. F5 QuickSave + auto-save tick + manualny save z UI)
        // mogłyby równocześnie iterować Serialize() per moduł, a moduły deserializują
        // **statyczne** kolekcje (PersonnelService.Employees, FleetService.OwnedVehicles,
        // TimetableService.Timetables). Współbieżność z game tickiem (payroll daily,
        // OnDayEnded, hire) → InvalidOperationException("Collection was modified") w
        // środku Serialize → cały save abortowany.
        //
        // Używamy `WaitAsync(timeout).ConfigureAwait(false)`:
        // - timeout = 30s, longer = bug ⇒ failed save zamiast hang UI (nie blokujemy
        //   main thread synchronicznie, bo to powodowałoby UI freeze).
        // - ConfigureAwait(false) — continuation zostaje na thread pool. Storage I/O
        //   nie potrzebuje main thread; przekierowanie continuation back na main
        //   przez UnitySynchronizationContext byłoby niepotrzebnym kosztem.
        // - Tradeoff: gdy gate był zajęty, post-await kontynuacja jest na thread pool.
        //   Wtedy Serialize() per moduł wywołane na thread pool → Unity API
        //   (FindAnyObjectByType etc.) rzuci `UnityException: can only be called from
        //   main thread` → moduł leci do failedModules → SaveAsync abortuje cały save
        //   (linia ~110). To **graceful failure** z czytelnym logiem, nie silent
        //   corruption. W praktyce ta ścieżka jest rzadka (typowy gracz nie F5-uje w
        //   trakcie auto-save).
        private readonly SemaphoreSlim _opGate = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan GateAcquireTimeout = TimeSpan.FromSeconds(30);

        public SaveOrchestrator(ISaveStorage storage, string gameVersion = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _gameVersion = gameVersion ?? Application.version ?? "0.0.0-unknown";
        }

        public ISaveStorage Storage => _storage;

        // ── SAVE ──────────────────────────────────────

        /// <summary>Zapisuje aktualny state wszystkich zarejestrowanych modułów do slot'a.
        /// Wraca true na success, false na storage failure albo błąd serializacji modułu.</summary>
        public async Task<bool> SaveAsync(string slotId, string slotName,
                                          string saveType = SaveTypes.Manual,
                                          double playtime = 0,
                                          string gameTimeIso = "")
        {
            if (string.IsNullOrEmpty(slotId))
            {
                Log.Warn("[SaveOrchestrator] SaveAsync: slotId empty");
                return false;
            }

            if (!await _opGate.WaitAsync(GateAcquireTimeout).ConfigureAwait(false))
            {
                Log.Warn($"[SaveOrchestrator] SaveAsync('{slotId}') gate timeout {GateAcquireTimeout.TotalSeconds}s — " +
                         $"concurrent operation in progress. Aborting save.");
                return false;
            }
            try
            {
                return await SaveAsyncInner(slotId, slotName, saveType, playtime, gameTimeIso).ConfigureAwait(false);
            }
            finally
            {
                _opGate.Release();
            }
        }

        private async Task<bool> SaveAsyncInner(string slotId, string slotName,
                                                string saveType, double playtime, string gameTimeIso)
        {
            var bundle = new SaveBundle
            {
                Manifest = new SaveManifest
                {
                    GameVersion = _gameVersion,
                    BundleSchemaVersion = 1,
                    Playtime = playtime,
                    GameTimeIso = gameTimeIso,
                    SavedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    SaveType = saveType,
                    SlotName = slotName ?? ""
                }
            };

            int succeeded = 0, failed = 0;
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            var perModuleMs = new System.Text.StringBuilder();
            var failedModules = new List<string>();
            foreach (var module in SaveRegistry.All)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var json = module.Serialize();
                    sw.Stop();
                    perModuleMs.Append($" {module.ModuleId}={sw.ElapsedMilliseconds}ms");
                    if (json == null)
                    {
                        Log.Warn($"[SaveOrchestrator] Module '{module.ModuleId}' Serialize() returned null — aborting save");
                        failed++;
                        failedModules.Add(module.ModuleId);
                        continue;
                    }
                    bundle.AddModule(module.ModuleId, module.SchemaVersion, json);
                    succeeded++;
                }
                catch (Exception e)
                {
                    sw.Stop();
                    perModuleMs.Append($" {module.ModuleId}=THREW({sw.ElapsedMilliseconds}ms)");
                    Log.Error($"[SaveOrchestrator] Module '{module.ModuleId}' Serialize threw: {e.Message}");
                    failed++;
                    failedModules.Add(module.ModuleId);
                }
            }
            long serializeMs = totalSw.ElapsedMilliseconds;

            if (failed > 0)
            {
                totalSw.Stop();
                Log.Error($"[SaveOrchestrator] SaveAsync('{slotId}') aborted: " +
                          $"{failed} module(s) failed during Serialize: {string.Join(", ", failedModules)}. " +
                          $"Existing slot was not overwritten. | total={totalSw.ElapsedMilliseconds}ms " +
                          $"(serialize={serializeMs}ms) |{perModuleMs}");
                return false;
            }

            // HMAC po wszystkich AddModule.
            // TD-017: ComputeHmac serializuje bundle do kanonicznych bajtów (czysta funkcja, zero
            // Unity API) — offload na worker thread żeby zdjąć alloc/CPU burst z main. Bundle jest
            // już niemutowalny (moduły zserializowane), główny wątek czeka na await → brak race.
            var hmacSw = System.Diagnostics.Stopwatch.StartNew();
            bundle.Manifest.Hmac = await Task.Run(() => HmacService.ComputeHmac(bundle)).ConfigureAwait(false);
            long hmacMs = hmacSw.ElapsedMilliseconds;

            var storageSw = System.Diagnostics.Stopwatch.StartNew();
            // ConfigureAwait(false) — continuation zostaje na thread pool zamiast wracać
            // przez UnitySynchronizationContext na main thread. Storage I/O już skończone,
            // dalej tylko logowanie i return — main thread niepotrzebny.
            bool stored = await _storage.SaveAsync(slotId, bundle).ConfigureAwait(false);
            long storageMs = storageSw.ElapsedMilliseconds;

            totalSw.Stop();
            Log.Info($"[SaveOrchestrator] SaveAsync('{slotId}'): " +
                     $"{succeeded} modules OK, {failed} failed, storage={stored} " +
                     $"| total={totalSw.ElapsedMilliseconds}ms " +
                     $"(serialize={serializeMs}ms, hmac={hmacMs}ms, storage={storageMs}ms) " +
                     $"|{perModuleMs}");
            return stored;
        }

        // ── LOAD ──────────────────────────────────────

        /// <summary>Wczytuje slot, weryfikuje HMAC, restore'uje wszystkie moduły.
        /// Per-module exception → InitializeDefault + dodaje do FailedModules
        /// (graceful degradation). Wraca <see cref="LoadResult"/> z metadanymi.</summary>
        public async Task<LoadResult> LoadAsync(string slotId, bool ignoreHmac = false)
        {
            if (string.IsNullOrEmpty(slotId))
                return LoadResult.NotFound();

            // LoadAsync: BEZ ConfigureAwait(false) — captured context (Unity main thread)
            // jest potrzebny dla `module.Deserialize()` które używa Unity API. Quit-time
            // nie load'uje, więc nie ma deadlock concern dla tej ścieżki.
            if (!await _opGate.WaitAsync(GateAcquireTimeout))
            {
                Log.Warn($"[SaveOrchestrator] LoadAsync('{slotId}') gate timeout {GateAcquireTimeout.TotalSeconds}s — " +
                         $"concurrent operation in progress.");
                return LoadResult.Failed("Concurrent operation timeout");
            }
            try
            {
                return await LoadAsyncInner(slotId, ignoreHmac);
            }
            finally
            {
                _opGate.Release();
            }
        }

        private async Task<LoadResult> LoadAsyncInner(string slotId, bool ignoreHmac)
        {
            SaveBundle bundle;
            try
            {
                // BEZ ConfigureAwait(false): po await wywołujemy module.Deserialize()
                // które używa Unity API (Object.FindAnyObjectByType etc.) — wymaga main thread.
                bundle = await _storage.LoadAsync(slotId);
            }
            catch (Exception e)
            {
                Log.Error($"[SaveOrchestrator] LoadAsync storage failed: {e.Message}");
                return LoadResult.Failed(e.Message);
            }

            if (bundle == null) return LoadResult.NotFound();

            // HMAC weryfikacja (3-stanowa). LegacyMatch → po Deserialize zrobimy re-sign
            // żeby kolejne loady nie warning'owały (#9 fix 2026-05-15: wcześniej Verify
            // logował "Save will be re-signed on next write" ale nigdzie nie re-signował).
            HmacVerifyResult hmacResult = HmacVerifyResult.Match;
            if (!ignoreHmac)
            {
                hmacResult = HmacService.VerifyDetailed(bundle);
                if (hmacResult == HmacVerifyResult.Mismatch)
                {
                    Log.Warn($"[SaveOrchestrator] HMAC mismatch for '{slotId}' — modified save");
                    return LoadResult.ModifiedSave();
                }
                if (hmacResult == HmacVerifyResult.LegacyMatch)
                {
                    Log.Info($"[SaveOrchestrator] Legacy HMAC matched for '{slotId}' — będzie re-signed po Deserialize.");
                }
            }

            // Wersja gry — load tylko jeśli save z naszej wersji lub starszej
            if (IsVersionNewer(bundle.Manifest.GameVersion, _gameVersion))
            {
                Log.Warn($"[SaveOrchestrator] Save '{slotId}' from newer version " +
                         $"({bundle.Manifest.GameVersion} > {_gameVersion})");
                return LoadResult.NewerVersion(bundle.Manifest.GameVersion);
            }

            // Per-module restore
            var failedModules = new List<string>();
            foreach (var module in SaveRegistry.All)
            {
                if (!bundle.TryGetModule(module.ModuleId, out var json, out var sourceVersion))
                {
                    // Moduł nie był w save'cie (np. nowy moduł dodany w nowszej wersji)
                    Log.Info($"[SaveOrchestrator] Module '{module.ModuleId}' not in bundle — InitializeDefault");
                    try { module.InitializeDefault(); }
                    catch (Exception e)
                    {
                        Log.Error($"[SaveOrchestrator] InitializeDefault('{module.ModuleId}') threw: {e.Message}");
                    }
                    continue;
                }

                // M13-12: migrator chain dla starszych save'ów
                if (sourceVersion < module.SchemaVersion)
                {
                    try
                    {
                        json = MigrationRunner.Migrate(module.ModuleId, sourceVersion,
                                                       module.SchemaVersion, json);
                        sourceVersion = module.SchemaVersion;
                    }
                    catch (MigrationGapException mge)
                    {
                        Log.Warn($"[SaveOrchestrator] Module '{module.ModuleId}' migration gap: {mge.Message}. " +
                                 $"InitializeDefault.");
                        try { module.InitializeDefault(); }
                        catch (Exception e) { Log.Error($"[SaveOrchestrator] InitializeDefault threw: {e.Message}"); }
                        failedModules.Add(module.ModuleId);
                        continue;
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"[SaveOrchestrator] Module '{module.ModuleId}' migrator threw: {e.Message}. " +
                                 $"InitializeDefault.");
                        try { module.InitializeDefault(); }
                        catch (Exception e2) { Log.Error($"[SaveOrchestrator] InitializeDefault threw: {e2.Message}"); }
                        failedModules.Add(module.ModuleId);
                        continue;
                    }
                }
                else if (sourceVersion > module.SchemaVersion)
                {
                    Log.Warn($"[SaveOrchestrator] Module '{module.ModuleId}' save version v{sourceVersion} > " +
                             $"current v{module.SchemaVersion} — save z nowszej wersji modułu. " +
                             $"Trying direct deserialize (no downgrade migrators).");
                }

                try
                {
                    module.Deserialize(json, sourceVersion);
                }
                catch (Exception e)
                {
                    Log.Warn($"[SaveOrchestrator] Module '{module.ModuleId}' Deserialize failed: " +
                             $"{e.GetType().Name}: {e.Message}. InitializeDefault.");
                    try { module.InitializeDefault(); }
                    catch (Exception e2)
                    {
                        Log.Error($"[SaveOrchestrator] InitializeDefault('{module.ModuleId}') after fail also threw: {e2.Message}");
                    }
                    failedModules.Add(module.ModuleId);
                }
            }

            // Crash-hunt #1A: napraw dangling cross-module referencje (pojazd↔obieg) PO wszystkich
            // modułach (order-independent). Zwł. po PartialLoad jeden moduł mógł paść i zostawić
            // niespójność (vehicle → znikły obieg, obieg → vehicleId spoza floty) → cichy zły stan.
            try { RailwayManager.Timetable.CirculationService.RepairDanglingReferences(); }
            catch (Exception e) { Log.Warn($"[SaveOrchestrator] RepairDanglingReferences threw: {e.Message}"); }

            // #9 fix: re-sign legacy HMAC po successful deserialize. Nie robimy gdy są
            // failedModules (state może być uszkodzony — nie utrwalajmy złego HMAC'a).
            // Nie robimy też gdy ignoreHmac=true (gracz akceptował modified save, my nie
            // wiemy czy stan jest poprawny; user musi wykonać manual save żeby utrwalić).
            if (hmacResult == HmacVerifyResult.LegacyMatch && failedModules.Count == 0)
            {
                bundle.Manifest.Hmac = HmacService.ComputeHmac(bundle);
                Log.Info($"[SaveOrchestrator] Re-signing '{slotId}' z aktualnym HMAC algorithm (auto-upgrade).");
                // Fire-and-forget — gate IO chroni przed race, błąd tylko w log (nie blokujemy load).
                _ = ResignLegacyAsync(slotId, bundle);
            }

            if (failedModules.Count == 0)
            {
                Log.Info($"[SaveOrchestrator] LoadAsync('{slotId}'): all {SaveRegistry.Count} modules OK");
                return LoadResult.Success();
            }
            else
            {
                Log.Warn($"[SaveOrchestrator] LoadAsync('{slotId}'): {failedModules.Count} modules failed: " +
                         string.Join(", ", failedModules));
                return LoadResult.PartialLoad(failedModules);
            }
        }

        private async Task ResignLegacyAsync(string slotId, SaveBundle bundle)
        {
            try
            {
                bool ok = await _storage.SaveAsync(slotId, bundle).ConfigureAwait(false);
                if (!ok) Log.Warn($"[SaveOrchestrator] ResignLegacy '{slotId}' storage failed");
            }
            catch (Exception e)
            {
                Log.Warn($"[SaveOrchestrator] ResignLegacy '{slotId}' threw: {e.Message}");
            }
        }

        /// <summary>Comparator: czy wersja A jest nowsza od B. Format "1.2.3" / "0.13.0-alpha".
        /// Compare numerycznie pierwsze 3 segmenty (major.minor.patch), suffix ignorowany.</summary>
        private static bool IsVersionNewer(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            int[] av = ParseVersion(a);
            int[] bv = ParseVersion(b);
            for (int i = 0; i < 3; i++)
            {
                if (av[i] > bv[i]) return true;
                if (av[i] < bv[i]) return false;
            }
            return false;
        }

        private static int[] ParseVersion(string v)
        {
            var result = new int[3];
            int dashIdx = v.IndexOf('-');
            if (dashIdx > 0) v = v.Substring(0, dashIdx);
            string[] parts = v.Split('.');
            for (int i = 0; i < 3 && i < parts.Length; i++)
                int.TryParse(parts[i], out result[i]);
            return result;
        }
    }
}
