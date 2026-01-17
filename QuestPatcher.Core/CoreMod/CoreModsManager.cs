using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuestPatcher.Core.CoreMod.Models;
using QuestPatcher.Core.Utils;
using Serilog;

namespace QuestPatcher.Core.CoreMod
{
    public class CoreModsManager : SharableLoading<ReadOnlyDictionary<string, CoreMods>>
    {
        private const string BeatSaberCoreModsUrl =
            "https://raw.githubusercontent.com/QuestPackageManager/bs-coremods/main/core_mods.json";

        private readonly HttpClient _client = new();

        protected override async Task<ReadOnlyDictionary<string, CoreMods>> LoadAsync(CancellationToken cToken)
        {
            Log.Information("Loading Core Mods");
            string res = await _client.GetStringAsync(BeatSaberCoreModsUrl, cToken);
            var coreMods = JsonSerializer.Deserialize<Dictionary<string, CoreMods>>(res);

            if (coreMods == null)
            {
                throw new Exception("Failed to deserialize core mods, invalid data structure");
            }

            Log.Debug("Loaded core mods for {CoreMods} versions", coreMods.Count);
            return new ReadOnlyDictionary<string, CoreMods>(coreMods);
        }

        public async Task<IReadOnlyList<CoreModData>> GetCoreModsAsync(string version, bool refresh = false,
            CancellationToken cToken = default)
        {
            var data = await GetOrLoadAsync(refresh, cToken);
            if (data.TryGetValue(version, out var coreMods))
            {
                return coreMods.Mods.AsReadOnly();
            }

            return Array.Empty<CoreModData>();
        }
    }
}
