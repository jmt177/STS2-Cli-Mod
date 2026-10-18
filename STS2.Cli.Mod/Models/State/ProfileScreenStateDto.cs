using System.Diagnostics.CodeAnalysis;

namespace STS2.Cli.Mod.Models.State;

/// <summary>
///     Profile screen state DTO. The game has 3 fixed local save-profile slots
///     (independent save files, saved relics/decks/run-history); this screen lets the
///     player switch between them.
/// </summary>
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public class ProfileScreenStateDto
{
    /// <summary>
    ///     The currently active profile id (1-3).
    /// </summary>
    public int CurrentProfileId { get; set; }
}
