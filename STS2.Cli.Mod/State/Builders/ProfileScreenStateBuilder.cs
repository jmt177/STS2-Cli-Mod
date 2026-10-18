using MegaCrit.Sts2.Core.Saves;
using STS2.Cli.Mod.Models.State;
using STS2.Cli.Mod.Utils;

namespace STS2.Cli.Mod.State.Builders;

/// <summary>
///     Builds <see cref="ProfileScreenStateDto" /> from <see cref="SaveManager" />.
/// </summary>
public static class ProfileScreenStateBuilder
{
    private static readonly ModLogger Logger = new("ProfileScreenStateBuilder");

    /// <summary>
    ///     Builds the profile screen state. Returns null if the profile screen is not open
    ///     or the current profile id is unavailable.
    /// </summary>
    public static ProfileScreenStateDto? Build()
    {
        try
        {
            if (UiUtils.FindProfileScreen() == null)
            {
                Logger.Warning("NProfileScreen not found in main menu submenu stack");
                return null;
            }

            return new ProfileScreenStateDto { CurrentProfileId = SaveManager.Instance.CurrentProfileId };
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to build profile screen state: {ex.Message}");
            return null;
        }
    }
}
