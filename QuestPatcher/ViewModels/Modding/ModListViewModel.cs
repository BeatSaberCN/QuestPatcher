using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using QuestPatcher.Core;
using QuestPatcher.Core.Modding;
using QuestPatcher.Models;
using QuestPatcher.Utils;
using ReactiveUI;

namespace QuestPatcher.ViewModels.Modding
{
    public class ModListViewModel : ViewModelBase
    {
        public string Title { get; }

        public bool ShowBrowse { get; }
        
        private bool _showDownloadLocalization = true;

        public bool ShowDownloadLocalization
        {
            get => _showDownloadLocalization;
            private set
            {
                _showDownloadLocalization = value;
                this.RaisePropertyChanged();
            }
        }

        public OperationLocker Locker { get; }
        public ObservableCollection<ModViewModel> DisplayedMods { get; } = new();

        private readonly BrowseImportManager _browseManager;

        public ModListViewModel(string title, bool showBrowse, ObservableCollection<IMod> mods, ModManager modManager, InstallManager installManager, Window mainWindow, OperationLocker locker, BrowseImportManager browseManager)
        {
            Title = title;
            ShowBrowse = showBrowse;
            Locker = locker;
            _browseManager = browseManager;

            // There's probably a better way to create my ModViewModel for the mods in this ObservableCollection
            // If there if, please tell me/PR it.
            // I can't just use the mods directly because I want to add prompts for installing/uninstalling (e.g. incorrect game version)
            mods.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                {
                    DisplayedMods.Clear();
                    ShowDownloadLocalization = false;
                    return;
                }

                bool newItemsContainsPolyglot = false;
                if (args.NewItems != null)
                {
                    foreach (IMod mod in args.NewItems)
                    {
                        if (mod.Id == "PolyglotInject")
                        {
                            ShowDownloadLocalization = false;
                            newItemsContainsPolyglot = true;
                        }
                        DisplayedMods.Add(new ModViewModel(mod, modManager, installManager, mainWindow, locker));
                    }
                }
                if (args.OldItems != null)
                {
                    foreach (IMod mod in args.OldItems)
                    {
                        if (!newItemsContainsPolyglot && mod.Id == "PolyglotInject") // it may be moved so it is still in the collections
                        {
                            ShowDownloadLocalization = true;
                        }
                        DisplayedMods.Remove(DisplayedMods.Single(modView => modView.Mod == mod));
                    }
                }
            };
        }

        public async void OnBrowseClick()
        {
            await _browseManager.ShowModsBrowse();
        }
        
        public async void OnCheckCoreModsClick()
        {
            await _browseManager.CheckCoreMods();
        }

        public void OnDownloadLocalizationClick()
        {
            Util.OpenWebpage("https://github.com/qe201020335/PolyglotInject/releases");
        }
    }
}
