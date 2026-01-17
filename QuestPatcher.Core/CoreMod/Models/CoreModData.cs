using System;
using System.Text.Json.Serialization;
using Version = SemanticVersioning.Version;

namespace QuestPatcher.Core.CoreMod.Models
{
    public sealed record CoreModData
    {
        [JsonPropertyName("id")]
        public string Id { get; }

        [JsonPropertyName("version")]
        public string Version { get; }

        [JsonPropertyName("downloadLink")]
        public string DownloadLink { get; }

        [JsonPropertyName("filename")]
        public string? Filename { get; }

        public Version? SemVer { get; }

        public Uri DownloadUri { get; }

        [JsonConstructor]
        public CoreModData(string id, string version, string downloadLink, string filename)
        {
            Id = id;
            Version = version;
            SemVer = SemanticVersioning.Version.TryParse(version, out var semVer) ? semVer : null;
            DownloadLink = downloadLink;
            DownloadUri = new Uri(downloadLink);
            Filename = filename;
        }

        public override string ToString()
        {
            return $"{Id}@{Version}";
        }
    }
}
