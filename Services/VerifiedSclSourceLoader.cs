using System.Security.Cryptography;
using System.Xml.Linq;

namespace ArIED61850Tester.Services;

public sealed record VerifiedSclSource(
    string FullPath,
    string Sha256,
    string Xml);

/// <summary>
/// Loads exactly the source bytes previously accepted by the SCL workspace. A missing
/// file or SHA-256 mismatch is a hard stop: online SCL-assisted connect must never use
/// a silently edited design file or fall back to live discovery without the operator.
/// </summary>
public static class VerifiedSclSourceLoader
{
    public static async Task<VerifiedSclSource> LoadAsync(
        string sourcePath,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidDataException("SCL source path is empty. Re-open the trusted SCL/CID before connecting.");
        if (string.IsNullOrWhiteSpace(expectedSha256))
            throw new InvalidDataException("SCL source SHA-256 is missing. Re-open the trusted SCL/CID before connecting.");

        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The trusted SCL/CID source file is no longer available. Re-open it before connecting.", fullPath);

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var expected = NormalizeSha256(expectedSha256);
        if (!string.Equals(actualSha256, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Trusted SCL/CID changed after import. Expected SHA-256 {expected}, actual {actualSha256}. Re-open the file so the design authority is explicit.");
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        return new VerifiedSclSource(
            fullPath,
            actualSha256,
            document.ToString(SaveOptions.DisableFormatting));
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException($"Invalid SCL SHA-256 evidence '{value}'.");
        return normalized;
    }
}
