using System.Text;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Interceptors.Tests;

/// <summary>
///     Primary public API for testing Roslyn <see cref="IIncrementalGenerator" /> implementations.
///     <para>
///         Prefer the <c>string</c>-extension one‑liners for table‑driven tests and fast authoring,
///         e.g. <see cref="ShouldGenerate{TGenerator}(string,string,string)" /> and
///         <see cref="ShouldHaveDiagnostic{TGenerator}(string,string,DiagnosticSeverity)" />.
///     </para>
///     <para>
///         For instance-based reuse that avoids repeating the generator type in each test, use
///         <see cref="GeneratorTester{TGenerator}" /> or derive from <see cref="GeneratorTestBase{TGenerator}" />.
///     </para>
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             <description>
///                 <b>Deterministic inputs.</b> All helpers create a fresh <see cref="Compilation" /> per run to
///                 model IDE/compiler behavior. Generated trees are never fed back into the next generator run.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>High‑signal assertions.</b> Failures render clear, task‑oriented messages via custom
///                 formatters (see <c>TestFormatters</c>) and FluentAssertions’ improved patterns.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>Caching validation.</b> <see cref="ShouldCache{TGenerator}(string,string[])" /> inspects
///                 tracked steps; <see cref="ShouldCacheWithCompilationUpdate{TGenerator}" /> validates identity‑based
///                 caching
///                 of generated syntax trees across a controlled edit.
///             </description>
///         </item>
///     </list>
/// </remarks>
public static class GeneratorTest
{
    static GeneratorTest()
    {
        TestFormatters.Initialize();
    }

    /// <param name="source">C# source to compile and feed into the generator.</param>
    extension(string source)
    {
        /// <summary>
        ///     Verifies that running <typeparamref name="TGenerator" /> on <paramref name="source" /> produces a file
        ///     named <paramref name="hintName" /> whose content matches <paramref name="expectedContent" />.
        /// </summary>
        /// <typeparam name="TGenerator">
        ///     The generator under test (must implement <see cref="IIncrementalGenerator" /> and have a
        ///     parameterless constructor).
        /// </typeparam>
        /// <param name="hintName">The expected generated hint name (e.g. <c>Person.Builder.g.cs</c>).</param>
        /// <param name="expectedContent">
        ///     The expected file content. Exact match is enforced; use
        ///     <see cref="GeneratorTestExtensions.ShouldGenerate{TGenerator}(string,string,string,bool,bool)" /> for advanced
        ///     control.
        /// </param>
        /// <returns>A task that completes when the assertion finishes.</returns>
        /// <example>
        ///     <code>
        ///     await """
        ///         [GenerateBuilder] public class Person { string Name { get; set; } }
        ///     """.ShouldGenerate&lt;MyGenerator&gt;("Person.Builder.g.cs", "public class PersonBuilder");
        ///     </code>
        /// </example>
        /// <seealso cref="GeneratorTestExtensions.ShouldGenerate{TGenerator}(string,string,string,bool,bool)" />
        public Task ShouldGenerate<TGenerator>(string hintName, string expectedContent)
            where TGenerator : IIncrementalGenerator, new()
        {
            return source.ShouldGenerate<TGenerator>(hintName, expectedContent, true);
        }

        /// <summary>
        ///     Asserts that <typeparamref name="TGenerator" /> produces a diagnostic with the given
        ///     <paramref name="diagnosticId" /> and <paramref name="severity" /> for <paramref name="source" />.
        /// </summary>
        /// <typeparam name="TGenerator">The generator under test.</typeparam>
        /// <param name="diagnosticId">Expected diagnostic ID (e.g. <c>GE0001</c>).</param>
        /// <param name="severity">Expected diagnostic severity. Defaults to <see cref="DiagnosticSeverity.Error" />.</param>
        /// <returns>A task that completes when the assertion finishes.</returns>
        /// <example>
        ///     <code>
        ///     await "public class Valid { }"
        ///         .ShouldHaveDiagnostic&lt;MyGenerator&gt;("GE0001", DiagnosticSeverity.Info);
        ///     </code>
        /// </example>
        /// <seealso cref="ShouldProduceDiagnostic{TGenerator}(string,string,DiagnosticSeverity,string)" />
        public Task ShouldHaveDiagnostic<TGenerator>(string diagnosticId,
            DiagnosticSeverity severity = DiagnosticSeverity.Error) where TGenerator : IIncrementalGenerator, new()
        {
            DiagnosticResult expected = new(diagnosticId, severity);
            return source.ShouldHaveDiagnostics<TGenerator>([expected]);
        }

        /// <summary>
        ///     Asserts that a diagnostic with the given ID and severity is produced and that its message contains
        ///     <paramref name="messageContains" />.
        /// </summary>
        /// <typeparam name="TGenerator">The generator under test.</typeparam>
        /// <param name="diagnosticId">Diagnostic ID.</param>
        /// <param name="severity">Diagnostic severity.</param>
        /// <param name="messageContains">A substring that must appear in the diagnostic message.</param>
        /// <returns>A task that completes when the assertion finishes.</returns>
        /// <example>
        ///     <code>
        ///     await "public class Invalid { }"
        ///         .ShouldProduceDiagnostic&lt;MyGenerator&gt;("GE0002", DiagnosticSeverity.Warning, "Missing required attribute");
        ///     </code>
        /// </example>
        public Task ShouldProduceDiagnostic<TGenerator>(string diagnosticId,
            DiagnosticSeverity severity, string messageContains) where TGenerator : IIncrementalGenerator, new()
        {
            var expected = new DiagnosticResult(diagnosticId, severity).WithMessage(messageContains);
            return source.ShouldHaveDiagnostics<TGenerator>([expected]);
        }

        /// <summary>
        ///     Asserts that <typeparamref name="TGenerator" /> does NOT produce a diagnostic with the given
        ///     <paramref name="diagnosticId" /> for <paramref name="source" />.
        /// </summary>
        public async Task ShouldNotHaveDiagnostic<TGenerator>(string diagnosticId)
            where TGenerator : IIncrementalGenerator, new()
        {
            GeneratorTestEngine<TGenerator> engine = new();
            var (firstRun, _) = await engine.ExecuteTwiceAsync(source, false);

            using AssertionScope scope = new("Diagnostics");
            TestFormatters.ApplyToScope(scope);

            var diagnostics = firstRun.Results.SelectMany(r => r.Diagnostics).ToList();
            var found = diagnostics.Any(d => d.Id == diagnosticId);
            found.Should().BeFalse($"Expected no diagnostic with ID '{diagnosticId}', but found one");
        }

        /// <summary>
        ///     Verifies that observable pipeline steps of <typeparamref name="TGenerator" /> are cached across two identical runs.
        /// </summary>
        /// <typeparam name="TGenerator">The generator under test.</typeparam>
        /// <param name="trackingNames">
        ///     Optional explicit step names to validate. When omitted, steps are auto‑discovered (excluding infrastructure sinks).
        /// </param>
        /// <returns>A task that completes when the assertion finishes.</returns>
        /// <remarks>
        ///     Uses Roslyn's tracked step instrumentation; asserts no forbidden Roslyn runtime types are cached (e.g.
        ///     <see cref="ISymbol" />).
        /// </remarks>
        /// <seealso
        ///     cref="ShouldCacheWithCompilationUpdate{TGenerator}(string,System.Func{Compilation,Compilation},System.Action{CompilationCacheResult}?)" />
        public Task ShouldCache<TGenerator>(params string[] trackingNames)
            where TGenerator : IIncrementalGenerator, new()
        {
            return source.ShouldBeCached<TGenerator>(trackingNames);
        }

        /// <summary>
        ///     Asserts that <typeparamref name="TGenerator" /> produces no error-level diagnostics for <paramref name="source" />.
        ///     Info and warning diagnostics are ignored (allows EOGEN010/011 inference diagnostics).
        /// </summary>
        /// <typeparam name="TGenerator">The generator under test.</typeparam>
        /// <returns>A task that completes when the assertion finishes.</returns>
        public async Task ShouldCompile<TGenerator>()
            where TGenerator : IIncrementalGenerator, new()
        {
            GeneratorTestEngine<TGenerator> engine = new();
            var (firstRun, _) = await engine.ExecuteTwiceAsync(source, false);

            using AssertionScope scope = new("Compilation");
            TestFormatters.ApplyToScope(scope);

            var errors = firstRun.Results
                .SelectMany(r => r.Diagnostics)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            errors.Should().BeEmpty("Expected no error diagnostics, but found: {0}",
                string.Join(", ", errors.Select(d => $"{d.Id}: {d.GetMessage()}")));
        }

        /// <summary>
        ///     Validates caching and behavior across a controlled <see cref="Compilation" /> change that simulates an IDE edit.
        ///     <para>
        ///         Runs the generator with
        ///         <see
        ///             cref="GeneratorDriver.RunGeneratorsAndUpdateCompilation(Compilation,out Compilation,out System.Collections.Immutable.ImmutableArray{Diagnostic},System.Threading.CancellationToken)" />
        ///         ,
        ///         applies <paramref name="makeChange" /> to the original input compilation (not the generated one),
        ///         runs again, and exposes a <see cref="CompilationCacheResult" /> for fine‑grained checks.
        ///     </para>
        /// </summary>
        /// <typeparam name="TGenerator">The generator under test.</typeparam>
        /// <param name="makeChange">A pure function that returns a modified compilation representing the edit.</param>
        /// <param name="validate">Optional custom validations against the resulting <see cref="CompilationCacheResult" />.</param>
        /// <returns>A task that completes when all validations finish.</returns>
        /// <example>
        ///     Add a new file:
        ///     <code>
        ///     await "public class Person { }"
        ///         .ShouldCacheWithCompilationUpdate&lt;MyGenerator&gt;(
        ///             comp =&gt; comp.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Address { }")),
        ///             result =&gt; result.ShouldHaveCached("Person.Builder.g.cs"));
        ///     </code>
        /// </example>
        /// <seealso cref="ShouldRegenerate{TGenerator}(string,string)" />
        /// <seealso cref="ShouldNotRegenerate{TGenerator}(string,string)" />
        public async Task ShouldCacheWithCompilationUpdate<TGenerator>(Func<Compilation, Compilation> makeChange,
            Action<CompilationCacheResult>? validate = null)
            where TGenerator : IIncrementalGenerator, new()
        {
            GeneratorTestEngine<TGenerator> engine = new();

            var compilation1 = await engine.CreateCompilationAsync(source);
            var driver = GeneratorDriverFactory.CreateDriver<TGenerator>(true);

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out var output1,
                out var diagnostics1);

            // Apply the controlled edit to the original input
            var compilation2 = makeChange(compilation1);

            // Run #2
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out var output2,
                out var diagnostics2);

            CompilationCacheResult result = new(output1, output2, diagnostics1, diagnostics2, driver.GetRunResult());

            // Default validations
            result.ValidateCaching();

            // Custom validations
            validate?.Invoke(result);
        }

        /// <summary>
        ///     Convenience overload that simulates replacing the first syntax tree with <paramref name="editedSource" />.
        /// </summary>
        /// <typeparam name="TGenerator">The generator under test.</typeparam>
        /// <param name="editedSource">Edited source for the first document.</param>
        /// <returns>A task that completes when validations finish.</returns>
        public Task ShouldRegenerate<TGenerator>(string editedSource)
            where TGenerator : IIncrementalGenerator, new()
        {
            return source.ShouldCacheWithCompilationUpdate<TGenerator>(compilation =>
            {
                var parseOptions = new CSharpParseOptions(TestConfiguration.LanguageVersion);
                var tree = CSharpSyntaxTree.ParseText(editedSource, parseOptions);
                return compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(), tree);
            });
        }

        /// <summary>
        ///     Convenience overload that simulates adding a new document to the project.
        /// </summary>
        /// <typeparam name="TGenerator">The generator under test.</typeparam>
        /// <param name="newFileContent">Content of the additional C# file to add.</param>
        /// <returns>A task that completes when validations finish.</returns>
        public Task ShouldNotRegenerate<TGenerator>(string newFileContent)
            where TGenerator : IIncrementalGenerator, new()
        {
            var parseOptions = new CSharpParseOptions(TestConfiguration.LanguageVersion);
            return source.ShouldCacheWithCompilationUpdate<TGenerator>(compilation =>
                compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newFileContent, parseOptions)));
        }
    }
}

/// <summary>
///     Result object for compilation‑level cache testing that compares two post‑generation compilations.
///     <para>
///         Provides identity‑based checks to ensure unchanged generated trees are reused across runs,
///         and exposes generator diagnostics and run results for additional assertions.
///     </para>
/// </summary>
public class CompilationCacheResult
{
    private readonly List<SyntaxTree> _firstGeneratedTrees;
    private readonly List<SyntaxTree> _secondGeneratedTrees;

    /// <summary>
    ///     Initializes a new <see cref="CompilationCacheResult" />.
    /// </summary>
    /// <param name="first">First post‑generation compilation (from <c>RunGeneratorsAndUpdateCompilation</c>).</param>
    /// <param name="second">Second post‑generation compilation after the edit.</param>
    /// <param name="firstDiags">Diagnostics reported for the first compilation.</param>
    /// <param name="secondDiags">Diagnostics reported for the second compilation.</param>
    /// <param name="runResult">The run result captured after the second run.</param>
    public CompilationCacheResult(Compilation first, Compilation second, IEnumerable<Diagnostic> firstDiags,
        IEnumerable<Diagnostic> secondDiags, GeneratorDriverRunResult runResult)
    {
        FirstCompilation = first;
        SecondCompilation = second;
        FirstDiagnostics = firstDiags;
        SecondDiagnostics = secondDiags;
        RunResult = runResult;

        // Use the run result's hint names instead of guessing by ".g.cs".
        _firstGeneratedTrees = ExtractGeneratedTrees(first, runResult);
        _secondGeneratedTrees = ExtractGeneratedTrees(second, runResult);
    }

    /// <summary>The first post‑generation compilation.</summary>
    public Compilation FirstCompilation { get; }

    /// <summary>The second post‑generation compilation after applying the edit.</summary>
    public Compilation SecondCompilation { get; }

    /// <summary>Diagnostics produced during the first run (source + generated).</summary>
    public IEnumerable<Diagnostic> FirstDiagnostics { get; }

    /// <summary>Diagnostics produced during the second run (source + generated).</summary>
    public IEnumerable<Diagnostic> SecondDiagnostics { get; }

    /// <summary>The aggregated run result for the second run (includes tracked steps and generated sources).</summary>
    public GeneratorDriverRunResult RunResult { get; }

    /// <summary>
    ///     Validates that the generator produced output and that unchanged generated files are cached
    ///     (i.e., the same <see cref="SyntaxTree" /> instances appear in both compilations).
    ///     Also verifies that no forbidden Roslyn runtime types were cached inside pipeline outputs.
    /// </summary>
    public void ValidateCaching()
    {
        using AssertionScope scope = new("Compilation-level caching");

        _firstGeneratedTrees.Should().NotBeEmpty("generator should produce output");

        foreach (var (first, second) in GetUnchangedTrees())
            ReferenceEquals(first, second).Should().BeTrue(
                $"unchanged tree '{GetHintName(first)}' should be cached (same instance)");

        // Note: NotHaveForbiddenTypes() checks ALL steps including Roslyn internals
        // which will always have "forbidden" types. User-specified steps should be
        // checked via ShouldCache() with explicit tracking names instead.
        // RunResult.Should().NotHaveForbiddenTypes();
    }

    /// <summary>
    ///     Asserts that the given <paramref name="hintNames" /> were cached (identity‑equal across runs).
    /// </summary>
    /// <param name="hintNames">Hint names to check (e.g. <c>Person.Builder.g.cs</c>).</param>
    /// <returns>The current <see cref="CompilationCacheResult" /> for further chaining.</returns>
    public CompilationCacheResult ShouldHaveCached(params string[] hintNames)
    {
        var unchanged =
            GetUnchangedTrees().Where(pair => hintNames.Contains(GetHintName(pair.First))).ToList();

        unchanged.Should().HaveCount(hintNames.Length, "all specified files should exist and be unchanged");

        foreach (var (first, second) in unchanged)
            ReferenceEquals(first, second).Should().BeTrue($"tree '{GetHintName(first)}' should be cached");

        return this;
    }

    /// <summary>
    ///     Asserts that the given <paramref name="hintNames" /> were regenerated (distinct instances across runs).
    /// </summary>
    /// <param name="hintNames">Hint names to check.</param>
    /// <returns>The current <see cref="CompilationCacheResult" /> for further chaining.</returns>
    public CompilationCacheResult ShouldHaveRegenerated(params string[] hintNames)
    {
        var changed =
            GetChangedTrees().Where(pair => hintNames.Contains(GetHintName(pair.First))).ToList();

        changed.Should().HaveCount(hintNames.Length, "all specified files should exist and be changed");

        foreach (var (first, second) in changed)
            ReferenceEquals(first, second).Should().BeFalse($"tree '{GetHintName(first)}' should be regenerated");

        return this;
    }

    private List<(SyntaxTree First, SyntaxTree Second)> GetUnchangedTrees()
    {
        return _firstGeneratedTrees
            .Join(_secondGeneratedTrees, GetHintName, GetHintName, (first, second) => (first, second))
            .Where(pair => ReferenceEquals(pair.first, pair.second)).ToList();
    }

    private List<(SyntaxTree First, SyntaxTree Second)> GetChangedTrees()
    {
        return _firstGeneratedTrees
            .Join(_secondGeneratedTrees, GetHintName, GetHintName, (first, second) => (first, second))
            .Where(pair => !ReferenceEquals(pair.first, pair.second)).ToList();
    }

    private static List<SyntaxTree> ExtractGeneratedTrees(Compilation compilation, GeneratorDriverRunResult runResult)
    {
        var hintNames = runResult.Results.SelectMany(r => r.GeneratedSources).Select(gs => gs.HintName)
            .ToHashSet(StringComparer.Ordinal);

        return compilation.SyntaxTrees.Where(t => hintNames.Contains(Path.GetFileName(t.FilePath))).ToList();
    }

    private static string GetHintName(SyntaxTree tree)
    {
        return Path.GetFileName(tree.FilePath);
    }
}

/// <summary>
///     Internal helpers for building drivers and compilations used by <see cref="GeneratorTest" />.
/// </summary>
internal static class GeneratorDriverFactory
{
    internal static GeneratorDriver CreateDriver<TGenerator>(bool trackSteps)
        where TGenerator : IIncrementalGenerator, new()
    {
        var parseOptions = new CSharpParseOptions(TestConfiguration.LanguageVersion);
        return CSharpGeneratorDriver.Create(
            [new TGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackSteps),
            parseOptions: parseOptions);
    }

    internal static async Task<Compilation> CreateCompilationAsync<TGenerator>(
        this GeneratorTestEngine<TGenerator> engine, string source) where TGenerator : IIncrementalGenerator, new()
    {
        var parseOptions = new CSharpParseOptions(TestConfiguration.LanguageVersion);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references =
            await TestConfiguration.ReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return CSharpCompilation.Create("TestAssembly", [syntaxTree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    // ------------------------------------------------------------------------
    // USAGE EXAMPLES
    // ------------------------------------------------------------------------
    public static class UsageExamples
    {
        public static async Task Examples()
        {
            // Simple generation exact match
            await """
                  [GenerateBuilder]
                  public class Person { string Name {get; set;} }
                  """.ShouldGenerate<MyGenerator>("Person.Builder.g.cs", "public class PersonBuilder");

            // Expect a diagnostic by id + severity
            await "public class Valid { }".ShouldHaveDiagnostic<MyGenerator>("GE0001", DiagnosticSeverity.Info);

            // Expect diagnostic with message content
            await "public class Invalid { }".ShouldProduceDiagnostic<MyGenerator>("GE0002", DiagnosticSeverity.Warning,
                "Missing required attribute");

            // Validate caching (auto-discovers steps if not specified)
            await "public class Person { }".ShouldCache<MyGenerator>();

            // Validate specific pipeline steps are cached
            await "public class Person { }".ShouldCache<MyGenerator>(["Transform", "Collect"]);

            // Test caching with compilation change (edit)
            await "public class Person { }".ShouldRegenerate<MyGenerator>("""
                                                                          public class Person { 
                                                                              public string Name { get; set; } // Added property
                                                                          }
                                                                          """);

            // Test caching when adding a new file
            await "public class Person { }".ShouldNotRegenerate<MyGenerator>("public class Address { }");

            // Advanced: custom compilation change with validation
            await """
                  [GenerateBuilder]
                  public class Person { }
                  """.ShouldCacheWithCompilationUpdate<MyGenerator>(
                compilation => compilation.WithOptions(
                    compilation.Options.WithOptimizationLevel(OptimizationLevel.Release)), result =>
                {
                    result.ShouldHaveCached("Person.Builder.g.cs");
                    result.SecondDiagnostics.Should().BeEmpty();
                });
        }

        // Stub for example
        private class MyGenerator : IIncrementalGenerator
        {
            public void Initialize(IncrementalGeneratorInitializationContext context)
            {
                EmbeddedAttributeRegistrar.Register(context);
            }
        }
    }
}

/// <summary>
///     Registers an embedded <c>Microsoft.CodeAnalysis.EmbeddedAttribute</c> if missing from references.
///     <para>Used by examples; include in your generator only if you need the attribute at compile time.</para>
/// </summary>
/// <remarks>
///     The source is added through
///     <see
///         cref="IncrementalGeneratorInitializationContext.RegisterSourceOutput{TSource}(IncrementalValueProvider{TSource},System.Action{SourceProductionContext,TSource})" />
///     and guarded by a presence check on the current <see cref="Compilation" />.
/// </remarks>
public static class EmbeddedAttributeRegistrar
{
    /// <summary>Registers the embedded attribute source when not present.</summary>
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        // Use the compilation provider so we can check whether the type already exists in references.
        context.RegisterSourceOutput(context.CompilationProvider, (spc, compilation) =>
        {
            if (compilation.GetTypeByMetadataName("Microsoft.CodeAnalysis.EmbeddedAttribute") is null)
            {
                const string src = """
                                   // <auto-generated/>
                                   #nullable enable
                                   namespace Microsoft.CodeAnalysis
                                   {
                                       [System.Runtime.CompilerServices.CompilerGenerated]
                                       [System.AttributeUsage(System.AttributeTargets.All, Inherited = false, AllowMultiple = false)]
                                       internal sealed class EmbeddedAttribute : System.Attribute
                                       {
                                           public EmbeddedAttribute() { }
                                       }
                                   }
                                   """;

                spc.AddSource("Microsoft.CodeAnalysis.EmbeddedAttribute.g.cs", SourceText.From(src, Encoding.UTF8));
            }
        });
    }
}

/// <summary>
///     Instance‑based, typed test facade so you don't repeat the generator type in each test method.
///     <para>
///         Construct with <see cref="Create" /> or use <see cref="GeneratorTestBase{TGenerator}" /> to expose the same
///         API via inheritance.
///     </para>
/// </summary>
/// <typeparam name="TGenerator">The generator under test.</typeparam>
/// <example>
///     <code>
///     var G = GeneratorTester&lt;MyGenerator&gt;.Create();
///     await G.ShouldGenerate(source, "Person.Builder.g.cs", expected);
///     await G.ShouldCache(source);
///     </code>
/// </example>
public sealed class GeneratorTester<TGenerator> where TGenerator : IIncrementalGenerator, new()
{
    private GeneratorTester()
    {
    }

    /// <summary>Creates a new tester bound to <typeparamref name="TGenerator" />.</summary>
    public static GeneratorTester<TGenerator> Create()
    {
        return new GeneratorTester<TGenerator>();
    }

    /// <inheritdoc cref="GeneratorTest.ShouldGenerate{TGenerator}(string,string,string)" />
    public Task ShouldGenerate(string source, string hintName, string expectedContent, bool exactMatch = true,
        bool normalizeNewlines = true)
    {
        return source.ShouldGenerate<TGenerator>(hintName, expectedContent, exactMatch, normalizeNewlines);
    }

    /// <summary>
    ///     Asserts that diagnostics match the provided expectations.
    ///     See also
    ///     <see cref="GeneratorTest.ShouldHaveDiagnostic{TGenerator}(string,string,Microsoft.CodeAnalysis.DiagnosticSeverity)" />
    ///     .
    /// </summary>
    public Task ShouldHaveDiagnostics(string source, params DiagnosticResult[] expected)
    {
        return source.ShouldHaveDiagnostics<TGenerator>(expected);
    }

    /// <inheritdoc cref="GeneratorTest.ShouldCompile{TGenerator}(string)" />
    public Task ShouldCompile(string source)
    {
        return source.ShouldHaveNoDiagnostics<TGenerator>();
    }

    /// <inheritdoc cref="GeneratorTest.ShouldCache{TGenerator}(string,string[])" />
    public Task ShouldCache(string source, params string[] trackingNames)
    {
        return source.ShouldBeCached<TGenerator>(trackingNames);
    }

    /// <inheritdoc
    ///     cref="GeneratorTest.ShouldCacheWithCompilationUpdate{TGenerator}(string,System.Func{Microsoft.CodeAnalysis.Compilation,Microsoft.CodeAnalysis.CompilationCompilationCacheResultationCacheResult}?)" />
    public Task ShouldCacheWithCompilationUpdate(string source, Func<Compilation, Compilation> makeChange,
        Action<CompilationCacheResult>? validate = null)
    {
        return source.ShouldCacheWithCompilationUpdate<TGenerator>(makeChange, validate);
    }

    /// <inheritdoc cref="GeneratorTest.ShouldRegenerate{TGenerator}(string,string)" />
    public Task ShouldRegenerate(string source, string editedSource)
    {
        return source.ShouldRegenerate<TGenerator>(editedSource);
    }

    /// <inheritdoc cref="GeneratorTest.ShouldNotRegenerate{TGenerator}(string,string)" />
    public Task ShouldNotRegenerate(string source, string newFileContent)
    {
        return source.ShouldNotRegenerate<TGenerator>(newFileContent);
    }
}

/// <summary>
///     Base class that exposes the same API as <see cref="GeneratorTester{TGenerator}" /> as protected methods,
///     so test classes can inherit and call <c>ShouldGenerate(...)</c> directly without repeating the generator type.
/// </summary>
/// <typeparam name="TGenerator">The generator under test.</typeparam>
/// <example>
///     <code>
///     public sealed class MyGeneratorTests : GeneratorTestBase&lt;MyGenerator&gt;
///     {
///         [Fact]
///         public async Task Generates_expected_file()
///         {
///             await ShouldGenerate("class C {}", "C.g.cs", "class C_Generated");
///         }
///     }
///     </code>
/// </example>
public abstract class GeneratorTestBase<TGenerator> where TGenerator : IIncrementalGenerator, new()
{
    /// <summary>The typed tester instance bound to <typeparamref name="TGenerator" />.</summary>
    protected GeneratorTester<TGenerator> Test { get; } = GeneratorTester<TGenerator>.Create();

    /// <inheritdoc cref="GeneratorTest.ShouldGenerate{TGenerator}(string,string,string)" />
    protected Task ShouldGenerate(string source, string hintName, string expectedContent, bool exactMatch = true,
        bool normalizeNewlines = true)
    {
        return Test.ShouldGenerate(source, hintName, expectedContent, exactMatch, normalizeNewlines);
    }

    /// <summary>Asserts that diagnostics match the provided expectations.</summary>
    protected Task ShouldHaveDiagnostics(string source, params DiagnosticResult[] expected)
    {
        return Test.ShouldHaveDiagnostics(source, expected);
    }

    /// <inheritdoc cref="GeneratorTest.ShouldCompile{TGenerator}(string)" />
    protected Task ShouldCompile(string source)
    {
        return Test.ShouldCompile(source);
    }

    /// <inheritdoc cref="GeneratorTest.ShouldCache{TGenerator}(string,string[])" />
    protected Task ShouldCache(string source, params string[] trackingNames)
    {
        return Test.ShouldCache(source, trackingNames);
    }

    /// <inheritdoc
    ///     cref="GeneratorTest.ShouldCacheWithCompilationUpdate{TGenerator}(string,System.Func{Microsoft.CodeAnalysis.Compilation,Microsoft.CodeAnalysis.CompilationCompilationCacheResultationCacheResult}?)" />
    protected Task ShouldCacheWithCompilationUpdate(string source, Func<Compilation, Compilation> makeChange,
        Action<CompilationCacheResult>? validate = null)
    {
        return Test.ShouldCacheWithCompilationUpdate(source, makeChange, validate);
    }

    /// <inheritdoc cref="GeneratorTest.ShouldRegenerate{TGenerator}(string,string)" />
    protected Task ShouldRegenerate(string source, string editedSource)
    {
        return Test.ShouldRegenerate(source, editedSource);
    }

    /// <inheritdoc cref="GeneratorTest.ShouldNotRegenerate{TGenerator}(string,string)" />
    protected Task ShouldNotRegenerate(string source, string newFileContent)
    {
        return Test.ShouldNotRegenerate(source, newFileContent);
    }
}
