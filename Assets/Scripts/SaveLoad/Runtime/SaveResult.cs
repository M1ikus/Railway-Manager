using System.Collections.Generic;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: Wynik operacji LoadAsync. Discriminated union — każda kombinacja
    /// (success/failure) ma osobną semantykę dla SaveLoadUI / orchestratora.
    /// </summary>
    public class LoadResult
    {
        public LoadStatus Status { get; }
        public List<string> FailedModules { get; }
        public string ErrorMessage { get; }

        private LoadResult(LoadStatus status, List<string> failed = null, string error = null)
        {
            Status = status;
            FailedModules = failed ?? new List<string>();
            ErrorMessage = error;
        }

        public static LoadResult Success() => new LoadResult(LoadStatus.Success);

        public static LoadResult NotFound() => new LoadResult(LoadStatus.NotFound);

        public static LoadResult ModifiedSave() => new LoadResult(LoadStatus.ModifiedSave);

        public static LoadResult NewerVersion(string saveVersion) =>
            new LoadResult(LoadStatus.NewerVersion, error: $"Save z wersji {saveVersion} jest nowszy niż gra.");

        /// <summary>Niektóre moduły failed deserialize ale ogólny load przeszedł.
        /// SaveLoadUI pokaże toast "Save uszkodzony w modułach: X, Y. Zainicjalizowano domyślnie."</summary>
        public static LoadResult PartialLoad(List<string> failedModules) =>
            new LoadResult(LoadStatus.PartialLoad, failedModules);

        /// <summary>Hard failure — load zerwany na poziomie bundle (nie da się wczytać/dekompresji/parse).</summary>
        public static LoadResult Failed(string error) =>
            new LoadResult(LoadStatus.Failed, error: error);

        public bool IsSuccess => Status == LoadStatus.Success || Status == LoadStatus.PartialLoad;
    }

    public enum LoadStatus
    {
        /// <summary>Wszystkie moduły zdeserializowane bez błędów.</summary>
        Success,

        /// <summary>Slot nie istnieje na dysku.</summary>
        NotFound,

        /// <summary>HMAC mismatch — save zmodyfikowany ręcznie. Gracz może wybrać load mimo to (UI confirm).</summary>
        ModifiedSave,

        /// <summary>Save z nowszej wersji gry — nie wczytujemy bo nieznane formatu.</summary>
        NewerVersion,

        /// <summary>Niektóre moduły failed → InitializeDefault. Reszta załadowana OK.</summary>
        PartialLoad,

        /// <summary>Hard failure — bundle nie wczytany (corrupt gzip / IO error / itd.).</summary>
        Failed
    }
}
