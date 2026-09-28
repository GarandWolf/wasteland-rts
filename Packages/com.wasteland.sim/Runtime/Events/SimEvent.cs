namespace Wasteland.Sim.Events
{
    /// <summary>
    /// Sim-facing event. Payloads are ids and ticks only (plus integer reason
    /// codes). Consumed in <see cref="EventId"/> order.
    /// </summary>
    public readonly struct SimEvent
    {
        public readonly int EventId;
        public readonly int Tick;
        public readonly SimEventKind Kind;
        public readonly int SubjectId;
        public readonly int AuxId;

        public SimEvent(int eventId, int tick, SimEventKind kind, int subjectId, int auxId)
        {
            EventId = eventId;
            Tick = tick;
            Kind = kind;
            SubjectId = subjectId;
            AuxId = auxId;
        }

        public CommandRejectReason RejectReason
        {
            get { return (CommandRejectReason)AuxId; }
        }
    }
}
