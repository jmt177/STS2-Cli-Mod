using STS2.Cli.Mod.Models.State;

namespace STS2.Cli.Mod.State.Builders;

/// <summary>
///     Derives the list of legal actions for the current screen from an already-built
///     <see cref="GameStateDto" />. One entry per screen type in <see cref="Builders" />,
///     keyed by <see cref="GameStateDto.Screen" /> — the same registry-over-switch shape as
///     <see cref="Server.PipeServer" />'s command dispatch, applied to "what can I do on this
///     screen" instead of "how do I run this command". Adding a new screen means adding one
///     dictionary entry here, not another branch in three unrelated switches.
/// </summary>
/// <remarks>
///     Pure function of the state DTO: no live game-API calls, so it can never contradict what
///     <c>state</c> just reported and carries no extra risk beyond the existing builders. See
///     <see cref="LegalActionDto" /> for the "best-effort" caveat.
/// </remarks>
public static class LegalActionsBuilder
{
    private static readonly Dictionary<string, Func<GameStateDto, List<LegalActionDto>>> Builders = new()
    {
        ["MENU"] = BuildMenuActions,
        ["SINGLEPLAYER_SUBMENU"] = BuildSingleplayerSubmenuActions,
        ["CHARACTER_SELECT"] = BuildCharacterSelectActions,
        ["MAP"] = BuildMapActions,
        ["COMBAT"] = BuildCombatActions,
        ["HAND_SELECT"] = BuildHandSelectActions,
        ["GRID_CARD_SELECT"] = BuildGridCardSelectActions,
        ["EVENT"] = BuildEventActions,
        ["REST_SITE"] = BuildRestSiteActions,
        ["TREASURE"] = BuildTreasureActions,
        ["SHOP"] = BuildShopActions,
        ["REWARD"] = BuildRewardActions,
        ["CARD_REWARD"] = BuildRewardActions,
        ["TRI_SELECT"] = BuildTriSelectActions,
        ["RELIC_SELECT"] = BuildRelicSelectActions,
        ["BUNDLE_SELECT"] = BuildBundleSelectActions,
        ["CRYSTAL_SPHERE"] = BuildCrystalSphereActions,
        ["GAME_OVER"] = BuildGameOverActions
    };

    /// <summary>
    ///     Builds the legal action list for <paramref name="state" />. Empty while
    ///     <see cref="GameStateDto.IsBusy" /> (nothing is safe to issue yet) or on screens with
    ///     no registered builder (MENU-adjacent loading states, UNKNOWN).
    /// </summary>
    public static List<LegalActionDto> Build(GameStateDto state)
    {
        if (state.IsBusy)
            return [];

        return Builders.TryGetValue(state.Screen, out var builder) ? builder(state) : [];
    }

    /// <summary>Returns the 0-based occurrence count of <paramref name="key" /> seen so far, then increments it.</summary>
    private static int NextNth(Dictionary<string, int> seen, string key)
    {
        var nth = seen.GetValueOrDefault(key);
        seen[key] = nth + 1;
        return nth;
    }

    // --- Pre-run ---

    private static List<LegalActionDto> BuildMenuActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var menu = state.Menu;
        if (menu == null) return actions;

        if (menu.HasRunSave)
        {
            actions.Add(new LegalActionDto { Command = "continue_run", Label = "Continue the saved run" });
            actions.Add(new LegalActionDto { Command = "abandon_run", Label = "Abandon the saved run" });
        }
        else
        {
            actions.Add(new LegalActionDto { Command = "new_run", Label = "Start a new run" });
        }

        return actions;
    }

    private static List<LegalActionDto> BuildSingleplayerSubmenuActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var submenu = state.SingleplayerSubmenu;
        if (submenu == null) return actions;

        if (submenu.StandardAvailable)
            actions.Add(new LegalActionDto { Command = "choose_game_mode", Id = "standard", Label = "Standard run" });
        if (submenu.DailyAvailable)
            actions.Add(new LegalActionDto { Command = "choose_game_mode", Id = "daily", Label = "Daily challenge" });
        if (submenu.CustomAvailable)
            actions.Add(new LegalActionDto { Command = "choose_game_mode", Id = "custom", Label = "Custom run" });

        return actions;
    }

    private static List<LegalActionDto> BuildCharacterSelectActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var cs = state.CharacterSelect;
        if (cs == null) return actions;

        foreach (var character in cs.AvailableCharacters)
            if (!character.IsLocked)
                actions.Add(new LegalActionDto
                {
                    Command = "select_character", Id = character.CharacterId, Label = character.CharacterName
                });

        for (var level = 0; level <= cs.MaxAscension; level++)
            actions.Add(new LegalActionDto
            {
                Command = "set_ascension", Args = [level], Label = $"Set ascension {level}"
            });

        if (cs.CanEmbark)
            actions.Add(new LegalActionDto { Command = "embark", Label = "Start the run" });

        return actions;
    }

    // --- Map ---

    private static List<LegalActionDto> BuildMapActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var map = state.Map;
        if (map == null) return actions;

        var nodesByCoord = new Dictionary<(int, int), MapNodeDto>();
        foreach (var node in map.Nodes)
            nodesByCoord[(node.Col, node.Row)] = node;

        foreach (var coord in map.TravelableCoords)
        {
            nodesByCoord.TryGetValue((coord.Col, coord.Row), out var node);
            actions.Add(new LegalActionDto
            {
                Command = "choose_map_node", Args = [coord.Col, coord.Row], Label = node?.Type
            });
        }

        return actions;
    }

    // --- Combat and its sub-states ---

    private static List<LegalActionDto> BuildCombatActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var combat = state.Combat;
        if (combat == null) return actions;

        var aliveTargets = combat.Enemies.Where(e => e.IsAlive).Select(e => e.CombatId).ToList();

        var seenCardIds = new Dictionary<string, int>();
        foreach (var card in combat.Hand)
        {
            var nth = NextNth(seenCardIds, card.Id);
            if (!card.CanPlay) continue;

            var targetRequired = card.TargetType == "AnyEnemy";
            actions.Add(new LegalActionDto
            {
                Command = "play_card",
                Id = card.Id,
                Nth = nth > 0 ? nth : null,
                TargetRequired = targetRequired,
                ValidTargets = targetRequired ? aliveTargets : null,
                Label = card.Name
            });
        }

        var seenPotionIds = new Dictionary<string, int>();
        foreach (var potion in state.Player?.Potions ?? [])
        {
            var nth = NextNth(seenPotionIds, potion.Id);
            if (potion.Usage == "Automatic") continue;

            var targetRequired = potion.TargetType == "AnyEnemy";
            actions.Add(new LegalActionDto
            {
                Command = "use_potion",
                Id = potion.Id,
                Nth = nth > 0 ? nth : null,
                TargetRequired = targetRequired,
                ValidTargets = targetRequired ? aliveTargets : null,
                Label = potion.Name
            });
        }

        actions.Add(new LegalActionDto { Command = "end_turn", Label = "End turn" });

        return actions;
    }

    private static List<LegalActionDto> BuildHandSelectActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var handSelect = state.HandSelect;
        if (handSelect == null) return actions;

        var seen = new Dictionary<string, int>();
        foreach (var card in handSelect.SelectableCards)
        {
            var nth = NextNth(seen, card.CardId);
            actions.Add(new LegalActionDto
            {
                Command = "hand_select_card", Id = card.CardId, Nth = nth > 0 ? nth : null, Label = card.CardName
            });
        }

        if (handSelect.RequireManualConfirmation && handSelect.CanConfirm)
            actions.Add(new LegalActionDto { Command = "hand_confirm_selection", Label = "Confirm selection" });

        return actions;
    }

    private static List<LegalActionDto> BuildGridCardSelectActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var gridSelect = state.GridCardSelect;
        if (gridSelect == null) return actions;

        var seen = new Dictionary<string, int>();
        foreach (var card in gridSelect.Cards)
        {
            var nth = NextNth(seen, card.CardId);
            actions.Add(new LegalActionDto
            {
                Command = "grid_select_card", Id = card.CardId, Nth = nth > 0 ? nth : null, Label = card.CardName
            });
        }

        if (gridSelect.Cancelable)
            actions.Add(new LegalActionDto { Command = "grid_select_skip", Label = "Skip selection" });

        return actions;
    }

    // --- Room-based ---

    private static List<LegalActionDto> BuildEventActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var ev = state.Event;
        if (ev == null) return actions;

        if (ev.IsInDialogue)
        {
            actions.Add(new LegalActionDto { Command = "advance_dialogue", Label = "Advance dialogue" });
            return actions;
        }

        foreach (var option in ev.Options)
        {
            if (option.IsLocked || option.WasChosen) continue;

            actions.Add(option.IsProceed
                ? new LegalActionDto { Command = "proceed", Label = option.Title }
                : new LegalActionDto { Command = "choose_event", Args = [option.Index], Label = option.Title });
        }

        return actions;
    }

    private static List<LegalActionDto> BuildRestSiteActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var restSite = state.RestSite;
        if (restSite == null) return actions;

        foreach (var option in restSite.Options)
            if (option.IsEnabled)
                actions.Add(new LegalActionDto
                {
                    Command = "choose_rest_option", Id = option.OptionId, Label = option.Name
                });

        if (restSite.CanProceed)
            actions.Add(new LegalActionDto { Command = "proceed", Label = "Leave rest site" });

        return actions;
    }

    private static List<LegalActionDto> BuildTreasureActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var treasure = state.Treasure;
        if (treasure == null) return actions;

        if (!treasure.IsChestOpened)
        {
            actions.Add(new LegalActionDto { Command = "open_chest", Label = "Open the chest" });
            return actions;
        }

        foreach (var relic in treasure.Relics)
            actions.Add(new LegalActionDto { Command = "pick_relic", Args = [relic.Index], Label = relic.Name });

        if (treasure.CanProceed)
            actions.Add(new LegalActionDto { Command = "proceed", Label = "Leave treasure room" });

        return actions;
    }

    private static List<LegalActionDto> BuildShopActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var shop = state.Shop;
        if (shop == null) return actions;

        var seenCards = new Dictionary<string, int>();
        foreach (var card in shop.Cards)
        {
            var nth = NextNth(seenCards, card.CardId);
            if (!card.IsStocked || shop.PlayerGold < card.Cost) continue;
            actions.Add(new LegalActionDto
            {
                Command = "shop_buy_card", Id = card.CardId, Nth = nth > 0 ? nth : null, Label = card.CardName
            });
        }

        foreach (var relic in shop.Relics)
        {
            if (!relic.IsStocked || shop.PlayerGold < relic.Cost) continue;
            actions.Add(new LegalActionDto { Command = "shop_buy_relic", Id = relic.RelicId, Label = relic.RelicName });
        }

        foreach (var potion in shop.Potions)
        {
            if (!potion.IsStocked || shop.PlayerGold < potion.Cost) continue;
            actions.Add(new LegalActionDto
            {
                Command = "shop_buy_potion", Id = potion.PotionId, Label = potion.PotionName
            });
        }

        if (shop.CardRemoval is { IsUsed: false } removal && shop.PlayerGold >= removal.Cost)
            actions.Add(new LegalActionDto { Command = "shop_remove_card", Label = "Remove a card from the deck" });

        if (shop.CanProceed)
            actions.Add(new LegalActionDto { Command = "proceed", Label = "Leave the shop" });

        return actions;
    }

    // --- Overlay stack ---

    private static List<LegalActionDto> BuildRewardActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var rewards = state.Rewards;
        if (rewards == null) return actions;

        var seenByType = new Dictionary<string, int>();
        foreach (var reward in rewards.Rewards)
        {
            // reward_claim only supports these four types; Card goes through reward_choose_card /
            // reward_skip_card below, and CardRemoval / LinkedRewardSet have no claim command yet.
            var rewardType = reward.Type switch
            {
                "Gold" => "gold",
                "Potion" => "potion",
                "Relic" => "relic",
                "SpecialCard" => "special_card",
                _ => null
            };

            if (rewardType != null)
            {
                var nth = NextNth(seenByType, rewardType);
                var id = reward.Type switch
                {
                    "Potion" => reward.PotionId,
                    "Relic" => reward.RelicId,
                    "SpecialCard" => reward.CardId,
                    _ => null
                };
                actions.Add(new LegalActionDto
                {
                    Command = "reward_claim", RewardType = rewardType, Id = id,
                    Nth = nth > 0 ? nth : null, Label = reward.Description
                });
            }
            else if (reward.Type == "Card" && reward.CardChoices != null)
            {
                var cardNth = NextNth(seenByType, "card");
                foreach (var choice in reward.CardChoices)
                    actions.Add(new LegalActionDto
                    {
                        Command = "reward_choose_card", Id = choice.Id,
                        Nth = cardNth > 0 ? cardNth : null, Label = choice.Name
                    });
                actions.Add(new LegalActionDto
                {
                    Command = "reward_skip_card", Nth = cardNth > 0 ? cardNth : null, Label = "Skip card reward"
                });
            }
        }

        if (rewards.CanSkip != false)
            actions.Add(new LegalActionDto { Command = "proceed", Label = "Leave reward screen" });

        return actions;
    }

    private static List<LegalActionDto> BuildTriSelectActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var triSelect = state.TriSelect;
        if (triSelect == null) return actions;

        foreach (var card in triSelect.Cards)
            actions.Add(new LegalActionDto { Command = "tri_select_card", Id = card.CardId, Label = card.CardName });

        if (triSelect.CanSkip)
            actions.Add(new LegalActionDto { Command = "tri_select_skip", Label = "Skip selection" });

        return actions;
    }

    private static List<LegalActionDto> BuildRelicSelectActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var relicSelect = state.RelicSelect;
        if (relicSelect == null) return actions;

        foreach (var relic in relicSelect.Relics)
            actions.Add(new LegalActionDto { Command = "relic_select", Args = [relic.Index], Label = relic.Name });

        if (relicSelect.CanSkip)
            actions.Add(new LegalActionDto { Command = "relic_skip", Label = "Skip relic selection" });

        return actions;
    }

    private static List<LegalActionDto> BuildBundleSelectActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var bundleSelect = state.BundleSelect;
        if (bundleSelect == null) return actions;

        if (bundleSelect.PreviewShowing)
        {
            if (bundleSelect.CanConfirm)
                actions.Add(new LegalActionDto { Command = "bundle_confirm", Label = "Confirm bundle" });
            if (bundleSelect.CanCancel)
                actions.Add(new LegalActionDto { Command = "bundle_cancel", Label = "Cancel preview" });
            return actions;
        }

        foreach (var bundle in bundleSelect.Bundles)
            actions.Add(new LegalActionDto
            {
                Command = "bundle_select", Args = [bundle.Index], Label = $"Bundle of {bundle.CardCount} cards"
            });

        return actions;
    }

    private static List<LegalActionDto> BuildCrystalSphereActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var crystal = state.CrystalSphere;
        if (crystal == null) return actions;

        if (crystal.CanUseBigTool && crystal.Tool != "big")
            actions.Add(new LegalActionDto { Command = "crystal_set_tool", Id = "big", Label = "Switch to big tool" });
        if (crystal.CanUseSmallTool && crystal.Tool != "small")
            actions.Add(new LegalActionDto
            {
                Command = "crystal_set_tool", Id = "small", Label = "Switch to small tool"
            });

        foreach (var cell in crystal.ClickableCells)
            actions.Add(new LegalActionDto { Command = "crystal_click_cell", Args = [cell.X, cell.Y] });

        if (crystal.CanProceed)
            actions.Add(new LegalActionDto { Command = "crystal_proceed", Label = "Leave the mini-game" });

        return actions;
    }

    // --- Game over ---

    private static List<LegalActionDto> BuildGameOverActions(GameStateDto state)
    {
        var actions = new List<LegalActionDto>();
        var gameOver = state.GameOver;
        if (gameOver == null) return actions;

        if (gameOver.CanReturnToMenu)
            actions.Add(new LegalActionDto { Command = "return_to_menu", Label = "Return to the main menu" });

        return actions;
    }
}
