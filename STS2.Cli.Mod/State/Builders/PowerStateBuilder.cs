using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using STS2.Cli.Mod.Models.State;
using STS2.Cli.Mod.Utils;
using static STS2.Cli.Mod.Utils.TextUtils;

namespace STS2.Cli.Mod.State.Builders;

/// <summary>
///     Builds PowerStateDto list from a creature's powers.
/// </summary>
public static class PowerStateBuilder
{
    private static readonly ModLogger Logger = new("PowerStateBuilder");

    /// <summary>
    ///     Builds power states from a creature's powers.
    ///     Only includes visible powers.
    /// </summary>
    public static List<PowerStateDto> Build(IEnumerable<PowerModel> powers)
    {
        var result = new List<PowerStateDto>();

        foreach (var power in powers)
            try
            {
                if (!power.IsVisible) continue;

                result.Add(new PowerStateDto
                {
                    Id = power.Id.Entry,
                    Name = StripGameTags(power.Title.GetFormattedText()),
                    Amount = power.DisplayAmount,
                    Type = power.Type.ToString(),
                    StackType = power.StackType.ToString(),
                    Description = StripGameTags(BuildDescription(power))
                });
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to build power state: {ex.Message}");
            }

        return result;
    }

    /// <summary>
    ///     Renders a power's description the same way the game's own tooltip does.
    ///     <see cref="PowerModel.SmartDescription" /> is a template (e.g. Strength's is
    ///     literally <c>"{Amount:cond:...} attack damage by {Amount:abs()}."</c>) whose
    ///     <c>{Amount:...}</c>/<c>{OwnerName:...}</c>/etc. placeholders are only resolved once
    ///     bound — calling <c>SmartDescription.GetFormattedText()</c> directly (with nothing
    ///     bound) fails formatting and leaks the raw template text. <see cref="PowerModel.HoverTips" />
    ///     does the binding (Amount, OwnerName, Applier/Target, ...) exactly as the in-game
    ///     tooltip does, so reuse it instead of re-implementing the binding here.
    /// </summary>
    private static string BuildDescription(PowerModel power)
    {
        if (power.HoverTips.FirstOrDefault() is HoverTip hoverTip)
            return hoverTip.Description;

        // Fallback: generic (non-smart) description without the live Amount substituted.
        return power.Description.GetFormattedText();
    }
}