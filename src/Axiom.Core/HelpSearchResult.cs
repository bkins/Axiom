namespace Axiom.Core;

public sealed record HelpSearchResult(string Topic, string Fragment, string Excerpt)
{
    public string Label => Topic + " — " + Excerpt;
}
