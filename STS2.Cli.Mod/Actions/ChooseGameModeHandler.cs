using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using STS2.Cli.Mod.Actions.Utils;
using STS2.Cli.Mod.Models.Messages;
using STS2.Cli.Mod.State;
using STS2.Cli.Mod.Utils;

namespace STS2.Cli.Mod.Actions;

/// <summary>
///     Handles the <c>choose_game_mode</c> CLI command.
///     Selects a game mode (standard, daily, custom) from the singleplayer submenu,
///     which navigates to the corresponding screen (character select, daily run, or custom run).
/// </summary>
/// <remarks>
///     <para>
///         <b>CLI command:</b> <c>sts2 choose_game_mode &lt;mode&gt;</c>
///     </para>
///     <para><b>Scene:</b> Singleplayer submenu (after clicking Singleplayer on main menu).</para>
/// </remarks>
public static class ChooseGameModeHandler
{
    private static readonly ModLogger Logger = new("ChooseGameModeHandler");

    /// <summary>
    ///     Clicks the corresponding button on the singleplayer submenu,
    ///     then polls until the character select screen appears.
    ///     Validates parameters and current screen state.
    ///     Must be called on the Godot main thread (via <see cref="MainThreadExecutor" />).
    /// </summary>
    public static async Task<object> ExecuteAsync(Request request)
    {
        var mode = request.Id?.ToLowerInvariant();
        Logger.Info($"Requested to choose game mode: {mode}");

        if (string.IsNullOrEmpty(mode))
            return Responses.Error("MISSING_ARGUMENT", "Game mode is required (standard, daily, custom)");

        if (mode != "standard" && mode != "daily" && mode != "custom")
            return Responses.Error("INVALID_GAME_MODE", $"Invalid game mode: {mode}. Valid modes: standard, daily, custom");

        // Guard: Must be on the SINGLEPLAYER_SUBMENU screen
        var currentScreen = StateHandler.DetectScreen();
        if (currentScreen != "SINGLEPLAYER_SUBMENU")
        {
            Logger.Warning($"Cannot choose game mode: not on singleplayer submenu (current: {currentScreen})");
            return Responses.Error("NOT_ON_SINGLEPLAYER_SUBMENU", $"Not on singleplayer submenu screen (current: {currentScreen})");
        }

        try
        {
            // Find the singleplayer submenu via UiUtils
            var submenu = UiUtils.FindSingleplayerSubmenu();
            if (submenu == null)
            {
                Logger.Error("NSingleplayerSubmenu not found");
                return Responses.Error("UI_NOT_FOUND", "Singleplayer submenu not found");
            }

            // Find the button for the requested mode
            var buttonName = mode switch
            {
                "standard" => "StandardButton",
                "daily" => "DailyButton",
                "custom" => "CustomRunButton",
                _ => null
            };

            if (buttonName == null)
                return Responses.Error("INVALID_GAME_MODE", $"Invalid game mode: {mode}");

            var button = submenu.GetNodeOrNull<NButton>(buttonName);
            if (button == null)
            {
                Logger.Error($"Button not found: {buttonName}");
                return Responses.Error("BUTTON_NOT_FOUND", $"Button not found: {buttonName}");
            }

            if (!button.IsEnabled)
            {
                Logger.Warning($"Button is disabled: {buttonName} (game mode not unlocked)");
                return Responses.Error("MODE_NOT_UNLOCKED", $"Game mode '{mode}' is not unlocked yet");
            }

            // Click the button via EmitSignal
            Logger.Info($"Clicking {buttonName}");
            button.EmitSignal(NClickableControl.SignalName.Released, button);

            // Wait for the character select screen to appear
            await Task.Delay(ActionUtils.PostClickDelayMs);
            var screenReady = await ActionUtils.PollUntilAsync(
                () => UiUtils.FindCharacterSelectScreen() != null,
                ActionUtils.UiTimeoutMs);

            if (!screenReady)
            {
                Logger.Warning($"Timed out waiting for character select after choosing game mode '{mode}'");
                return Responses.Ok(new { action = "CHOOSE_GAME_MODE", mode },
                    "Timed out waiting for character select screen");
            }

            Logger.Info($"Game mode '{mode}' selected, character select screen ready");
            return Responses.Ok(new { action = "CHOOSE_GAME_MODE", mode, screen = "CHARACTER_SELECT" });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to choose game mode: {ex.Message}");
            return Responses.Error("ACTION_FAILED", $"Failed to choose game mode: {ex.Message}");
        }
    }
}