using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: ISaveStorage impl piszący do lokalnego folderu.
    ///
    /// Lokalizacja: <c>Application.persistentDataPath/Saves/{slotId}.rmsave</c>.
    /// Na Windows: <c>C:\Users\X\AppData\LocalLow\DefaultCompany\RailwayManager\Saves\</c>.
    /// (per-user, app-isolated, Unity standardowa konwencja).
    ///
    /// Atomowy zapis: <c>FileStream</c> z <see cref="FileOptions.WriteThrough"/> →
    /// <see cref="FileStream.FlushAsync()"/> → <see cref="File.Replace"/>. WriteThrough
    /// bypassuje OS write cache, FlushAsync wymusza completion przed Dispose, dzięki czemu
    /// po `SaveAsync` return true bytes są fizycznie na dysku — odporne na BSoD/power loss
    /// w okno auto-save. Bez WriteThrough+FlushAsync bytes lecą do buffer cache i mogą
    /// zniknąć przy crashu w ~kilku sekund po zwrocie. W razie crash w trakcie zapisu
    /// oryginalny plik zostaje nienaruszony (worst case zostaje .tmp do posprzątania).
    ///
    /// ListAsync() iteruje pliki + wczytuje TYLKO manifest (pierwszy fragment gzip)
    /// — nie cały bundle. Dla 100 save'ów lista ładuje się ~100ms zamiast ~10s.
    /// </summary>
    public class LocalDiskStorage : ISaveStorage
    {
        public const string FileExtension = ".rmsave";
        public const string TempExtension = ".rmsave.tmp";

        private readonly string _saveFolder;

        // Gate IO chroniący przed konkurencją Save × List × Load × Delete na tych samych
        // plikach. Bez tego ListAsync mógł czytać .rmsave w trakcie File.Replace →
        // exception → slot pomijany w UI; dwa równoczesne SaveAsync na różne sloty mogły
        // konkurować o write cache. Per-instance — smoke tests używają osobnego folderu
        // (osobny LocalDiskStorage instance) i nie kolidują z prod.
        private readonly SemaphoreSlim _ioGate = new SemaphoreSlim(1, 1);

        public LocalDiskStorage(string subfolder = "Saves")
        {
            _saveFolder = Path.Combine(AppPaths.PersistentRoot, subfolder);
            EnsureFolder();
        }

        public string SaveFolder => _saveFolder;

        private void EnsureFolder()
        {
            if (!Directory.Exists(_saveFolder))
            {
                Directory.CreateDirectory(_saveFolder);
                Log.Info($"[LocalDiskStorage] Created save folder: {_saveFolder}");
            }
        }

        // ── Save ──────────────────────────────────────

        public async Task<bool> SaveAsync(string slotId, SaveBundle bundle)
        {
            if (!IsValidSlotId(slotId))
            {
                Log.Warn($"[LocalDiskStorage] SaveAsync: invalid slotId '{slotId}' " +
                         "(allowed: ^[a-zA-Z0-9_-]{1,64}$, no Windows reserved names).");
                return false;
            }
            if (bundle == null) return false;

            string finalPath = GetFilePath(slotId);
            string tempPath = finalPath + ".tmp";

            await _ioGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // TD-017: Serialize (JObject → JSON → gzip) to czysta funkcja, zero Unity API —
                // offload na worker thread żeby zdjąć alloc/CPU burst z main (bundle niemutowalny).
                byte[] bytes = await Task.Run(() => BundleSerializer.Serialize(bundle)).ConfigureAwait(false);

                // FileOptions.WriteThrough → bypass OS write buffer cache, bytes lecą prosto
                // na dysk. FileOptions.Asynchronous → prawdziwe async I/O zamiast emulacji.
                // FlushAsync na końcu wymusza, żeby wszystkie pending writes faktycznie
                // skończyły się przed zamknięciem stream'a — bez tego po `using { ... }`
                // bytes mogą jeszcze siedzieć w write buffer i zniknąć przy crashu w
                // ~sekund po SaveAsync return true.
                //
                // ConfigureAwait(false): continuation zostaje na thread pool — storage I/O
                // nie potrzebuje main thread, więc unikamy zbędnego przerzucania kontekstu.
                const int bufferSize = 4096;
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                                                   FileShare.None, bufferSize,
                                                   FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }

                // Atomowy rename: jeśli finalPath istnieje, File.Replace zapewnia atomic.
                // Jeśli nie istnieje, używamy File.Move (Replace by failed).
                if (File.Exists(finalPath))
                {
                    File.Replace(tempPath, finalPath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempPath, finalPath);
                }

                Log.Info($"[LocalDiskStorage] Saved '{slotId}' ({bytes.Length} bytes) to {finalPath}");
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[LocalDiskStorage] SaveAsync('{slotId}') failed: {e.GetType().Name}: {e.Message}");
                // Cleanup tempPath jeśli zostało
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore */ }
                return false;
            }
            finally
            {
                _ioGate.Release();
            }
        }

        // ── Load ──────────────────────────────────────

        public async Task<SaveBundle> LoadAsync(string slotId)
        {
            if (!IsValidSlotId(slotId))
            {
                Log.Warn($"[LocalDiskStorage] LoadAsync: invalid slotId '{slotId}'.");
                return null;
            }
            string path = GetFilePath(slotId);

            await _ioGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!File.Exists(path))
                {
                    Log.Warn($"[LocalDiskStorage] LoadAsync: slot '{slotId}' not found at {path}");
                    return null;
                }

                try
                {
                    byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                    var bundle = BundleSerializer.Deserialize(bytes);
                    Log.Info($"[LocalDiskStorage] Loaded '{slotId}' ({bytes.Length} bytes, {bundle.Modules.Count} modules)");
                    return bundle;
                }
                catch (Exception e)
                {
                    Log.Error($"[LocalDiskStorage] LoadAsync('{slotId}') failed: {e.GetType().Name}: {e.Message}");
                    throw;
                }
            }
            finally
            {
                _ioGate.Release();
            }
        }

        // ── Delete ────────────────────────────────────

        public async Task<bool> DeleteAsync(string slotId)
        {
            if (!IsValidSlotId(slotId))
            {
                Log.Warn($"[LocalDiskStorage] DeleteAsync: invalid slotId '{slotId}'.");
                return false;
            }
            string path = GetFilePath(slotId);

            await _ioGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log.Info($"[LocalDiskStorage] Deleted '{slotId}'");
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[LocalDiskStorage] DeleteAsync('{slotId}') failed: {e.Message}");
                return false;
            }
            finally
            {
                _ioGate.Release();
            }
        }

        // ── List ──────────────────────────────────────

        public async Task<List<SaveSlotInfo>> ListAsync()
        {
            var result = new List<SaveSlotInfo>();
            if (!Directory.Exists(_saveFolder)) return result;

            await _ioGate.WaitAsync().ConfigureAwait(false);
            try
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(_saveFolder, "*" + FileExtension);
                }
                catch (Exception e)
                {
                    Log.Error($"[LocalDiskStorage] ListAsync GetFiles failed: {e.Message}");
                    return result;
                }

                // Parallel manifest read — wcześniej sequential foreach z `await ReadManifestOnly`
                // dawał N × ~50-100ms (100 save'ów = 5-10s freeze SaveLoadUI). Z Task.WhenAll
                // dyski SSD/NVMe robią read N w parallel; HDD spinduje raz, ~speedup 2-5×.
                var tasks = new Task<SaveSlotInfo>[files.Length];
                for (int i = 0; i < files.Length; i++)
                {
                    string path = files[i]; // capture
                    tasks[i] = Task.Run(async () =>
                    {
                        try { return await ReadManifestOnly(path); }
                        catch (Exception e)
                        {
                            Log.Warn($"[LocalDiskStorage] Skipping corrupt save '{Path.GetFileName(path)}': {e.Message}");
                            return null;
                        }
                    });
                }

                var infos = await Task.WhenAll(tasks).ConfigureAwait(false);
                foreach (var info in infos)
                    if (info != null) result.Add(info);

                // Sortowanie: najnowsze pierwsze (po SavedAt malejąco)
                result.Sort((a, b) => string.Compare(b.SavedAt, a.SavedAt, StringComparison.Ordinal));
                return result;
            }
            finally
            {
                _ioGate.Release();
            }
        }

        // ExistsAsync poza gate'em IO — single-call File.Exists jest atomic na poziomie OS,
        // a używany m.in. w UI/orchestrator pre-checkach gdzie blokowanie na save'ie byłoby
        // sztuczne (UI tylko sprawdza czy wyświetlić "Continue" w MainMenu). Wynik może być
        // stale o jedną ramkę, co jest akceptowalne dla decyzji UI.
        public Task<bool> ExistsAsync(string slotId)
        {
            if (!IsValidSlotId(slotId)) return Task.FromResult(false);
            return Task.FromResult(File.Exists(GetFilePath(slotId)));
        }

        // ── Helpers ───────────────────────────────────

        // Whitelist regex: alfanumeryczne + _- , 1-64 znaków. Bezpieczne dla wszystkich
        // OS (Windows reserved names CON/PRN/AUX/NUL/COM*/LPT* są też alfanumeryczne, ale
        // są dłuższe niż 3 znaki — i tak filtrujemy je explicit niżej).
        private static readonly Regex SafeSlotIdRegex = new Regex(@"^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);

        // Reserved Windows nazwy (case-insensitive) — File.WriteAllBytes na "CON" itp. rzuca IOException.
        private static readonly HashSet<string> ReservedWindowsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>Walidacja slotId. Whitelist regex `^[a-zA-Z0-9_-]{1,64}$` + reject Windows
        /// reserved names. Aktualnie wszystkie slotId generowane programatycznie są safe
        /// (`save_yyyyMMdd_HHmmss`, `autosave_001`, `quicksave`), ale jeśli kiedykolwiek user
        /// input trafi do slotId (custom save name) → ten guard zapobiega path traversal,
        /// invalid char IO exception, i collision z reserved names.</summary>
        public static bool IsValidSlotId(string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) return false;
            if (!SafeSlotIdRegex.IsMatch(slotId)) return false;
            if (ReservedWindowsNames.Contains(slotId)) return false;
            return true;
        }

        private string GetFilePath(string slotId)
        {
            return Path.Combine(_saveFolder, slotId + FileExtension);
        }

        /// <summary>Czyta tylko manifest z bundle'a — nie parsuje module sections (JObject.Parse
        /// modułów to ~ms × moduł × N save'ów). GzipStream dekompresuje streaming, więc czytamy
        /// z niego TYLKO pierwsze ~kilkaset bajtów (magic + manifest), reszta pliku ignored.
        /// Patrz <see cref="BundleSerializer.DeserializeManifestOnly"/>.</summary>
        private async Task<SaveSlotInfo> ReadManifestOnly(string path)
        {
            byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            var manifest = BundleSerializer.DeserializeManifestOnly(bytes);

            string slotId = Path.GetFileNameWithoutExtension(path);
            return new SaveSlotInfo
            {
                SlotId = slotId,
                FilePath = path,
                SlotName = manifest.SlotName,
                SaveType = manifest.SaveType,
                GameVersion = manifest.GameVersion,
                GameTimeIso = manifest.GameTimeIso,
                SavedAt = manifest.SavedAt,
                Playtime = manifest.Playtime,
                FileSizeBytes = bytes.Length
            };
        }
    }
}
