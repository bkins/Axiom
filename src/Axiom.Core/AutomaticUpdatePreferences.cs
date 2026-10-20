namespace Axiom.Core;

public sealed record AutomaticUpdatePreferences(bool Enabled = true, string Channel = "stable")
{
    public string FeedChannel => Channel switch
    {
        "stable" => "win-x64-stable",
        "preview" => "win-x64-preview",
        _ => throw new ArgumentException("Choose Stable or Preview.")
    };
}
