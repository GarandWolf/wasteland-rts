using System;
using System.Collections.Generic;

namespace Wasteland.Sim.Commands
{
    /// <summary>
    /// Commands indexed by execute tick. Same-tick commands are stored in
    /// (issuerId, sequence) order so iteration is independent of enqueue
    /// order across issuers — required for lockstep.
    /// </summary>
    public sealed class CommandQueue
    {
        private readonly SortedDictionary<int, List<SimCommand>> _byExecuteTick = new SortedDictionary<int, List<SimCommand>>();

        public int Count { get; private set; }

        public void Enqueue(SimCommand command)
        {
            if (!_byExecuteTick.TryGetValue(command.ExecuteTick, out List<SimCommand> list))
            {
                list = new List<SimCommand>();
                _byExecuteTick.Add(command.ExecuteTick, list);
            }

            int index = list.BinarySearch(command);
            if (index < 0)
            {
                index = ~index;
            }

            list.Insert(index, command);
            Count++;
        }

        public IReadOnlyList<SimCommand> CommandsForTick(int executeTick)
        {
            if (_byExecuteTick.TryGetValue(executeTick, out List<SimCommand> list))
            {
                return list;
            }

            return Array.Empty<SimCommand>();
        }

        public void Clear()
        {
            _byExecuteTick.Clear();
            Count = 0;
        }

        public void RemoveTick(int executeTick)
        {
            if (_byExecuteTick.TryGetValue(executeTick, out List<SimCommand> list))
            {
                Count -= list.Count;
                _byExecuteTick.Remove(executeTick);
            }
        }

        /// <summary>
        /// All queued commands in execute order: tick, then issuer (faction/seat), then submit sequence.
        /// </summary>
        public void CopyTo(List<SimCommand> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            foreach (KeyValuePair<int, List<SimCommand>> pair in _byExecuteTick)
            {
                List<SimCommand> list = pair.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    into.Add(list[i]);
                }
            }
        }
    }
}
