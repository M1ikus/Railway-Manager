using RailwayManager.Fleet;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Partial FleetPanelUI — helpers do statusu przegladow taboru.
    /// Pure helpers, bez UI building (UI w DetailPopup partial).
    /// </summary>
    public partial class FleetPanelUI
    {
        // ── INSPECTION HELPERS ────────────────────────

        private long NowGameTime => (long)RailwayManager.Core.GameState.GameTimeSeconds;

        private float GetInspectionUrgency(FleetVehicleData v)
            => v.inspections != null
               ? v.inspections.GetMostUrgent(InspectionCatalog.GetForSeries(v.seriesId), NowGameTime, v.mileageKm).progress
               : 0f;

        private float GetInspectionUrgency(FleetMarketVehicle v)
            => v.inspections != null
               ? v.inspections.GetMostUrgent(InspectionCatalog.GetForSeries(v.seriesId), 0, v.mileageKm).progress
               : 0f;

        private Color GetInspectionColor(float kmRemaining)
        {
            if (kmRemaining <= 1000f) return InspUrgent;
            if (kmRemaining <= 5000f) return InspWarn;
            return InspOk;
        }

        private Color GetInspectionColorFromProgress(float progress)
        {
            if (progress >= 0.95f) return InspUrgent;
            if (progress >= 0.80f) return InspWarn;
            return InspOk;
        }

        private string GetInspectionToneLabel(float progress)
        {
            if (progress >= 0.95f) return "Pilne";
            if (progress >= 0.80f) return "Wkrotce";
            return "OK";
        }

        /// <summary>Formatuje pozostaly limit (km/czas) wybierajac bardziej pilny wymiar.</summary>
        private string FormatInspectionRemaining(InspectionSchedule.LevelStatus s, InspectionIntervals intervals)
        {
            if (!s.hasKmLimit)
                return InspectionSchedule.FormatRemainingTime(s.remainingSec);
            if (!s.hasTimeLimit)
                return InspectionSchedule.FormatRemainingKm(s.remainingKm);

            if (intervals == null) intervals = InspectionIntervals.CreateDefault();

            // oba limity — wybierz ten, ktory jest blizej limitu (mniej pozostalo procentowo)
            float kmLimit = s.level == InspectionLevel.P4 ? intervals.p4LimitKm : intervals.p5LimitKm;
            int   yrLimit = s.level == InspectionLevel.P4 ? intervals.p4LimitYears : intervals.p5LimitYears;
            float kmFrac   = Mathf.Max(0f, s.remainingKm) / kmLimit;
            float timeFrac = Mathf.Max(0f, s.remainingSec) / (float)(yrLimit * InspectionSchedule.SEC_YEAR);
            return kmFrac <= timeFrac
                ? InspectionSchedule.FormatRemainingKm(s.remainingKm)
                : InspectionSchedule.FormatRemainingTime(s.remainingSec);
        }

        /// <summary>LEGACY — domyślne intervals.</summary>
        private string FormatInspectionRemaining(InspectionSchedule.LevelStatus s)
            => FormatInspectionRemaining(s, InspectionIntervals.CreateDefault());

        private string FormatInspectionCompact(InspectionSchedule.LevelStatus s)
            => $"{s.level} • {FormatInspectionRemaining(s)}";
    }
}
