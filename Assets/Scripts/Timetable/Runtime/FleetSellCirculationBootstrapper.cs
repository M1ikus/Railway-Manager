using UnityEngine;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Instaluje veto sprzedaży taboru: pojazd przypisany do obiegu nie może być sprzedany.
    /// Fleet/Depot nie widzą Timetable (asmdef), więc <see cref="FleetService"/> eksponuje
    /// <c>SellVetoHook</c>, a Timetable (wyższa warstwa) podstawia implementację przy starcie.
    /// Wzór jak <see cref="CouplingCirculationBootstrapper"/> / CrewCheckHook.
    /// </summary>
    public static class FleetSellCirculationBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            FleetService.SellVetoHook = VetoIfInCirculation;
        }

        private static string VetoIfInCirculation(int vehicleId)
        {
            var circs = CirculationService.GetCirculationsForVehicle(vehicleId);
            if (circs != null && circs.Count > 0)
                return $"Pojazd w obiegu „{circs[0].name}” — usuń z obiegu najpierw";
            return null;
        }
    }
}
