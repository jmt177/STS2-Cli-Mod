using System.Diagnostics.CodeAnalysis;

namespace STS2.Cli.Mod.Models.State;

/// <summary>
///     One legal action available in the current screen. Mirrors the pipe <see cref="Messages.Request" />
///     shape (cmd/id/nth/args/reward_type) so a consumer can build the next request almost directly
///     from an entry here instead of inferring legality from raw <c>state</c> fields.
/// </summary>
/// <remarks>
///     Best-effort, not a guarantee: entries are derived purely from the already-built <c>GameStateDto</c>
///     (no extra live game-API calls), so a few game-side checks the mod doesn't track yet (e.g. exact
///     "hittable" rules beyond alive/dead, potion belt capacity) aren't applied. The existing per-command
///     error codes are still the source of truth if an action from this list is ever rejected.
/// </remarks>
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public class LegalActionDto
{
    /// <summary>The CLI/pipe command name (e.g. "play_card", "choose_map_node").</summary>
    public required string Command { get; set; }

    /// <summary>Item id, when the command targets one (card/potion/relic/option id).</summary>
    public string? Id { get; set; }

    /// <summary>
    ///     N-th occurrence (0-based) when multiple items share the same <see cref="Id" /> or
    ///     <see cref="RewardType" />. Omitted (meaning 0) when there is only one match.
    /// </summary>
    public int? Nth { get; set; }

    /// <summary>Positional integer arguments, for commands that take them (map coords, indices).</summary>
    public int[]? Args { get; set; }

    /// <summary>Reward type for <c>reward_claim</c> entries: gold, potion, relic, special_card.</summary>
    public string? RewardType { get; set; }

    /// <summary>True if the command requires a <c>--target</c> enemy combat_id.</summary>
    public bool? TargetRequired { get; set; }

    /// <summary>Combat IDs of enemies that would be valid for <c>--target</c>, when <see cref="TargetRequired" />.</summary>
    public List<uint>? ValidTargets { get; set; }

    /// <summary>Short human-readable label for display/logging (card name, option title, etc.).</summary>
    public string? Label { get; set; }
}
