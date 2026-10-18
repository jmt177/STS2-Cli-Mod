using System.CommandLine;
using STS2.Cli.Cmd.Models.Messages;

namespace STS2.Cli.Cmd.Commands;

/// <summary>
///     Console-exec command: runs a raw dev-console command by calling the game's console
///     processor directly, instead of requiring the command to be typed into the in-game
///     console by hand. Testing aid only -- see <c>ConsoleExecHandler</c> remarks on the mod side.
/// </summary>
internal static class ConsoleExecCommand
{
    /// <summary>
    ///     Creates the console_exec command. Tokens are joined with spaces before sending, so
    ///     `sts2 console_exec potion BLOOD_POTION` runs `potion BLOOD_POTION` without needing
    ///     shell quoting.
    /// </summary>
    public static Command Create()
    {
        var command = new Command("console_exec", "Run a raw dev-console command (testing aid; see docs)");

        var tokensArg = new Argument<string[]>("tokens")
        {
            Description = "Console command and arguments, e.g. potion BLOOD_POTION",
            Arity = ArgumentArity.OneOrMore
        };
        command.Arguments.Add(tokensArg);

        command.SetAction(parseResult =>
        {
            var tokens = parseResult.GetValue(tokensArg) ?? [];
            var pretty = CommandExecutor.IsPretty(parseResult);

            return CommandExecutor.ExecuteAsync(
                () => new Request { Cmd = "console_exec", Id = string.Join(' ', tokens) },
                pretty);
        });

        return command;
    }
}
