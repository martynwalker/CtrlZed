namespace CtrlZed;

/// <summary>
/// Stable runtime helpers exposed to compiled CtrlZed macros.
/// </summary>
public static class CtrlZedAi
{
    /// <summary>
    /// Returns the model currently configured in CtrlZed for the named AI provider.
    /// This avoids hard-coding provider model identifiers in macros.
    /// </summary>
    public static string GetModel(string provider)
    {
        if(string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider name cannot be empty.",nameof(provider));
        return AiDirectService.GetProfile(provider.Trim()).Model;
    }
}
