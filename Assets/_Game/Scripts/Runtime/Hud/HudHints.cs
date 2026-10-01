using System.Collections.Generic;
using System.Text;

namespace Puttshack.Runtime.Hud
{
    public enum HintKind
    {
        Key,
        Text,
        Gap,
    }

    public struct HintToken
    {
        public HintKind Kind;
        /// <summary>Keycap id (enter, esc, tab, shift, space, f1, up, down, left, right, a-z, 0-9) or display text.</summary>
        public string Value;

        public HintToken(HintKind kind, string value)
        {
            Kind = kind;
            Value = value;
        }
    }

    /// <summary>
    /// Turns hint strings such as <c>"←→ aim   ↑↓ club   ENTER ready"</c> or <c>"1-0 / SHIFT+1-0 lane"</c> into keycaps and
    /// labels. Three or more spaces separate groups. Key words are the arrows, ENTER, ESC, TAB, SHIFT, SPACE, F1, single
    /// capital letters or digits, digit ranges (<c>1-0</c>) and <c>+</c> chords of those; everything else is a label.
    /// </summary>
    public static class HudHints
    {
        private static readonly string[] NamedKeys = { "ENTER", "ESC", "TAB", "SHIFT", "SPACE", "F1" };

        public static List<HintToken> Parse(string hints)
        {
            var tokens = new List<HintToken>();
            if (string.IsNullOrWhiteSpace(hints)) return tokens;

            var text = new StringBuilder();
            var keys = new List<HintToken>();
            foreach (string group in SplitGroups(hints.Trim()))
            {
                if (tokens.Count > 0) tokens.Add(new HintToken(HintKind.Gap, null));
                foreach (string word in group.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    keys.Clear();
                    if (TryKeys(word, keys))
                    {
                        Flush(text, tokens);
                        tokens.AddRange(keys);
                    }
                    else
                    {
                        if (text.Length > 0) text.Append(' ');
                        text.Append(word.ToUpperInvariant());
                    }
                }
                Flush(text, tokens);
            }
            return tokens;
        }

        private static IEnumerable<string> SplitGroups(string hints)
        {
            int start = 0, spaces = 0;
            for (int i = 0; i < hints.Length; i++)
            {
                if (hints[i] == ' ')
                {
                    spaces++;
                    continue;
                }
                if (spaces >= 3)
                {
                    yield return hints.Substring(start, i - spaces - start);
                    start = i;
                }
                spaces = 0;
            }
            yield return hints.Substring(start);
        }

        private static void Flush(StringBuilder text, List<HintToken> tokens)
        {
            if (text.Length == 0) return;
            tokens.Add(new HintToken(HintKind.Text, text.ToString()));
            text.Clear();
        }

        private static bool TryKeys(string word, List<HintToken> keys)
        {
            if (word.Length == 0) return false;

            bool arrows = true;
            foreach (char c in word) arrows &= ArrowId(c) != null;
            if (arrows)
            {
                foreach (char c in word) keys.Add(new HintToken(HintKind.Key, ArrowId(c)));
                return true;
            }

            foreach (string named in NamedKeys)
            {
                if (word == named)
                {
                    keys.Add(new HintToken(HintKind.Key, named.ToLowerInvariant()));
                    return true;
                }
            }

            if (word.Length == 1 && (char.IsDigit(word[0]) || (word[0] >= 'A' && word[0] <= 'Z')))
            {
                keys.Add(new HintToken(HintKind.Key, word.ToLowerInvariant()));
                return true;
            }

            if (word.Length == 3 && char.IsDigit(word[0]) && word[1] == '-' && char.IsDigit(word[2]))
            {
                keys.Add(new HintToken(HintKind.Key, word.Substring(0, 1)));
                keys.Add(new HintToken(HintKind.Text, "\u2013"));
                keys.Add(new HintToken(HintKind.Key, word.Substring(2, 1)));
                return true;
            }

            int plus = word.IndexOf('+');
            if (plus > 0 && plus < word.Length - 1)
            {
                var left = new List<HintToken>();
                var right = new List<HintToken>();
                if (!TryKeys(word.Substring(0, plus), left) || !TryKeys(word.Substring(plus + 1), right)) return false;
                keys.AddRange(left);
                keys.Add(new HintToken(HintKind.Text, "+"));
                keys.AddRange(right);
                return true;
            }
            return false;
        }

        private static string ArrowId(char c)
        {
            switch (c)
            {
                case '\u2190': return "left";
                case '\u2192': return "right";
                case '\u2191': return "up";
                case '\u2193': return "down";
                default: return null;
            }
        }
    }
}
