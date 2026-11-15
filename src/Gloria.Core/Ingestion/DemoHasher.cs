using System.Security.Cryptography;

namespace Gloria.Core.Ingestion;

/// <summary>Content hash used as a demo's identity, so re-importing the same file is detected.</summary>
public static class DemoHasher
{
    /// <returns>Lowercase hex SHA-256 of the stream's remaining contents.</returns>
    public static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}
