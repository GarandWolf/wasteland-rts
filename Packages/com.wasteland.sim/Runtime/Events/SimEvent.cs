namespace Wasteland.Sim.Events
{
    /// <summary>
    /// Sim-facing event. Payloads are ids, ticks, and integer params.
    /// A rejection stores <see cref="CommandRejectReason"/> in <see cref="AuxId"/>
    /// and the reason's integer params in <see cref="Param0"/> and <see cref="Param1"/>.
    /// Consumed in <see cref="EventId"/> order. No strings.
    /// </summary>
    public readonly struct SimEvent
    {
        public readonly int EventId;
        public readonly int Tick;
        public readonly SimEventKind Kind;
        public readonly int SubjectId;
        public readonly int AuxId;
        public readonly int Param0;
        public readonly int Param1;

        public SimEvent(int eventId, int tick, SimEventKind kind, int subjectId, int auxId)
            : this(eventId, tick, kind, subjectId, auxId, 0, 0)
        {
        }

        public SimEvent(
            int eventId,
            int tick,
            SimEventKind kind,
            int subjectId,
            int auxId,
            int param0,
            int param1)
        {
            EventId = eventId;
            Tick = tick;
            Kind = kind;
            SubjectId = subjectId;
            AuxId = auxId;
            Param0 = param0;
            Param1 = param1;
        }

        public CommandRejectReason RejectReason
        {
            get { return (CommandRejectReason)AuxId; }
        }
    }
}
