using System.CommandLine;
using STS2.Cli.Cmd.Models.Messages;

namespace STS2.Cli.Cmd.Commands;

/// <summary>
///     Legal-actions command: lists every legal action for the current screen as
///     machine-readable {command, id, args, ...} entries, instead of making the caller infer
///     legality from raw <c>state</c> fields. Supports the same <c>--wait</c> contract as <c>state</c>.
/// </summary>
internal static class LegalActionsCommand
{
    /// <summary>
    ///     Creates the legal_actions command.
    /// </summary>
    public static Command Create()
    {
        var command = new Command("legal_actions", "List every legal action available in the current screen");

        var waitOption = new Option<bool>("--wait")
        {
            Description = "Wait until the game is no longer busy (is_busy=false) before returning",
            DefaultValueFactory = _ => false
        };

        var waitTimeoutOption = new Option<int>("--wait-timeout")
        {
            Description = "Timeout in milliseconds for --wait (default 10000)",
            DefaultValueFactory = _ => 10000
        };

        command.Options.Add(waitOption);
        command.Options.Add(waitTimeoutOption);

        command.SetAction(parseResult =>
        {
            var pretty = CommandExecutor.IsPretty(parseResult);
            var wait = parseResult.GetValue(waitOption);
            var waitTimeout = parseResult.GetValue(waitTimeoutOption);

            return CommandExecutor.ExecuteAsync(
                () => new Request
                {
                    Cmd = "legal_actions",
                    WaitStable = wait,
                    WaitTimeoutMs = waitTimeout
                },
                pretty);
        });

        return command;
    }
}
