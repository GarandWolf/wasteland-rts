namespace Wasteland.Sim.Events
{
    public enum CommandRejectReason : byte
    {
        None = 0,
        TooLate = 1,
        UnknownSquad = 2,
        DuplicateSquad = 3,
        NotOwner = 4,
        NegativeSpeed = 5,
        UnknownKind = 6
    }
}
