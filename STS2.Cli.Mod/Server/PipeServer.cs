using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using STS2.Cli.Mod.Actions;
using STS2.Cli.Mod.Models.Messages;
using STS2.Cli.Mod.State;
using STS2.Cli.Mod.Utils;

namespace STS2.Cli.Mod.Server;

/// <summary>
///     Named Pipe server for communication with the CLI tool.
///     Runs a single-connection loop: create pipe → wait for the client → handle one request → disconnect → repeat.
/// </summary>
public static class PipeServer
{
    private const string PipeName = "sts2-cli-mod";
    private const int PipeBufferSize = 4096;
    private static readonly ModLogger Logger = new("PipeServer");
    private static CancellationTokenSource? _cts;

    /// <summary>
    ///     Starts the pipe server loop in the background.
    ///     Safe to call multiple times; subsequent calls are no-ops.
    /// </summary>
    public static void Start()
    {
        if (_cts != null)
        {
            Logger.Warning("Named Pipe Server already started");
            return;
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunServerLoopAsync(_cts.Token));
    }

    /// <summary>
    ///     Main server loop that creates pipe instances and waits for client connections.
    ///     Automatically recreates the pipe after each client disconnects.
    /// </summary>
    /// <param name="ct">Cancellation token to stop the server loop.</param>
    private static async Task RunServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                Logger.Info("Creating Named Pipe instance...");

                pipe = CreatePipeServer();

                Logger.Info("Waiting for CLI connection...");
                await pipe.WaitForConnectionAsync(ct);
                Logger.Info("CLI connected!");
                await HandleClientAsync(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"Named Pipe error: {ex.Message}");
                await Task.Delay(1000, ct); // Wait before retry
            }
            finally
            {
                if (pipe != null) await pipe.DisposeAsync();
            }
        }
    }

    /// <summary>
    ///     Handles a single client connection.
    ///     Reads one request, processes it, writes the response, then returns (caller disposes pipe).
    /// </summary>
    /// <param name="pipe">The connected named pipe stream.</param>
    /// <param name="ct">Cancellation token.</param>
    private static async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            // leaveOpen: true — pipe lifecycle is managed by the caller (RunServerLoopAsync)
            var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true);
            writer.AutoFlush = true;

            // Short connection mode: handle single request per connection
            var requestJson = await reader.ReadLineAsync(ct);
            if (requestJson == null)
                return;

            Logger.Info($"Received: {requestJson}");

            // Parse request
            Request? request = null;
            try
            {
                request = JsonSerializer.Deserialize<Request>(requestJson, JsonOptions.Default);
            }
            catch (Exception)
            {
                // Deserialization failed — fall through to null check below
            }

            if (request == null)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(
                    Responses.Error("INVALID_REQUEST", "Failed to parse request"),
                    JsonOptions.Default));
                return;
            }

            // Process the request and write the response
            var response = await ProcessRequestAsync(request);
            var responseJson = JsonSerializer.Serialize(response, JsonOptions.Default);
            await writer.WriteLineAsync(responseJson);
        }
        catch (IOException)
        {
            Logger.Info("Pipe Client disconnected");
        }
        catch (Exception ex)
        {
            Logger.Error($"Pipe Client handler error: {ex.Message}");
        }
    }

    /// <summary>
    ///     Command pattern: one delegate per command name, each adapted to the uniform
    ///     <c>Func&lt;Request, Task&lt;object&gt;&gt;</c> shape regardless of the underlying handler's
    ///     actual signature (some take a <see cref="Request" />, some take none; some expose extra
    ///     methods like <c>ExecuteSkipAsync</c> alongside <c>ExecuteAsync</c>). Replaces a 44-case
    ///     switch statement with a lookup table, and doubles as a registry of every known command —
    ///     see <see cref="AvailableCommands" />.
    /// </summary>
    private static readonly Dictionary<string, Func<Request, Task<object>>> Handlers = BuildHandlerRegistry();

    /// <summary>
    ///     Every command name the mod can dispatch (excludes <c>ping</c>, which is handled as a
    ///     fast path with no game-state access). Useful for discovery — e.g. validating that a
    ///     client-side command list stays in sync with the mod.
    /// </summary>
    public static IReadOnlyCollection<string> AvailableCommands => Handlers.Keys;

    /// <summary>
    ///     Processes a parsed request and routes it to the appropriate handler.
    ///     All game-thread commands use <see cref="MainThreadExecutor.RunOnMainThreadAsync{T}" />
    ///     to marshal execution to the Godot main thread.
    /// </summary>
    /// <param name="request">The parsed request object.</param>
    /// <returns>Response object to be serialized as JSON.</returns>
    private static async Task<object> ProcessRequestAsync(Request request)
    {
        try
        {
            var cmd = request.Cmd.ToLowerInvariant();

            // Connection test: no game state access, so it skips main-thread marshaling entirely.
            if (cmd == "ping")
                return Responses.Ok(new { connected = true });

            if (!Handlers.TryGetValue(cmd, out var handler))
                return Responses.Error("UNKNOWN_COMMAND", $"Unknown command: {request.Cmd}");

            return await MainThreadExecutor.RunOnMainThreadAsync(() => handler(request));
        }
        catch (Exception ex)
        {
            return Responses.Error("INTERNAL_ERROR", ex.Message);
        }
    }

    /// <summary>
    ///     Builds the command registry. Each entry adapts a handler class's actual method(s) to
    ///     the uniform <c>Func&lt;Request, Task&lt;object&gt;&gt;</c> shape the dispatcher expects.
    /// </summary>
    private static Dictionary<string, Func<Request, Task<object>>> BuildHandlerRegistry()
    {
        return new Dictionary<string, Func<Request, Task<object>>>
        {
            // --- State query ---
            ["state"] = StateHandler.HandleRequestAsync,
            ["view_deck"] = _ => ViewDeckHandler.ExecuteAsync(),

            // --- Main menu ---
            ["continue_run"] = _ => ContinueRunHandler.ExecuteAsync(),
            ["new_run"] = _ => NewRunHandler.ExecuteAsync(),
            ["abandon_run"] = _ => AbandonRunHandler.ExecuteAsync(),
            ["choose_game_mode"] = ChooseGameModeHandler.ExecuteAsync,

            // --- Character select ---
            ["select_character"] = SelectCharacterHandler.ExecuteAsync,
            ["set_ascension"] = SetAscensionHandler.ExecuteAsync,
            ["embark"] = _ => EmbarkHandler.ExecuteAsync(),

            // --- Map navigation ---
            ["choose_map_node"] = ChooseMapNodeHandler.ExecuteAsync,

            // --- Combat ---
            ["play_card"] = PlayCardHandler.ExecuteAsync,
            ["end_turn"] = EndTurnHandler.ExecuteAsync,
            ["use_potion"] = UsePotionHandler.ExecuteAsync,

            // --- Combat sub-states (hand/grid selection) ---
            ["hand_select_card"] = HandSelectCardHandler.ExecuteAsync,
            ["hand_confirm_selection"] = _ => HandSelectCardHandler.ExecuteConfirmAsync(),
            ["grid_select_card"] = GridSelectCardHandler.ExecuteAsync,
            ["grid_select_skip"] = _ => GridSelectCardHandler.ExecuteSkipAsync(),

            // --- Event rooms ---
            ["choose_event"] = ChooseEventHandler.ExecuteAsync,
            ["advance_dialogue"] = AdvanceDialogueHandler.ExecuteAsync,

            // --- Rest site ---
            ["choose_rest_option"] = ChooseRestOptionHandler.ExecuteAsync,

            // --- Treasure room ---
            ["open_chest"] = _ => OpenChestHandler.ExecuteAsync(),
            ["pick_relic"] = PickRelicHandler.ExecuteAsync,

            // --- Shop ---
            ["shop_buy_card"] = ShopBuyCardHandler.ExecuteAsync,
            ["shop_buy_relic"] = ShopBuyRelicHandler.ExecuteAsync,
            ["shop_buy_potion"] = ShopBuyPotionHandler.ExecuteAsync,
            ["shop_remove_card"] = _ => ShopRemoveCardHandler.ExecuteAsync(),

            // --- Post-combat rewards ---
            ["reward_claim"] = RewardClaimHandler.ExecuteAsync,
            ["reward_choose_card"] = RewardCardHandler.ExecuteAsync,
            ["reward_skip_card"] = RewardCardHandler.ExecuteSkipAsync,

            // --- Overlay screens (shared across contexts) ---
            ["tri_select_card"] = TriSelectCardHandler.ExecuteAsync,
            ["tri_select_skip"] = TriSelectCardHandler.ExecuteSkipAsync,
            ["relic_select"] = RelicSelectHandler.ExecuteAsync,
            ["relic_skip"] = _ => RelicSelectHandler.ExecuteSkipAsync(),
            ["bundle_select"] = BundleSelectHandler.HandleSelectAsync,
            ["bundle_confirm"] = _ => BundleSelectHandler.HandleConfirmAsync(),
            ["bundle_cancel"] = _ => BundleSelectHandler.HandleCancelAsync(),
            ["crystal_set_tool"] = CrystalSphereHandler.HandleSetToolAsync,
            ["crystal_click_cell"] = CrystalSphereHandler.HandleClickCellAsync,
            ["crystal_proceed"] = _ => CrystalSphereHandler.HandleProceedAsync(),

            // --- Navigation (shared across contexts) ---
            ["proceed"] = _ => ProceedHandler.ExecuteAsync(),

            // --- Game over ---
            ["return_to_menu"] = _ => ReturnToMenuHandler.ExecuteAsync()
        };
    }

    /// <summary>
    ///     Creates a <see cref="NamedPipeServerStream" /> with platform-appropriate settings.
    ///     On Windows, sets a permissive ACL so the CLI process (running under the same or different user)
    ///     can connect. On Linux/macOS, uses the standard constructor (Unix Domain Sockets, no ACL needed).
    /// </summary>
    private static NamedPipeServerStream CreatePipeServer()
    {
#pragma warning disable CA1416 // Validate platform compatibility
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pipeSecurity = new PipeSecurity();
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                PipeBufferSize,
                PipeBufferSize,
                pipeSecurity);
        }
#pragma warning restore CA1416

        // Linux/macOS: Standard constructor uses Unix Domain Sockets
        // No ACL needed — CLI and game run under the same user
        return new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }
}