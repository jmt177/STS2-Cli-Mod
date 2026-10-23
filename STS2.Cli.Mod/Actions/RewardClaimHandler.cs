using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using STS2.Cli.Mod.Actions.Utils;
using STS2.Cli.Mod.Models.Messages;
using STS2.Cli.Mod.State;
using STS2.Cli.Mod.State.Builders;
using STS2.Cli.Mod.Utils;

namespace STS2.Cli.Mod.Actions;

/// <summary>
///     Handles the <c>reward_claim</c> CLI command.
///     Claims a non-card reward (gold, potion, relic, special card, card removal) by type + ID.
///     Uses <see cref="NClickableControl.ForceClick" /> on the <see cref="NRewardButton" />
///     to trigger the full game UI flow: claim animation, signal emission, and button removal.
///     Card removal rewards are the exception — they open a card selection screen instead of
///     claiming instantly, so they're handled by <see cref="ExecuteCardRemovalClaimAsync" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>CLI commands:</b> <c>sts2 reward_claim --type &lt;type&gt; [--id &lt;id&gt;] [--nth &lt;n&gt;]</c>
///     </para>
///     <para><b>Scene:</b> Reward screen (<see cref="NRewardsScreen" />) after combat or events.</para>
/// </remarks>
public static class RewardClaimHandler
{
    private static readonly ModLogger Logger = new("RewardClaimHandler");

    /// <summary>
    ///     Claims a reward by type and optional ID.
    ///     Must be called on the Godot main thread (via <see cref="MainThreadExecutor" />).
    /// </summary>
    public static async Task<object> ExecuteAsync(Request request)
    {
        if (string.IsNullOrEmpty(request.RewardType))
            return Responses.Error("MISSING_ARGUMENT", "Reward type required (--type)");

        var nthValue = request.Nth ?? 0;
        Logger.Info($"Requested to claim reward: type={request.RewardType}, id={request.Id ?? "null"}, nth={nthValue}");

        var rewardType = request.RewardType;
        var itemId = request.Id;
        var nth = nthValue;

        try
        {
            // --- Validation ---

            var screen = UiUtils.FindScreenInOverlay<NRewardsScreen>();
            if (screen == null)
                return Responses.Error("NOT_ON_REWARD_SCREEN", "Reward screen is not active");

            var rewardButtons = UiUtils.FindRewardButtons(screen);

            // Find rewards of the requested type
            var matchingRewards = FindRewardsByType(rewardButtons, rewardType, itemId);

            if (matchingRewards.Count == 0)
            {
                // Build a list of available reward types for the error message
                var availableTypes = GetAvailableRewardTypes(rewardButtons);
                Logger.Warning(
                    $"No {rewardType} reward found with id={itemId ?? "null"}. Available: {string.Join(", ", availableTypes)}");

                return new
                {
                    ok = false,
                    error = "REWARD_NOT_FOUND",
                    message = $"No {rewardType} reward found" + (itemId != null ? $" with ID '{itemId}'" : ""),
                    available_types = availableTypes
                };
            }

            // If only one matching reward, nth must be 0
            if (matchingRewards.Count == 1 && nth != 0)
                return Responses.Error("AMBIGUOUS_REWARD", $"Only one {rewardType} reward available, but nth={nth} was specified. Use nth=0 or omit --nth.");

            // If multiple matching rewards, nth is required to be specified (or defaults to 0)
            if (nth < 0 || nth >= matchingRewards.Count)
            {
                var idPart = itemId != null ? $" with ID '{itemId}'" : "";
                return Responses.Error("INVALID_REWARD_INDEX", $"{rewardType}{idPart} has {matchingRewards.Count} copies. Use nth from 0 to {matchingRewards.Count - 1}.");
            }

            var (rewardButton, reward) = matchingRewards[nth];

            // Card rewards must use choose_card / skip_card commands
            if (reward is CardReward)
                return Responses.Error("USE_CHOOSE_CARD", "Card rewards must be claimed with choose_card or skipped with skip_card");

            // CardRemovalReward opens a card selection screen (like shop_remove_card) instead of
            // claiming instantly, so it needs its own flow rather than the simple ForceClick-and-wait
            // path below (the reward button is not removed from the tree until a card is picked).
            if (reward is CardRemovalReward)
                return await ExecuteCardRemovalClaimAsync(rewardButton);

            // --- Claim the reward via ForceClick ---

            var rewardTypeName = GetRewardTypeName(reward);
            Logger.Info($"Claiming {rewardType} reward (nth={nth}): {rewardTypeName} via ForceClick");

            rewardButton.ForceClick();

            // Wait for the reward button to be removed from the UI.
            var removed = await WaitForButtonRemoval(rewardButton);
            if (!removed)
            {
                // Button still presents — claim likely failed (e.g., potion belt full)
                if (reward is PotionReward)
                    return Responses.Error("POTION_BELT_FULL", "Cannot claim potion — potion belt is full");

                return Responses.Error("CLAIM_FAILED", "Reward claim was not successful");
            }

            Logger.Info($"Reward claimed successfully: {rewardTypeName}");

            return Responses.Ok(new
            {
                reward_type = rewardType,
                item_id = itemId,
                nth,
                claimed = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to claim reward: {ex.Message}");
            return Responses.Error("INTERNAL_ERROR", ex.Message);
        }
    }

    /// <summary>
    ///     Finds all rewards matching the specified type and optional ID.
    /// </summary>
    private static List<(NRewardButton Button, Reward Reward)> FindRewardsByType(
        List<NRewardButton> rewardButtons, string rewardType, string? itemId)
    {
        var result = new List<(NRewardButton Button, Reward Reward)>();

        foreach (var button in rewardButtons)
        {
            var reward = button.Reward;
            if (reward == null) continue;

            var matches = rewardType.ToLowerInvariant() switch
            {
                "gold" => reward is GoldReward,
                "potion" => reward is PotionReward pr &&
                            (itemId == null || MatchesId(pr.Potion?.Id.Entry, itemId)),
                "relic" => reward is RelicReward rr &&
                           (itemId == null || MatchesId(rr.ClaimedRelic?.Id.Entry, itemId) ||
                            MatchesId(UiUtils.GetPrivateField<RelicModel>(rr, "_relic")?.Id.Entry, itemId)),
                "special_card" => reward is SpecialCardReward scr &&
                                  (itemId == null ||
                                   MatchesId(UiUtils.GetPrivateField<CardModel>(scr, "_card")?.Id.Entry, itemId)),
                "card_removal" => reward is CardRemovalReward,
                _ => false
            };

            if (matches)
                result.Add((button, reward));
        }

        return result;
    }

    /// <summary>
    ///     Checks if two IDs match (case-insensitive).
    /// </summary>
    private static bool MatchesId(string? actualId, string? expectedId)
    {
        if (actualId == null || expectedId == null) return false;
        return actualId.Equals(expectedId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Gets a list of available reward types for error messages.
    /// </summary>
    private static List<string> GetAvailableRewardTypes(List<NRewardButton> rewardButtons)
    {
        var types = new List<string>();
        foreach (var button in rewardButtons)
            if (button.Reward != null)
                types.Add(GetRewardTypeName(button.Reward));
        return types;
    }

    /// <summary>
    ///     Waits for a reward button to be removed from the scene tree after ForceClick.
    /// </summary>
    private static async Task<bool> WaitForButtonRemoval(NRewardButton button)
    {
        var removed = await ActionUtils.PollUntilAsync(
            () => !GodotObject.IsInstanceValid(button) || !button.IsInsideTree(),
            ActionUtils.UiTimeoutMs);

        if (!removed)
            Logger.Warning($"Timed out waiting for reward button removal after {ActionUtils.UiTimeoutMs}ms");

        return removed;
    }

    /// <summary>
    ///     Claims a <see cref="CardRemovalReward" />. Unlike the other reward types, claiming this
    ///     one calls <c>CardSelectCmd.FromDeckForRemoval</c> internally (via
    ///     <c>RewardSynchronizer.DoUnsyncedCardRemoval</c>), which opens a card selection screen
    ///     (<see cref="NCardGridSelectionScreen" />) and blocks until the player picks a card to
    ///     remove — the reward button is not removed from the tree, and no RewardClaimed signal
    ///     fires, until that selection completes. So instead of waiting for button removal like
    ///     every other reward type, this polls for the selection screen to appear and hands off to
    ///     <c>grid_select_card</c> / <c>grid_select_skip</c>, mirroring <c>shop_remove_card</c>.
    /// </summary>
    private static async Task<object> ExecuteCardRemovalClaimAsync(NRewardButton rewardButton)
    {
        Logger.Info("Claiming CardRemoval reward via ForceClick");
        rewardButton.ForceClick();

        var appeared = await ActionUtils.PollUntilAsync(
            () => NOverlayStack.Instance?.Peek() is NCardGridSelectionScreen,
            ActionUtils.UiTimeoutMs);

        if (!appeared)
        {
            Logger.Warning("Timed out waiting for card removal selection screen to appear");
            return Responses.Error("TIMEOUT", "Card removal selection screen did not appear in time");
        }

        var screen = StateHandler.DetectScreen();
        Logger.Info($"Card removal reward opened card selection, detected screen: {screen}");

        return Responses.Ok(new
        {
            status = "selection_required",
            next_action = "grid_select_card",
            reward_type = "card_removal",
            screen,
            grid_card_select = GridCardSelectStateBuilder.Build()
        });
    }

    /// <summary>
    ///     Gets the reward type name string for the response.
    /// </summary>
    private static string GetRewardTypeName(Reward reward)
    {
        return reward switch
        {
            GoldReward => "Gold",
            PotionReward => "Potion",
            RelicReward => "Relic",
            SpecialCardReward => "SpecialCard",
            CardRemovalReward => "CardRemoval",
            CardReward => "Card",
            _ => reward.GetType().Name
        };
    }
}