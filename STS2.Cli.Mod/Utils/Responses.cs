namespace STS2.Cli.Mod.Utils;

/// <summary>
///     Factory methods for the mod's JSON response envelope (<c>{"ok": true/false, ...}</c>).
///     Centralizes the shape so every command handler builds success/error responses the same
///     way, instead of each repeating <c>new { ok = ..., error = ..., message = ... }</c> inline.
///     Purely a construction helper — the JSON shape and field order are unchanged from before.
/// </summary>
public static class Responses
{
    /// <summary>Builds a success response: <c>{"ok": true, "data": ...}</c>.</summary>
    /// <param name="data">The payload to return, or <c>null</c> for commands with no data.</param>
    /// <param name="warning">
    ///     Optional non-fatal warning (e.g., a wait/poll timed out but a best-effort result is
    ///     still returned anyway). Omitted from the JSON when null.
    /// </param>
    public static object Ok(object? data = null, string? warning = null)
    {
        return new { ok = true, data, warning };
    }

    /// <summary>Builds an error response: <c>{"ok": false, "error": "CODE", "message": "..."}</c>.</summary>
    /// <param name="error">SCREAMING_SNAKE_CASE error code (see docs/cli-reference.md).</param>
    /// <param name="message">Human-readable description of the failure.</param>
    public static object Error(string error, string message)
    {
        return new { ok = false, error, message };
    }
}
