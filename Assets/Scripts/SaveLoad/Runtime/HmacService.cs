using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RailwayManager.Core;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: HMAC-SHA256 podpis bundle'a (anti-tamper, best-effort).
    ///
    /// Zasada: secret key hardcoded w build constants → atakujący z dostępem do
    /// binarki może wyekstrahować i forge HMAC. To NIE jest security boundary,
    /// tylko detekcja przypadkowej / casual modyfikacji save'a (np. gracz otworzył
    /// JSON i zmienił `money: 1000000`).
    ///
    /// Sygnatura: HMAC-SHA256(SECRET, concat(file_hashes_sorted_by_name)).
    /// File hash = SHA256 sterowanej-do-stringa JObject'a (ToString deterministyczny).
    ///
    /// Modyfikacja modułu → file hash zmienia → HMAC mismatch → SaveOrchestrator
    /// wraca <see cref="LoadResult.ModifiedSave"/> → SaveLoadUI pyta gracza
    /// "Save zmodyfikowany — load mimo to?"
    /// </summary>
    public static class HmacService
    {
        // Secret key — hardcoded w buildzie. Anti-cheat best-effort, nie security.
        // Post-EA można dodać per-build randomization (CI step generuje key i wpisuje
        // do BuildConstants.cs — różny per release), tu starczy stała.
        private static readonly byte[] SecretKey = Encoding.UTF8.GetBytes(
            "RailwayManager_HMAC_SecretKey_v1_M13_alpha_donottamper");

        /// <summary>
        /// Wylicza HMAC dla bundle'a. Obejmuje manifest bez pola Hmac oraz moduły
        /// posortowane alfabetycznie, żeby kolejność była deterministyczna niezależnie
        /// od insertion order.
        /// </summary>
        public static string ComputeHmac(SaveBundle bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));

            var sortedIds = GetSortedModuleIds(bundle);
            using (var sha = SHA256.Create())
            {
                var aggregate = new MemoryStream();

                var manifestHash = sha.ComputeHash(Encoding.UTF8.GetBytes(CreateManifestPayload(bundle.Manifest)));
                aggregate.Write(manifestHash, 0, manifestHash.Length);

                foreach (var id in sortedIds)
                {
                    var idBytes = Encoding.UTF8.GetBytes(id);
                    aggregate.Write(idBytes, 0, idBytes.Length);

                    var json = bundle.Modules[id];
                    string serialized = json?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}";
                    var fileHash = sha.ComputeHash(Encoding.UTF8.GetBytes(serialized));
                    aggregate.Write(fileHash, 0, fileHash.Length);
                }

                using (var hmac = new HMACSHA256(SecretKey))
                {
                    aggregate.Position = 0;
                    var sig = hmac.ComputeHash(aggregate);
                    return Convert.ToBase64String(sig);
                }
            }
        }

        /// <summary>Weryfikuje czy podpis w manifeście pasuje do aktualnej zawartości.
        /// Wraca true gdy match (nie zmodyfikowany), false gdy mismatch (modified save).
        /// Wrapper zachowany dla kompatybilności — preferuj <see cref="VerifyDetailed"/> jeśli
        /// chcesz odróżnić legacy match od full match (do auto-resign).</summary>
        public static bool Verify(SaveBundle bundle)
        {
            return VerifyDetailed(bundle) != HmacVerifyResult.Mismatch;
        }

        /// <summary>3-stanowa weryfikacja: <see cref="HmacVerifyResult.Match"/> = aktualny algorytm
        /// pasuje, <see cref="HmacVerifyResult.LegacyMatch"/> = stary (module-only) algorytm pasuje
        /// → caller powinien zrobić re-sign żeby uniknąć warning przy kolejnych loadach,
        /// <see cref="HmacVerifyResult.Mismatch"/> = save zmodyfikowany.</summary>
        public static HmacVerifyResult VerifyDetailed(SaveBundle bundle)
        {
            if (bundle == null || bundle.Manifest == null) return HmacVerifyResult.Mismatch;
            if (string.IsNullOrEmpty(bundle.Manifest.Hmac)) return HmacVerifyResult.Mismatch;

            string expected = ComputeHmac(bundle);
            if (FixedTimeEquals(expected, bundle.Manifest.Hmac))
                return HmacVerifyResult.Match;

            string legacyExpected = ComputeLegacyModuleOnlyHmac(bundle);
            if (FixedTimeEquals(legacyExpected, bundle.Manifest.Hmac))
                return HmacVerifyResult.LegacyMatch;

            return HmacVerifyResult.Mismatch;
        }

        private static string ComputeLegacyModuleOnlyHmac(SaveBundle bundle)
        {
            var sortedIds = GetSortedModuleIds(bundle);

            using (var sha = SHA256.Create())
            {
                var aggregate = new MemoryStream();
                foreach (var id in sortedIds)
                {
                    var json = bundle.Modules[id];
                    string serialized = json?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}";
                    var fileHash = sha.ComputeHash(Encoding.UTF8.GetBytes(serialized));
                    aggregate.Write(fileHash, 0, fileHash.Length);
                }

                using (var hmac = new HMACSHA256(SecretKey))
                {
                    aggregate.Position = 0;
                    var sig = hmac.ComputeHash(aggregate);
                    return Convert.ToBase64String(sig);
                }
            }
        }

        private static string CreateManifestPayload(SaveManifest manifest)
        {
            var moduleVersions = new JObject();
            if (manifest?.ModuleVersions != null)
            {
                foreach (var kv in manifest.ModuleVersions.OrderBy(k => k.Key, StringComparer.Ordinal))
                    moduleVersions[kv.Key] = kv.Value;
            }

            var payload = new JObject
            {
                ["GameVersion"] = manifest?.GameVersion ?? "",
                ["BundleSchemaVersion"] = manifest?.BundleSchemaVersion ?? 0,
                ["Playtime"] = manifest?.Playtime ?? 0,
                ["GameTimeIso"] = manifest?.GameTimeIso ?? "",
                ["SavedAt"] = manifest?.SavedAt ?? "",
                ["SaveType"] = manifest?.SaveType ?? "",
                ["SlotName"] = manifest?.SlotName ?? "",
                ["ModuleVersions"] = moduleVersions
            };
            return payload.ToString(Formatting.None);
        }

        private static System.Collections.Generic.List<string> GetSortedModuleIds(SaveBundle bundle) =>
            bundle.Modules.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        private static bool FixedTimeEquals(string expected, string actual)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected ?? ""),
                Encoding.UTF8.GetBytes(actual ?? ""));
        }
    }

    public enum HmacVerifyResult
    {
        /// <summary>Aktualny algorytm HMAC matchuje — save nie modyfikowany.</summary>
        Match,
        /// <summary>Stary (module-only) algorytm matchuje — save z poprzedniej wersji gry.
        /// Caller powinien zrobić re-sign żeby uniknąć warning przy kolejnych loadach.</summary>
        LegacyMatch,
        /// <summary>HMAC mismatch — save zmodyfikowany ręcznie albo corrupt manifest.</summary>
        Mismatch
    }
}
