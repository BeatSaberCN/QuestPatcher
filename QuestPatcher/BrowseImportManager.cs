using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QuestPatcher.Core;
using QuestPatcher.Core.CoreMod;
using QuestPatcher.Core.CoreMod.Models;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Utils;
using QuestPatcher.Models;
using QuestPatcher.Resources;
using QuestPatcher.Services;
using QuestPatcher.Utils;
using Serilog;
using Version = SemanticVersioning.Version;

namespace QuestPatcher
{
    /// <summary>
    /// Handles creating browse dialogs for importing files, and also the importing of unknown files
    /// </summary>
    public class BrowseImportManager
    {

        private readonly OtherFilesManager _otherFilesManager;
        private readonly ModManager _modManager;
        private readonly Window _mainWindow;
        private readonly InstallManager _installManager;
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly OperationLocker _locker;
        private readonly QuestPatcherUiService _uiService;
        private readonly SpecialFolders _specialFolders;
        private readonly CoreModsManager _coreModsManager;

        private readonly FilePickerFileType _modsFilter = new("Quest Mods")
        {
            Patterns = new List<string>() { "*.qmod" }
        };

        private Queue<FileImportInfo>? _currentImportQueue;

        public BrowseImportManager(OtherFilesManager otherFilesManager, ModManager modManager, Window mainWindow,
            InstallManager installManager, OperationLocker locker, QuestPatcherUiService uiService,
            ExternalFilesDownloader filesDownloader, SpecialFolders specialFolders, CoreModsManager coreModsManager)
        {
            _otherFilesManager = otherFilesManager;
            _modManager = modManager;
            _mainWindow = mainWindow;
            _installManager = installManager;
            _locker = locker;
            _uiService = uiService;
            _filesDownloader = filesDownloader;
            _specialFolders = specialFolders;
            _coreModsManager = coreModsManager;
        }

        private static FilePickerFileType GetCosmeticFilter(FileCopyType copyType)
        {
            return new FilePickerFileType(copyType.NamePlural)
            {
                Patterns = copyType.SupportedExtensions.Select(extension => $"*.{extension}").ToList()
            };
        }
        
        public async Task InstallApkFromUrl(string url, string? saveFileName = null)
        {
            DialogBuilder dialog = new()
            {
                Title = "即将开始安装",
                Text = "安装可能需要两分钟左右，该过程中将暂时无法点击软件窗口，请耐心等待，\n点击下方“好的”按钮，即可开始安装。",
                HideCancelButton = true
            };
            if (!await dialog.OpenDialogue(_mainWindow)) return;

            _locker.StartOperation();

            try
            {
                string path = Path.Combine(_specialFolders.TempFolder, saveFileName ?? "apkToInstall.apk");
                if (File.Exists(path)) File.Delete(path);
                await _filesDownloader.DownloadUri(url, path, saveFileName ?? "Apk");
                await _installManager.InstallApp(path);
                DialogBuilder builder1 = new()
                {
                    Title = "安装已完成！",
                    Text = "点击确定以继续",
                    HideCancelButton = true
                };
                builder1.OkButton.Text = "确定";
                await builder1.OpenDialogue(_mainWindow);
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to install APK from URL: {Message}", e.Message);
                DialogBuilder builder = new()
                {
                    Title = "出错了！",
                    Text = "APK下载或安装过程中出现了一个意料之外的错误。",
                    HideCancelButton = true
                };
                builder.WithException(e);
                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                _locker.FinishOperation();
            }
        }

        /// <summary>
        /// Opens a browse dialog for installing mods only.
        /// </summary>
        /// <returns>A task that completes when the dialog has closed and the files have been imported</returns>
        public async Task ShowModsBrowse()
        {
            await ShowDialogAndHandleResult(new() { _modsFilter });
        }

        /// <summary>
        /// Opens a browse dialog for installing this particular type of file copy/cosmetic.
        /// </summary>
        /// <param name="cosmeticType"></param>
        /// <returns>A task that completes when the dialog has closed and the files have been imported</returns>
        public async Task ShowFileCopyBrowse(FileCopyType cosmeticType)
        {
            await ShowDialogAndHandleResult(new() { GetCosmeticFilter(cosmeticType) }, cosmeticType);
        }

        private async Task ShowDialogAndHandleResult(List<FilePickerFileType> filters, FileCopyType? knownFileCopyType = null)
        {
            var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = true,
                FileTypeFilter = filters
            });

            if (files == null)
            {
                return;
            }

            await AttemptImportFiles(files.Select(file => new FileImportInfo(file.Path.LocalPath)
            {
                PreferredCopyType = knownFileCopyType
            }).ToList());
        }

        /// <summary>
        /// Imports multiple files, and finds what type they are first.
        /// Will prompt the user with any errors while importing the files.
        /// If a list of files is already importing, these files will be added to the queue
        /// </summary>
        /// <param name="files">The <see cref="FileImportInfo"/> of each file to import.</param>
        public async Task AttemptImportFiles(ICollection<FileImportInfo> files)
        {
            bool queueExisted = _currentImportQueue != null;
            if (_currentImportQueue == null)
            {
                _currentImportQueue = new Queue<FileImportInfo>();
            }

            // Append all files to the new or existing queue
            Log.Debug("Enqueuing {FilesEnqueued} files", files.Count);
            foreach (var importInfo in files)
            {
                _currentImportQueue.Enqueue(importInfo);
            }

            // If a queue already existed, that will be processed with our enqueued files, so we can stop here
            if (queueExisted)
            {
                Log.Debug("Queue is already being processed");
                return;
            }

            // Otherwise, we process the current queue
            Log.Debug("Processing queue . . .");

            // Do nothing if attempting to import files when operations are ongoing that are not file imports
            // TODO: Ideally this would wait until the lock is free and then continue
            if (!_locker.IsFree)
            {
                Log.Error("Failed to process files: Operations are still ongoing");
                _currentImportQueue = null;
                return;
            }
            _locker.StartOperation();
            try
            {
                await ProcessImportQueue();
            }
            finally
            {
                _locker.FinishOperation();
                _currentImportQueue = null;
            }
        }

        /// <summary>
        /// Attempts to download and import a file from a HTTP(S) server.
        /// </summary>
        /// <param name="uri">The URI to download the file from.</param>
        public async Task AttemptImportUri(Uri uri)
        {
            // Download the data to a temporary file. This is necessary as we need a seekable stream.
            var tempFile = new TempFile();
            HttpContentHeaders headers;
            try
            {
                if (_locker.IsFree)
                {
                    // Make sure that the download progress bar is visible
                    _locker.StartOperation();
                }

                // TODO: Should probably make DownloadUri also take a Uri to encourage better error handling when parsing in other parts of the app.
                headers = await _filesDownloader.DownloadUri(uri.ToString(), tempFile.Path, Path.GetFileName(uri.LocalPath));
            }
            catch (FileDownloadFailedException)
            {
                var builder = new DialogBuilder
                {
                    Title = Strings.BrowseImport_DownloadFailed_Title,
                    Text = String.Format(Strings.BrowseImport_DownloadFailed_Text, uri),
                    HideCancelButton = true
                };
                await builder.OpenDialogue(_mainWindow);
                tempFile.Dispose();
                return;
            }
            finally
            {
                _locker.FinishOperation();
            }

            // Get the file name/extension from the headers
            string? extension = Path.GetExtension(headers.ContentDisposition?.FileName?
                // Due to a bug in dotnet, quotes are added at both ends of the filename, so remove these to avoid a mangled file extension.
                .TrimStart('\"')
                .TrimEnd('\"'));
            if (extension == null)
            {
                var builder = new DialogBuilder
                {
                    Title = Strings.BrowseImport_BadUrl_Title,
                    Text = String.Format(Strings.BrowseImport_BadUrl_Text, uri),
                    HideCancelButton = true
                };
                await builder.OpenDialogue(_mainWindow);
                tempFile.Dispose();
                return;
            }

            // Import the downloaded temporary file
            await AttemptImportFiles(new List<FileImportInfo> {
                new FileImportInfo(tempFile.Path)
                {
                    OverrideExtension = extension,
                    IsTemporaryFile = true
                }
            });
        }

        /// <summary>
        /// Processes the current import queue until it reaches zero in size.
        /// Displays exceptions for any failed files
        /// </summary>
        private async Task ProcessImportQueue()
        {
            if (_currentImportQueue == null)
            {
                throw new InvalidOperationException("Cannot process import queue if there is no import queue assigned");
            }

            // Attempt to import each file, and catch the exceptions if any to display them below
            Dictionary<string, Exception> failedFiles = new();
            int totalProcessed = 0; // We cannot know how many files were enqueued in total, so we keep track of that here
            while (_currentImportQueue.TryDequeue(out var importInfo))
            {
                string path = importInfo.Path;
                totalProcessed++;
                try
                {
                    Log.Information("Importing {ImportingFileName} . . .", Path.GetFileName(path));
                    await ImportUnknownFile(importInfo);
                }
                catch (Exception ex)
                {
                    failedFiles[path] = ex;
                }

                if (importInfo.IsTemporaryFile)
                {
                    Log.Debug("Deleting temporary file {Path}", importInfo.Path);
                    try
                    {
                        File.Delete(importInfo.Path);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Failed to delete temporary file", ex);
                    }
                }
            }
            _currentImportQueue = null; // New files added should go to a new queue

            Log.Information("{SuccessfullyProcessed}/{TotalFilesProcessed} files imported successfully", totalProcessed - failedFiles.Count, totalProcessed);

            if (failedFiles.Count == 0) { return; }

            bool multiple = failedFiles.Count > 1;

            var builder = new DialogBuilder
            {
                Title = Strings.BrowseImport_ImportFailed_Title,
                HideCancelButton = true
            };

            if (multiple)
            {
                // Show the exceptions for multiple files in the logs to avoid a giagantic dialog
                builder.Text = Strings.BrowseImport_ImportFailed_Multiple_Text;
                foreach (var pair in failedFiles)
                {
                    Log.Error("{FileName} 安装失败：{Error}", Path.GetFileName(pair.Key), pair.Value.Message);
                    Log.Debug(pair.Value, "Full error");
                }
            }
            else
            {
                // Display single files with more detail for the user
                string filePath = failedFiles.Keys.First();
                var ex = failedFiles.Values.First();
                string fileName = Path.GetFileName(filePath);
                // Don't display the full stack trace for InstallationExceptions, since these are thrown by QP and are not bugs/issues
                if (ex is InstallationException)
                {
                    builder.Text = String.Format(Strings.BrowseImport_ImportFailed_Single_Exception_Text, fileName, ex.Message);
                }
                else
                {
                    builder.Text = String.Format(Strings.BrowseImport_ImportFailed_Single_Text, fileName);
                    builder.WithException(ex);
                }
                Log.Error("Failed to install {FileName}: {Error}", fileName, ex.Message);
                Log.Debug(ex, "Full Error");
            }

            await builder.OpenDialogue(_mainWindow);
        }

        /// <summary>
        /// Attempts to import a ZIP file by extracting the contents to temporary files.
        /// </summary>
        /// <param name="importInfo">The ZIP file to import</param>
        private async Task ImportZip(FileImportInfo importInfo)
        {
            using var zip = ZipFile.OpenRead(importInfo.Path);

            var toEnqueue = new List<FileImportInfo>();

            // Somebody tried dragging in a Beat Saber song, which QP doesn't support copying.
            // Inform the user as such.
            if (zip.Entries.Any(entry => entry.FullName.Equals("info.dat", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InstallationException($"This file appears to be a beat saber song." +
                    " QuestPatcher does not support importing Beat Saber songs.");
            }


            foreach (var entry in zip.Entries)
            {
                // Extract each entry to a temporary file and enqueue it
                var temp = new TempFile();

                Log.Information("Extracting {EntryName}", entry.Name);
                try
                {
                    using var tempStream = File.OpenWrite(temp.Path);
                    using var entryStream = entry.Open();

                    await entryStream.CopyToAsync(tempStream);

                    toEnqueue.Add(new FileImportInfo(temp.Path)
                    {
                        IsTemporaryFile = true,
                        OverrideExtension = Path.GetExtension(entry.FullName),
                    });
                }
                catch (Exception ex)
                {
                    // Make sure the temporary file is deleted if it couldn't be queued.
                    temp.Dispose();
                    Log.Error(ex, "Failed to extract file in ZIP");
                }
            }

            await AttemptImportFiles(toEnqueue);
        }

        /// <summary>
        /// Figures out what the given file is, and installs it accordingly.
        /// Throws an exception if the file cannot be installed by QuestPatcher.
        /// </summary>
        /// <param name="importInfo">Information about the file to import</param>
        private async Task ImportUnknownFile(FileImportInfo importInfo)
        {
            string extension = importInfo.OverrideExtension ?? Path.GetExtension(importInfo.Path).ToLower();

            if (extension == ".zip")
            {
                Log.Information("Extracting ZIP contents to import");
                await ImportZip(importInfo);
                return;
            }

            // Attempt to install as a mod first
            if (await TryImportMod(importInfo))
            {
                return;
            }

            // Attempt to copy the file to the quest as a map, hat or similar
            List<FileCopyType> copyTypes;
            if (importInfo.PreferredCopyType == null || !importInfo.PreferredCopyType.SupportedExtensions.Contains(extension[1..]))
            {
                copyTypes = _otherFilesManager.GetFileCopyTypes(extension);
            }
            else
            {
                // If we already know the file copy type
                // e.g. from dragging into a particular part of the UI, or for browsing for a particular file type,
                // we don't need to prompt on which file copy type to use
                copyTypes = new() { importInfo.PreferredCopyType };
            }

            if (copyTypes.Count > 0)
            {
                FileCopyType copyType;
                if (copyTypes.Count > 1)
                {
                    // If there are multiple different file copy types for this file, prompt the user to decide what they want to import it as
                    var chosen = await OpenSelectCopyTypeDialog(copyTypes, importInfo.Path);
                    if (chosen == null)
                    {
                        Log.Information("No file type selected, cancelling import of {FileName}", Path.GetFileName(importInfo.Path));
                        return;
                    }
                    else
                    {
                        copyType = chosen;
                    }
                }
                else
                {
                    // Otherwise, just use the only type available
                    copyType = copyTypes[0];
                }

                await copyType.PerformCopy(importInfo.Path);
                return;
            }

            // if there are no core mods and the user cancels the import,
            // TryImportMod will return false even though the file is a qmod
            if (extension != ".qmod")
            {
                throw new InstallationException($"未知文件类型 {extension}");
            }

            throw new InstallationException("qmod文件可能损坏或者核心mod缺失");
        }

        /// <summary>
        /// Opens a dialog to allow the user to choose between multiple different file copy destinations to import a file as.
        /// </summary>
        /// <param name="copyTypes">The available file copy types for this file</param>
        /// <param name="path">The path of the file</param>
        /// <returns>The selected FileCopyType, or null if the user pressed cancel/closed the dialog</returns>
        private async Task<FileCopyType?> OpenSelectCopyTypeDialog(List<FileCopyType> copyTypes, string path)
        {
            FileCopyType? selectedType = null;

            var builder = new DialogBuilder
            {
                Title = Strings.BrowseImport_MultipleImport_Title,
                Text = String.Format(Strings.BrowseImport_MultipleImport_Text, Path.GetFileName(path)),
                HideOkButton = true,
                HideCancelButton = true
            };

            List<ButtonInfo> dialogButtons = new();
            foreach (var copyType in copyTypes)
            {
                dialogButtons.Add(new ButtonInfo
                {
                    ReturnValue = true,
                    CloseDialogue = true,
                    OnClick = () =>
                    {
                        selectedType = copyType;
                    },
                    Text = copyType.NameSingular
                });
            }
            builder.WithButtons(dialogButtons);

            await builder.OpenDialogue(_mainWindow);
            return selectedType;
        }

        private async Task<IReadOnlyList<CoreModData>?> GetCoreModsAsync(bool refresh)
        {
            try
            {
                string version = _installManager.InstalledApp!.Version;
                return await _coreModsManager.GetCoreModsAsync(version, refresh);
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to get core mods");
                var builder = new DialogBuilder
                {
                    Title = "无法检查核心Mod", Text = "检查核心Mod时发生了意料之外的错误", HideCancelButton = true
                };
                builder.WithException(e);
                await builder.OpenDialogue(_mainWindow);
                return null;
            }
        }
        
        /**
         * Return true if all core mods are installed or the user want's to ignore missing core mods
         */
        // TODO: Refactor goto
        public async Task<bool> CheckCoreMods(bool manualCheck = false, bool lockTheLocker = false, bool refreshCoreMods = false)
        {
            if (lockTheLocker) _locker.StartOperation();

            var coreMods = await GetCoreModsAsync(refreshCoreMods);
            if (coreMods is null)
            {
                // Failed to load core mods
                goto CoreModsNotOK;
            }

            if (coreMods.Count > 0)
            {
                var missingCoreMods = new List<CoreModData>();
                foreach(var coreMod in coreMods)
                {
                    var existingCoreMod = _modManager.AllMods.Find((mod => mod.Id == coreMod.Id));
                    if (existingCoreMod == null)
                    {
                        // not installed at all, or not for the right version of the game
                        missingCoreMods.Add(coreMod);
                    }
                    else if (Version.TryParse(coreMod.Version, true, out var version) && version > existingCoreMod.Version)
                    {
                        // this coreMod is newer than the installed one
                        // don't allow core mod downgrade when checking against core mod json
                        
                        missingCoreMods.Add(coreMod); // install the new one
                    }
                    else
                    {
                        // the existing one is the "latest", enable it if not already
                        
                        // we can't reliably check existingCoreMod's target game version
                        // existingCoreMod.PackageVersion can be null which we will assume it will work
                        
                        // existingCoreMod.PackageVersion can be not matching the game installed while still
                        // list as the core mod for the installed game version
                        
                        // game downgrade or upgrade from qp will delete all mods
                        
                        if (!existingCoreMod.IsInstalled)
                        {
                            await existingCoreMod.Install();
                        }
                    }
                }
                
                if (missingCoreMods.Count != 0)
                {
                    Log.Warning("Core Mods Missing: {Mods}", missingCoreMods);
                    DialogBuilder builder = new()
                    {
                        Title = "缺失核心Mod",
                        Text = "你缺少了必须要安装的一些核心Mod，这会导致许多第三方Mod无法运行，因为他们均依赖核心Mod。\n" +
                        "而自定义歌曲等基础功能，也是由核心Mod来实现的。\n" +
                        "是否补全核心Mod？"
                    };
                    builder.OkButton.Text = "帮我补全";
                    if (await builder.OpenDialogue(_mainWindow))
                    {
                        await InstallMissingCoreMods(missingCoreMods);
                        goto CoreModsOK;
                    }
                    goto CoreModsNotOK;
                } 
                if (manualCheck)
                {
                    DialogBuilder builder = new()
                    {
                        Title = "核心Mod安装正确！",
                        Text = "恭喜你，你已经装好了核心Mod！",
                        HideCancelButton = true
                    };
                    await builder.OpenDialogue(_mainWindow);
                }
                goto CoreModsOK;
            }
            else
            {
                DialogBuilder builder = new()
                {
                    Title = "未找到该版本的核心Mod！",
                    Text = $"你当前安装的游戏版本为{_installManager.InstalledApp?.Version ?? "null"}，但核心Mod还没有更新，还没有适配该版本，所以无法安装核心Mod。\n" +
                    $"你可以先降级游戏再重新打补丁装Mod。\n如需降级请查看新手教程左下角",
                    HideCancelButton = manualCheck
                };
                builder.OkButton.Text = manualCheck ? "我知道了！" : "仍然安装";
                builder.WithButtons(
                    new ButtonInfo
                    {
                        Text = "进入新手教程",
                        ReturnValue = true,
                        OnClick = () => Util.OpenWebpage("https://bs.wgzeyu.com/oq-guide-qp/")
                    });
                
                if (await builder.OpenDialogue(_mainWindow))
                {
                    goto CoreModsOK;
                }
                else
                {
                    goto CoreModsNotOK;
                }
            }
            
            CoreModsOK:
            if (lockTheLocker) _locker.FinishOperation();
            return true;
            
            CoreModsNotOK:
            if (lockTheLocker) _locker.FinishOperation();
            return false;
            
        }

        private async Task<bool> InstallMissingCoreMods(IList<CoreModData> mods) 
        {
            foreach(var mod in mods)
            {
                string modUrl = mod.DownloadLink;
                if (_uiService.Config.UseMirrorDownload) modUrl = await DownloadMirrorUtil.Instance.GetMirrorUrl(modUrl);
                string path = Path.Combine(_specialFolders.TempFolder, mod.Filename ?? "coremod_tmp.qmod");
                await _filesDownloader.DownloadUri(modUrl, path, mod.Filename ?? mod.Id);
                await TryImportMod(new FileImportInfo(path) { IsTemporaryFile = true }, false, false);
            }
            await _modManager.SaveMods();
            return true;
        }
        
        /// <summary>
        /// Imports then installs a mod.
        /// Will prompt to ask the user if they want to install the mod in the case that it is outdated
        /// </summary>
        /// <param name="importInfo">Information about the mod file to import.</param>
        /// <param name="checkCoreMods">Whether to check core mods before import the mod</param>
        /// <param name="checkPackageVersion">Whether to check the package version indicated in the mod manifest</param>
        /// <returns>Whether or not the file could be imported as a mod</returns>
        public async Task<bool> TryImportMod(FileImportInfo importInfo, bool checkCoreMods = true, bool checkPackageVersion = true)
        {
            if (checkCoreMods)
                if (!await CheckCoreMods())
                    return false;

            // Import the mod file and copy it to the quest
            var mod = await _modManager.TryParseMod(importInfo.Path, importInfo.OverrideExtension);
            if (mod is null)
            {
                return false;
            }

            if (mod.ModLoader != _installManager.InstalledApp?.ModLoader)
            {
                var builder = new DialogBuilder
                {
                    Title = Strings.Mod_WrongModLoader_Title,
                    Text = String.Format(Strings.Mod_WrongModLoader_Text, mod.ModLoader, _installManager.InstalledApp?.ModLoader)
                };
                builder.OkButton.Text = Strings.Mod_WrongModLoader_Repatch;
                builder.CancelButton.Text = Strings.Generic_NotNow;
                if (await builder.OpenDialogue(_mainWindow))
                {
                    _uiService.OpenRepatchMenu(mod.ModLoader);
                }

                return true;
            }

            Debug.Assert(_installManager.InstalledApp != null);

            // Prompt the user for outdated mods instead of enabling them automatically
            if (checkPackageVersion && mod.PackageVersion != null && mod.PackageVersion != _installManager.InstalledApp.Version)
            {
                var builder = new DialogBuilder
                {
                    Title = Strings.Mod_OutdatedMod_Title,
                    Text = String.Format(Strings.Mod_OutdatedMod_Text, mod.PackageVersion, _installManager.InstalledApp.Version),
                };
                builder.OkButton.Text = Strings.Mod_OutdatedMod_EnableNow;
                builder.CancelButton.Text = Strings.Generic_Cancel;

                if (!await builder.OpenDialogue(_mainWindow))
                {
                    return true;
                }
            }

            // Automatically install the mod once it has been imported
            // TODO: Is this desirable? Would it make sense to require it to be enabled manually
            await mod.Install();
            await _modManager.SaveMods();
            return true;
        }
    }
}
