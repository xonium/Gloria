using System.Text;
using Gloria.Core.Ingestion;

namespace Gloria.Core.Tests.Ingestion;

public class DemoHasherTests
{
    [Fact]
    public async Task ComputeSha256Async_ReturnsLowercaseHexDigest()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc"));

        var hash = await DemoHasher.ComputeSha256Async(stream, TestContext.Current.CancellationToken);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }
}
