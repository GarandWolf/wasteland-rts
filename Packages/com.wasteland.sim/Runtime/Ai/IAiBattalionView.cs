namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Living battalions for mix and engineer checks. Each pin carries
    /// the balance unit id string; role counts come from the catalog.
    /// </summary>
    public interface IAiBattalionView
    {
        int Count { get; }

        AiBattalionPin At(int index);
    }
}
