using System;
using System.Collections.Generic;
using System.Text;
using QuestPatcher.Core.Models;
using Version = SemanticVersioning.Version;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// Exception thrown for errors while parsing, installing, and uninstalling mods, alongside the importing of file copies and other files.
    /// </summary>
    public class InstallationException : Exception
    {
        public InstallationException(string message) : base(message) { }
        public InstallationException(string? message, Exception cause) : base(message, cause) { }
    }

    /// <summary>
    /// Thrown when a mod is made for a different app than the one being modded.
    /// </summary>
    public class ModWrongAppException : InstallationException
    {
        public string PackageId { get; }
        public string AppId { get; }

        public ModWrongAppException(string packageId, string appId)
            : base($"Mod is intended for app {packageId}, but {appId} is selected")
        {
            PackageId = packageId;
            AppId = appId;
        }
    }

    /// <summary>
    /// Thrown when installing an older version of an installed mod while mod downgrades are blocked.
    /// </summary>
    public class ModDowngradeException : InstallationException
    {
        public string ModId { get; }
        public Version ExistingVersion { get; }
        public Version NewVersion { get; }

        public ModDowngradeException(string modId, Version existingVersion, Version newVersion)
            : base($"Version of existing {modId} ({existingVersion}) is greater than installing version ({newVersion}). Direct version downgrades are not permitted")
        {
            ModId = modId;
            ExistingVersion = existingVersion;
            NewVersion = newVersion;
        }
    }

    /// <summary>
    /// A mod that depends on a version range of another mod, which the new version of that mod is not in.
    /// </summary>
    public record DependencyConflict(string DependantId, SemanticVersioning.Range VersionRange);

    /// <summary>
    /// Thrown when upgrading or downgrading a mod would break installed mods that depend on it.
    /// </summary>
    public class ModDependencyConflictException : InstallationException
    {
        public string ModId { get; }
        public Version NewVersion { get; }
        public IReadOnlyList<DependencyConflict> Conflicts { get; }

        public ModDependencyConflictException(string modId, Version newVersion, IReadOnlyList<DependencyConflict> conflicts)
            : base(CreateMessage(modId, newVersion, conflicts))
        {
            ModId = modId;
            NewVersion = newVersion;
            Conflicts = conflicts;
        }

        private static string CreateMessage(string modId, Version newVersion, IReadOnlyList<DependencyConflict> conflicts)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Failed to upgrade installation of mod {modId} to {newVersion}: ");
            foreach (var conflict in conflicts)
            {
                builder.AppendLine($"Dependency of mod {conflict.DependantId} requires version range {conflict.VersionRange} of {modId}, however the version of {modId} being upgraded to ({newVersion}) does not intersect this range");
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Thrown when a mod needs a different mod loader than the one the app is patched with.
    /// </summary>
    public class ModLoaderMismatchException : InstallationException
    {
        public string ModId { get; }
        public ModLoader ModLoader { get; }
        public ModLoader? InstalledModLoader { get; }

        public ModLoaderMismatchException(string modId, ModLoader modLoader, ModLoader? installedModLoader)
            : base("Mod loader mis-match")
        {
            ModId = modId;
            ModLoader = modLoader;
            InstalledModLoader = installedModLoader;
        }
    }
}
