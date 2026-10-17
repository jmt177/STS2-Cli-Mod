using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using STS2.Cli.Mod.Actions.Utils;
using STS2.Cli.Mod.Models.Messages;
using STS2.Cli.Mod.Models.State;
using STS2.Cli.Mod.State.Builders;
using STS2.Cli.Mod.Utils;

namespace STS2.Cli.Mod.State;

/// <summary>
///     Handles the 'state' command by extracting the current game state
///     from Slay the Spire 2 using direct type references.
/// </summary>
public static class StateHandler
{
    private static readonly ModLogger Logger = new("StateHandler");

    /// <summary>Default timeout (ms) for <c>state --wait</c> when the caller does not specify one.</summary>
    private const int DefaultWaitStableTimeoutMs = 10000;

    // Monotonic state version tracking. Access is single-threaded: all state requests
    // are serialized (one pipe connection at a time) and run on the Godot main thread.
    private static long _stateVersion;
    private static string? _lastStateSignature;

    /// <summary>
    ///     Handles the state request.
    /// </summary>
    /// <param name="request">The parsed request object.</param>
    /// <returns>Response containing the game state DTO, or an error if extraction failed.</returns>
    public static async Task<object> HandleRequestAsync(Request request)
    {
        Logger.Info("Requested game state");

        var settled = true;
        if (request.WaitStable)
        {
            var timeoutMs = request.WaitTimeoutMs is > 0 ? request.WaitTimeoutMs.Value : DefaultWaitStableTimeoutMs;
            settled = await ActionUtils.PollUntilAsync(() => !DetectIsBusy(DetectScreen()), timeoutMs);
        }

        var state = GetState(request.IncludePileDetails);

        if (state.Error != null)
            return new { ok = false, error = "STATE_EXTRACTION_ERROR", message = state.Error };

        if (!settled)
            return new
            {
                ok = true, data = state,
                warning = "Timed out waiting for the game to settle (is_busy is still true)"
            };

        return new { ok = true, data = state };
    }

    /// <summary>
    ///     Gets the current game state.
    /// </summary>
    /// <param name="includePileDetails">Whether to include full card descriptions in pile listings.</param>
    private static GameStateDto GetState(bool includePileDetails = false)
    {
        try
        {
            var screen = DetectScreen();
            var state = new GameStateDto
            {
                Screen = screen,
                IsBusy = DetectIsBusy(screen)
            };

            // Top-level player: available on every in-run screen, not just combat.
            if (RunManager.Instance.IsInProgress)
                try
                {
                    var runState = RunManager.Instance.DebugOnlyGetState();
                    if (runState?.Players.Count > 0)
                        state.Player = PlayerStateBuilder.Build(runState.Players[0]);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to build top-level player state: {ex.Message}");
                }

            switch (state.Screen)
            {
                // --- Pre-run ---
                case "CHARACTER_SELECT":
                    state.CharacterSelect = CharacterSelectStateBuilder.Build();
                    break;
                case "SINGLEPLAYER_SUBMENU":
                    state.SingleplayerSubmenu = SingleplayerSubmenuStateBuilder.Build();
                    break;
                case "MENU":
                    state.Menu = MenuStateBuilder.Build();
                    break;

                // --- Combat and its sub-states ---
                case "HAND_SELECT":
                    state.Combat = CombatStateBuilder.Build(includePileDetails);
                    state.HandSelect = HandSelectStateBuilder.Build();
                    break;
                case "GRID_CARD_SELECT":
                    state.GridCardSelect = GridCardSelectStateBuilder.Build();
                    if (CombatManager.Instance.IsInProgress)
                        state.Combat = CombatStateBuilder.Build(includePileDetails);
                    break;
                case "TRI_SELECT":
                    state.TriSelect = TriSelectStateBuilder.Build();
                    if (CombatManager.Instance.IsInProgress)
                        state.Combat = CombatStateBuilder.Build(includePileDetails);
                    break;
                case "BUNDLE_SELECT":
                    state.BundleSelect = BundleSelectStateBuilder.Build();
                    break;
                case "CRYSTAL_SPHERE":
                    state.CrystalSphere = CrystalSphereStateBuilder.Build();
                    break;
                case "COMBAT":
                    state.Combat = CombatStateBuilder.Build(includePileDetails);
                    break;

                // --- Map ---
                case "MAP":
                    state.Map = MapStateBuilder.Build();
                    break;

                // --- Overlay stack ---
                case "REWARD":
                case "CARD_REWARD":
                    state.Rewards = RewardStateBuilder.Build();
                    break;
                case "RELIC_SELECT":
                    state.RelicSelect = RelicSelectStateBuilder.Build();
                    break;
                case "GAME_OVER":
                    state.GameOver = GameOverStateBuilder.Build();
                    break;

                // --- Room-based ---
                case "EVENT":
                    state.Event = EventStateBuilder.Build();
                    break;
                case "REST_SITE":
                    state.RestSite = RestSiteStateBuilder.Build();
                    break;
                case "TREASURE":
                    state.Treasure = TreasureStateBuilder.Build();
                    break;
                case "SHOP":
                    state.Shop = ShopStateBuilder.Build();
                    break;
            }

            // Bump the version only if the observable state changed since the last read.
            // Computed before Timestamp is assigned so the timestamp does not perturb the diff.
            state.StateVersion = ComputeStateVersion(state);
            state.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return state;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to extract game state: {ex.Message}");
            return new GameStateDto { Screen = "ERROR", Error = ex.Message };
        }
    }

    /// <summary>
    ///     Computes the monotonic <see cref="GameStateDto.StateVersion" />.
    ///     Serializes the full state DTO (Timestamp and StateVersion are still at their
    ///     defaults at this point) and increments the counter only when the result differs
    ///     from the previous read.
    /// </summary>
    private static long ComputeStateVersion(GameStateDto state)
    {
        try
        {
            var signature = JsonSerializer.Serialize(state, JsonOptions.Default);
            if (signature != _lastStateSignature)
            {
                _lastStateSignature = signature;
                _stateVersion++;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to compute state signature: {ex.Message}");
        }

        return _stateVersion;
    }

    /// <summary>
    ///     Detects whether the game is mid-transition or still resolving a previous action,
    ///     meaning the caller should wait before issuing another action.
    ///     Conservative: only returns <c>true</c> for the cases we can detect reliably
    ///     (room/loading transitions during a run, combat outside the play phase or with
    ///     player actions disabled, and map travel animations). All click-driven screens
    ///     — events, shop, rest, rewards, and card/relic/bundle selection overlays — report
    ///     <c>false</c>; their handlers already wait for their own transitions.
    /// </summary>
    /// <param name="screen">The screen string from <see cref="DetectScreen" />.</param>
    public static bool DetectIsBusy(string screen)
    {
        try
        {
            switch (screen)
            {
                case "MENU" or "SINGLEPLAYER_SUBMENU" or "CHARACTER_SELECT":
                    return false;

                // Between rooms / loading / fade transitions during an active run.
                case "UNKNOWN":
                    return RunManager.Instance.IsInProgress;

                // Combat: the player may act only during the play phase with actions enabled.
                case "COMBAT":
                    return CombatManager.Instance.IsOverOrEnding
                           || !ActionUtils.IsLocalPlayerInPlayPhase()
                           || CombatManager.Instance.PlayerActionsDisabled;

                // Map: a travel animation is in progress.
                case "MAP":
                    return NMapScreen.Instance is { IsOpen: true } && !NMapScreen.Instance.IsTravelEnabled;

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to determine busy state: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Detects which screen the player is currently on.
    ///     Checks pre-run screens (menu / submenus) first, then in-run screens
    ///     grouped by: combat → map → overlays → rooms → game over.
    /// </summary>
    public static string DetectScreen()
    {
        // --- Pre-run: main menu and its submenu stack ---
        if (UiUtils.FindCharacterSelectScreen() != null) return "CHARACTER_SELECT";
        if (UiUtils.FindSingleplayerSubmenu() != null) return "SINGLEPLAYER_SUBMENU";
        if (UiUtils.FindMainMenu() != null) return "MENU";

        // All screens below require an active run
        if (!RunManager.Instance.IsInProgress) return "UNKNOWN";

        // --- Combat and its sub-states ---
        if (CombatManager.Instance.IsInProgress)
        {
            if (NPlayerHand.Instance is { IsInCardSelection: true }) return "HAND_SELECT";

            var combatOverlayStack = NOverlayStack.Instance;
            if (combatOverlayStack != null)
            {
                var children = combatOverlayStack.GetChildren();
                for (var i = children.Count - 1; i >= 0; i--)
                    switch (children[i])
                    {
                        case NCardGridSelectionScreen: return "GRID_CARD_SELECT";
                        case NChooseACardSelectionScreen: return "TRI_SELECT";
                        case NChooseABundleSelectionScreen: return "BUNDLE_SELECT";
                        case NCrystalSphereScreen: return "CRYSTAL_SPHERE";
                    }
            }

            return "COMBAT";
        }

        // Map takes priority over overlays — after proceeding from rewards,
        // NRewardsScreen may linger in the overlay stack.
        if (NMapScreen.Instance is { IsOpen: true }) return "MAP";

        // --- Overlay stack (checked before rooms because events/rest can trigger overlays) ---
        var overlayStack = NOverlayStack.Instance;
        if (overlayStack != null)
        {
            var children = overlayStack.GetChildren();
            for (var i = children.Count - 1; i >= 0; i--)
                switch (children[i])
                {
                    case NCardRewardSelectionScreen: return "CARD_REWARD";
                    case NCrystalSphereScreen: return "CRYSTAL_SPHERE";
                    case NRewardsScreen: return "REWARD";
                    case NChooseACardSelectionScreen: return "TRI_SELECT";
                    case NCardGridSelectionScreen: return "GRID_CARD_SELECT";
                    case NChooseARelicSelection: return "RELIC_SELECT";
                    case NChooseABundleSelectionScreen: return "BUNDLE_SELECT";
                    case NGameOverScreen: return "GAME_OVER";
                }
        }

        // --- Room-based screens ---
        var eventRoom = NEventRoom.Instance;
        if (eventRoom is not null && eventRoom.IsInsideTree()) return "EVENT";

        var restSiteRoom = NRestSiteRoom.Instance;
        if (restSiteRoom is not null && restSiteRoom.IsInsideTree()) return "REST_SITE";

        var treasureRoom = NRun.Instance?.TreasureRoom;
        if (treasureRoom is not null && treasureRoom.IsInsideTree()) return "TREASURE";

        var merchantRoom = NRun.Instance?.MerchantRoom;
        if (merchantRoom is not null && merchantRoom.IsInsideTree()) return "SHOP";

        return "UNKNOWN";
    }
}