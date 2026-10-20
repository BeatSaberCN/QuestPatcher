namespace QuestPatcher.Core.Models;

/// <summary>
///     Represents the result of the startup process.
/// </summary>
public enum StartupResult
{
    /// <summary>
    ///     The startup process completed successfully and the core components are loaded.
    /// </summary>
    Loaded,

    /// <summary>
    ///     The startup process failed because the game is not installed on the device.
    /// </summary>
    GameNotInstalled,

    /// <summary>
    ///     The startup process failed because a cracked version of the game was detected.
    /// </summary>
    GameIsCracked
}

