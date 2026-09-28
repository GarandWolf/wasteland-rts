namespace Wasteland.Sim.Events
{
    public enum CommandRejectReason : byte
    {
        None = 0,
        TooLate = 1,
        UnknownBattalion = 2,
        DuplicateBattalion = 3,
        NotOwner = 4,
        NegativeSpeed = 5,
        UnknownKind = 6
    }
}
