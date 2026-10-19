using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using QuestPatcher.Core.Utils;

namespace QuestPatcher.Core.Models
{
    public class Config : INotifyPropertyChanged
    {
        public string AppId
        {
            get;
            set
            {
                if (value != field)
                {
                    field = value;
                    NotifyPropertyChanged();
                }
            }
        } = SharedConstants.BeatSaberPackageID;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Language Language { get; set; } = Language.Default;

        public bool DisplayLogs
        {
            get;
            set
            {
                if (value != field)
                {
                    field = value;
                    NotifyPropertyChanged();
                }
            }
        }

        // Populated in place when loading, a null value in the config file is ignored
        [JsonInclude]
        [JsonPropertyName("patchingPermissions")]
        [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
        public PatchingOptions PatchingOptions { get; private init => field = value ?? field; } = new();

        public bool ShowPatchingOptions
        {
            get;
            set
            {
                if (value != field)
                {
                    field = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool UseMirrorDownload { get; set; } = true;

        public ExternalModSource ExternalModSource
        {
            get;
            set
            {
                if (value != field)
                {
                    field = value;
                    NotifyPropertyChanged();
                }
            }
        } = ExternalModSource.BSQModsCN;

        public bool ExpertMode
        {
            get;
            set
            {
                if (value != field)
                {
                    field = value;
                    NotifyPropertyChanged();
                }

                if (!value)
                {
                    PatchingOptions.CleanUpMods = true;
                    PatchingOptions.AllowDowngrade = true;
                    PatchingOptions.InstallCoreMods = true;
                }
            }
        }

        public string SelectedThemeName { get; set; } = "Dark";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
