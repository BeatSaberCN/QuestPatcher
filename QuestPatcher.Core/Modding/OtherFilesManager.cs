using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// Allows the management (viewing/deleting) and uploading of files of particular file extensions to particular folders.
    /// For example, a mod may have support for loading a PNG file. The mod can then state this in its manifest, and we use this
    /// information to provide a file manager for the PNG files for this mod.
    /// </summary>
    public class OtherFilesManager
    {
        /// <summary>
        /// The current folders where files may need to be copied to.
        /// </summary>
        public ObservableCollection<FileCopyType> CurrentDestinations { get; }

        public OtherFilesManager(AndroidDebugBridge debugBridge)
        {
            // Load the file copy paths from resources
            // I put them in there to allow for easier changing, although it makes things a little messier in here
            using var pathsStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuestPatcher.Core.Resources.file-copy-paths.json");
            Debug.Assert(pathsStream != null);

            var serializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Deserialize the FileCopyInfo for each file copy
            var copyInfoIndex = JsonSerializer.Deserialize<List<FileCopyInfo>>(pathsStream, serializerOptions);
            Debug.Assert(copyInfoIndex != null);

            // Copy those into an ObservableCollection of FileCopyType, passing in the debug bridge to allow fetching the files of each type
            CurrentDestinations = [.. copyInfoIndex.Select(info => new FileCopyType(debugBridge, info))];
        }

        /// <summary>
        /// Gets the file copy destinations that can support files of the given extension.
        /// </summary>
        /// <param name="extension">The file extension to search for. May be uppercase or lowercase. May or may not be period prefixed.</param>
        /// <returns>The list of file copy destinations that work with the extension.</returns>
        public List<FileCopyType> GetFileCopyTypes(string extension)
        {
            // Sanitise the extension to remove periods and make it lower case
            extension = extension.Replace(".", "").ToLower();

            return CurrentDestinations.Where(copyType => copyType.SupportedExtensions.Contains(extension)).ToList();
        }

        /// <summary>
        /// Adds the given file copy.
        /// </summary>
        /// <param name="type">The <see cref="FileCopyType"/> to add.</param>
        public void RegisterFileCopy(FileCopyType type)
        {
            CurrentDestinations.Add(type);
        }

        /// <summary>
        /// Removes the given file copy.
        /// </summary>
        /// <param name="type">The <see cref="FileCopyType"/> to remove.</param>
        public void RemoveFileCopy(FileCopyType type)
        {
            CurrentDestinations.Remove(type);
        }
    }
}
