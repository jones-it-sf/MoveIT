using System.Runtime.Versioning;
using System.Text.Json;
using MoveIT.Core.Util;

namespace MoveIT.Core.Migration;

/// <summary>One folder per migrated profile, holding registry exports and the rollback manifest.</summary>
[SupportedOSPlatform("windows")]
public sealed class BackupSession
{
    public const string ManifestFile = "manifest.json";
    public const string SourceProfileListFile = "ProfileList-source.reg";
    public const string TargetProfileListFile = "ProfileList-target.reg";
    public const string SourceProfileGuidFile = "ProfileGuid-source.reg";

    private BackupSession(string directory, MigrationManifest manifest)
    {
        Directory = directory;
        Manifest = manifest;
    }

    public string Directory { get; }

    public MigrationManifest Manifest { get; }

    private static string RegExe => Path.Combine(Environment.SystemDirectory, "reg.exe");

    public static BackupSession Create(string root, string label, MigrationManifest manifest)
    {
        root = AppDirectories.Ensure(root);
        var baseName = $"{DateTime.Now:yyyyMMdd-HHmmss}_{label}";
        var path = Path.Combine(root, baseName);
        for (int suffix = 2; System.IO.Directory.Exists(path); suffix++)
        {
            path = Path.Combine(root, $"{baseName}-{suffix}");
        }

        System.IO.Directory.CreateDirectory(path);
        var session = new BackupSession(path, manifest);
        session.Save();
        return session;
    }

    public static BackupSession Load(string directory)
    {
        var file = Path.Combine(directory, ManifestFile);
        if (!File.Exists(file))
        {
            throw new MigrationException($"{directory} does not contain a MoveIT {ManifestFile}.");
        }

        var manifest = JsonSerializer.Deserialize(File.ReadAllText(file), MoveItJsonContext.Default.MigrationManifest)
            ?? throw new MigrationException($"{file} is empty.");
        return new BackupSession(directory, manifest);
    }

    public void Save()
    {
        var file = Path.Combine(Directory, ManifestFile);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Manifest, MoveItJsonContext.Default.MigrationManifest));
        File.Move(temporary, file, overwrite: true);
    }

    public void MarkStep(string step)
    {
        Manifest.CompletedSteps.Add(step);
        Save();
    }

    public bool HasFile(string fileName) => File.Exists(Path.Combine(Directory, fileName));

    public void ExportRegistryKey(string localMachinePath, string fileName)
    {
        var result = ProcessRunner.Run(
            RegExe,
            ["export", $@"HKLM\{localMachinePath}", Path.Combine(Directory, fileName), "/y", "/reg:64"],
            TimeSpan.FromMinutes(1));
        if (result.ExitCode != 0)
        {
            throw new MigrationException($@"Could not back up HKLM\{localMachinePath}: {result.Output.Trim()}");
        }
    }

    public void ImportRegistryFile(string fileName)
    {
        var result = ProcessRunner.Run(RegExe, ["import", Path.Combine(Directory, fileName), "/reg:64"], TimeSpan.FromMinutes(1));
        if (result.ExitCode != 0)
        {
            throw new MigrationException($"Could not restore {fileName}: {result.Output.Trim()}");
        }
    }
}
