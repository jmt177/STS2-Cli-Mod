using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen;
using MegaCrit.Sts2.Core.Saves;
using STS2.Cli.Mod.Actions.Utils;
using STS2.Cli.Mod.Models.Messages;
using STS2.Cli.Mod.State;
using STS2.Cli.Mod.Utils;

namespace STS2.Cli.Mod.Actions;

/// <summary>
///     Handles the <c>switch_profile</c> CLI command.
///     Switches to one of the game's 3 fixed local save-profile slots via the main menu's
///     profile screen (<c>NMainMenu.OpenProfileScreen</c> -&gt; click the target
///     <see cref="NProfileButton" />), the same path the in-game UI uses.
/// </summary>
/// <remarks>
///     <para>
///         <b>CLI command:</b> <c>sts2 switch_profile &lt;profile_id&gt;</c> (1-3)
///     </para>
///     <para><b>Scene:</b> Main menu.</para>
///     <para>
///         Intended for testing: switching to an unused profile gives a disposable save
///         (no run, no deck/relic history) to freely exercise other commands on, without
///         touching the player's main profile. Switching only loads the target profile's own
///         save data (<c>SaveManager.SwitchProfileId</c> + re-init prefs/progress); it never
///         writes to the profile being switched away from.
///     </para>
/// </remarks>
public static class SwitchProfileHandler
{
    private static readonly ModLogger Logger = new("SwitchProfileHandler");

    /// <summary>
    ///     Executes the switch_profile command.
    ///     Must be called on the Godot main thread (via <see cref="MainThreadExecutor" />).
    /// </summary>
    public static async Task<object> ExecuteAsync(Request request)
    {
        if (request.Args == null || request.Args.Length < 1)
            return Responses.Error("MISSING_ARGUMENT", "Profile id required (1-3)");

        var profileId = request.Args[0];
        if (profileId is < 1 or > 3)
            return Responses.Error("INVALID_PROFILE_ID", $"Profile id must be 1-3, got {profileId}");

        Logger.Info($"Requested to switch to profile {profileId}");

        try
        {
            // --- Guard: Must be on the MENU screen ---
            var currentScreen = StateHandler.DetectScreen();
            if (currentScreen != "MENU")
                return Responses.Error("NOT_ON_MENU", $"Not on main menu screen (current: {currentScreen})");

            var mainMenu = NGame.Instance?.MainMenu;
            if (mainMenu == null)
                return Responses.Error("UI_NOT_FOUND", "Main menu not found");

            // Already on the requested profile: nothing to do (clicking this button in the
            // real UI would just close the profile screen, not re-trigger a switch).
            if (SaveManager.Instance.CurrentProfileId == profileId)
                return Responses.Ok(new { profile_id = profileId, already_active = true });

            // --- Open the profile screen ---
            Logger.Info("Opening profile screen");
            mainMenu.OpenProfileScreen();

            var screenOpened = await ActionUtils.PollUntilAsync(
                () => UiUtils.FindProfileScreen() != null, ActionUtils.UiTimeoutMs);

            if (!screenOpened)
                return Responses.Error("UI_NOT_FOUND", "Profile screen did not open in time");

            var profileScreen = UiUtils.FindProfileScreen();
            var button = profileScreen?.GetNodeOrNull<NProfileButton>($"%ProfileButton{profileId}");
            if (button == null)
                return Responses.Error("UI_NOT_FOUND", $"Profile button {profileId} not found");

            // --- Click the target profile's button and wait for the switch to complete ---
            Logger.Info($"Clicking profile button {profileId}");
            button.ForceClick();

            var switched = await ActionUtils.PollUntilAsync(
                () => SaveManager.Instance.CurrentProfileId == profileId && StateHandler.DetectScreen() == "MENU",
                ActionUtils.ActionTimeoutMs);

            if (!switched)
            {
                Logger.Warning("Timed out waiting for profile switch to complete");
                return Responses.Ok(new { profile_id = profileId },
                    "Timed out waiting for profile switch to complete");
            }

            Logger.Info($"Switched to profile {profileId}");
            return Responses.Ok(new { profile_id = profileId, screen = "MENU" });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to switch profile: {ex.Message}");
            return Responses.Error("INTERNAL_ERROR", ex.Message);
        }
    }
}
