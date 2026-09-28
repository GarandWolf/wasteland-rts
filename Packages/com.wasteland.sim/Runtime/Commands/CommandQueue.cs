using System;
using System.Collections.Generic;

namespace Wasteland.Sim.Commands
{
    /// <summary>
    /// Commands indexed by the tick they will execute. Same-tick commands are
    /// stored in canonical order (player, unit, kind, args) so iteration is
    /// independent of submit order — required for lockstep.
    /// </summary>
    public sealed class CommandQueue
    {
        private readonly SortedDictionary<int, List<SimCommand>> _byTick = new SortedDictionary<int, List<SimCommand>>();

        public int Count { get; private set; }

        public void Enqueue(SimCommand command)
        {
            if (!_byTick.TryGetValue(command.Tick, out List<SimCommand> list))
            {
                list = new List<SimCommand>();
                _byTick.Add(command.Tick, list);
            }

            int index = list.BinarySearch(command);
            if (index < 0)
            {
                index = ~index;
            }

            list.Insert(index, command);
            Count++;
        }

        /// <summary>
        /// Returns commands scheduled for <paramref name="tick"/> in canonical
        /// order, or an empty list. The snapshot remains owned by the queue;
        /// callers must not mutate it.
        /// </summary>
        public IReadOnlyList<SimCommand> CommandsForTick(int tick)
        {
            if (_byTick.TryGetValue(tick, out List<SimCommand> list))
            {
                return list;
            }

            return Array.Empty<SimCommand>();
        }

        public void RemoveTick(int tick)
        {
            if (_byTick.TryGetValue(tick, out List<SimCommand> list))
            {
                Count -= list.Count;
                _byTick.Remove(tick);
            }
        }

        public IEnumerable<SimCommand> AllInOrder()
        {
            foreach (KeyValuePair<int, List<SimCommand>> pair in _byTick)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    yield return pair.Value[i];
                }
            }
        }
    }
}
