using STS2.Cli.Mod.Actions.Utils;
using STS2.Cli.Mod.Models.Messages;
using STS2.Cli.Mod.State.Builders;
using STS2.Cli.Mod.Utils;

namespace STS2.Cli.Mod.State;

/// <summary>
///     Handles the <c>legal_actions</c> command: builds a fresh state snapshot (same path as
///     <c>state</c>) and derives the list of legal actions for the current screen from it via
///     <see cref="LegalActionsBuilder" />.
/// </summary>
public static class LegalActionsHandler
{
    private static readonly ModLogger Logger = new("LegalActionsHandler");

    /// <summary>
    ///     Handles the legal_actions request. Supports the same <c>wait_stable</c> /
    ///     <c>wait_timeout_ms</c> contract as <c>state --wait</c>.
    /// </summary>
    public static async Task<object> HandleRequestAsync(Request request)
    {
        Logger.Info("Requested legal actions");

        var settled = true;
        if (request.WaitStable)
        {
            var timeoutMs = request.WaitTimeoutMs is > 0
                ? request.WaitTimeoutMs.Value
                : StateHandler.DefaultWaitStableTimeoutMs;
            settled = await ActionUtils.PollUntilAsync(() => !StateHandler.DetectIsBusy(StateHandler.DetectScreen()), timeoutMs);
        }

        var state = StateHandler.GetState();
        if (state.Error != null)
            return Responses.Error("STATE_EXTRACTION_ERROR", state.Error);

        var legalActions = LegalActionsBuilder.Build(state);
        Logger.Info($"Built {legalActions.Count} legal action(s) for screen {state.Screen}");

        var data = new
        {
            screen = state.Screen,
            is_busy = state.IsBusy,
            state_version = state.StateVersion,
            legal_actions = legalActions
        };

        if (!settled)
            return Responses.Ok(data, "Timed out waiting for the game to settle (is_busy is still true)");

        return Responses.Ok(data);
    }
}
