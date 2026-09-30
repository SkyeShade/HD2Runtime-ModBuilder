using System.Globalization;
using System.Text;
using HD2RuntimeGUI.Core.Localization;

namespace HD2RuntimeGUI.Core.Scripting;

// Syntax checking for hand-written mod Lua: a Lua 5.1 lexer and recursive-descent parser (with LuaJIT's goto and labels). It reports the
// first syntax error the game's Lua would report, with its line and column, and never executes anything. Semantic checks against the SDK
// (event names, action names) live in LuaScriptAnalyzer. Error messages are UI text (LuaCheck.Syntax.*); the Lua tokens in them
// ('end', '<eof>', the source near the error) are arguments.
public enum LuaTokenKind { Name, Keyword, Number, String, Symbol, Eof }
public sealed record LuaToken(LuaTokenKind Kind, string Text, int Line, int Column, string? Value = null)
{
    public bool Is(string text) => (Kind is LuaTokenKind.Keyword or LuaTokenKind.Symbol) && Text == text;
}
public sealed record LuaDiagnostic(int Line, int Column, string Severity, string Message, string? Code = null)
{
    public const string Error = "error", Warning = "warning";
}
public sealed class LuaSyntaxException(int line, int column, string message) : Exception(message)
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

public static class LuaLexer
{
    public static readonly HashSet<string> Keywords = ["and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"];
    private static readonly string[] Symbols = ["...", "..", "==", "~=", "<=", ">=", "::", "+", "-", "*", "/", "%", "^", "#", "<", ">", "=", "(", ")", "{", "}",
        "[", "]", ";", ":", ",", "."];

    public static List<LuaToken> Tokenize(string source)
    {
        var tokens = new List<LuaToken>(); int i = 0, line = 1, lineStart = 0;
        var s = source;
        if (s.StartsWith('﻿')) i = 1;
        if (s.Length > i + 1 && s[i] == '#' && s[i + 1] == '!') while (i < s.Length && s[i] != '\n') i++; // shebang line
        LuaSyntaxException Fail(string message, int at) => new(line, at - lineStart + 1, message);
        void NewLine(int at) { line++; lineStart = at + 1; }
        while (true)
        {
            while (i < s.Length && (s[i] is ' ' or '\t' or '\r' or '\n' or '\f' or '\v')) { if (s[i] == '\n') NewLine(i); i++; }
            if (i >= s.Length) break;
            var start = i; var col = i - lineStart + 1; var startLine = line; char c = s[i];
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                i += 2;
                if (LongBracket(s, i) is int level) { i = SkipLong(s, i, level, "comment", ref line, ref lineStart, startLine, col); continue; }
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                var word = s[start..i];
                tokens.Add(new(Keywords.Contains(word) ? LuaTokenKind.Keyword : LuaTokenKind.Name, word, startLine, col));
                continue;
            }
            if (char.IsDigit(c) || c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
            {
                if (c == '0' && i + 1 < s.Length && (s[i + 1] is 'x' or 'X'))
                {
                    i += 2; var digits = i;
                    while (i < s.Length && (Uri.IsHexDigit(s[i]) || s[i] == '.')) i++;
                    if (i < s.Length && (s[i] is 'p' or 'P')) { i++; if (i < s.Length && (s[i] is '+' or '-')) i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
                    if (i == digits) throw Fail(CoreText.Format("LuaCheck.Syntax.MalformedNumber", s[start..i]), start);
                }
                else
                {
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    if (i < s.Length && (s[i] is 'e' or 'E')) { i++; if (i < s.Length && (s[i] is '+' or '-')) i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
                }
                // LuaJIT 64-bit integer and imaginary suffixes.
                while (i < s.Length && (s[i] is 'u' or 'U' or 'l' or 'L' or 'i' or 'I')) i++;
                if (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) { while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.')) i++; throw Fail(CoreText.Format("LuaCheck.Syntax.MalformedNumber", s[start..i]), start); }
                if (s[start..i].Count(x => x == '.') > 1) throw Fail(CoreText.Format("LuaCheck.Syntax.MalformedNumber", s[start..i]), start);
                tokens.Add(new(LuaTokenKind.Number, s[start..i], startLine, col));
                continue;
            }
            if (c is '"' or '\'')
            {
                i++; var value = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length || s[i] == '\n') throw Fail(CoreText.Format("LuaCheck.Syntax.UnfinishedStringNear", Near(s[start..Math.Min(i, s.Length)])), start);
                    if (s[i] == c) { i++; break; }
                    if (s[i] == '\\')
                    {
                        i++; if (i >= s.Length) throw Fail(CoreText.Get("LuaCheck.Syntax.UnfinishedString"), start);
                        var e = s[i];
                        switch (e)
                        {
                            case 'n': value.Append('\n'); i++; break;
                            case 't': value.Append('\t'); i++; break;
                            case 'r': value.Append('\r'); i++; break;
                            case 'a': value.Append('\a'); i++; break;
                            case 'b': value.Append('\b'); i++; break;
                            case 'f': value.Append('\f'); i++; break;
                            case 'v': value.Append('\v'); i++; break;
                            case '\\': case '"': case '\'': value.Append(e); i++; break;
                            case '\n': value.Append('\n'); NewLine(i); i++; break;
                            case '\r': value.Append('\n'); i++; if (i < s.Length && s[i] == '\n') { NewLine(i); i++; } break;
                            case 'x':
                                if (i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2])) { value.Append((char)int.Parse(s.AsSpan(i + 1, 2), NumberStyles.HexNumber)); i += 3; }
                                else throw Fail(CoreText.Format("LuaCheck.Syntax.InvalidEscape", "\\x"), i - 1);
                                break;
                            case 'z':
                                i++; while (i < s.Length && char.IsWhiteSpace(s[i])) { if (s[i] == '\n') NewLine(i); i++; }
                                break;
                            case 'u' when i + 1 < s.Length && s[i + 1] == '{':
                                var close = s.IndexOf('}', i + 2); if (close < 0) throw Fail(CoreText.Format("LuaCheck.Syntax.InvalidEscape", "\\u"), i - 1);
                                i = close + 1; break;
                            default:
                                if (char.IsDigit(e)) { var n = 0; var k = 0; while (k < 3 && i < s.Length && char.IsDigit(s[i])) { n = n * 10 + (s[i] - '0'); i++; k++; } if (n > 255) throw Fail(CoreText.Get("LuaCheck.Syntax.EscapeTooLarge"), i - k - 1); value.Append((char)n); }
                                else throw Fail(CoreText.Format("LuaCheck.Syntax.InvalidEscape", "\\" + e), i - 1);
                                break;
                        }
                        continue;
                    }
                    value.Append(s[i]); i++;
                }
                tokens.Add(new(LuaTokenKind.String, s[start..i], startLine, col, value.ToString()));
                continue;
            }
            if (c == '[' && LongBracket(s, i) is int stringLevel)
            {
                var open = i + stringLevel + 2; var end = SkipLong(s, i, stringLevel, "string", ref line, ref lineStart, startLine, col);
                var content = s[open..(end - stringLevel - 2)];
                if (content.StartsWith("\r\n", StringComparison.Ordinal)) content = content[2..]; else if (content.StartsWith('\n')) content = content[1..];
                tokens.Add(new(LuaTokenKind.String, s[start..end], startLine, col, content)); i = end;
                continue;
            }
            var symbol = Symbols.FirstOrDefault(x => string.CompareOrdinal(s, i, x, 0, x.Length) == 0);
            if (symbol == null) throw Fail(CoreText.Format("LuaCheck.Syntax.UnexpectedSymbol", c.ToString()), i);
            tokens.Add(new(LuaTokenKind.Symbol, symbol, startLine, col)); i += symbol.Length;
        }
        tokens.Add(new(LuaTokenKind.Eof, "<eof>", line, i - lineStart + 1));
        return tokens;
    }
    // "[[", "[=[", "[==[" ... at i: returns the level, or null when it is not a long bracket.
    private static int? LongBracket(string s, int i)
    {
        if (i >= s.Length || s[i] != '[') return null;
        var j = i + 1; while (j < s.Length && s[j] == '=') j++;
        return j < s.Length && s[j] == '[' ? j - i - 1 : null;
    }
    private static int SkipLong(string s, int i, int level, string what, ref int line, ref int lineStart, int startLine, int col)
    {
        var close = "]" + new string('=', level) + "]"; var j = i + level + 2;
        while (j < s.Length)
        {
            if (s[j] == '\n') { line++; lineStart = j + 1; }
            if (string.CompareOrdinal(s, j, close, 0, close.Length) == 0) return j + close.Length;
            j++;
        }
        throw new LuaSyntaxException(startLine, col, CoreText.Format(what == "comment" ? "LuaCheck.Syntax.UnfinishedLongComment" : "LuaCheck.Syntax.UnfinishedLongString", startLine, "<eof>"));
    }
    private static string Near(string text) => text.Length > 40 ? text[..40] : text;
}

public sealed class LuaParser
{
    private readonly List<LuaToken> tokens; private int p;
    private int loops; private readonly Stack<bool> varargs = new();
    private LuaParser(List<LuaToken> tokens) { this.tokens = tokens; }
    public static List<LuaToken> Check(string source)
    {
        var tokens = LuaLexer.Tokenize(source); var parser = new LuaParser(tokens);
        parser.varargs.Push(true); parser.Block(); parser.Expect("<eof>");
        return tokens;
    }
    // The first syntax error as a diagnostic, or none.
    public static IReadOnlyList<LuaDiagnostic> Diagnose(string source)
    {
        try { Check(source); return []; }
        catch (LuaSyntaxException e) { return [new(e.Line, e.Column, LuaDiagnostic.Error, e.Message, "syntax")]; }
    }
    private LuaToken T => tokens[p];
    private LuaToken Next() => tokens[p++];
    private bool At(string text) => T.Is(text) || T.Kind == LuaTokenKind.Eof && text == "<eof>";
    private bool Accept(string text) { if (!At(text)) return false; p++; return true; }
    // A LuaCheck.Syntax.* message whose last placeholder is the token the error is near ('<eof>' at the end).
    private LuaSyntaxException Error(string key, LuaToken? at = null, params object?[] args)
    {
        var t = at ?? T;
        return new(t.Line, t.Column, CoreText.Format(key, [.. args, t.Kind == LuaTokenKind.Eof ? "<eof>" : t.Text]));
    }
    private void Expect(string text, string? key = null) { if (!Accept(text)) throw key == null ? Error("LuaCheck.Syntax.Expected", null, text) : Error(key); }
    private void Match(string close, string open, LuaToken opener)
    {
        if (Accept(close)) return;
        throw opener.Line == T.Line ? Error("LuaCheck.Syntax.Expected", null, close) : Error("LuaCheck.Syntax.ExpectedToClose", null, close, open, opener.Line);
    }
    private string Name() { if (T.Kind != LuaTokenKind.Name) throw Error("LuaCheck.Syntax.NameExpected", null, "<name>"); return Next().Text; }
    private static bool BlockEnd(LuaToken t) => t.Kind == LuaTokenKind.Eof || t.Kind == LuaTokenKind.Keyword && t.Text is "end" or "else" or "elseif" or "until";

    private void Block()
    {
        while (!BlockEnd(T))
        {
            if (At("return")) { Next(); if (!BlockEnd(T) && !At(";")) ExpList(); Accept(";"); if (!BlockEnd(T)) throw Error("LuaCheck.Syntax.Expected", null, "<eof>"); return; }
            Statement();
        }
    }
    private void Statement()
    {
        var t = T;
        switch (t.Kind == LuaTokenKind.Keyword || t.Kind == LuaTokenKind.Symbol ? t.Text : "")
        {
            case ";": Next(); return;
            case "if":
                Next(); Expr(); Expect("then"); Block();
                while (At("elseif")) { Next(); Expr(); Expect("then"); Block(); }
                if (Accept("else")) Block();
                Match("end", "if", t); return;
            case "while": Next(); Expr(); Expect("do"); loops++; Block(); loops--; Match("end", "while", t); return;
            case "do": Next(); Block(); Match("end", "do", t); return;
            case "for":
                Next(); Name();
                if (Accept("=")) { Expr(); Expect(","); Expr(); if (Accept(",")) Expr(); }
                else { while (Accept(",")) Name(); Expect("in", "LuaCheck.Syntax.AssignOrInExpected"); ExpList(); }
                Expect("do"); loops++; Block(); loops--; Match("end", "for", t); return;
            case "repeat": Next(); loops++; Block(); loops--; Match("until", "repeat", t); Expr(); return;
            case "function":
                Next(); Name(); var method = false;
                while (At(".") || At(":")) { method = Next().Text == ":"; Name(); if (method) break; }
                FunctionBody(t); return;
            case "local":
                Next();
                if (Accept("function")) { Name(); FunctionBody(t); return; }
                do Name(); while (Accept(","));
                if (Accept("=")) ExpList();
                return;
            case "return": throw Error("LuaCheck.Syntax.Expected", null, "<eof>");
            case "break": Next(); if (loops == 0) throw Error("LuaCheck.Syntax.NoLoop", t); return;
            case "goto": Next(); Name(); return;
            case "::": Next(); Name(); Expect("::"); return;
        }
        // Expression statement: a call, or an assignment to variables.
        var callable = SuffixedExpr(out var lastIsCall, out var assignable);
        if (At("=") || At(","))
        {
            if (!assignable) throw Error("LuaCheck.Syntax.Error");
            while (Accept(",")) { SuffixedExpr(out _, out var ok); if (!ok) throw Error("LuaCheck.Syntax.Error"); }
            Expect("="); ExpList(); return;
        }
        if (!lastIsCall || !callable) throw Error("LuaCheck.Syntax.Error");
    }
    private void FunctionBody(LuaToken opener)
    {
        Expect("(");
        var vararg = false;
        if (!At(")"))
            do
            {
                if (Accept("...")) { vararg = true; break; }
                Name();
            } while (Accept(","));
        Expect(")");
        varargs.Push(vararg); var savedLoops = loops; loops = 0;
        Block(); Match("end", "function", opener);
        loops = savedLoops; varargs.Pop();
    }
    private void ExpList() { do Expr(); while (Accept(",")); }
    private static readonly Dictionary<string, (int Left, int Right)> Binary = new()
    {
        ["or"] = (1, 1), ["and"] = (2, 2), ["<"] = (3, 3), [">"] = (3, 3), ["<="] = (3, 3), [">="] = (3, 3), ["~="] = (3, 3), ["=="] = (3, 3),
        [".."] = (5, 4), ["+"] = (6, 6), ["-"] = (6, 6), ["*"] = (7, 7), ["/"] = (7, 7), ["%"] = (7, 7), ["^"] = (10, 9),
    };
    private const int UnaryPriority = 8;
    private void Expr(int limit = 0)
    {
        if (At("not") || At("-") || At("#")) { Next(); Expr(UnaryPriority); }
        else SimpleExpr();
        while ((T.Kind is LuaTokenKind.Symbol or LuaTokenKind.Keyword) && Binary.TryGetValue(T.Text, out var op) && op.Left > limit) { Next(); Expr(op.Right); }
    }
    private void SimpleExpr()
    {
        var t = T;
        if (t.Kind is LuaTokenKind.Number or LuaTokenKind.String) { Next(); return; }
        if (t.Kind == LuaTokenKind.Keyword && t.Text is "nil" or "true" or "false") { Next(); return; }
        if (t.Is("...")) { if (!varargs.Peek()) throw Error("LuaCheck.Syntax.Vararg"); Next(); return; }
        if (t.Is("{")) { Table(); return; }
        if (t.Is("function")) { Next(); FunctionBody(t); return; }
        SuffixedExpr(out _, out _);
    }
    // primaryexp { '.' NAME | '[' exp ']' | ':' NAME funcargs | funcargs }; reports whether the last suffix was a call and whether the
    // expression is assignable (a name or an index, not a call or a parenthesized expression).
    private bool SuffixedExpr(out bool lastIsCall, out bool assignable)
    {
        lastIsCall = false; assignable = false;
        if (T.Kind == LuaTokenKind.Name) { Next(); assignable = true; }
        else if (At("(")) { var open = Next(); Expr(); Match(")", "(", open); }
        else throw Error("LuaCheck.Syntax.UnexpectedSymbol");
        while (true)
        {
            if (Accept(".")) { Name(); assignable = true; lastIsCall = false; }
            else if (At("[")) { var open = Next(); Expr(); Match("]", "[", open); assignable = true; lastIsCall = false; }
            else if (Accept(":")) { Name(); Args(); assignable = false; lastIsCall = true; }
            else if (At("(") || At("{") || T.Kind == LuaTokenKind.String) { Args(); assignable = false; lastIsCall = true; }
            else return true;
        }
    }
    private void Args()
    {
        if (T.Kind == LuaTokenKind.String) { Next(); return; }
        if (At("{")) { Table(); return; }
        var open = T; Expect("(", "LuaCheck.Syntax.FunctionArgs");
        if (!At(")")) ExpList();
        Match(")", "(", open);
    }
    private void Table()
    {
        var open = Next();
        while (!At("}"))
        {
            if (At("[")) { var bracket = Next(); Expr(); Match("]", "[", bracket); Expect("="); Expr(); }
            else if (T.Kind == LuaTokenKind.Name && tokens[p + 1].Is("=")) { Next(); Next(); Expr(); }
            else Expr();
            if (!Accept(",") && !Accept(";")) break;
        }
        Match("}", "{", open);
    }
}
