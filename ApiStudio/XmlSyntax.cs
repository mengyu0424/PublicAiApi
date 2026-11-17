using System;
using System.Collections.Generic;

namespace ApiStudio
{
    // A tolerant lexer keeps unfinished XML editable without changing its text.
    internal static class XmlSyntax
    {
        public static List<JsonToken> Scan(string text)
        {
            var tokens = new List<JsonToken>();
            for (int i = 0; i < text.Length;)
            {
                if (text[i] != '<') { i++; continue; }
                int start = i;
                if (At(text, i, "<!--"))
                {
                    int close = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = close < 0 ? text.Length : close + 3;
                    Add(tokens, start, i, JsonTokenKind.XmlComment);
                    continue;
                }
                if (At(text, i, "<![CDATA["))
                {
                    Add(tokens, i, i + 9, JsonTokenKind.XmlMarkup);
                    int close = text.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                    if (close >= 0) Add(tokens, close, close + 3, JsonTokenKind.XmlMarkup);
                    i = close < 0 ? text.Length : close + 3;
                    continue;
                }
                if (At(text, i, "<!"))
                {
                    int close = text.IndexOf('>', i + 2);
                    i = close < 0 ? text.Length : close + 1;
                    Add(tokens, start, i, JsonTokenKind.XmlMarkup);
                    continue;
                }
                i++;
                if (i < text.Length && (text[i] == '/' || text[i] == '?')) i++;
                Add(tokens, start, i, JsonTokenKind.XmlMarkup);
                int name = i;
                while (i < text.Length && NameCharacter(text[i])) i++;
                Add(tokens, name, i, JsonTokenKind.XmlNode);
                while (i < text.Length)
                {
                    if (char.IsWhiteSpace(text[i])) { i++; continue; }
                    if (text[i] == '<') break;
                    if (text[i] == '>' || At(text, i, "/>") || At(text, i, "?>"))
                    {
                        int stop = text[i] == '>' ? i + 1 : i + 2;
                        Add(tokens, i, stop, JsonTokenKind.XmlMarkup); i = stop;
                        break;
                    }
                    if (text[i] == '\'' || text[i] == '"')
                    {
                        char quote = text[i];
                        int value = i++;
                        while (i < text.Length && text[i] != quote) i++;
                        if (i < text.Length) i++;
                        Add(tokens, value, i, JsonTokenKind.String);
                        continue;
                    }
                    int attribute = i;
                    while (i < text.Length && NameCharacter(text[i])) i++;
                    if (i > attribute) Add(tokens, attribute, i, JsonTokenKind.XmlAttribute);
                    else { Add(tokens, i, i + 1, JsonTokenKind.XmlMarkup); i++; }
                }
            }
            return tokens;
        }
        private static bool NameCharacter(char value)
        { return !char.IsWhiteSpace(value) && "<>=/?!\"'".IndexOf(value) < 0; }
        private static bool At(string text, int start, string value)
        { return start + value.Length <= text.Length && string.CompareOrdinal(text, start, value, 0, value.Length) == 0; }
        private static void Add(List<JsonToken> tokens, int start, int stop, JsonTokenKind kind)
        { if (stop > start) tokens.Add(new JsonToken { Start = start, Length = stop - start, Kind = kind }); }
    }
}
