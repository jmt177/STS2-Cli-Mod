using MegaCrit.Sts2.Core.Nodes.Debug;
using STS2.Cli.Mod.Models.Messages;
using STS2.Cli.Mod.Utils;

namespace STS2.Cli.Mod.Actions;

/// <summary>
///     Handles the <c>console_exec</c> CLI command.
///     Runs a raw dev-console command string by calling
///     <see cref="MegaCrit.Sts2.Core.DevConsole.DevConsole.ProcessCommand" /> directly on the
///     game's live console instance — the exact same entry point the in-game console UI calls
///     when the player presses Enter, just invoked programmatically instead of via keystrokes.
/// </summary>
/// <remarks>
///     <para>
///         <b>CLI command:</b> <c>sts2 console_exec &lt;token&gt; [&lt;token&gt;...]</c> (tokens are
///         joined with spaces, e.g. <c>sts2 console_exec potion BLOOD_POTION</c> runs
///         <c>potion BLOOD_POTION</c>)
///     </para>
///     <para><b>Scene:</b> Any. Availability of individual commands (e.g. requiring an active run)
///     is enforced by the console itself, the same as when typed by hand.</para>
///     <para>
///         Intended purely as a testing aid: lets the control layer stage game states
///         (spawn a specific potion/card/relic, warp to a room type, end a fight) that would
///         otherwise require slow or RNG-dependent normal play to reach. Treat it as a cheat/debug
///         surface, not a documented game feature — Mega Crit could change or remove any of these
///         commands at any time. See the console command list the player can reach in-game via the
///         <c>help</c> command.
///     </para>
/// </remarks>
public static class ConsoleExecHandler
{
    private static readonly ModLogger Logger = new("ConsoleExecHandler");

    /// <summary>
    ///     Executes the console_exec command.
    ///     Must be called on the Godot main thread (via <see cref="MainThreadExecutor" />).
    /// </summary>
    public static Task<object> ExecuteAsync(Request request)
    {
        return Task.FromResult<object>(ExecuteCore(request));
    }

    private static object ExecuteCore(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
            return Responses.Error("MISSING_ARGUMENT", "Console command string required (e.g. 'potion BLOOD_POTION')");

        var command = request.Id;
        Logger.Info($"Executing dev console command: {command}");

        try
        {
            var devConsole = UiUtils.GetPrivateField<MegaCrit.Sts2.Core.DevConsole.DevConsole>(
                NDevConsole.Instance, "_devConsole");
            if (devConsole == null)
                return Responses.Error("CONSOLE_NOT_AVAILABLE", "Dev console instance not found");

            var result = devConsole.ProcessCommand(command);
            Logger.Info($"Console command '{command}' -> success={result.success}, msg={result.msg}");

            if (!result.success)
                return Responses.Error("COMMAND_FAILED", result.msg);

            return Responses.Ok(new { command, message = result.msg });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to execute console command '{command}': {ex.Message}");
            return Responses.Error("INTERNAL_ERROR", ex.Message);
        }
    }
}
