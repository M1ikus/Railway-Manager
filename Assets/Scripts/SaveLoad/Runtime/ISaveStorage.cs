using System.Collections.Generic;
using System.Threading.Tasks;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: Abstrakcja nad fizyczną lokalizacją save'ów.
    /// Pozwala wymienić local-disk na Steam Cloud (M14) bez zmian w SaveOrchestrator.
    /// </summary>
    public interface ISaveStorage
    {
        /// <summary>Zapisuje bundle do slot'a. Atomowy (write to .tmp → fsync → rename).
        /// Wraca true na success, false na failure (IO error, permissions, full disk).</summary>
        Task<bool> SaveAsync(string slotId, SaveBundle bundle);

        /// <summary>Wczytuje bundle z slot'a. null jeśli slot nie istnieje; wyjątek jeśli
        /// istnieje, ale nie da się go odczytać/zdeserializować. SaveOrchestrator następnie
        /// weryfikuje HMAC + uruchamia migrator chain.</summary>
        Task<SaveBundle> LoadAsync(string slotId);

        /// <summary>Usuwa slot z dysku. Wraca true jeśli usunięty (lub nie istniał = no-op).</summary>
        Task<bool> DeleteAsync(string slotId);

        /// <summary>Lista wszystkich slot'ów (manifest only — nie load'uje pełnych bundle'ów).
        /// Sortowanie: po SavedAt malejąco (najnowsze pierwsze).</summary>
        Task<List<SaveSlotInfo>> ListAsync();

        /// <summary>Czy slot istnieje. Tańsze niż LoadAsync — tylko file existence check.</summary>
        Task<bool> ExistsAsync(string slotId);
    }
}
