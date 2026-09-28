using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class CommandQueueTests
    {
        [Fact]
        public void SameExecuteTick_SortedByIssuerThenSequence()
        {
            CommandQueue queue = new CommandQueue();
            SimCommand lateIssuer = SimCommand.Move(0, 9, 1, 100, 100).WithSchedule(3, sequence: 0);
            SimCommand earlyIssuer = SimCommand.Move(0, 1, 4, 200, 200).WithSchedule(3, sequence: 0);
            queue.Enqueue(lateIssuer);
            queue.Enqueue(earlyIssuer);

            IReadOnlyList<SimCommand> batch = queue.CommandsForTick(3);
            Assert.Equal(2, batch.Count);
            Assert.Equal(1, batch[0].IssuerId);
            Assert.Equal(9, batch[1].IssuerId);
        }
    }
}
