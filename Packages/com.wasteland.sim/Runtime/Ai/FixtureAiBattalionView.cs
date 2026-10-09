using System;

namespace Wasteland.Sim.Ai
{
    public sealed class FixtureAiBattalionView : IAiBattalionView
    {
        private AiBattalionPin[] _pins = Array.Empty<AiBattalionPin>();

        public int Count
        {
            get { return _pins.Length; }
        }

        public AiBattalionPin At(int index)
        {
            return _pins[index];
        }

        public void Set(params AiBattalionPin[] pins)
        {
            _pins = pins ?? Array.Empty<AiBattalionPin>();
        }
    }
}
