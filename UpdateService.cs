using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Fassadenplaner;

public sealed record UpdateInfo(
    Version Version,
    string? InstallerPath,
    string? DownloadUrl,
    string? Notes,
    string? Sha256,
    bool IsLocal);

public static class UpdateService
{
    private const string LocalBuildDirectory = @"C:\Fassadenplaner\Builds";

    // Für den späteren Verkauf kann hier einfach eine öffentliche JSON-Datei
    // auf der eigenen Domain hinterlegt werden. Dafür ist KEIN GitHub-Token nötig.
    // Erwartetes Schema:
    // {
    //   "version": "0.1.123",
    //   "downloadUrl": "https://.../Fassadenplaner-Setup.exe",
    //   "notes": "Kurze Änderungen",
    //   "sha256": "optional"
    // }
    private const string RemoteManifestUrl = "";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public static Version CurrentVersion
    {
        get
        {
            var assemblyVersion =
                Assembly.GetExecutingAssembly().GetName().Version;

            if (assemblyVersion is not null)
                return Normalize(assemblyVersion);

            return new Version(0, 1, 0);
        }
    }

    public static string CurrentVersionDisplay =>
        FormatVersion(CurrentVersion);

    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        var candidates = new List<UpdateInfo>();

        var local = TryReadLocalUpdate();
        if (local is not null)
            candidates.Add(local);

        if (!string.IsNullOrWhiteSpace(RemoteManifestUrl))
        {
            try
            {
                var remote = await ReadRemoteUpdateAsync();
                if (remote is not null)
                    candidates.Add(remote);
            }
            catch
            {
                // Lokale Entwicklungsupdates sollen auch funktionieren,
                // wenn der spätere Web-Updatekanal noch nicht erreichbar ist.
            }
        }

        var newest = candidates
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

        if (newest is null || newest.Version <= CurrentVersion)
            return null;

        return newest;
    }

    public static async Task<string> PrepareInstallerAsync(UpdateInfo update)
    {
        if (update.IsLocal)
        {
            if (string.IsNullOrWhiteSpace(update.InstallerPath) ||
                !File.Exists(update.InstallerPath))
            {
                throw new FileNotFoundException(
                    "Der neue Installer wurde im lokalen Build-Ordner nicht gefunden.");
            }

            return update.InstallerPath;
        }

        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
            throw new InvalidOperationException("Für dieses Update fehlt die Download-Adresse.");

        var targetDirectory = Path.Combine(
            Path.GetTempPath(),
            "Fassadenplaner",
            "Updates");

        Directory.CreateDirectory(targetDirectory);

        var targetPath = Path.Combine(
            targetDirectory,
            $"Fassadenplaner-Setup-{FormatVersion(update.Version)}.exe");

        using var response = await Http.GetAsync(
            update.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync())
        await using (var destination = File.Create(targetPath))
        {
            await source.CopyToAsync(destination);
        }

        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            var actualHash = await ComputeSha256Async(targetPath);

            if (!actualHash.Equals(
                    update.Sha256.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(targetPath);
                throw new InvalidOperationException(
                    "Die Prüfsumme des Updates stimmt nicht. Das Update wurde aus Sicherheitsgründen verworfen.");
            }
        }

        return targetPath;
    }

    public static void StartInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        });
    }

    private static UpdateInfo? TryReadLocalUpdate()
    {
        var infoPath = Path.Combine(LocalBuildDirectory, "LATEST.txt");
        var installerPath = Path.Combine(
            LocalBuildDirectory,
            "Fassadenplaner-Setup-LATEST.exe");

        if (!File.Exists(infoPath) || !File.Exists(installerPath))
            return null;

        var lines = File.ReadAllLines(infoPath);

        var versionText = lines
            .FirstOrDefault(x =>
                x.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
            ?.Split(':', 2)[1]
            .Trim();

        if (!TryParseVersion(versionText, out var version))
        {
            var runText = lines
                .FirstOrDefault(x =>
                    x.StartsWith("GitHub Run:", StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2)[1]
                .Trim();

            if (!int.TryParse(runText, out var runNumber))
                return null;

            version = new Version(0, 1, runNumber);
        }

        return new UpdateInfo(
            Normalize(version),
            installerPath,
            null,
            "Lokaler GitHub-Actions-Build ist bereit.",
            null,
            true);
    }

    private static async Task<UpdateInfo?> ReadRemoteUpdateAsync()
    {
        using var response = await Http.GetAsync(RemoteManifestUrl);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var manifest = JsonSerializer.Deserialize<RemoteManifest>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (manifest is null ||
            !TryParseVersion(manifest.Version, out var version) ||
            string.IsNullOrWhiteSpace(manifest.DownloadUrl))
        {
            return null;
        }

        return new UpdateInfo(
            Normalize(version),
            null,
            manifest.DownloadUrl,
            manifest.Notes,
            manifest.Sha256,
            false);
    }

    private static bool TryParseVersion(
        string? value,
        out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var clean = value.Trim();

        if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            clean = clean[1..];

        var dashIndex = clean.IndexOf('-');
        if (dashIndex >= 0)
            clean = clean[..dashIndex];

        if (!Version.TryParse(clean, out var parsed) || parsed is null)
            return false;

        version = parsed;
        return true;
    }

    private static Version Normalize(Version version)
        => new(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0));

    private static string FormatVersion(Version version)
        => $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private sealed class RemoteManifest
    {
        public string? Version { get; init; }
        public string? DownloadUrl { get; init; }
        public string? Notes { get; init; }
        public string? Sha256 { get; init; }
    }
}
