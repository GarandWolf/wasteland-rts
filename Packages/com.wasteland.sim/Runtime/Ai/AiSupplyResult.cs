namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Outcome of a bounded road-supply search. Budget exhaustion is not
    /// unreachability: callers must defer instead of treating the node as cut off.
    /// </summary>
    public enum AiSupplyStatus : byte
    {
        Reached = 0,
        Unreachable = 1,
        BudgetExhausted = 2
    }

    public readonly struct AiSupplyResult
    {
        public readonly AiSupplyStatus Status;
        public readonly int DistanceCm;

        private AiSupplyResult(AiSupplyStatus status, int distanceCm)
        {
            Status = status;
            DistanceCm = distanceCm;
        }

        public bool IsReached
        {
            get { return Status == AiSupplyStatus.Reached; }
        }

        public bool IsUnreachable
        {
            get { return Status == AiSupplyStatus.Unreachable; }
        }

        public bool IsBudgetExhausted
        {
            get { return Status == AiSupplyStatus.BudgetExhausted; }
        }

        public static AiSupplyResult Reached(int distanceCm)
        {
            return new AiSupplyResult(AiSupplyStatus.Reached, distanceCm);
        }

        public static AiSupplyResult Unreachable()
        {
            return new AiSupplyResult(AiSupplyStatus.Unreachable, AiConstants.UnreachableCm);
        }

        public static AiSupplyResult BudgetExhausted()
        {
            return new AiSupplyResult(AiSupplyStatus.BudgetExhausted, 0);
        }
    }
}
