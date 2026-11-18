using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ApiStudio
{
    internal enum JsonTokenKind { Plain, Key, String, Number, Bracket, Error, XmlNode, XmlAttribute, XmlMarkup, XmlComment }
    internal sealed class JsonToken
    {
        public int Start, Length, Depth;
        public JsonTokenKind Kind;
    }
    internal static class JsonSyntax
    {
        private static readonly Regex Scalar = new Regex(@"\A(?:true|false|null|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)\z", RegexOptions.CultureInvariant);
        public static List<JsonToken> Scan(string text)
        {
            var tokens = new List<JsonToken>();
            var openings = new Stack<JsonToken>();
            for (int i = 0; i < text.Length; i++)
            {
                int start = i;
                char c = text[i];
                if (c == '"')
                {
                    for (i++; i < text.Length; i++)
                    {
                        if (text[i] == '\\') { i++; continue; }
                        if (text[i] == '"') { i++; break; }
                    }
                    i = Math.Min(i, text.Length);
                    int after = i;
                    while (after < text.Length && char.IsWhiteSpace(text[after])) after++;
                    tokens.Add(new JsonToken { Start = start, Length = i - start, Kind = after < text.Length && text[after] == ':' ? JsonTokenKind.Key : JsonTokenKind.String });
                    i--;
                }
                else if (c == '{' || c == '[')
                {
                    var token = new JsonToken { Start = i, Length = 1, Depth = openings.Count, Kind = JsonTokenKind.Bracket };
                    tokens.Add(token); openings.Push(token);
                }
                else if (c == '}' || c == ']')
                {
                    bool matched = openings.Count > 0 && text[openings.Peek().Start] == (c == '}' ? '{' : '[');
                    if (matched) openings.Pop();
                    tokens.Add(new JsonToken { Start = i, Length = 1, Depth = openings.Count, Kind = matched ? JsonTokenKind.Bracket : JsonTokenKind.Error });
                }
                else if (c == '-' || char.IsDigit(c) || c == 't' || c == 'f' || c == 'n')
                {
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && ",:{}[]\"".IndexOf(text[i]) < 0) i++;
                    if (Scalar.IsMatch(text.Substring(start, i - start))) tokens.Add(new JsonToken { Start = start, Length = i - start, Kind = JsonTokenKind.Number });
                    i--;
                }
            }
            foreach (var token in openings) token.Kind = JsonTokenKind.Error;
            return tokens;
        }
        public static bool SelectionAt(string text, int index, out int start, out int length)
        {
            start = length = 0;
            if (index < 0 || index >= text.Length) return false;
            // Stop at the clicked token. Even a very large plain-text response
            // should not need a document-sized token list for a double click.
            for (int i = 0; i < text.Length && i <= index; i++)
            {
                if (text[i] == '"')
                {
                    int opening = i++;
                    while (i < text.Length)
                    {
                        if (text[i] == '\\') { i += 2; continue; }
                        if (text[i] == '"') break;
                        i++;
                    }
                    if (i >= text.Length) return false;
                    if (index <= i) { start = opening + 1; length = i - start; return true; }
                }
                else if (text[i] == '-' || char.IsDigit(text[i]) || text[i] == 't' || text[i] == 'f' || text[i] == 'n')
                {
                    int opening = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && ",:{}[]\"".IndexOf(text[i]) < 0) i++;
                    if (index < i)
                    {
                        if (!Scalar.IsMatch(text.Substring(opening, i - opening))) return false;
                        start = opening; length = i - opening; return true;
                    }
                    i--;
                }
            }
            return false;
        }
    }
}
