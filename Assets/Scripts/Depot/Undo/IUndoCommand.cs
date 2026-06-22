namespace DepotSystem.Undo
{
    public enum UndoCategory
    {
        Tory,            // BuildTrack (Track + Turnout + WashZone + Turntable + PitLift)
        SiecTrakcyjna,   // BuildCatenary
        Sciezki,         // BuildPath (Path + Road + Parking)
        Pomieszczenia    // BuildRoom (Walls + Buildings + Rooms)
    }

    public interface IUndoCommand
    {
        void Undo();
        string Description { get; }
    }
}
