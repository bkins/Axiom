namespace Axiom.Core;

/// <summary>Recipes may only fill an empty prompt at the original repository root.</summary>
public sealed class TerminalInputState(string token)
{
    private string _tail = "";
    private int _length;
    private bool _uncertain;
    public bool AtPrompt { get; private set; }
    public bool AtRepositoryRoot { get; private set; }
    public bool CanPrepare => AtPrompt && AtRepositoryRoot && _length == 0 && !_uncertain;
    public void Reset() { _tail = ""; _length = 0; _uncertain = false; AtPrompt = AtRepositoryRoot = false; }
    public void ObserveOutput(string output)
    {
        var prefix = "\u001b]777;" + token + ";";
        var text = _tail + output;
        var offset = 0;
        var consumed = 0;
        while ((offset = text.IndexOf(prefix, offset, StringComparison.Ordinal)) >= 0)
        {
            var end = text.IndexOf('\a', offset);
            if (end < 0) break;
            AtRepositoryRoot = text[(offset + prefix.Length)..end] == "root";
            AtPrompt = true; _length = 0; _uncertain = false;
            offset = end + 1;
            consumed = offset;
        }
        var remainder = text[consumed..];
        _tail = remainder[^Math.Min(remainder.Length, prefix.Length + 8)..];
    }
    public void ObserveInput(string input)
    {
        foreach (var character in input)
        {
            // PSReadLine can redraw an already empty prompt on Ctrl+C without calling prompt again.
            // No accepted input exists to cancel here; retain only that known-empty prompt.
            if (character == '\u0003' && AtPrompt && _length == 0 && !_uncertain) continue;
            if (character is '\r' or '\n' or '\u0003') { AtPrompt = false; _length = 0; _uncertain = false; }
            else if (character is '\b' or '\u007f') _length = Math.Max(0, _length - 1);
            else if (char.IsControl(character)) _uncertain = true;
            else _length++;
        }
    }
    public bool TryPrepare(string command)
    {
        if (!CanPrepare || string.IsNullOrWhiteSpace(command) || command.Any(char.IsControl)) return false;
        ObserveInput(command);
        return true;
    }
}
