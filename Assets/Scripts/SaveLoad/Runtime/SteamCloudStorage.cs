using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// Stub Steam Cloud storage — gated na **M14 Beta+Launch** milestone (kiedy dorzucimy
    /// Steamworks.NET package i podpiszemy `SteamRemoteStorage.FileWrite/FileRead`).
    ///
    /// Aktualnie: throw <see cref="NotImplementedException"/> ze wskazówką w
    /// <see cref="NotReadyMessage"/>. Player który wybierze SteamCloud w Settings (D27 koniec
    /// M13-13) dostanie clear error message.
    ///
    /// **Aby zaimplementować w M14:**
    /// 1. Dodaj `Steamworks.NET` package via UPM (Unity Package Manager).
    /// 2. Implement Save: `SteamRemoteStorage.FileWriteAsync(slotId + ".rmsave", bytes)`.
    /// 3. Implement Load: `SteamRemoteStorage.FileReadAsync(slotId + ".rmsave")`.
    /// 4. Implement List: enumerate przez `SteamRemoteStorage.GetFileCount()` + iterate
    ///    `SteamRemoteStorage.GetFileNameAndSize(i)`, filter po `.rmsave` extension.
    /// 5. Quota check: `SteamRemoteStorage.GetQuota(out long total, out long avail)`
    ///    przed Save — fail-fast gdy avail &lt; bytes.Length.
    /// 6. Zachować `_ioGate` SemaphoreSlim wzór z LocalDiskStorage (race protection).
    /// 7. SaveLoadServiceBootstrap.Bootstrap: switch `Storage = settings.UseSteamCloud
    ///    ? new SteamCloudStorage() : new LocalDiskStorage();`.
    ///
    /// Lokalna kopia: rozważyć `MirroredStorage(SteamCloudStorage primary, LocalDiskStorage backup)`
    /// — Save pisze do obu, Load preferuje cloud z fallback na local przy offline.
    /// </summary>
    public class SteamCloudStorage : ISaveStorage
    {
        private const string NotReadyMessage =
            "Steam Cloud storage czeka na M14 Steamworks integration. " +
            "Tymczasowo użyj LocalDiskStorage (default). " +
            "Implementacja: patrz komentarz dokumentacyjny SteamCloudStorage.cs.";

        public Task<bool> SaveAsync(string slotId, SaveBundle bundle) =>
            throw new NotImplementedException(NotReadyMessage);

        public Task<SaveBundle> LoadAsync(string slotId) =>
            throw new NotImplementedException(NotReadyMessage);

        public Task<bool> DeleteAsync(string slotId) =>
            throw new NotImplementedException(NotReadyMessage);

        public Task<List<SaveSlotInfo>> ListAsync() =>
            throw new NotImplementedException(NotReadyMessage);

        public Task<bool> ExistsAsync(string slotId) =>
            throw new NotImplementedException(NotReadyMessage);
    }
}
