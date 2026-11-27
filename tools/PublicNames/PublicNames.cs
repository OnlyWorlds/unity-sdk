// The package's public names, read from its C# source with Roslyn's parser (no Unity, no compile), for
// tools/verify_promise.py. Usage: dotnet run --project tools/PublicNames -- <folder> [<folder> ...]
//
// Prints one JSON object: {"decls": [[name, mark], ...], "reach": [name, ...], "files": N}.
// - A public name is a type declared public (nested only inside public types), a public member of one, a protected
//   member of a public class that is neither sealed nor static, an interface member, or an enum member. A member is
//   named by its owner and its name (`OnlyWorlds.Sdk.OWClient.AskAsync`); overloads share the name, a constructor is
//   `Owner.ctor`, a generic type or method carries its arity (`Resolve`1`).
// - One row per declaration, so a name with several overloads or partial declarations has several rows. Its mark is
//   null when the declaration carries no [Obsolete], else the attribute's message: a string literal, a const string
//   declared in the folders (by simple or dotted name), or a sum of those. "" for an [Obsolete] with no message;
//   "\u0000" and the expression when the message cannot be read that way.
// - "reach" adds the names a member of an in-tree base class or interface makes reachable under a derived type, so a
//   member moved into a base class is not read as gone.
// The preprocessor symbol UNITY_EDITOR is defined, as the Editor compiles the package.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class PublicNames
{
    static readonly List<string[]> Decls = new List<string[]>();
    static readonly Dictionary<string, List<string>> Members = new Dictionary<string, List<string>>();
    static readonly Dictionary<string, List<string>> Bases = new Dictionary<string, List<string>>();
    static readonly Dictionary<string, ExpressionSyntax> Consts = new Dictionary<string, ExpressionSyntax>();

    static bool Has(SyntaxTokenList mods, SyntaxKind k) => mods.Any(m => m.IsKind(k));

    static string Arity(TypeParameterListSyntax t) => t == null || t.Parameters.Count == 0 ? "" : "`" + t.Parameters.Count;

    static string Namespace(SyntaxNode n)
    {
        var parts = new List<string>();
        for (var p = n.Parent; p != null; p = p.Parent)
            if (p is BaseNamespaceDeclarationSyntax ns) parts.Insert(0, ns.Name.ToString());
        return string.Join(".", parts);
    }

    static AttributeSyntax Obsolete(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(l => l.Attributes).FirstOrDefault(a =>
        {
            var n = a.Name.ToString();
            return n == "Obsolete" || n == "ObsoleteAttribute" || n == "System.Obsolete" || n == "System.ObsoleteAttribute";
        });

    static string Eval(ExpressionSyntax e, int depth = 0)
    {
        if (depth > 20) return null;
        switch (e)
        {
            case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression):
                return lit.Token.ValueText;
            case InterpolatedStringExpressionSyntax interp when interp.Contents.All(c => c is InterpolatedStringTextSyntax):
                return string.Concat(interp.Contents.Cast<InterpolatedStringTextSyntax>().Select(c => c.TextToken.ValueText));
            case ParenthesizedExpressionSyntax par:
                return Eval(par.Expression, depth + 1);
            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.AddExpression):
                var l = Eval(bin.Left, depth + 1);
                var r = Eval(bin.Right, depth + 1);
                return l == null || r == null ? null : l + r;
            case IdentifierNameSyntax id:
                return Consts.TryGetValue(id.Identifier.Text, out var c1) ? Eval(c1, depth + 1) : null;
            case MemberAccessExpressionSyntax ma:
                return Consts.TryGetValue(ma.ToString(), out var c2) ? Eval(c2, depth + 1)
                    : Consts.TryGetValue(ma.Name.Identifier.Text, out var c3) ? Eval(c3, depth + 1) : null;
        }
        return null;
    }

    static string MarkOf(SyntaxList<AttributeListSyntax> lists)
    {
        var a = Obsolete(lists);
        if (a == null) return null;
        var arg = a.ArgumentList?.Arguments.FirstOrDefault(x => x.NameColon == null && x.NameEquals == null)
                  ?? a.ArgumentList?.Arguments.FirstOrDefault(x => x.NameColon?.Name.Identifier.Text == "message");
        if (arg == null) return "";
        return Eval(arg.Expression) ?? "\u0000" + arg.Expression;
    }

    static void Put(string owner, string name, SyntaxList<AttributeListSyntax> attrs)
    {
        Decls.Add(new[] { name, MarkOf(attrs) });
        if (owner != null)
        {
            if (!Members.TryGetValue(owner, out var list)) Members[owner] = list = new List<string>();
            list.Add(name.Substring(owner.Length + 1));
        }
    }

    static void Type(BaseTypeDeclarationSyntax t, string prefix, string owner)
    {
        bool isInterface = t is InterfaceDeclarationSyntax;
        string name = prefix + t.Identifier.Text + (t is TypeDeclarationSyntax td ? Arity(td.TypeParameterList) : "");
        Put(owner, name, t.AttributeLists);
        if (t.BaseList != null)
        {
            if (!Bases.TryGetValue(name, out var b)) Bases[name] = b = new List<string>();
            foreach (var bt in t.BaseList.Types)
            {
                var s = bt.Type.ToString();
                var lt = s.IndexOf('<');
                b.Add(lt < 0 ? s : s.Substring(0, lt) + "`" + (s.Count(ch => ch == ',') + 1));
            }
        }
        if (t is EnumDeclarationSyntax e)
        {
            foreach (var m in e.Members) Put(name, name + "." + m.Identifier.Text, m.AttributeLists);
            return;
        }
        bool open = t is ClassDeclarationSyntax && !Has(t.Modifiers, SyntaxKind.SealedKeyword) && !Has(t.Modifiers, SyntaxKind.StaticKeyword);
        foreach (var m in ((TypeDeclarationSyntax)t).Members)
        {
            bool visible = isInterface
                ? !Has(m.Modifiers, SyntaxKind.PrivateKeyword)
                : Has(m.Modifiers, SyntaxKind.PublicKeyword)
                  || (open && Has(m.Modifiers, SyntaxKind.ProtectedKeyword) && !Has(m.Modifiers, SyntaxKind.PrivateKeyword));
            if (!visible) continue;
            switch (m)
            {
                case BaseTypeDeclarationSyntax nested: Type(nested, name + ".", name); break;
                case DelegateDeclarationSyntax dd: Put(name, name + "." + dd.Identifier.Text + Arity(dd.TypeParameterList), dd.AttributeLists); break;
                case MethodDeclarationSyntax md: Put(name, name + "." + md.Identifier.Text + Arity(md.TypeParameterList), md.AttributeLists); break;
                case ConstructorDeclarationSyntax cd: if (!Has(cd.Modifiers, SyntaxKind.StaticKeyword)) Put(name, name + ".ctor", cd.AttributeLists); break;
                case PropertyDeclarationSyntax pd: Put(name, name + "." + pd.Identifier.Text, pd.AttributeLists); break;
                case IndexerDeclarationSyntax ix: Put(name, name + ".this[]", ix.AttributeLists); break;
                case EventDeclarationSyntax ev: Put(name, name + "." + ev.Identifier.Text, ev.AttributeLists); break;
                case EventFieldDeclarationSyntax ef: foreach (var v in ef.Declaration.Variables) Put(name, name + "." + v.Identifier.Text, ef.AttributeLists); break;
                case FieldDeclarationSyntax fd: foreach (var v in fd.Declaration.Variables) Put(name, name + "." + v.Identifier.Text, fd.AttributeLists); break;
                case OperatorDeclarationSyntax op: Put(name, name + ".op " + op.OperatorToken.Text, op.AttributeLists); break;
                case ConversionOperatorDeclarationSyntax co: Put(name, name + ".op " + co.ImplicitOrExplicitKeyword.Text + " " + co.Type, co.AttributeLists); break;
            }
        }
    }

    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("PublicNames: give one or more source folders"); return 2; }
        var options = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { "UNITY_EDITOR" });
        var trees = new List<SyntaxTree>();
        foreach (var root in args)
        {
            if (!Directory.Exists(root)) { Console.Error.WriteLine($"PublicNames: no folder {root}"); return 2; }
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path);
                var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (errors.Count > 0) { Console.Error.WriteLine($"PublicNames: {path} does not parse: {errors[0]}"); return 2; }
                trees.Add(tree);
            }
        }
        // Every const string in the folders, by simple name and by Type.Name, for an [Obsolete] message held in one.
        foreach (var tree in trees)
            foreach (var f in tree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().Where(f => Has(f.Modifiers, SyntaxKind.ConstKeyword)))
                foreach (var v in f.Declaration.Variables.Where(v => v.Initializer != null))
                {
                    Consts[v.Identifier.Text] = v.Initializer.Value;
                    if (f.Parent is BaseTypeDeclarationSyntax owner) Consts[owner.Identifier.Text + "." + v.Identifier.Text] = v.Initializer.Value;
                }
        foreach (var tree in trees)
        {
            var root = tree.GetRoot();
            foreach (var t in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                if (!(t.Parent is BaseTypeDeclarationSyntax) && Has(t.Modifiers, SyntaxKind.PublicKeyword))
                    Type(t, Namespace(t) == "" ? "" : Namespace(t) + ".", null);
            foreach (var d in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
                if (!(d.Parent is BaseTypeDeclarationSyntax) && Has(d.Modifiers, SyntaxKind.PublicKeyword))
                    Put(null, (Namespace(d) == "" ? "" : Namespace(d) + ".") + d.Identifier.Text + Arity(d.TypeParameterList), d.AttributeLists);
        }
        // Reachable through an in-tree base: the base's members (constructors aside) under the derived type's name.
        var byShort = new Dictionary<string, string>();
        foreach (var owner in Members.Keys.Concat(Bases.Keys).Distinct())
        {
            var s = owner.Substring(owner.LastIndexOf('.') + 1);
            if (!byShort.ContainsKey(s)) byShort[s] = owner;
        }
        var reach = new SortedSet<string>(StringComparer.Ordinal);
        IEnumerable<string> Inherited(string type, HashSet<string> seen)
        {
            if (!seen.Add(type) || !Bases.TryGetValue(type, out var bases)) yield break;
            foreach (var b in bases)
            {
                var s = b.Substring(b.LastIndexOf('.') + 1);
                if (!byShort.TryGetValue(s, out var full)) continue;
                if (Members.TryGetValue(full, out var ms)) foreach (var m in ms) if (m != "ctor") yield return m;
                foreach (var m in Inherited(full, seen)) yield return m;
            }
        }
        foreach (var type in Bases.Keys)
            foreach (var m in Inherited(type, new HashSet<string>()))
                reach.Add(type + "." + m);
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["decls"] = Decls.OrderBy(d => d[0], StringComparer.Ordinal).ToList(),
            ["reach"] = reach.ToList(),
            ["files"] = trees.Count,
        });
        Console.Out.Write(json);
        return 0;
    }
}
