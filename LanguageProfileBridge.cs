namespace CtrlZed;

public static class LanguageProfileBridge
{
    public static event Action? ReloadRequested;

    public static void NotifyReloadRequested()=>ReloadRequested?.Invoke();
}
