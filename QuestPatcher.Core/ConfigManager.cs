using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Utils;
using Serilog;

namespace QuestPatcher.Core
{
    public class ConfigManager
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        static ConfigManager()
        {
            SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public string ConfigPath { get; }

        private Config? _loadedConfig;
        private readonly string _legacyAppIdPath;

        public ConfigManager(SpecialFolders specialFolders)
        {
            ConfigPath = Path.Combine(specialFolders.DataFolder, "config.json");
            _legacyAppIdPath = Path.Combine(specialFolders.DataFolder, "appId.txt");
        }

        /// <summary>
        /// Gets the currently loaded config file, or loads the config file if none is loaded.
        /// Will attempt to recover corrupted configs by overwriting them with the default config.
        /// </summary>
        /// <returns>The loaded config</returns>
        public Config GetOrLoadConfig()
        {
            if (_loadedConfig == null)
            {
                _loadedConfig = LoadConfig();
                _loadedConfig.UseMirrorDownload = false; // force not using mirror
                _loadedConfig.Language = Language.ChineseSimplified; // force Chinese Simplified
                _loadedConfig.AppId = SharedConstants.BeatSaberPackageID; // force beat saber
                _loadedConfig.PatchingOptions.ExternalFiles = true; // most mods need it
                _loadedConfig.ExpertMode = _loadedConfig.ExpertMode; // sync advance options with expert mode
            }

            return _loadedConfig;
        }

        /// <summary>
        /// Loads the config from disk.
        /// If no config file exists, or it cannot be parsed, a default config is created and saved instead.
        /// </summary>
        /// <returns>The loaded config</returns>
        private Config LoadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                Log.Information("No config file found, saving default config");
                return SaveDefaultConfig();
            }

            Log.Information("Loading config . . .");

            Config newConfig;
            try
            {
                using var configStream = File.OpenRead(ConfigPath);
                newConfig = JsonSerializer.Deserialize<Config>(configStream, SerializerOptions)
                            ?? throw new FormatException("Loaded config contained no config object");
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                Log.Warning(ex, "Failed to load the config file, overwriting with default config instead!");
                return SaveDefaultConfig();
            }

            // In the past, an appId.txt file was used to store the app ID
            // Load this into the config, then delete the old file
            if (File.Exists(_legacyAppIdPath))
            {
                Log.Information("Loading app ID from legacy appId.txt");
                newConfig.AppId = File.ReadAllText(_legacyAppIdPath);
                File.Delete(_legacyAppIdPath);
                SaveConfig(newConfig);
            }

            return newConfig;
        }

        private Config SaveDefaultConfig()
        {
            var config = new Config();
            SaveConfig(config);
            return config;
        }

        /// <summary>
        /// Saves the loaded config file.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the config has not been loaded yet</exception>
        public void SaveConfig()
        {
            if (_loadedConfig == null) { throw new InvalidOperationException("Cannot save the config as it has not been loaded yet"); }

            SaveConfig(_loadedConfig);
        }

        private void SaveConfig(Config config)
        {
            Log.Information("Saving config file . . .");
            using var stream = File.Open(ConfigPath, FileMode.Create);
            JsonSerializer.Serialize(stream, config, SerializerOptions);
        }
    }
}
