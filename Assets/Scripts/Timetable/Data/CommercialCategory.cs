using System;
using System.Collections.Generic;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Kategoria handlowa — konfigurowana przez gracza, steruje UX, ekonomią i wymaganiami.
    /// Jest niezależna od kategorii rozkładowej IRJ (ta jest auto-wyliczana z trasy i kursowania).
    /// Przykłady: "Osobowy", "IC Krakowiak", "Sprinter", "Express Premium".
    /// </summary>
    [Serializable]
    public class CommercialCategory
    {
        public string id;                        // stabilny identyfikator, np. "os", "ic_krakowiak"
        public string displayName;               // nazwa wyświetlana
        public string shortCode;                 // skrót na tablicy, np. "Os", "IC"

        // ── Ceny ──────────────────────────────────
        /// <summary>
        /// M6-3: Step pricing tiers (preferowany). Gdy zapełnione → TicketSystem używa progów.
        /// Gdy puste → fallback do legacy basePriceZl + pricePerKmZl × dist.
        /// </summary>
        public List<PricingTier> pricingTiers = new();

        // ── Legacy pricing (fallback gdy pricingTiers puste) ──
        public float basePriceZl;                // cena bazowa biletu (stawka 2. klasy / domyślna)
        public float pricePerKmZl;               // narzut per km
        public float firstClassMultiplier = 1.5f;// LEGACY/nieużywane — zastąpione przez classFares (M-PaxV2 Faza A)

        // ── Cennik per klasa (M-PaxV2 Faza A) ──────
        /// <summary>
        /// Pełny cennik per klasa biletowa (SeatZoneType). Gdy klasa ma tu wpis → TicketSystem
        /// liczy z niego; brak wpisu → fallback do stawki domyślnej (pricingTiers / basePriceZl).
        /// Pozwala wycenić 1. klasę / sypialny / kuszetkę osobno zamiast jednej stawki 2. klasy.
        /// </summary>
        public List<ClassFare> classFares = new();

        // ── Wymagania taboru ──────────────────────
        public bool requiresAirConditioning;
        public bool requiresWiFi;
        public bool requiresPowerSockets;
        public bool requiresCatering;            // WR/bufet
        public bool requiresSleepingCar;         // WL (dla rozkładów nocnych)

        // ── Polityka postojów ─────────────────────
        public StopPolicy defaultStopPolicy = StopPolicy.ManualPerRoute;

        /// <summary>Minimalny czas postoju w sekundach (twardy limit w edytorze).</summary>
        public int minStopSeconds = 30;

        // ── Ruch ─────────────────────────────────
        /// <summary>Priorytet do przyszłego traffic management (wyższy = pierwszeństwo w konfliktach).</summary>
        public int trafficPriority = 1;

        /// <summary>Domyślny tryb składu gdy gracz tworzy rozkład tej kategorii.</summary>
        public CompositionMode defaultCompositionMode = CompositionMode.MultipleUnit;

        /// <summary>Sugerowana Vmax projektowa (ograniczona przez Vmax linii i taboru).</summary>
        public int suggestedMaxSpeedKmh = 120;

        // ── Metadane ─────────────────────────────
        public string notes;                     // dowolne notatki gracza
    }

    /// <summary>Domyślna polityka postojów dla kategorii handlowej (wpływ na pre-fill w kreatorze).</summary>
    public enum StopPolicy
    {
        /// <summary>Zatrzymuje się na wszystkich stacjach i przystankach na trasie.</summary>
        AllStations,
        /// <summary>Tylko stacje (railway=station), pomija przystanki (railway=halt).</summary>
        MajorStationsOnly,
        /// <summary>Gracz ustala ręcznie per trasa, zapisujemy w pamięci postojów.</summary>
        ManualPerRoute
    }
}
