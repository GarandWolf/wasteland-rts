using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Math;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class CommandQueueTests
    {
        [Fact]
        public void SameTick_SortedCanonically()
        {
            CommandQueue queue = new CommandQueue();
            SimCommand latePlayer = SimCommand.Move(3, 9, 1, Fix64Vec2.FromInts(1, 1));
            SimCommand earlyPlayer = SimCommand.Move(3, 1, 4, Fix64Vec2.FromInts(2, 2));
            queue.Enqueue(latePlayer);
            queue.Enqueue(earlyPlayer);

            IReadOnlyList<SimCommand> batch = queue.CommandsForTick(3);
            Assert.Equal(2, batch.Count);
            Assert.Equal(1, batch[0].PlayerId);
            Assert.Equal(9, batch[1].PlayerId);
        }
    }
}
