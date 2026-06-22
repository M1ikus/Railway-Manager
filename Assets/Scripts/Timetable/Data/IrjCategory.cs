using System;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Kategoria rozkładowa wg IRJ PKP: kombinacja grupy (pierwsze 2 litery)
    /// i litery trakcyjnej (3. litera). Np. (RegionalLocal, ElectricUnit) → "ROJ".
    /// Zawiera też liczbę cyfr numeru pociągu dla tej kategorii.
    /// </summary>
    [Serializable]
    public struct IrjCategory : IEquatable<IrjCategory>
    {
        public IrjGroup group;
        public TractionLetter traction;

        public IrjCategory(IrjGroup group, TractionLetter traction)
        {
            this.group = group;
            this.traction = traction;
        }

        public bool Equals(IrjCategory other) => group == other.group && traction == other.traction;
        public override bool Equals(object obj) => obj is IrjCategory o && Equals(o);
        public override int GetHashCode() => ((int)group * 31) ^ (int)traction;
        public override string ToString() => IrjCategoryCatalog.GetCode(this);
    }
}
