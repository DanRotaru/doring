using System.Windows.Input;

namespace DoRing.Services;

/// <summary>Parses strings like "Ctrl+Space" or "Win+Shift+S".</summary>
public static class HotKeyParser
{
    public static bool TryParse(string text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control; break;
                case "alt":
                    modifiers |= ModifierKeys.Alt; break;
                case "shift":
                    modifiers |= ModifierKeys.Shift; break;
                case "win":
                case "windows":
                case "meta":
                    modifiers |= ModifierKeys.Windows; break;
                default:
                    var keyName = part.ToLowerInvariant() switch
                    {
                        "plus" or "+" => nameof(Key.OemPlus),
                        "period" or "." => nameof(Key.OemPeriod),
                        "minus" or "-" => nameof(Key.OemMinus),
                        _ => part,
                    };
                    if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out key)) return false;
                    break;
            }
        }

        return key != Key.None;
    }
}
