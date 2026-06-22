using System;
using UnityEngine;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-10: Pojedyncze zadanie ruchu pracownika — idz do targetu + opcjonalny callback po dojechaniu.
    ///
    /// Tworzone przez <see cref="PersonnelDispatcher3D"/> i kolejkowane w
    /// <see cref="EmployeeWalkSimulator"/>.
    ///
    /// <para><b>TD-025:</b> task może mieć <see cref="nextTask"/> jako chain — gdy
    /// <see cref="onArrive"/> się wywoła, simulator automatycznie kolejkuje nextTask.
    /// To eliminuje need for caller'a re-enqueue w callback'u.</para>
    /// </summary>
    public class EmployeeWalkTask
    {
        public int employeeId;
        public Vector3 destination;
        /// <summary>Fluff text: "Spawn", "GoToWork", "GoHome", "GoToWorkshop", etc.</summary>
        public string purpose;
        public Action onArrive;
        public bool hurry; // W pośpiechu: 2.5 m/s zamiast 1.4

        /// <summary>MF-10: ID przypisanej instancji furniture (PlacedFurnitureItem.instanceId).
        /// -1 = brak. Diagnostic only — destination jest primary target (Vector3 obliczany
        /// w PersonnelDispatcher3D z accessSide cell instance'a). Pole pomocne dla logging
        /// i future polish (np. cancel walk gdy furniture deleted mid-flight).</summary>
        public int targetFurnitureId = -1;

        /// <summary>
        /// TD-025: opcjonalny chain — gdy ten task się skończy (po onArrive), simulator
        /// automatycznie kolejkuje nextTask. Pozwala na multi-step plan'y bez callback hell.
        /// </summary>
        public EmployeeWalkTask nextTask;
    }
}
