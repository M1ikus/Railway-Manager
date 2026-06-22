using System;

namespace RailwayManager.Fleet
{
    /// <summary>Pojedynczy wpis w historii pojazdu (zakup, przegląd, naprawa, przebudowa, zmiana właściciela).</summary>
    [Serializable]
    public class MaintenanceRecord
    {
        public long gameTimeSeconds;    // czas w grze (z GameState.GameTimeSeconds)
        public string recordType;       // "Zakup", "Przeglad P1".."P5", "Naprawa", "Przebudowa", "Zmiana wlasciciela"
        public string description;      // opis szczegolowy
        public long cost;               // koszt w PLN (0 jeśli bezkosztowe)
        public float mileageAtRecord;   // stan licznika w momencie zdarzenia
    }
}
