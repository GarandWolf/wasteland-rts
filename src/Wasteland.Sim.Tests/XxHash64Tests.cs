using System.Text;
using Wasteland.Sim.Hashing;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class XxHash64Tests
    {
        // Official / python-xxhash vectors, seed 0 unless noted.
        [Theory]
        [InlineData("", 0UL, 0xEF46DB3751D8E999UL)]
        [InlineData("a", 0UL, 0xD24EC4F1A98C6E5BUL)]
        [InlineData("abc", 0UL, 0x44BC2CF5AD770999UL)]
        [InlineData("Hello World", 0UL, 0x6334D20719245BC2UL)]
        [InlineData("0123456789abcdef0123456789abcdef", 0UL, 0x642A94958E71E6C5UL)]
        [InlineData("", 13UL, 0x99058BC02E06BBDBUL)]
        [InlineData("abc", 1UL, 0xBEA9CA8199328908UL)]
        public void Golden_OneShot(string text, ulong seed, ulong expected)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            Assert.Equal(expected, XxHash64.Hash(bytes, seed));
        }

        [Fact]
        public void Builder_MatchesOneShot()
        {
            byte[] bytes = Encoding.ASCII.GetBytes("Hello World");
            XxHash64Builder builder = new XxHash64Builder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.AddByte(bytes[i]);
            }

            Assert.Equal(XxHash64.Hash(bytes, 0), builder.Digest());
        }

        [Fact]
        public void Mix_TwoUlongs_Golden()
        {
            Assert.Equal(0xDE5D887A98579652UL, XxHash64.Mix(0xC0FFEEUL, 2UL));
        }
    }
}
