using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace QuestPatcher.Core.CoreMod.Models
{
    public sealed record CoreMods
    {
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; }

        [JsonPropertyName("mods")]
        public List<CoreModData> Mods { get; }

        [JsonConstructor]
        public CoreMods(DateTime lastUpdated, List<CoreModData> mods)
        {
            LastUpdated = lastUpdated;
            Mods = mods;
        }

        private bool PrintMembers(StringBuilder builder)
        {
            builder.Append($"LastUpdated = {LastUpdated}, Mods = [{string.Join(", ", Mods)}]");
            return true;
        }
    };
}
