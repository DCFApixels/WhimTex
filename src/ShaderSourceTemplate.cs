using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DCFApixels.WhimTex
{
    // Comment metadata selects source spans; HLSL semantics remain the compiler's responsibility.
    internal sealed class ShaderSourceTemplate
    {
        const int MaximumCacheEntries = 16;
        const long MaximumCacheCharacters = 4 * 1024 * 1024;
        static readonly object cacheLock = new object();
        static readonly Dictionary<(string physical, string display), CacheEntry> cache = new();
        static int cacheRevision = -1;
        static long cacheCharacters, clock;

        sealed class CacheEntry
        {
            internal ShaderSourceTemplate template;
            internal long used;
        }

        sealed class Declaration
        {
            internal string name;
            internal string[] dependencyNames;
            internal int line, index;
            internal int[] dependencies;
            internal readonly List<Instruction> instructions = new();
        }

        enum InstructionKind { Source, If, Else, EndIf }

        readonly struct Instruction
        {
            internal readonly InstructionKind kind;
            internal readonly string text;
            internal readonly int line;
            internal readonly bool negate;
            internal Instruction(InstructionKind kind, string text, int line, bool negate = false)
            { this.kind = kind; this.text = text; this.line = line; this.negate = negate; }
        }

        struct ParseBranch
        {
            internal int line;
            internal bool hasElse;
        }

        readonly struct GenerateBranch
        {
            internal readonly bool parent, condition;
            internal GenerateBranch(bool parent, bool condition)
            { this.parent = parent; this.condition = condition; }
        }

        struct LexicalState
        {
            internal bool blockComment, lineComment, continued;
            internal char quote;
            // Leading whitespace, directive name, include operand, or ordinary code.
            internal int preprocessor;
        }

        readonly string path;
        readonly long sourceCharacters;
        readonly List<Declaration> blockList = new(), featureList = new();
        readonly Dictionary<string, Declaration> blocksByName = new(StringComparer.Ordinal);
        readonly Dictionary<string, Declaration> featuresByName = new(StringComparer.Ordinal);
        int[] blockOrder, featureOrder;

        ShaderSourceTemplate(string path, int sourceCharacters)
        { this.path = path; this.sourceCharacters = sourceCharacters; }

        /// <summary>Loads a filesystem or Unity asset path. Source imports invalidate the bounded parse cache.</summary>
        internal static ShaderSourceTemplate Load(string path)
        {
            path = ValidatePath(path);
            var key = (path.StartsWith("Packages/", StringComparison.Ordinal) || path.StartsWith("Assets/", StringComparison.Ordinal)
                ? ShaderGraphSources.PhysicalPath(path) : Path.GetFullPath(path), path);
            lock (cacheLock)
            {
                int revision = ShaderProgramSourceRevision.Current;
                if (cacheRevision != revision)
                { cache.Clear(); cacheCharacters = 0; cacheRevision = revision; }
                if (cache.TryGetValue(key, out var hit))
                { hit.used = ++clock; return hit.template; }

                // Failed reads/parses never replace a valid template or enter the cache.
                var template = Parse(File.ReadAllText(key.Item1), path);
                if (template.sourceCharacters <= MaximumCacheCharacters &&
                    revision == ShaderProgramSourceRevision.Current)
                {
                    while (cache.Count > 0 && (cache.Count >= MaximumCacheEntries ||
                        cacheCharacters + template.sourceCharacters > MaximumCacheCharacters))
                    {
                        var oldest = key;
                        long age = long.MaxValue;
                        foreach (var pair in cache)
                            if (pair.Value.used < age) { oldest = pair.Key; age = pair.Value.used; }
                        cacheCharacters -= cache[oldest].template.sourceCharacters;
                        cache.Remove(oldest);
                    }
                    cache.Add(key, new CacheEntry { template = template, used = ++clock });
                    cacheCharacters += template.sourceCharacters;
                }
                return template;
            }
        }

        /// <summary>Parses and validates all metadata, without filesystem access or caching.</summary>
        internal static ShaderSourceTemplate Parse(string source, string path)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var template = new ShaderSourceTemplate(ValidatePath(path), source.Length);
            template.Read(source);
            return template;
        }

        void Read(string source)
        {
            Declaration block = null;
            var branches = new Stack<ParseBranch>();
            var lexical = new LexicalState();
            using var reader = new StringReader(source);
            int lineNumber = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                // A BOM is not part of the first HLSL token (Parse also accepts in-memory text).
                if (lineNumber == 1 && line.Length > 0 && line[0] == '\uFEFF') line = line.Substring(1);
                string directive = DirectiveText(line, lexical);
                if (directive == null)
                {
                    if (block != null) block.instructions.Add(new Instruction(InstructionKind.Source, line, lineNumber));
                    ScanLine(line, ref lexical, null, null);
                    continue;
                }
                if (lexical.continued || line.EndsWith("\\", StringComparison.Ordinal))
                    throw Error(lineNumber, "Annotations cannot split or continue a backslash-continued line.");

                int nameEnd = 4; // @wt.
                while (nameEnd < directive.Length && IsIdentifierPart(directive[nameEnd])) nameEnd++;
                string command = directive.Substring(4, nameEnd - 4);
                string tail = directive.Substring(nameEnd).Trim();
                switch (command)
                {
                    case "feature":
                        if (block != null) throw Error(lineNumber, "Feature declarations must be outside blocks.");
                        AddDeclaration(Arguments(tail, lineNumber), lineNumber, featureList, featuresByName, "feature");
                        break;
                    case "block":
                        if (block != null) throw Error(lineNumber, "Blocks cannot be nested; close '" + block.name + "' first.");
                        block = AddDeclaration(Arguments(tail, lineNumber), lineNumber, blockList, blocksByName, "block");
                        break;
                    case "end":
                        NoArguments(tail, lineNumber);
                        RequireBlock(block, lineNumber);
                        if (branches.Count != 0) throw Error(lineNumber, "Close @wt.if from line " + branches.Peek().line + " before @wt.end.");
                        block = null;
                        break;
                    case "if":
                        RequireBlock(block, lineNumber);
                        string feature = Arguments(tail, lineNumber);
                        bool negate = feature.StartsWith("!", StringComparison.Ordinal);
                        if (negate) feature = feature.Substring(1).Trim();
                        RequireIdentifier(feature, lineNumber, "feature condition");
                        block.instructions.Add(new Instruction(InstructionKind.If, feature, lineNumber, negate));
                        branches.Push(new ParseBranch { line = lineNumber });
                        break;
                    case "else":
                        NoArguments(tail, lineNumber);
                        RequireBlock(block, lineNumber);
                        if (branches.Count == 0) throw Error(lineNumber, "@wt.else has no matching @wt.if.");
                        var branch = branches.Pop();
                        if (branch.hasElse) throw Error(lineNumber, "Duplicate @wt.else for @wt.if from line " + branch.line + ".");
                        branch.hasElse = true;
                        branches.Push(branch);
                        block.instructions.Add(new Instruction(InstructionKind.Else, null, lineNumber));
                        break;
                    case "endif":
                        NoArguments(tail, lineNumber);
                        RequireBlock(block, lineNumber);
                        if (branches.Count == 0) throw Error(lineNumber, "@wt.endif has no matching @wt.if.");
                        branches.Pop();
                        block.instructions.Add(new Instruction(InstructionKind.EndIf, null, lineNumber));
                        break;
                    default:
                        throw Error(lineNumber, "Unknown annotation '" + directive + "'.");
                }
            }
            if (branches.Count != 0) throw Error(branches.Peek().line, "Unclosed @wt.if; expected @wt.endif.");
            if (block != null) throw Error(block.line, "Unclosed block '" + block.name + "'; expected @wt.end.");

            featureOrder = ResolveDeclarations(featureList, featuresByName, "feature");
            blockOrder = ResolveDeclarations(blockList, blocksByName, "block");
            foreach (var item in blockList)
                foreach (var instruction in item.instructions)
                    if (instruction.kind == InstructionKind.If && !featuresByName.ContainsKey(instruction.text))
                        throw Error(instruction.line, "Unknown feature '" + instruction.text + "'.");
        }

        static string DirectiveText(string line, LexicalState lexical)
        {
            if (lexical.blockComment || lexical.lineComment || lexical.quote != '\0') return null;
            string text = line.TrimStart();
            if (!text.StartsWith("//", StringComparison.Ordinal)) return null;
            text = text.Substring(2).Trim();
            return text.StartsWith("@wt.", StringComparison.Ordinal) ? text : null;
        }

        string Arguments(string tail, int line)
        {
            if (tail.Length < 2 || tail[0] != '(' || tail[tail.Length - 1] != ')')
                throw Error(line, "Expected '(...)' with no trailing text.");
            return tail.Substring(1, tail.Length - 2).Trim();
        }

        void NoArguments(string tail, int line)
        { if (tail.Length != 0) throw Error(line, "This annotation takes no arguments or trailing text."); }

        void RequireBlock(Declaration block, int line)
        { if (block == null) throw Error(line, "This annotation must be inside a block."); }

        Declaration AddDeclaration(string arguments, int line, List<Declaration> list,
            Dictionary<string, Declaration> names, string kind)
        {
            int separator = arguments.IndexOf(';');
            string name = (separator < 0 ? arguments : arguments.Substring(0, separator)).Trim();
            RequireIdentifier(name, line, kind + " name");
            if (names.TryGetValue(name, out var previous))
                throw Error(line, "Duplicate " + kind + " '" + name + "' (first declared at line " + previous.line + ").");
            string dependencies = separator < 0 ? "" : arguments.Substring(separator + 1).Trim();
            string[] dependencyNames = dependencies.Length == 0 ? Array.Empty<string>() : dependencies.Split(',');
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dependencyNames.Length; i++)
            {
                string dependency = dependencyNames[i].Trim();
                RequireIdentifier(dependency, line, kind + " dependency");
                if (!seen.Add(dependency)) throw Error(line, "Duplicate " + kind + " dependency '" + dependency + "'.");
                dependencyNames[i] = dependency;
            }
            var declaration = new Declaration { name = name, line = line, index = list.Count, dependencyNames = dependencyNames };
            list.Add(declaration);
            names.Add(name, declaration);
            return declaration;
        }

        int[] ResolveDeclarations(List<Declaration> declarations, Dictionary<string, Declaration> names, string kind)
        {
            var remaining = new int[declarations.Count];
            var dependents = new List<int>[declarations.Count];
            var ready = new SortedSet<int>();
            foreach (var declaration in declarations)
            {
                declaration.dependencies = new int[declaration.dependencyNames.Length];
                for (int i = 0; i < declaration.dependencyNames.Length; i++)
                {
                    string name = declaration.dependencyNames[i];
                    if (!names.TryGetValue(name, out var dependency))
                        throw Error(declaration.line, "Unknown " + kind + " dependency '" + name + "' in '" + declaration.name + "'.");
                    declaration.dependencies[i] = dependency.index;
                    if (dependents[dependency.index] == null) dependents[dependency.index] = new List<int>();
                    dependents[dependency.index].Add(declaration.index);
                }
                remaining[declaration.index] = declaration.dependencies.Length;
                if (declaration.dependencies.Length == 0) ready.Add(declaration.index);
            }
            var order = new int[declarations.Count];
            int count = 0;
            while (ready.Count != 0)
            {
                int index = ready.Min;
                ready.Remove(index);
                order[count++] = index;
                if (dependents[index] == null) continue;
                foreach (int dependent in dependents[index])
                    if (--remaining[dependent] == 0) ready.Add(dependent);
            }
            if (count != declarations.Count)
            {
                // Follow unresolved edges to report an actual cycle, without recursive traversal.
                int index = 0;
                while (remaining[index] == 0) index++;
                var chain = new List<int>();
                var visited = new HashSet<int>();
                while (visited.Add(index))
                {
                    chain.Add(index);
                    foreach (int dependency in declarations[index].dependencies)
                        if (remaining[dependency] > 0) { index = dependency; break; }
                }
                var cycle = new StringBuilder();
                for (int i = chain.IndexOf(index); i < chain.Count; i++)
                    cycle.Append(declarations[chain[i]].name).Append(" -> ");
                cycle.Append(declarations[index].name);
                throw Error(declarations[index].line, "Cyclic " + kind + " dependencies: " + cycle + ".");
            }
            return order;
        }

        /// <summary>Emits the selected blocks and their dependencies, with original source locations.</summary>
        internal string Generate(IEnumerable<string> blocks, IEnumerable<string> features,
            IReadOnlyDictionary<string, string> replacements)
        {
            bool[] selectedBlocks = Select(blocks, blockList, blocksByName, blockOrder, nameof(blocks));
            bool[] selectedFeatures = Select(features, featureList, featuresByName, featureOrder, nameof(features));
            Dictionary<string, string> tokens = null;
            if (replacements != null && replacements.Count != 0)
            {
                tokens = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in replacements)
                {
                    if (!IsIdentifier(pair.Key) || pair.Value == null ||
                        pair.Value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                        throw new ArgumentException(path + ":1: Replacements require identifier keys and non-null, single-line values.", nameof(replacements));
                    tokens.Add(pair.Key, pair.Value);
                }
            }
            var output = new StringBuilder();
            var branches = new Stack<GenerateBranch>();
            foreach (int index in blockOrder)
            {
                if (!selectedBlocks[index]) continue;
                bool enabled = true;
                int nextLine = -1;
                var lexical = new LexicalState();
                foreach (var instruction in blockList[index].instructions)
                {
                    switch (instruction.kind)
                    {
                        case InstructionKind.If:
                            bool condition = selectedFeatures[featuresByName[instruction.text].index] != instruction.negate;
                            branches.Push(new GenerateBranch(enabled, condition));
                            enabled = enabled && condition;
                            break;
                        case InstructionKind.Else:
                            var branch = branches.Peek();
                            enabled = branch.parent && !branch.condition;
                            break;
                        case InstructionKind.EndIf:
                            enabled = branches.Pop().parent;
                            break;
                        case InstructionKind.Source:
                            if (!enabled) break;
                            if (nextLine != instruction.line)
                                output.Append("#line ").Append(instruction.line).Append(" \"").Append(path).Append("\"\n");
                            ScanLine(instruction.text, ref lexical, tokens, output);
                            output.Append('\n');
                            nextLine = instruction.line + 1;
                            break;
                    }
                }
            }
            return output.ToString();
        }

        bool[] Select(IEnumerable<string> requested, List<Declaration> declarations,
            Dictionary<string, Declaration> names, int[] order, string parameter)
        {
            var selected = new bool[declarations.Count];
            if (requested != null)
                foreach (string name in requested)
                {
                    if (name == null || !names.TryGetValue(name, out var declaration))
                        throw new ArgumentException(path + ":1: Unknown requested " + parameter + " name '" + name + "'.", parameter);
                    selected[declaration.index] = true;
                }
            // Reverse topological order propagates selection through the complete dependency graph.
            for (int i = order.Length - 1; i >= 0; i--)
                if (selected[order[i]])
                    foreach (int dependency in declarations[order[i]].dependencies) selected[dependency] = true;
            return selected;
        }

        static void ScanLine(string line, ref LexicalState state,
            Dictionary<string, string> replacements, StringBuilder output)
        {
            if (!state.continued) state.preprocessor = 0;
            int copied = 0;
            for (int i = 0; i < line.Length;)
            {
                char current = line[i];
                char next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (state.lineComment) break;
                if (state.blockComment)
                {
                    if (current == '*' && next == '/') { state.blockComment = false; i += 2; }
                    else i++;
                    continue;
                }
                if (state.quote != '\0')
                {
                    if (current == '\\' && next != '\0' && state.quote != '>') i += 2;
                    else { if (current == state.quote) state.quote = '\0'; i++; }
                    continue;
                }
                if (current == '/' && next == '/') { state.lineComment = true; break; }
                if (current == '/' && next == '*') { state.blockComment = true; i += 2; continue; }
                if (char.IsWhiteSpace(current)) { i++; continue; }
                if (current == '#' && state.preprocessor == 0) { state.preprocessor = 1; i++; continue; }
                if (current == '<' && state.preprocessor == 2)
                { state.quote = '>'; state.preprocessor = 3; i++; continue; }
                if (current == '"' || current == '\'')
                { state.quote = current; state.preprocessor = 3; i++; continue; }
                if (IsDigit(current) || current == '.' && IsDigit(next))
                {
                    state.preprocessor = 3;
                    // Consume a preprocessing number, including suffixes and exponent signs.
                    // For example, replacement keys 'e3' or 'f' must not change 1e3 or 0.5f.
                    i++;
                    while (i < line.Length)
                    {
                        char previous = line[i - 1];
                        current = line[i];
                        if (IsIdentifierPart(current) || current == '.' ||
                            (current == '+' || current == '-') && (previous == 'e' || previous == 'E' || previous == 'p' || previous == 'P')) i++;
                        else break;
                    }
                    continue;
                }
                if (IsIdentifierStart(current))
                {
                    int start = i++;
                    while (i < line.Length && IsIdentifierPart(line[i])) i++;
                    string token = replacements != null || state.preprocessor == 1 ? line.Substring(start, i - start) : null;
                    state.preprocessor = state.preprocessor == 1 && token == "include" ? 2 : 3;
                    if (replacements != null && replacements.TryGetValue(token, out string replacement))
                    {
                        output.Append(line, copied, start - copied).Append(replacement);
                        copied = i;
                    }
                    continue;
                }
                if (current != '\\' || next != '\0') state.preprocessor = 3;
                i++;
            }
            if (output != null) output.Append(line, copied, line.Length - copied);
            state.continued = line.EndsWith("\\", StringComparison.Ordinal);
            if (!state.continued) state.lineComment = false;
        }

        static string ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("A source path without quotes or line breaks is required.", nameof(path));
            return path.Replace('\\', '/');
        }

        void RequireIdentifier(string name, int line, string role)
        { if (!IsIdentifier(name)) throw Error(line, "Invalid " + role + " '" + name + "'; expected [A-Za-z_][A-Za-z0-9_]*."); }

        static bool IsIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name) || !IsIdentifierStart(name[0])) return false;
            for (int i = 1; i < name.Length; i++) if (!IsIdentifierPart(name[i])) return false;
            return true;
        }

        static bool IsIdentifierStart(char value) => value >= 'A' && value <= 'Z' || value >= 'a' && value <= 'z' || value == '_';
        static bool IsIdentifierPart(char value) => IsIdentifierStart(value) || IsDigit(value);
        static bool IsDigit(char value) => value >= '0' && value <= '9';
        FormatException Error(int line, string message) => new FormatException(path + ":" + line + ": " + message);
    }
}
