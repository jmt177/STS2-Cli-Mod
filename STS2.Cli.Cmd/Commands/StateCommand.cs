using System.CommandLine;
using STS2.Cli.Cmd.Models.Messages;

namespace STS2.Cli.Cmd.Commands;

/// <summary>
///     State command with optional --include-pile-details flag.
/// </summary>
internal static class StateCommand
{
    /// <summary>
    ///     Creates the state command.
    /// </summary>
    public static Command Create()
    {
        var command = new Command("state", "Get current game state");

        var includePileDetailsOption = new Option<bool>("--include-pile-details")
        {
            Description = "Include full card descriptions in draw/discard/exhaust pile listings",
            DefaultValueFactory = _ => false
        };

        var waitOption = new Option<bool>("--wait")
        {
            Description = "Wait until the game is no longer busy (is_busy=false) before returning state",
            DefaultValueFactory = _ => false
        };

        var waitTimeoutOption = new Option<int>("--wait-timeout")
        {
            Description = "Timeout in milliseconds for --wait (default 10000)",
            DefaultValueFactory = _ => 10000
        };

        command.Options.Add(includePileDetailsOption);
        command.Options.Add(waitOption);
        command.Options.Add(waitTimeoutOption);

        command.SetAction(parseResult =>
        {
            var pretty = CommandExecutor.IsPretty(parseResult);
            var includePileDetails = parseResult.GetValue(includePileDetailsOption);
            var wait = parseResult.GetValue(waitOption);
            var waitTimeout = parseResult.GetValue(waitTimeoutOption);

            return CommandExecutor.ExecuteAsync(
                () => new Request
                {
                    Cmd = "state",
                    IncludePileDetails = includePileDetails,
                    WaitStable = wait,
                    WaitTimeoutMs = waitTimeout
                },
                pretty);
        });

        return command;
    }
}
