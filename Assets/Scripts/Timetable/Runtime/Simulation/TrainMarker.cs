using UnityEngine;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// Component na wizualizacji pociągu na mapie. Nośnik referencji do SimulatedTrain
    /// — klikanie obsługuje MapClickHandler (manual raycast, bo New Input System
    /// nie wspiera OnMouseDown).
    /// </summary>
    public class TrainMarker : MonoBehaviour
    {
        /// <summary>Globalny event — dowolny pociąg na mapie został kliknięty.</summary>
        public static event System.Action<TrainMarker> OnAnyTrainClicked;

        /// <summary>Referencja do SimulatedTrain reprezentowanego przez ten marker.</summary>
        public SimulatedTrain SimulatedTrain;

        public static void InvokeClick(TrainMarker marker)
        {
            OnAnyTrainClicked?.Invoke(marker);
        }
    }
}
