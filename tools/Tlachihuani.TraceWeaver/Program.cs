// Tlachihuani — Angela López Mendoza · XAIHT
//
// Trace weaver. For every C# source of a Tlachihuani product project, writes a traced
// copy that the build compiles instead of the original (Directory.Build.targets):
//
//   * TraceLog.Enter(...) at the start of every method, constructor, accessor, local
//     function and block lambda, with the arguments it received and the time it took;
//   * TraceLog.Step("<statement>") in front of every statement, so the log shows the
//     program running almost line by line.
//
// Inserted text never adds a line break, and each copy starts with a #line directive
// that names the original file, so file names, line numbers, stack traces and the
// debugger still point at the real source. The repository's sources are not changed.
//
//   Tlachihuani.TraceWeaver <output folder> <project folder> <list of sources> <lines|methods>
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Length != 4) { Console.Error.WriteLine("usage: <output> <project> <sources.txt> <lines|methods>"); return 2; }
string output = Path.GetFullPath(args[0]), project = Path.GetFullPath(args[1]);
bool lines = args[3] != "methods";
int woven = 0, steps = 0, scopes = 0;
foreach (var source in File.ReadAllLines(args[2]).Where(l => l.Length > 0))
{
    string full = Path.GetFullPath(source);
    string relative = full.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        ? Path.GetRelativePath(project, full) : Path.GetFileName(full);
    string target = Path.Combine(output, relative);
    byte[] raw = File.ReadAllBytes(full);
    bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
    string text = new UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
    string result = text.Contains("tlachihuani:trace-off", StringComparison.Ordinal)
        ? text : Weave(text, full, lines, ref steps, ref scopes);
    string final = "#line 1 \"" + full + "\"\n" + result;
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    if (!File.Exists(target) || File.ReadAllText(target) != final) File.WriteAllText(target, final, new UTF8Encoding(bom));
    woven++;
}
Console.WriteLine($"Tlachihuani trace weaver: {woven} files, {scopes} method scopes, {steps} statement steps ({(lines ? "lines" : "methods")}).");
return 0;

static string Weave(string text, string path, bool lines, ref int steps, ref int scopes)
{
    var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview), path);
    var root = tree.GetRoot();
    var edits = new List<(int Position, int Length, string Text)>();
    string file = Path.GetFileName(path);
    int unique = 0;

    foreach (var node in root.DescendantNodes())
    {
        // A trace scope at the start of every member body.
        if (node is BlockSyntax body && Owner(body) is { } owner)
        {
            var (name, parameters) = Describe(owner);
            int line = tree.GetLineSpan(owner.Span).StartLinePosition.Line + 1;
            // A body that opens with a TraceLog.Pass gets its scope right after it, so that its
            // own entry and exit lines belong to the pass (an idle pass then writes nothing).
            int at = body.Statements.FirstOrDefault() is { } first && StartsPass(first) ? first.Span.End : body.OpenBraceToken.Span.End;
            edits.Add((at, 0, " " + ScopeStatement(file, line, name, parameters, ++unique)));
            scopes++;
        }
        // Expression-bodied methods and local functions become a block with the same scope.
        else if (node is ArrowExpressionClauseSyntax arrow && arrow.Parent is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
        {
            var member = (CSharpSyntaxNode)arrow.Parent;
            var (name, parameters) = Describe(member);
            int line = tree.GetLineSpan(member.Span).StartLinePosition.Line + 1;
            var semicolon = member is MethodDeclarationSyntax m ? m.SemicolonToken : ((LocalFunctionStatementSyntax)member).SemicolonToken;
            if (semicolon.IsMissing) continue;
            bool isVoid = ReturnsNothing(member);
            string open = "{ " + ScopeStatement(file, line, name, parameters, ++unique) + " ";
            open += arrow.Expression is ThrowExpressionSyntax ? "" : isVoid ? "" : "return ";
            edits.Add((arrow.ArrowToken.SpanStart, arrow.ArrowToken.Span.Length, open));
            edits.Add((semicolon.Span.End, 0, " }"));
            scopes++;
        }

        if (!lines) continue;
        // A step in front of every statement of every block, switch section and program.
        // (Plain arrays: a collection expression here would build a detached SyntaxList
        // whose nodes no longer know their position in the file.)
        StatementSyntax[] statements = node switch
        {
            BlockSyntax block => block.Statements.ToArray(),
            SwitchSectionSyntax section => section.Statements.ToArray(),
            GlobalStatementSyntax topLevel => new[] { topLevel.Statement },
            _ => Array.Empty<StatementSyntax>()
        };
        foreach (var statement in statements)
        {
            if (statement is LocalFunctionStatementSyntax) continue;
            var target = statement is LabeledStatementSyntax labeled ? labeled.Statement : statement;
            if (target is LocalFunctionStatementSyntax) continue;
            // The statement that starts a pass is not stepped: its step would land outside the pass.
            if (StartsPass(target)) continue;
            edits.Add((target.SpanStart, 0, "global::Tlachihuani.Contracts.TraceLog.Step(" + Literal(Summary(target)) + "); "));
            steps++;
        }
    }

    if (Environment.GetEnvironmentVariable("TL_WEAVE_DEBUG") == "1")
        foreach (var e in edits) Console.WriteLine($"edit at {e.Position} (+{e.Length}): {e.Text[..Math.Min(60, e.Text.Length)]}");
    var builder = new StringBuilder(text);
    foreach (var (position, length, insert) in edits.OrderByDescending(e => e.Position).ThenByDescending(e => e.Length))
    {
        builder.Remove(position, length);
        builder.Insert(position, insert);
    }
    return builder.ToString();
}

// "var pass = TraceLog.Pass(...)" or "using var pass = ... TraceLog.Pass(...) ...".
static bool StartsPass(StatementSyntax statement)
    => statement is LocalDeclarationStatementSyntax declaration
       && declaration.Declaration.ToString().Contains("TraceLog.Pass(", StringComparison.Ordinal);

// Members whose block body gets a scope.
static CSharpSyntaxNode? Owner(BlockSyntax body) => body.Parent switch
{
    BaseMethodDeclarationSyntax m when m.Body == body => m,
    AccessorDeclarationSyntax a when a.Body == body => a,
    LocalFunctionStatementSyntax f when f.Body == body => f,
    AnonymousFunctionExpressionSyntax l when l.Block == body => l,
    _ => null
};

static (string Name, List<string> Parameters) Describe(CSharpSyntaxNode owner)
{
    string type = owner.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "Program";
    (string name, ParameterListSyntax? list) = owner switch
    {
        MethodDeclarationSyntax m => (m.Identifier.Text, m.ParameterList),
        ConstructorDeclarationSyntax c => (c.Identifier.Text + " (constructor)", c.ParameterList),
        DestructorDeclarationSyntax d => ("~" + d.Identifier.Text, d.ParameterList),
        OperatorDeclarationSyntax o => ("operator " + o.OperatorToken.Text, o.ParameterList),
        ConversionOperatorDeclarationSyntax c => ("conversion to " + c.Type, c.ParameterList),
        AccessorDeclarationSyntax a => (AccessorName(a), null),
        LocalFunctionStatementSyntax f => (Enclosing(f) + "." + f.Identifier.Text, f.ParameterList),
        AnonymousFunctionExpressionSyntax l => ("lambda in " + Enclosing(l), null),
        _ => ("?", null)
    };
    var parameters = new List<string>();
    if (list is not null)
        foreach (var p in list.Parameters)
        {
            string text = p.Type?.ToString() ?? "";
            if (p.Modifiers.Any(SyntaxKind.OutKeyword) || p.Modifiers.Any(SyntaxKind.ScopedKeyword)
                || text.Contains("Span", StringComparison.Ordinal) || text.Contains('*', StringComparison.Ordinal)
                || text.Contains("Utf8JsonReader", StringComparison.Ordinal) || p.Identifier.Text == "_" || p.Identifier.Text.Length == 0)
                continue;
            parameters.Add(p.Identifier.ToString());
        }
    return (type + "." + name, parameters);
}

static string AccessorName(AccessorDeclarationSyntax accessor)
{
    var owner = accessor.Parent?.Parent;
    string property = owner switch
    {
        PropertyDeclarationSyntax p => p.Identifier.Text,
        IndexerDeclarationSyntax => "this[]",
        EventDeclarationSyntax e => e.Identifier.Text,
        _ => "?"
    };
    return property + "." + accessor.Keyword.Text;
}

static string Enclosing(SyntaxNode node)
{
    foreach (var ancestor in node.Ancestors())
        switch (ancestor)
        {
            case MethodDeclarationSyntax m: return m.Identifier.Text;
            case ConstructorDeclarationSyntax c: return c.Identifier.Text;
            case LocalFunctionStatementSyntax f: return f.Identifier.Text;
            case AccessorDeclarationSyntax a: return AccessorName(a);
            case PropertyDeclarationSyntax p: return p.Identifier.Text;
            case GlobalStatementSyntax: return "Program";
        }
    return "?";
}

static bool ReturnsNothing(CSharpSyntaxNode member)
{
    var (type, modifiers) = member switch
    {
        MethodDeclarationSyntax m => (m.ReturnType.ToString(), m.Modifiers),
        LocalFunctionStatementSyntax f => (f.ReturnType.ToString(), f.Modifiers),
        _ => ("", default)
    };
    if (type == "void") return true;
    return modifiers.Any(SyntaxKind.AsyncKeyword) && type is "Task" or "ValueTask" or "System.Threading.Tasks.Task";
}

static string ScopeStatement(string file, int line, string name, List<string> parameters, int unique)
{
    var text = new StringBuilder("using var __tlTrace").Append(unique).Append(" = global::Tlachihuani.Contracts.TraceLog.Enter(")
        .Append(Literal(file + ":" + line + " " + name)).Append(", ").Append(Literal(string.Join(",", parameters.Select(p => p.TrimStart('@')))));
    foreach (var p in parameters) text.Append(", (object?)").Append(p);
    return text.Append(");").ToString();
}

// The statement as one short line: whitespace collapsed, at most 200 characters.
static string Summary(StatementSyntax statement)
{
    string code = statement switch
    {
        BlockSyntax => "{ … }",
        IfStatementSyntax s => "if (" + s.Condition + ")",
        WhileStatementSyntax s => "while (" + s.Condition + ")",
        DoStatementSyntax s => "do … while (" + s.Condition + ")",
        ForStatementSyntax s => "for (" + s.Declaration + s.Initializers + "; " + s.Condition + "; " + s.Incrementors + ")",
        ForEachStatementSyntax s => "foreach (" + s.Type + " " + s.Identifier + " in " + s.Expression + ")",
        ForEachVariableStatementSyntax s => "foreach (" + s.Variable + " in " + s.Expression + ")",
        SwitchStatementSyntax s => "switch (" + s.Expression + ")",
        TryStatementSyntax => "try",
        UsingStatementSyntax s => "using (" + (s.Declaration?.ToString() ?? s.Expression?.ToString()) + ")",
        LockStatementSyntax s => "lock (" + s.Expression + ")",
        CheckedStatementSyntax => "checked",
        UnsafeStatementSyntax => "unsafe",
        FixedStatementSyntax s => "fixed (" + s.Declaration + ")",
        _ => statement.ToString()
    };
    var collapsed = new StringBuilder(code.Length);
    bool space = false;
    foreach (char c in code)
    {
        if (char.IsWhiteSpace(c)) { if (!space && collapsed.Length > 0) collapsed.Append(' '); space = true; }
        else { collapsed.Append(c); space = false; }
    }
    string one = collapsed.ToString().Trim();
    return one.Length <= 200 ? one : one[..200] + "…";
}

static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
