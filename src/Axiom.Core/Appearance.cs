namespace Axiom.Core;

public enum AppearancePreference
{
    System,
    Light,
    Dark
}

public interface IAppearanceStore
{
    Task<AppearancePreference> GetAppearanceAsync(CancellationToken cancellationToken = default);
    Task SetAppearanceAsync(AppearancePreference preference, CancellationToken cancellationToken = default);
}
