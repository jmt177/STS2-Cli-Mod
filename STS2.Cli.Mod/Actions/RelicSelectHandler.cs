using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using STS2.Cli.Mod.Actions.Utils;
using STS2.Cli.Mod.Models.Messages;
using STS2.Cli.Mod.State;
using STS2.Cli.Mod.Utils;

namespace STS2.Cli.Mod.Actions;

/// <summary>
///     Handles the <c>relic_select</c> and <c>relic_skip</c> CLI commands.
///     Selects or skips a relic from the "choose a relic" screen (<see cref="NChooseARelicSelection" />).
///     This overlay appears after boss fights and certain events, presenting relics for the player
///     to pick one (or skip). Selection is done by calling <c>ForceClick()</c> on the target
///     <see cref="NRelicBasicHolder" />, which emits <c>Released</c> -> <c>SelectHolder()</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b>CLI commands:</b>
///     </para>
///     <list type="bullet">
///         <item>
///             <c>sts2 relic_select &lt;index&gt;</c>
///         </item>
///         <item>
///             <c>sts2 relic_skip</c>
///         </item>
///     </list>
///     <para><b>Scene:</b> Boss relic choice screen or event relic choice screen.</para>
/// </remarks>
public static class RelicSelectHandler
{
    private static readonly ModLogger Logger = new("RelicSelectHandler");

    /// <summary>
    ///     Selects a relic by index from the "choose a relic" selection screen.
    /// </summary>
    public static async Task<object> ExecuteAsync(Request request)
    {
        if (request.Args == null || request.Args.Length < 1)
            return Responses.Error("MISSING_ARGUMENT", "Relic index required (0-based)");

        var relicIndex = request.Args[0];
        Logger.Info($"Requested to select relic at index {relicIndex}");

        try
        {
            // --- Guard: Check relic selection screen ---
            var screen = UiUtils.FindScreenInOverlay<NChooseARelicSelection>();
            if (screen == null)
                return Responses.Error("NOT_IN_RELIC_SELECT", "Not in relic selection screen. Use 'sts2 state' to check current screen.");

            // --- Guard: Find relic holders ---
            var holders = UiUtils.FindAll<NRelicBasicHolder>(screen);
            if (holders.Count == 0)
                return Responses.Error("NO_RELICS_AVAILABLE", "No relics available in the selection screen");

            // --- Guard: Check relic index ---
            if (relicIndex < 0 || relicIndex >= holders.Count)
                return Responses.Error("INVALID_RELIC_INDEX", $"Relic index {relicIndex} is out of range (0-{holders.Count - 1})");

            // --- Select the relic ---
            var holder = holders[relicIndex];
            var relicModel = holder.Relic.Model;
            var relicId = relicModel.Id.Entry;
            Logger.Info($"Selecting relic at index {relicIndex}: {relicId}");

            holder.ForceClick();

            // --- Poll for the overlay to be removed ---
            await ActionUtils.PollUntilAsync(() =>
            {
                var current = UiUtils.FindScreenInOverlay<NChooseARelicSelection>();
                return current == null;
            }, ActionUtils.UiTimeoutMs);

            // --- Return result ---
            var resultScreen = StateHandler.DetectScreen();
            Logger.Info($"After selecting relic, detected screen: {resultScreen}");

            return Responses.Ok(new
            {
                action = "RELIC_SELECT",
                relic_id = relicId,
                relic_index = relicIndex,
                screen = resultScreen
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to select relic: {ex.Message}");
            return Responses.Error("INTERNAL_ERROR", ex.Message);
        }
    }

    /// <summary>
    ///     Skips the relic selection by clicking the skip button.
    /// </summary>
    public static async Task<object> ExecuteSkipAsync()
    {
        Logger.Info("Requested to skip relic selection");

        try
        {
            // --- Guard: Check relic selection screen ---
            var screen = UiUtils.FindScreenInOverlay<NChooseARelicSelection>();
            if (screen == null)
                return Responses.Error("NOT_IN_RELIC_SELECT", "Not in relic selection screen. Use 'sts2 state' to check current screen.");

            // --- Find and click the skip button ---
            // NChooseARelicSelection has a NChoiceSelectionSkipButton at node path "SkipButton"
            var skipButton = UiUtils.FindFirst<NChoiceSelectionSkipButton>(screen);
            if (skipButton == null)
                return Responses.Error("SKIP_BUTTON_NOT_FOUND", "Skip button not found in relic selection screen");

            Logger.Info("Skipping relic selection");
            skipButton.ForceClick();

            // --- Poll for the overlay to be removed ---
            await ActionUtils.PollUntilAsync(() =>
            {
                var current = UiUtils.FindScreenInOverlay<NChooseARelicSelection>();
                return current == null;
            }, ActionUtils.UiTimeoutMs);

            // --- Return result ---
            var resultScreen = StateHandler.DetectScreen();
            Logger.Info($"After skipping relic selection, detected screen: {resultScreen}");

            return Responses.Ok(new
            {
                action = "RELIC_SKIP",
                screen = resultScreen
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to skip relic selection: {ex.Message}");
            return Responses.Error("INTERNAL_ERROR", ex.Message);
        }
    }
}