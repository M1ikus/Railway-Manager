using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem.OutdoorEquipment
{
    /// <summary>
    /// MVP 2026-05-03 — typy infrastruktury outdoor stawianej w trybie Build Track sub-mode'ach
    /// (WashZone/Turntable/PitLift). Niezależne od mebli w Hall (te są indoor).
    ///
    /// Pełen gameplay impact (myjnia faktycznie czyści, turntable obraca, pitlift podnosi):
    /// odłożone do M-Modernization. Ten plik to tylko **placement infrastructure** — gracz
    /// może postawić cuboid placeholder w danym miejscu z walidacją size.
    /// </summary>
    public enum OutdoorEquipmentType
    {
        WashZone,     // myjnia outdoor (płyta z bramami)
        Turntable,    // obrotnica
        PitLift,      // kanał + podnośnik outdoor
        FuelStation,  // MM-9 / MM-D14: stacja paliw (DMU spalinowe, max 50m)
        WaterService  // MM-17: wodowanie (woda + zb. fekaliów dla pasażerskich, max 50m)
    }

    /// <summary>Preset definicji per typ — min size + kolor placeholder cuboid'a.</summary>
    public struct OutdoorEquipmentPreset
    {
        public float minWidth;
        public float minDepth;
        public string label;
        public Color color;
    }

    /// <summary>Static dictionary z parametrami per typ (analogicznie do RoomRequirements.MinSize).</summary>
    public static class OutdoorEquipmentDefinitions
    {
        public static readonly Dictionary<OutdoorEquipmentType, OutdoorEquipmentPreset> Presets = new()
        {
            [OutdoorEquipmentType.WashZone] = new OutdoorEquipmentPreset
            {
                minWidth = 8f,
                minDepth = 6f,
                label = "Myjnia",
                color = new Color(0.12f, 0.56f, 1f)         // niebieski
            },
            [OutdoorEquipmentType.Turntable] = new OutdoorEquipmentPreset
            {
                minWidth = 12f,
                minDepth = 12f,
                label = "Obrotnica",
                color = new Color(0.78f, 0.45f, 0.15f)      // brąz
            },
            [OutdoorEquipmentType.PitLift] = new OutdoorEquipmentPreset
            {
                minWidth = 6f,
                minDepth = 4f,
                label = "Kanal/Podnosnik",
                color = new Color(0.55f, 0.55f, 0.55f)      // szary
            },
            [OutdoorEquipmentType.FuelStation] = new OutdoorEquipmentPreset
            {
                minWidth = 4f,
                minDepth = 3f,
                label = "Stacja paliw",
                color = new Color(0.96f, 0.55f, 0.13f)      // pomarańczowy (MM-D14)
            },
            [OutdoorEquipmentType.WaterService] = new OutdoorEquipmentPreset
            {
                minWidth = 4f,
                minDepth = 2f,
                label = "Wodowanie",
                color = new Color(0.30f, 0.80f, 0.95f)      // jasnoniebieski (MM-17)
            }
        };

        /// <summary>Mapping TrackBuildSubMode → OutdoorEquipmentType (lub null gdy sub-mode nie jest outdoor).</summary>
        public static OutdoorEquipmentType? FromSubMode(TrackBuildSubMode mode) => mode switch
        {
            TrackBuildSubMode.WashZone => OutdoorEquipmentType.WashZone,
            TrackBuildSubMode.Turntable => OutdoorEquipmentType.Turntable,
            TrackBuildSubMode.PitLift => OutdoorEquipmentType.PitLift,
            TrackBuildSubMode.FuelStation => OutdoorEquipmentType.FuelStation,
            TrackBuildSubMode.WaterService => OutdoorEquipmentType.WaterService,
            _ => null
        };

        /// <summary>MM-D14: maksymalna długość pojazdu jaką dany typ outdoor obsłuży.</summary>
        public static float GetMaxVehicleLength(OutdoorEquipmentType type) => type switch
        {
            OutdoorEquipmentType.WashZone => 64f,     // pełen skład EZT
            OutdoorEquipmentType.Turntable => 25f,    // pojedynczy pojazd
            OutdoorEquipmentType.PitLift => 18f,      // krótki pojazd
            OutdoorEquipmentType.FuelStation => 50f,  // długie pojazdy też tankuje
            OutdoorEquipmentType.WaterService => 50f, // pasażerskie wszystkie długości
            _ => 0f,
        };
    }

    /// <summary>Placed instance — runtime state + serializowalne (future DepotSavable).</summary>
    [System.Serializable]
    public class PlacedOutdoorEquipment
    {
        public int instanceId;
        public OutdoorEquipmentType type;
        public Vector3 cornerA;     // rectangle corners (world coords)
        public Vector3 cornerB;
        public GameObject visualObject;  // runtime, nie serializowane
    }
}
