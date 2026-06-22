using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-6: Centralne eventy zyciowe Personnel (L4, emerytury, vacancy).
    /// Subskrybenci: <see cref="UI.PersonnelNotificationToastUI"/>, <see cref="Runtime.DispatcherService"/> (M8-7).
    ///
    /// CrewVacancyDetected (D21): wywolywane gdy pracownik ktory mial byc OnShift jest nieobecny
    /// (L4 / emerytura / urlop). W M8-6 skeleton — crewCirculationId null bo CrewCirculation
    /// (M8-8) jeszcze nie istnieje. NotificationToast pokazuje powiadomienie z placeholder buttons.
    /// </summary>
    public static class PersonnelEvents
    {
        public static event Action<Employee> OnRetirementAnnounced;
        public static event Action<Employee> OnEmployeeRetired;
        public static event Action<Employee> OnEmployeeGotSick;
        public static event Action<Employee> OnEmployeeRecovered;
        public static event Action<CrewVacancyData> OnCrewVacancyDetected;

        public static void RaiseRetirementAnnounced(Employee e) => OnRetirementAnnounced?.Invoke(e);
        public static void RaiseEmployeeRetired(Employee e) => OnEmployeeRetired?.Invoke(e);
        public static void RaiseEmployeeGotSick(Employee e) => OnEmployeeGotSick?.Invoke(e);
        public static void RaiseEmployeeRecovered(Employee e) => OnEmployeeRecovered?.Invoke(e);
        public static void RaiseCrewVacancyDetected(CrewVacancyData data) => OnCrewVacancyDetected?.Invoke(data);
    }

    /// <summary>
    /// Dane CrewVacancy — przekazywane do NotificationToastUI + DispatcherService (M8-7).
    /// <see cref="crewCirculationId"/>=null w M8-6 (brak CrewCirculation jeszcze).
    /// </summary>
    public class CrewVacancyData
    {
        public int employeeId;
        public EmployeeRole role;
        public string affectedDateIso;
        public int? crewCirculationId;        // null do M8-8
        public int? affectedDutyIndex;        // null do M8-8
        public CrewVacancyReason reason;
        /// <summary>Text dla UI (opcjonalny custom message).</summary>
        public string customMessage;
    }

    public enum CrewVacancyReason
    {
        SickLeave,
        Vacation,
        RetirementDeparture,
        Training,
        Unknown
    }
}
