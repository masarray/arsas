using System.IO;
using System.Text.Json;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public static class TesterProjectStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task SaveAsync(string path, Iec61850TesterProject project, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, project, Options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Iec61850TesterProject?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Iec61850TesterProject>(stream, Options, cancellationToken).ConfigureAwait(false);
    }
}
