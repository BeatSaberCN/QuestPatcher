using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Models;
using QuestPatcher.Resources;
using QuestPatcher.Utils;
using QuestPatcher.ViewModels;
using QuestPatcher.ViewModels.ModBrowser;
using QuestPatcher.ViewModels.Modding;
using QuestPatcher.Views;
using Serilog;
using Serilog.Events;

namespace QuestPatcher.Services
{
    /// <summary>
    /// Implementation of QuestPatcherService that uses UI message boxes and creates the viewmodel for displaying in UI
    /// </summary>
    public class QuestPatcherUiService : QuestPatcherService
    {
        private readonly Window _mainWindow;

        private readonly IClassicDesktopStyleApplicationLifetime _appLifetime;

        private LoggingViewModel? _loggingViewModel;
        private OperationLocker? _operationLocker;
        private BrowseImportManager? _browseManager;
        private OtherItemsViewModel? _otherItemsView;
        private PatchingViewModel? _patchingView;
        private AboutViewModel? _aboutView;
        private BrowseModViewModel? _browseModView;

        private readonly ThemeManager _themeManager;
        private bool _isShuttingDown;
        private bool _updateChecked;

        public QuestPatcherUiService(IClassicDesktopStyleApplicationLifetime appLifetime) : base(new UIPrompter())
        {
            _appLifetime = appLifetime;
            _themeManager = new ThemeManager(Config, SpecialFolders);

            // Deal with language configuration before we load the UI
            try
            {
                var language = Config.Language.ToCultureInfo();
                Strings.Culture = language;
            }
            catch (Exception)
            {
                Log.Warning("Failed to set language from config: {Code}", Config.Language);
                Config.Language = Language.Default;
                Strings.Culture = null;
            }

            _mainWindow = PrepareUi();

            _appLifetime.MainWindow = _mainWindow;
            var prompter = (UIPrompter) Prompter;
            prompter.Init(_mainWindow);

            _mainWindow.Opened += async (_, _) => await LoadAndHandleErrors();
            _mainWindow.Closing += OnMainWindowClosing;
        }

        private Window PrepareUi()
        {
            _loggingViewModel = new LoggingViewModel();
            MainWindow window = new()
            {
                Width = 900,
                Height = 550
            };
            _operationLocker = new();
            _operationLocker.StartOperation(); // Still loading
            _browseManager = new BrowseImportManager(OtherFilesManager, ModManager, window, InstallManager,
                _operationLocker, this, FilesDownloader, SpecialFolders, CoreModManager);
            ProgressViewModel progressViewModel = new(_operationLocker, FilesDownloader);
            _otherItemsView = new OtherItemsViewModel(OtherFilesManager, window, _browseManager, _operationLocker, progressViewModel);
            _patchingView = new PatchingViewModel(Config, _operationLocker, PatchingManager, InstallManager,
                CoreModManager, DowngradeManger, window, progressViewModel, FilesDownloader);
            _aboutView = new AboutViewModel(progressViewModel);
            _browseModView = new BrowseModViewModel(window, Config, _operationLocker, progressViewModel, InstallManager,
                ModManager, ExternalModManager);

            MainWindowViewModel mainWindowViewModel = new(
                new LoadedViewModel(
                    _patchingView,
                    new ManageModsViewModel(ModManager, InstallManager, window, _operationLocker, progressViewModel, _browseManager),
                    _loggingViewModel,
                    new ToolsViewModel(Config, progressViewModel, _operationLocker, window, SpecialFolders, InstallManager, DebugBridge, this, InfoDumper,
                        _themeManager, _browseManager, ModManager, ExitApplication),
                    _otherItemsView,
                    _aboutView,
                    Config,
                    InstallManager,
                    _browseManager,
                    _browseModView
                ),
                new LoadingViewModel(progressViewModel, _loggingViewModel, Config),
                this
            );
            window.DataContext = mainWindowViewModel;

            return window;
        }

        private async Task LoadAndHandleErrors()
        {
            Debug.Assert(_operationLocker != null); // Main window has been loaded, so this is assigned
            if (_operationLocker.IsFree) // Necessary since the operation may have started earlier if this is the first load. Otherwise, we need to start the operation on subsequent loads
            {
                _operationLocker.StartOperation();
            }

            if (!_updateChecked)
            {
                _ = CheckForUpdates(); // Check for updates in the background
                _updateChecked = true;
            }

            StartupResult result;
            DialogBuilder builder;
            try
            {
                result = await RunStartup();
            }
            catch (Exception ex)
            {
                builder = new DialogBuilder
                {
                    Title = Strings.Loading_UnhandledError_Title,
                    Text = Strings.Loading_UnhandledError_Text,
                    HideCancelButton = true,
                };
                builder.WithException(ex);
                await builder.OpenDialogue(_mainWindow);
                Log.Error($"Exiting QuestPatcher due to unhandled load error: {ex}");
                ExitApplication();
                return;
            }
            finally
            {
                _operationLocker.FinishOperation();
            }

            switch (result)
            {
                case StartupResult.Loaded:
                    // Files are not loaded during startup, since we need to check ADB status first
                    // So instead, we refresh the currently selected file copy after starting, if there is one
                    if (_otherItemsView != null)
                    {
                        await _otherItemsView.RefreshFiles();
                    }

                    break;
                case StartupResult.GameNotInstalled:
                    builder = new DialogBuilder
                    {
                        Title = "尚未安装BeatSaber",
                        Text = "请先安装正版BeatSaber！",
                        HideCancelButton = true,
                        OkButton = { Text = "安装APK" }
                    };

                    bool success = await builder.OpenDialogue(_mainWindow) && await OpenGameInstallerMenu(false);
                    if (!success)
                    {
                        ExitApplication();
                    }

                    break;
                case StartupResult.GameIsCracked:
                    builder = new DialogBuilder
                    {
                        Title = "非原版BeatSaber！",
                        Text = "检测到已安装的BeatSaber版本可能存在异常，\n" +
                               "你安装的游戏有可能是盗版，QuestPatcher不兼容盗版，请支持正版！",
                        HideOkButton = true,
                        CancelButton = { Text = Strings.Generic_OK }
                    };

                    var button1 = new ButtonInfo
                    {
                        Text = "为何不兼容盗版？",
                        CloseDialogue = false,
                        ReturnValue = false,
                        OnClick = () => Util.OpenWebpage("https://bs.wgzeyu.com/oq-guide-qp/#sbwc8866")
                    };

                    var button2 = new ButtonInfo
                    {
                        Text = "如何购买正版？",
                        CloseDialogue = false,
                        ReturnValue = false,
                        OnClick = () => Util.OpenWebpage("https://bs.wgzeyu.com/buy/#bs_quest")
                    };

                    var button3 = new ButtonInfo { Text = "卸载当前版本", CloseDialogue = true, ReturnValue = true };

                    builder.WithButtons(button1, button2, button3);

                    if (await builder.OpenDialogue(_mainWindow))
                    {
                        _operationLocker.StartOperation();
                        try
                        {
                            await InstallManager.UninstallApp();
                        }
                        catch (Exception e)
                        {
                            Log.Error(e, "Failed to uninstall cracked game");
                        }
                        finally
                        {
                            _operationLocker.FinishOperation();
                        }
                    }

                    ExitApplication();
                    break;
                default:
                    Log.Fatal("Unexpected startup result: {Result}", result);
                    throw new UnreachableException("Unexpected startup result: " + result);
            }
        }

        private async void OnMainWindowClosing(object? sender, CancelEventArgs args)
        {
            Debug.Assert(_operationLocker != null);

            // Avoid showing this prompt if not in an operation, or if we are closing the window from exiting the application
            if (_operationLocker.IsFree || _isShuttingDown) return;

            // Closing while operations are in progress is a bad idea, so we warn the user
            // We must set this to true at first, even if the user might press OK later.
            // This is since the caller of the event will not wait for our async handler to finish
            args.Cancel = true;
            var builder = new DialogBuilder
            {
                Title = Strings.Prompt_OperationInProgress_Title,
                Text = Strings.Prompt_OperationInProgress_Text
            };
            builder.OkButton.Text = Strings.Generic_CloseAnyway;

            // Now we can exit the application if the user decides to
            if (await builder.OpenDialogue(_mainWindow))
            {
                ExitApplication();
            }
        }

        /// <summary>
        /// Opens a window that allows the user to change the modloader they have installed by re-patching their app.
        /// </summary>
        /// <param name="preferredModloader">The modloader that will be selected for patching by default. The user can change this.</param>
        public async void OpenRepatchMenu(ModLoader? preferredModloader = null)
        {
            if (preferredModloader != null)
            {
                Config.PatchingOptions.ModLoader = (ModLoader) preferredModloader;
            }

            Window menuWindow = new RepatchWindow();
            menuWindow.DataContext = new RepatchWindowViewModel(_patchingView!, Config, menuWindow);
            menuWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            await menuWindow.ShowDialog(_mainWindow);
        }

        public void OpenDowngradeMenu()
        {
            Window downgradeWindow = new DowngradeWindow();
            var vm = new DowngradeViewModel(downgradeWindow, _mainWindow, Config, InstallManager, DowngradeManger,
                _operationLocker!);
            downgradeWindow.DataContext = vm;
            downgradeWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _ = downgradeWindow.ShowDialog(_mainWindow);
        }

        /// <summary>
        /// Open the game installer menu to install a new version of the game.
        /// Will reload QuestPatcher if the installation is successful.
        /// </summary>
        /// <param name="isVersionSwitching">Whether we are switching versions or freshly installing</param>
        /// <returns>Whether new apk is successfully installed</returns>
        public async Task<bool> OpenGameInstallerMenu(bool isVersionSwitching)
        {
            Window downgradeWindow = new GameInstallerWindow();
            var vm = new GameInstallerViewModel(isVersionSwitching, downgradeWindow, this, _operationLocker!, InstallManager, ModManager);
            downgradeWindow.DataContext = vm;
            downgradeWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _ = downgradeWindow.ShowDialog(_mainWindow);
            bool succeeded = await vm.NewAppInstalled;
            if (succeeded)
            {
                DialogBuilder builder1 = new()
                {
                    Title = "安装已完成！",
                    Text = "点击确定以重启QuestPatcher",
                    HideCancelButton = true
                };
                await builder1.OpenDialogue(_mainWindow);
                await Reload();
            }

            return succeeded;
        }

        private async Task Reload()
        {
            if (_loggingViewModel != null)
            {
                _loggingViewModel.LoggedText = ""; // Avoid confusing people by not showing existing logs
            }

            ModManager.Reset();
            InstallManager.ResetInstalledApp();
            await LoadAndHandleErrors();
        }

        protected override void SetLoggingOptions(LoggerConfiguration configuration)
        {
            configuration.MinimumLevel.Verbose()
                .WriteTo.File(Path.Combine(SpecialFolders.LogsFolder, "log.log"), LogEventLevel.Verbose, "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Console()
                .WriteTo.Sink(
                new StringDelegateSink(str =>
                {
                    if (_loggingViewModel != null)
                    {
                        _loggingViewModel.LoggedText += str + "\n";
                    }
                }),
                LogEventLevel.Information
            );
        }

        protected override void ExitApplication()
        {
            _isShuttingDown = true;
            _appLifetime.Shutdown();
        }
    }
}
