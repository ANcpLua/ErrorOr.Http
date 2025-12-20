using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using AwesomeAssertions.Formatting;
using AwesomeAssertions.Primitives;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Interceptors.Tests;

public static class TestConfiguration
{
    public const double PerformanceTolerancePercent = 0.05;
    public const bool EnableJsonReporting = true;
    public static readonly TimeSpan PerformanceToleranceAbsolute = TimeSpan.FromMilliseconds(2);

    public static LanguageVersion LanguageVersion { get; set; } = LanguageVersion.Preview;

    public static ReferenceAssemblies ReferenceAssemblies { get; set; } = ReferenceAssemblies.Net.Net90
        .AddPackages([
            new PackageIdentity("ErrorOr", "2.0.1"),
            new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0")
        ]);

    public static ImmutableArray<PortableExecutableReference> AdditionalReferences { get; } =
    [
        MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ErrorOr.ErrorOr<>).Assembly.Location),
        MetadataReference.CreateFromFile(
            typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder).Assembly.Location)
    ];

    public static IReadOnlyList<MetadataReference> CombineReferences(
        ImmutableArray<MetadataReference> references)
    {
        List<MetadataReference> combined = new(references.Length + AdditionalReferences.Length);
        combined.AddRange(references);
        combined.AddRange(AdditionalReferences);
        return combined;
    }
}

public static class GeneratorTestExtensions
{
    static GeneratorTestExtensions()
    {
        TestFormatters.Initialize();
    }

    public static DiagnosticResult Diagnostic(string id, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        return new DiagnosticResult(id, severity);
    }

    private static string BuildStepValidationError(IEnumerable<string> missingSteps, IEnumerable<string> availableSteps)
    {
        StringBuilder sb = new();
        sb.AppendLine("CACHING VALIDATION ERROR: Missing tracking step name(s)");
        foreach (var step in missingSteps) sb.AppendLine($"  - {step}");
        sb.AppendLine("\nAvailable steps:");
        foreach (var step in availableSteps.OrderBy(x => x))
            sb.AppendLine(
                $"  {(GeneratorStepAnalyzer.IsInfrastructureStep(step) ? "🔧 (Sink)" : "✅ (Observable)")} {step}");
        return sb.ToString();
    }

    private static void ValidateStepPerformance(GeneratorDriverRunResult firstRun, GeneratorDriverRunResult secondRun,
        IEnumerable<string> stepNames)
    {
        var firstSteps =
            GeneratorStepAnalyzer.ExtractSteps(firstRun);
        var secondSteps =
            GeneratorStepAnalyzer.ExtractSteps(secondRun);

        foreach (var stepName in stepNames)
        {
            var first = firstSteps[stepName];
            var second = secondSteps[stepName];
            first.Should().HaveSameCount(second, $"step {stepName} should run same number of times");

            for (var i = 0; i < first.Length; i++)
            {
                var allowedTime = first[i].ElapsedTime + Max(TestConfiguration.PerformanceToleranceAbsolute,
                    TimeSpan.FromTicks(
                        (long)(first[i].ElapsedTime.Ticks * TestConfiguration.PerformanceTolerancePercent)));

                second[i].ElapsedTime.Should().BeLessThanOrEqualTo(allowedTime,
                    $"cached run of {stepName}[{i}] should not be materially slower than baseline");
            }
        }

        static TimeSpan Max(TimeSpan a, TimeSpan b)
        {
            return a >= b ? a : b;
        }
    }

    extension(string source)
    {
        public async Task ShouldGenerate<TGenerator>(string hintName, string expectedContent,
            bool exactMatch = true, bool normalizeNewlines = true) where TGenerator : IIncrementalGenerator, new()
        {
            GeneratorTestEngine<TGenerator> engine = new();
            var (firstRun, _) = await engine.ExecuteTwiceAsync(source, true);

            using AssertionScope scope = new($"Generated file '{hintName}'");
            TestFormatters.ApplyToScope(scope);

            // Note: NotHaveForbiddenTypes() checks ALL steps including Roslyn's built-in
            // CompilationProvider which will always cache a Compilation object.
            // Use ShouldCache() with explicit tracking names for caching validation.
            var generated = firstRun.Should().HaveGeneratedSource(hintName).Which;
            generated.Should().HaveContent(expectedContent, exactMatch, normalizeNewlines);
            firstRun.Should().HaveNoDiagnostics();
        }

        public async Task ShouldHaveDiagnostics<TGenerator>(params DiagnosticResult[] expected)
            where TGenerator : IIncrementalGenerator, new()
        {
            GeneratorTestEngine<TGenerator> engine = new();
            var (firstRun, _) = await engine.ExecuteTwiceAsync(source, false);

            using AssertionScope scope = new("Diagnostics");
            TestFormatters.ApplyToScope(scope);

            var diagnostics = firstRun.Results.SelectMany(r => r.Diagnostics).ToList();
            diagnostics.BeEquivalentToDiagnostics(expected);
        }

        public Task ShouldHaveNoDiagnostics<TGenerator>()
            where TGenerator : IIncrementalGenerator, new()
        {
            return source.ShouldHaveDiagnostics<TGenerator>();
        }

        public async Task ShouldBeCached<TGenerator>(params string[] trackingNames)
            where TGenerator : IIncrementalGenerator, new()
        {
            GeneratorTestEngine<TGenerator> engine = new();
            var (firstRun, secondRun) =
                await engine.ExecuteTwiceAsync(source, true);

            var availableSteps =
                firstRun.Results.SelectMany(r => r.TrackedSteps).Select(kv => kv.Key).Distinct().ToList();
            string[] stepsToTrack;
            if (trackingNames is { Length: > 0 })
            {
                var missingSteps = trackingNames.Except(availableSteps, StringComparer.Ordinal).ToArray();
                if (missingSteps.Length > 0)
                    throw new InvalidOperationException(BuildStepValidationError(missingSteps, availableSteps));
                stepsToTrack = trackingNames;
            }
            else
            {
                stepsToTrack = availableSteps.Where(s => !GeneratorStepAnalyzer.IsInfrastructureStep(s)).ToArray();
                if (stepsToTrack.Length is 0 && availableSteps.Count > 0)
                    throw new InvalidOperationException(
                        "Auto-Discovery Failed: No observable user steps found. Ensure pipeline steps are named (e.g., .WithTrackingName(\"MyStep\")).\n" +
                        BuildStepValidationError([], availableSteps));
            }

            var report = GeneratorCachingReport.Create(firstRun, secondRun, typeof(TGenerator));

            using AssertionScope scope = new($"{typeof(TGenerator).Name} Caching Pipeline");
            TestFormatters.ApplyToScope(scope);

            report.Should().BeValidAndCached(stepsToTrack);

            if (stepsToTrack is { Length: > 0 })
                ValidateStepPerformance(firstRun, secondRun, stepsToTrack);
        }
    }
}

public sealed class GeneratorTestEngine<TGenerator> where TGenerator : IIncrementalGenerator, new()
{
    private readonly CSharpCompilationOptions _compilationOptions = new(OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true);

    private readonly CSharpParseOptions _parseOptions =
        new(TestConfiguration.LanguageVersion, DocumentationMode.Diagnose);

    public async Task<(GeneratorDriverRunResult FirstRun, GeneratorDriverRunResult SecondRun)> ExecuteTwiceAsync(
        string source, bool trackSteps)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), _parseOptions);
        var references =
            await TestConfiguration.ReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var compilation =
            CSharpCompilation.Create("TestAssembly", [syntaxTree], references, _compilationOptions);

        var driver = CreateGeneratorDriver(trackSteps, _parseOptions);

        driver = driver.RunGenerators(compilation);
        var firstRun = driver.GetRunResult();

        var secondCompilation = CSharpCompilation.Create(compilation.AssemblyName!,
            compilation.SyntaxTrees, compilation.References, _compilationOptions);
        driver = driver.RunGenerators(secondCompilation);
        var secondRun = driver.GetRunResult();

        return (firstRun, secondRun);
    }

    private static GeneratorDriver CreateGeneratorDriver(bool trackSteps, CSharpParseOptions parseOptions)
    {
        return CSharpGeneratorDriver.Create([new TGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackSteps),
            parseOptions: parseOptions);
    }
}

public static class GeneratorStepAnalyzer
{
    private static readonly string[] SinkStepPatterns =
    [
        "RegisterSourceOutput", "RegisterImplementationSourceOutput", "RegisterPostInitializationOutput", "SourceOutput"
    ];

    private static readonly string[] InfrastructureFiles =
    [
        "Attribute.g.cs", "Attributes.g.cs", "EmbeddedAttribute", "Polyfill"
    ];

    public static Dictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> ExtractSteps(
        GeneratorDriverRunResult result)
    {
        return result.Results.SelectMany(x => x.TrackedSteps).GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.SelectMany(kv => kv.Value).ToImmutableArray());
    }

    public static bool IsSink(string stepName)
    {
        return SinkStepPatterns.Any(p => stepName.AsSpan().Contains(p.AsSpan(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsInfrastructureStep(string stepName)
    {
        return !string.IsNullOrEmpty(stepName) && IsSink(stepName);
    }

    public static bool IsInfrastructureFile(string fileName)
    {
        return InfrastructureFiles.Any(p => fileName.AsSpan().Contains(p.AsSpan(), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record ForbiddenTypeViolation(string StepName, Type ForbiddenType, string Path);

public static class ForbiddenTypeAnalyzer
{
    private static readonly HashSet<Type> ForbiddenTypes =
    [
        typeof(ISymbol),
        typeof(Compilation),
        typeof(SemanticModel),
        typeof(SyntaxNode),
        typeof(SyntaxTree),
        Type.GetType("Microsoft.CodeAnalysis.IOperation, Microsoft.CodeAnalysis") ??
        throw new TypeLoadException("IOperation type could not be resolved")
    ];

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldCache = new();

    public static IReadOnlyList<ForbiddenTypeViolation> AnalyzeGeneratorRun(GeneratorDriverRunResult run)
    {
        List<ForbiddenTypeViolation> violations = new();
        HashSet<object> visited = new(ReferenceEqualityComparer.Instance);

        foreach (var (stepName, steps) in run.Results.SelectMany(r =>
                     r.TrackedSteps))
        foreach (var step in steps)
        foreach (var (output, _) in step.Outputs)
        {
            Visit(output, stepName, "Output");
            if (violations.Count >= 256) return violations;
        }

        return violations;

        void Visit(object? node, string step, string path)
        {
            if (node is null) return;

            var type = node.GetType();

            if (!type.IsValueType && !visited.Add(node)) return;

            if (IsForbiddenType(type))
            {
                violations.Add(new ForbiddenTypeViolation(step, type, path));
                return;
            }

            if (IsAllowedType(type)) return;

            if (node is IEnumerable collection and not string)
            {
                var index = 0;
                foreach (var element in collection)
                {
                    Visit(element, step, $"{path}[{index++}]");
                    if (violations.Count >= 256) return;
                }

                return;
            }

            foreach (var field in GetRelevantFields(type))
            {
                Visit(field.GetValue(node), step, $"{path}.{field.Name}");
                if (violations.Count >= 256) return;
            }
        }
    }

    public static FieldInfo[] GetRelevantFields(Type type)
    {
        return FieldCache.GetOrAdd(type,
            t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                             BindingFlags.DeclaredOnly).Where(f => !IsAllowedType(f.FieldType)).ToArray());
    }

    private static bool IsForbiddenType(Type type)
    {
        if (ForbiddenTypes.Contains(type)) return true;

        foreach (var forbidden in ForbiddenTypes)
            if (forbidden.IsAssignableFrom(type))
                return true;

        return false;
    }

    private static bool IsAllowedType(Type type)
    {
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
               type == typeof(DateTime) || type == typeof(Guid) || type == typeof(TimeSpan) ||
               (Nullable.GetUnderlyingType(type) is { } underlying && IsAllowedType(underlying));
    }
}

public sealed class GeneratorCachingReport
{
    private GeneratorCachingReport(string generatorName, IReadOnlyList<GeneratorStepAnalysis> observableSteps,
        IReadOnlyList<GeneratorStepAnalysis> sinkSteps, IReadOnlyList<ForbiddenTypeViolation> violations,
        bool producedOutput)
    {
        GeneratorName = generatorName;
        ObservableSteps = observableSteps;
        SinkSteps = sinkSteps;
        ForbiddenTypeViolations = violations;
        ProducedOutput = producedOutput;
    }

    public string GeneratorName { get; }
    public IReadOnlyList<GeneratorStepAnalysis> ObservableSteps { get; }
    public IReadOnlyList<GeneratorStepAnalysis> SinkSteps { get; }
    public IReadOnlyList<ForbiddenTypeViolation> ForbiddenTypeViolations { get; }
    public bool ProducedOutput { get; }
    public bool IsCorrect => ForbiddenTypeViolations.Count is 0;

    public static GeneratorCachingReport Create(GeneratorDriverRunResult firstRun, GeneratorDriverRunResult secondRun,
        Type generatorType)
    {
        var violations = ForbiddenTypeAnalyzer.AnalyzeGeneratorRun(firstRun);

        var firstSteps =
            GeneratorStepAnalyzer.ExtractSteps(firstRun);
        var secondSteps =
            GeneratorStepAnalyzer.ExtractSteps(secondRun);

        List<GeneratorStepAnalysis> observableSteps = [];
        List<GeneratorStepAnalysis> sinkSteps = [];

        foreach (var stepName in firstSteps.Keys.Union(secondSteps.Keys).OrderBy(n => n, StringComparer.Ordinal))
        {
            var firstStepData =
                firstSteps.GetValueOrDefault(stepName, ImmutableArray<IncrementalGeneratorRunStep>.Empty);
            var secondStepData =
                secondSteps.GetValueOrDefault(stepName, ImmutableArray<IncrementalGeneratorRunStep>.Empty);
            var hasForbidden = violations.Any(v => v.StepName == stepName);
            GeneratorStepAnalysis analysis = new(stepName, firstStepData, secondStepData, hasForbidden);

            if (GeneratorStepAnalyzer.IsInfrastructureStep(stepName)) sinkSteps.Add(analysis);
            else observableSteps.Add(analysis);
        }

        var producedOutput = secondRun.Results.SelectMany(r => r.GeneratedSources)
            .Any(gs => !GeneratorStepAnalyzer.IsInfrastructureFile(gs.HintName));

        return new GeneratorCachingReport(generatorType.Name, observableSteps, sinkSteps, violations, producedOutput);
    }
}

public readonly struct GeneratorStepAnalysis
{
    public string StepName { get; }
    public int Cached { get; }
    public int Unchanged { get; }
    public int Modified { get; }
    public int New { get; }
    public int Removed { get; }
    public bool HasForbiddenTypes { get; }
    public TimeSpan ElapsedTimeFirstRun { get; }
    public TimeSpan ElapsedTimeSecondRun { get; }

    public int TotalOutputs => Cached + Unchanged + Modified + New + Removed;
    public bool IsCachedSuccessfully => Modified is 0 && New is 0 && Removed is 0;

    public GeneratorStepAnalysis(string stepName, ImmutableArray<IncrementalGeneratorRunStep> firstRun,
        ImmutableArray<IncrementalGeneratorRunStep> secondRun, bool hasForbiddenTypes)
    {
        StepName = stepName;
        HasForbiddenTypes = hasForbiddenTypes;

        int cached = 0, unchanged = 0, modified = 0, @new = 0, removed = 0;
        foreach (var step in secondRun)
        foreach (var output in step.Outputs)
            switch (output.Reason)
            {
                case IncrementalStepRunReason.Cached: cached++; break;
                case IncrementalStepRunReason.Unchanged: unchanged++; break;
                case IncrementalStepRunReason.Modified: modified++; break;
                case IncrementalStepRunReason.New: @new++; break;
                case IncrementalStepRunReason.Removed: removed++; break;
                default: modified++; break;
            }

        Cached = cached;
        Unchanged = unchanged;
        Modified = modified;
        New = @new;
        Removed = removed;
        ElapsedTimeFirstRun = firstRun.IsDefaultOrEmpty
            ? TimeSpan.Zero
            : firstRun.Aggregate(TimeSpan.Zero, (t, s) => t + s.ElapsedTime);
        ElapsedTimeSecondRun = secondRun.IsDefaultOrEmpty
            ? TimeSpan.Zero
            : secondRun.Aggregate(TimeSpan.Zero, (t, s) => t + s.ElapsedTime);
    }

    public string FormatBreakdown()
    {
        return $"C:{Cached} U:{Unchanged} | M:{Modified} N:{New} R:{Removed} (Total:{TotalOutputs})";
    }

    public string FormatPerformance()
    {
        return $"{ElapsedTimeFirstRun.TotalMilliseconds:F2}ms -> {ElapsedTimeSecondRun.TotalMilliseconds:F2}ms";
    }
}

public static class GeneratorTestAssertions
{
    public static CachingReportAssertions Should(this GeneratorCachingReport subject)
    {
        return new CachingReportAssertions(subject, AssertionChain.GetOrCreate());
    }

    public static GeneratorRunResultAssertions Should(this GeneratorDriverRunResult subject)
    {
        return new GeneratorRunResultAssertions(subject, AssertionChain.GetOrCreate());
    }

    public static GeneratedSourceAssertions Should(this GeneratedSourceResult subject)
    {
        return new GeneratedSourceAssertions(subject, AssertionChain.GetOrCreate());
    }

    extension(ObjectAssertions assertions)
    {
        public AndConstraint<ObjectAssertions> BeAt(Location expected,
            string because = "", params object[] becauseArgs)
        {
            var actual = assertions.Subject as Location;
            if (actual == null)
            {
                assertions.Subject.Should().NotBeNull(because, becauseArgs);
                return new AndConstraint<ObjectAssertions>(assertions);
            }

            var actualSpan = actual.GetMappedLineSpan();
            var expectedSpan = expected.GetMappedLineSpan();

            using AssertionScope scope = new("location comparison");

            actual.Kind.Should().Be(expected.Kind, "location kind should match", because, becauseArgs);

            if (actual.IsInSource && expected.IsInSource)
                actualSpan.Should().BeEquivalentTo(expectedSpan,
                    options => options.ComparingByMembers<FileLinePositionSpan>().Using<LinePosition>(ctx =>
                    {
                        ctx.Subject.Should().Be(ctx.Expectation, "at line {0}, column {1}", ctx.Expectation.Line + 1,
                            ctx.Expectation.Character + 1);
                    }).WhenTypeIs<LinePosition>(), because, becauseArgs);
            else if (actual.IsInSource != expected.IsInSource)
                throw new AssertionFailedException(
                    $"Expected location to have IsInSource={expected.IsInSource}, but found {actual.IsInSource}");

            return new AndConstraint<ObjectAssertions>(assertions);
        }

        public AndConstraint<ObjectAssertions> BeAtLocation(int line, int column,
            string filePath = "", string because = "", params object[] becauseArgs)
        {
            if (assertions.Subject is not Diagnostic diagnostic)
            {
                assertions.Subject.Should().NotBeNull("diagnostic should not be null", because, becauseArgs);
                return new AndConstraint<ObjectAssertions>(assertions);
            }

            using AssertionScope scope = new("diagnostic location");
            var location = diagnostic.Location;
            var mappedSpan = location.GetMappedLineSpan();

            if (!location.IsInSource)
                throw new AssertionFailedException(
                    "Expected diagnostic to have a source location, but it was not in source");

            var actualLine = mappedSpan.StartLinePosition.Line + 1;
            var actualColumn = mappedSpan.StartLinePosition.Character + 1;

            actualLine.Should().Be(line, "diagnostic should be at line {0}", line, because, becauseArgs);
            actualColumn.Should().Be(column, "diagnostic should be at column {0}", column, because, becauseArgs);

            if (string.IsNullOrEmpty(filePath)) return new AndConstraint<ObjectAssertions>(assertions);
            var normalizedActual = TextUtilities.NormalizePath(mappedSpan.Path);
            var normalizedExpected = TextUtilities.NormalizePath(filePath);
            normalizedActual.Should().Be(normalizedExpected, "diagnostic should be in file {0}", filePath, because,
                becauseArgs);

            return new AndConstraint<ObjectAssertions>(assertions);
        }
    }
}

public sealed class CachingReportAssertions : ReferenceTypeAssertions<GeneratorCachingReport, CachingReportAssertions>
{
    public CachingReportAssertions(GeneratorCachingReport subject, AssertionChain chain) : base(subject, chain)
    {
    }

    protected override string Identifier => $"CachingReport[{Subject?.GeneratorName}]";

    [CustomAssertion]
    public AndConstraint<CachingReportAssertions> BeValidAndCached(string[]? requiredSteps = null, string because = "",
        params object[] becauseArgs)
    {
        CurrentAssertionChain.BecauseOf(because, becauseArgs).WithExpectation(
            $"Expected {Identifier} to be valid and optionally cached, ", ch => ch.Given(() => Subject)
                .ForCondition(report => report is not null).FailWith("but the CachingReport was <null>.").Then
                .Given(report =>
                {
                    // Only check steps that user explicitly wants to track
                    var stepsToCheck = requiredSteps is { Length: > 0 }
                        ? new HashSet<string>(requiredSteps, StringComparer.Ordinal)
                        : null;

                    var finalFailedCaching = report.ObservableSteps
                        .Where(s => stepsToCheck is null || stepsToCheck.Contains(s.StepName))
                        .Where(s => !s.IsCachedSuccessfully)
                        .ToList();

                    // Only count forbidden violations in user-specified steps (skip Roslyn internals)
                    var relevantViolations = stepsToCheck is not null
                        ? report.ForbiddenTypeViolations.Where(v => stepsToCheck.Contains(v.StepName)).ToList()
                        : report.ForbiddenTypeViolations;

                    return new
                    {
                        Report = report,
                        ValidateCaching = requiredSteps is { Length: > 0 },
                        FailedCaching = finalFailedCaching,
                        ForbiddenCount = relevantViolations.Count,
                        report.ProducedOutput
                    };
                }).ForCondition(x =>
                    x.ForbiddenCount is 0 && (!x.ValidateCaching || x.FailedCaching.Count is 0) && (x.ProducedOutput ||
                        !(x.ForbiddenCount > 0 || (x.ValidateCaching && x.FailedCaching.Count > 0)))).FailWith(
                    "{0}\n{1}",
                    x => BuildSummary(x.ForbiddenCount, x.ValidateCaching ? x.FailedCaching.Count : 0,
                        x.ProducedOutput),
                    x => BuildComprehensiveFailureReport(x.Report, x.ValidateCaching ? x.FailedCaching : [],
                        requiredSteps)));

        return new AndConstraint<CachingReportAssertions>(this);

        static string BuildSummary(int forbiddenCount, int failedCount, bool producedOutput)
        {
            List<string> reasons = new(3);
            if (forbiddenCount > 0) reasons.Add("Forbidden Types Detected");
            if (failedCount > 0) reasons.Add($"Caching Failures ({failedCount} steps)");
            if (!producedOutput) reasons.Add("No Meaningful Output");
            return $"Pipeline validation failed due to: {string.Join(", ", reasons)}.";
        }
    }

    private string BuildComprehensiveFailureReport(GeneratorCachingReport report,
        List<GeneratorStepAnalysis> failedCaching, string[]? requiredSteps)
    {
        StringBuilder sb = new();
        var issueNumber = 0;

        if (report.ForbiddenTypeViolations.Count > 0)
            foreach (var group in report.ForbiddenTypeViolations.GroupBy(v =>
                         v.StepName))
            {
                issueNumber++;
                sb.AppendLine($"--- ISSUE {issueNumber} (CRITICAL): Forbidden Type Cached in '{group.Key}' ---");
                sb.AppendLine("  Detail: Caching ISymbol/Compilation/SyntaxNode causes IDE performance degradation.");
                sb.AppendLine("  Recommendation: Store only simple, equatable data (prefer 'record').");
                foreach (var violation in group)
                    sb.AppendLine($"    • {violation.ForbiddenType.FullName} at {violation.Path}");
                sb.AppendLine();
            }

        foreach (var step in failedCaching)
        {
            issueNumber++;
            sb.AppendLine($"--- ISSUE {issueNumber}: Step Not Cached '{step.StepName}' ---");
            sb.AppendLine($"  Breakdown: {step.FormatBreakdown()}");
            sb.AppendLine(step.HasForbiddenTypes
                ? "  Root Cause: Likely forbidden Roslyn runtime types cached."
                : "  Recommendation: Ensure output model has value equality.");
            sb.AppendLine();
        }

        if (!report.ProducedOutput && issueNumber is 0)
        {
            issueNumber++;
            sb.AppendLine($"--- ISSUE {issueNumber}: No Meaningful Output Produced ---");
            sb.AppendLine("  Detail: Generator produced no non-infrastructure hint files.");
        }

        sb.AppendLine("=== Full Pipeline Overview ===");
        foreach (var step in report.ObservableSteps.OrderBy(x => x.StepName))
        {
            var tracked = requiredSteps?.Contains(step.StepName) == true ? "[Tracked]" : "";
            var forbidden = step.HasForbiddenTypes ? "🚨" : "";
            var icon = step.IsCachedSuccessfully ? "✓" : "✗";
            sb.AppendLine(
                $"  {icon} {step.StepName} {tracked} {forbidden} | {step.FormatBreakdown()} | {step.FormatPerformance()}");
        }

        if (TestConfiguration.EnableJsonReporting)
        {
            sb.AppendLine("\n--- MACHINE REPORT (JSON) ---");
            List<object> machineIssues = new();
            if (report.ForbiddenTypeViolations.Count > 0)
                foreach (var group in report.ForbiddenTypeViolations
                             .GroupBy(v => v.StepName))
                    machineIssues.Add(new
                    {
                        type = "ForbiddenType", severity = "CRITICAL", step = group.Key, count = group.Count()
                    });

            foreach (var step in failedCaching)
                machineIssues.Add(new
                {
                    type = "CacheFailure",
                    severity = "ERROR",
                    step = step.StepName,
                    breakdown = new
                    {
                        step.Cached,
                        step.Unchanged,
                        step.Modified,
                        step.New,
                        step.Removed
                    }
                });

            if (!report.ProducedOutput) machineIssues.Add(new { type = "NoOutput", severity = "WARN" });

            var payload = new
            {
                generator = report.GeneratorName,
                producedOutput = report.ProducedOutput,
                forbidden = report.ForbiddenTypeViolations.Select(v => new
                {
                    step = v.StepName, type = v.ForbiddenType.FullName, v.Path
                }),
                failedSteps = failedCaching.Select(s => new
                {
                    s.StepName,
                    s.Cached,
                    s.Unchanged,
                    s.Modified,
                    s.New,
                    s.Removed
                }),
                machineIssues
            };
            sb.AppendLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        return sb.ToString();
    }
}

public sealed class
    GeneratedSourceAssertions : ReferenceTypeAssertions<GeneratedSourceResult, GeneratedSourceAssertions>
{
    public GeneratedSourceAssertions(GeneratedSourceResult subject, AssertionChain chain) : base(subject, chain)
    {
    }

    protected override string Identifier => $"GeneratedSource[{Subject.HintName}]";

    [CustomAssertion]
    public AndConstraint<GeneratedSourceAssertions> HaveContent(string expected, bool exactMatch = true,
        bool normalizeNewlines = true, string because = "", params object[] becauseArgs)
    {
        var actual = Subject.SourceText.ToString();
        if (normalizeNewlines)
        {
            actual = TextUtilities.NormalizeNewlines(actual);
            expected = TextUtilities.NormalizeNewlines(expected);
        }

        CurrentAssertionChain.BecauseOf(because, becauseArgs).WithExpectation("Expected {0} content to match, ",
            c => c.Given(() => exactMatch ? actual == expected : actual.Contains(expected, StringComparison.Ordinal))
                .ForCondition(ok => ok).FailWith("{0}", _ => BuildContentFailureReport(actual, expected, exactMatch)));

        return new AndConstraint<GeneratedSourceAssertions>(this);
    }

    private string BuildContentFailureReport(string actual, string expected, bool exactMatch)
    {
        StringBuilder sb = new();
        sb.AppendLine();
        sb.AppendLine("GENERATION ASSERTION FAILED");
        sb.AppendLine("═══════════════════════════");
        sb.AppendLine($"File: {Subject.HintName} | Match Type: {(exactMatch ? "Exact" : "Contains")}");
        sb.AppendLine();

        if (exactMatch)
        {
            var differenceIndex = TextUtilities.FirstDiffIndex(expected, actual);
            if (differenceIndex < 0) differenceIndex = Math.Min(expected.Length, actual.Length);

            sb.AppendLine("Failure: Content mismatch detected.");
            sb.AppendLine(TextUtilities.BuildContextualDiff(expected, actual, differenceIndex));

            var (expectedLine, actualLine) = TextUtilities.GetLineAtIndex(expected, actual, differenceIndex);
            sb.AppendLine("One-line caret:");
            sb.AppendLine(TextUtilities.BuildOneLineCaret(expectedLine, actualLine));
        }
        else
        {
            sb.AppendLine("Failure: Expected content not found in generated file.");
            sb.AppendLine($"Expected to find: \"{expected}\"");
            sb.AppendLine($"In generated content of {actual.Length} characters");
        }

        return sb.ToString();
    }
}

public sealed class
    GeneratorRunResultAssertions : ReferenceTypeAssertions<GeneratorDriverRunResult, GeneratorRunResultAssertions>
{
    public GeneratorRunResultAssertions(GeneratorDriverRunResult subject, AssertionChain chain) : base(subject, chain)
    {
    }

    protected override string Identifier => "generator result";

    [CustomAssertion]
    public AndWhichConstraint<GeneratorRunResultAssertions, GeneratedSourceResult> HaveGeneratedSource(string hintName,
        string because = "", params object[] becauseArgs)
    {
        var allSources = Subject.Results.SelectMany(r => r.GeneratedSources).ToList();
        var found = allSources.Any(s => s.HintName == hintName);
        var available = string.Join(", ", allSources.Select(s => s.HintName));

        CurrentAssertionChain.BecauseOf(because, becauseArgs).ForCondition(found).FailWith(
            "Expected generated source with hint name {0}, but it was not found. Available: [{1}]", hintName,
            available);

        var which = allSources.First(s => s.HintName == hintName);
        return new AndWhichConstraint<GeneratorRunResultAssertions, GeneratedSourceResult>(this, which);
    }

    [CustomAssertion]
    public AndConstraint<GeneratorRunResultAssertions> HaveNoDiagnostics(string because = "",
        params object[] becauseArgs)
    {
        var diagnostics = Subject.Results.SelectMany(r => r.Diagnostics).ToList();
        CurrentAssertionChain.BecauseOf(because, becauseArgs).ForCondition(diagnostics.Count is 0).FailWith(
            "Expected no diagnostics, but found {0}:\n{1}", diagnostics.Count,
            string.Join("\n", diagnostics.Select(d => "  - " + DiagnosticComparable.FromDiagnostic(d).Format())));
        return new AndConstraint<GeneratorRunResultAssertions>(this);
    }

    [CustomAssertion]
    public AndConstraint<GeneratorRunResultAssertions> NotHaveForbiddenTypes(string because = "",
        params object[] becauseArgs)
    {
        var trackingEnabled = Subject!.Results.Any(r => r.TrackedSteps is { Count: > 0 });
        CurrentAssertionChain.BecauseOf(because, becauseArgs).WithExpectation(
            "Expected {0} to not cache forbidden Roslyn types (ISymbol, Compilation, SyntaxNode, etc.).",
            ch => ch.ForCondition(trackingEnabled)
                .FailWith("but step tracking was disabled, preventing analysis. (Framework: ensure trackSteps=true).")
                .Then.Given(() => ForbiddenTypeAnalyzer.AnalyzeGeneratorRun(Subject!))
                .ForCondition(violations => violations.Count is 0).FailWith("but found {0} violations:\n{1}",
                    violations => violations.Count, BuildViolationReport));

        return new AndConstraint<GeneratorRunResultAssertions>(this);

        static string BuildViolationReport(IEnumerable<ForbiddenTypeViolation> violations)
        {
            StringBuilder sb = new();
            sb.AppendLine("  CRITICAL: Caching Roslyn runtime types leads to IDE performance/memory issues.");
            foreach (var group in violations.GroupBy(v => v.StepName))
            {
                sb.AppendLine($"  - Step '{group.Key}':");
                foreach (var violation in group)
                    sb.AppendLine($"      • {violation.ForbiddenType.FullName} at {violation.Path}");
            }

            return sb.ToString();
        }
    }
}

public static class DiagnosticAssertionExtensions
{
    public static AndConstraint<DiagnosticCollectionAssertions> BeEquivalentToDiagnostics(
        this IEnumerable<Diagnostic> assertions, IEnumerable<DiagnosticResult>? expected, string because = "",
        params object[] becauseArgs)
    {
        var diagnosticList = assertions.ToList();
        var chain = AssertionChain.GetOrCreate();
        DiagnosticCollectionAssertions assertion = new(diagnosticList, chain);
        return assertion.BeEquivalentToDiagnostics(expected, because, becauseArgs);
    }
}

public sealed class
    DiagnosticCollectionAssertions : ReferenceTypeAssertions<IEnumerable<Diagnostic>, DiagnosticCollectionAssertions>
{
    private readonly AssertionChain _chain;
    private readonly IReadOnlyList<Diagnostic> _subject;

    public DiagnosticCollectionAssertions(IReadOnlyList<Diagnostic> subject, AssertionChain chain) : base(subject,
        chain)
    {
        _subject = subject;
        _chain = chain;
    }

    protected override string Identifier => "Diagnostics";

    public AndConstraint<DiagnosticCollectionAssertions> BeEquivalentToDiagnostics(
        IEnumerable<DiagnosticResult>? expected, string because = "", params object[] becauseArgs)
    {
        var actualDiagnostics = _subject.Select(DiagnosticComparable.FromDiagnostic).ToList();
        var expectedDiagnostics = (expected ?? [])
            .Select(DiagnosticComparable.FromResult).ToList();

        actualDiagnostics = OrderDiagnostics(actualDiagnostics);
        expectedDiagnostics = OrderDiagnostics(expectedDiagnostics);

        _chain.BecauseOf(because, becauseArgs).WithExpectation(
            "Expected diagnostic collection to have the same count, ",
            ch => ch.ForCondition(actualDiagnostics.Count == expectedDiagnostics.Count).FailWith(
                "but found {0} actual vs {1} expected.", actualDiagnostics.Count, expectedDiagnostics.Count));

        if (actualDiagnostics.Count != expectedDiagnostics.Count)
        {
            var summary = BuildCountMismatchReport(actualDiagnostics, expectedDiagnostics);
            _chain.FailWith("{0}", summary);
            return new AndConstraint<DiagnosticCollectionAssertions>(this);
        }

        List<(int Index, string Property, string Expected, string Actual)> differences = new();
        for (var i = 0; i < expectedDiagnostics.Count; i++)
        {
            var difference =
                DiagnosticComparable.FindFirstPropertyDifference(expectedDiagnostics[i], actualDiagnostics[i]);
            if (difference is not null)
                differences.Add((i, difference.Value.Property, difference.Value.Expected, difference.Value.Actual));
        }

        if (differences.Count > 0)
        {
            var comprehensiveReport = BuildAllDifferencesReport(differences, expectedDiagnostics, actualDiagnostics);
            _chain.ForCondition(false).BecauseOf(because, becauseArgs).FailWith("{0}", comprehensiveReport);
        }

        return new AndConstraint<DiagnosticCollectionAssertions>(this);

        static List<DiagnosticComparable> OrderDiagnostics(IEnumerable<DiagnosticComparable> diagnostics)
        {
            return diagnostics.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Line)
                .ThenBy(x => x.Column)
                .ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        }

        static string BuildAllDifferencesReport(
            List<(int Index, string Property, string Expected, string Actual)> differences,
            List<DiagnosticComparable> expectedDiagnostics, List<DiagnosticComparable> actualDiagnostics)
        {
            StringBuilder sb = new();
            sb.AppendLine($"Found {differences.Count} diagnostic differences:");
            sb.AppendLine();

            foreach (var (index, property, expected, actual) in differences)
            {
                sb.AppendLine(
                    $"--- DIFFERENCE {differences.IndexOf((index, property, expected, actual)) + 1} at Index {index} ---");
                sb.AppendLine($"Property: {property}");
                sb.AppendLine($"Expected: '{expected}'");
                sb.AppendLine($"Actual:   '{actual}'");
                sb.AppendLine();

                var caretBlock = TextUtilities.BuildCaretBlock(expectedDiagnostics[index].Format(),
                    actualDiagnostics[index].Format());
                sb.AppendLine("Contextual comparison:");
                foreach (var line in caretBlock.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    sb.Append("  ").AppendLine(line);
                sb.AppendLine();
            }

            sb.AppendLine("=== FULL DIAGNOSTIC COMPARISON ===");
            sb.AppendLine("Expected Diagnostics:");
            if (expectedDiagnostics.Count is 0) sb.AppendLine("  (None)");
            for (var i = 0; i < expectedDiagnostics.Count; i++)
            {
                var marker = differences.Any(d => d.Index == i) ? "❌" : "✅";
                sb.AppendLine($"  {marker} [{i}] {expectedDiagnostics[i].Format()}");
            }

            sb.AppendLine("\nActual Diagnostics:");
            if (actualDiagnostics.Count is 0) sb.AppendLine("  (None)");
            for (var i = 0; i < actualDiagnostics.Count; i++)
            {
                var marker = differences.Any(d => d.Index == i) ? "❌" : "✅";
                sb.AppendLine($"  {marker} [{i}] {actualDiagnostics[i].Format()}");
            }

            return sb.ToString();
        }

        static string BuildCountMismatchReport(List<DiagnosticComparable> actual, List<DiagnosticComparable> expected)
        {
            StringBuilder sb = new();
            sb.AppendLine("Diagnostic count mismatch.");
            sb.AppendLine("\nActual Diagnostics:");
            if (actual.Count is 0) sb.AppendLine("  (None)");
            foreach (var diagnostic in actual) sb.AppendLine($"  - {diagnostic.Format()}");
            sb.AppendLine("\nExpected Diagnostics:");
            if (expected.Count is 0) sb.AppendLine("  (None)");
            foreach (var diagnostic in expected) sb.AppendLine($"  - {diagnostic.Format()}");
            return sb.ToString();
        }
    }
}

public sealed record DiagnosticComparable(
    string Id,
    DiagnosticSeverity Severity,
    string Path,
    int Line,
    int Column,
    string Message)
{
    public static DiagnosticComparable FromDiagnostic(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetMappedLineSpan();
        var hasLocation = diagnostic.Location.IsInSource && span.IsValid;
        return new DiagnosticComparable(diagnostic.Id, diagnostic.Severity,
            hasLocation ? TextUtilities.NormalizePath(span.Path) : string.Empty,
            hasLocation ? span.StartLinePosition.Line + 1 : 0, hasLocation ? span.StartLinePosition.Character + 1 : 0,
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    public static DiagnosticComparable FromResult(DiagnosticResult result)
    {
        var hasLocation = result is { HasLocation: true, Spans.Length: > 0 };
        var span = hasLocation ? result.Spans[0].Span : default;
        var path = hasLocation && span.IsValid ? span.Path : string.Empty;
        var line = hasLocation && span.IsValid ? span.StartLinePosition.Line + 1 : 0;
        var column = hasLocation && span.IsValid ? span.StartLinePosition.Character + 1 : 0;

        return new DiagnosticComparable(result.Id, result.Severity, TextUtilities.NormalizePath(path), line, column,
            result.Message ?? string.Empty);
    }

    public string Format()
    {
        var path = string.IsNullOrEmpty(Path) ? "<no-file>" : Path;
        var message = TextUtilities.NormalizeWhitespace(Message);
        return Line > 0 ? $"{path}@{Line}:{Column} {Id} ({Severity}): {message}" : $"{Id} ({Severity}): {message}";
    }

    public static (string Property, string Expected, string Actual)? FindFirstPropertyDifference(
        DiagnosticComparable expected, DiagnosticComparable actual)
    {
        if (expected.Severity != actual.Severity)
            return ("Severity", expected.Severity.ToString(), actual.Severity.ToString());
        if (!string.Equals(expected.Id, actual.Id, StringComparison.Ordinal)) return ("Id", expected.Id, actual.Id);

        if (expected.Line > 0)
        {
            if (!string.Equals(expected.Path, actual.Path, StringComparison.OrdinalIgnoreCase))
                return ("FilePath", expected.Path, actual.Path);
            if (expected.Line != actual.Line) return ("Line", expected.Line.ToString(), actual.Line.ToString());
            if (expected.Column != actual.Column)
                return ("Column", expected.Column.ToString(), actual.Column.ToString());
        }

        // Skip message comparison if expected message is empty (allows testing just ID and severity)
        var expectedMessage = TextUtilities.NormalizeWhitespace(expected.Message);
        if (!string.IsNullOrEmpty(expectedMessage))
        {
            var actualMessage = TextUtilities.NormalizeWhitespace(actual.Message);
            if (!string.Equals(expectedMessage, actualMessage, StringComparison.Ordinal))
                return ("Message", expectedMessage, actualMessage);
        }

        return null;
    }
}

public static class TextUtilities
{
    public static string NormalizePath(string? path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim();
    }

    public static string NormalizeNewlines(string source)
    {
        return source.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    public static string NormalizeWhitespace(string? input)
    {
        return string.IsNullOrWhiteSpace(input)
            ? string.Empty
            : Regex.Replace(input, @"\s+", " ").Trim();
    }

    public static int FirstDiffIndex(string a, string b)
    {
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
            if (a[i] != b[i])
                return i;
        return a.Length != b.Length ? length : -1;
    }

    public static string BuildCaretBlock(string expectedLine, string actualLine, string indent = "")
    {
        string quotedExpected = $"\"{expectedLine}\"", quotedActual = $"\"{actualLine}\"";
        var index = FirstDiffIndex(quotedExpected, quotedActual);
        if (index < 0) index = Math.Min(quotedExpected.Length, quotedActual.Length);

        const string expectedLabel = "Expected: ";
        const string actualLabel = "Actual:   ";
        StringBuilder sb = new();
        sb.AppendLine($"{indent}{expectedLabel}{quotedExpected}");
        sb.AppendLine($"{indent}{actualLabel}{quotedActual}");
        sb.Append(indent).Append(new string(' ', actualLabel.Length + index)).AppendLine("^");
        return sb.ToString().TrimEnd();
    }

    public static string BuildContextualDiff(string expected, string actual, int diffIndex, int contextLines = 3)
    {
        StringBuilder sb = new();

        var expectedLines = expected.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var actualLines = actual.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

        int line = 0, count = 0;
        for (var i = 0; i < expectedLines.Length; i++)
        {
            var next = count + expectedLines[i].Length + (i < expectedLines.Length - 1 ? 1 : 0);
            if (next > diffIndex || i == expectedLines.Length - 1)
            {
                line = i;
                break;
            }

            count = next;
        }

        var expectedLine = line < expectedLines.Length ? expectedLines[line] : "(end of expected)";
        var actualLine = line < actualLines.Length ? actualLines[line] : "(end of actual)";

        var col = Math.Max(0, diffIndex - count) + 1;
        sb.AppendLine($"📍 Difference at line {line + 1}, character {col}:");
        sb.AppendLine(BuildCaretBlock(expectedLine, actualLine, "  "));
        sb.AppendLine("\nContext in generated file:");
        var start = Math.Max(0, line - contextLines);
        var end = Math.Min(actualLines.Length - 1, line + contextLines);

        sb.AppendLine("┌─────────────────────────────");
        for (var i = start; i <= end; i++)
        {
            var marker = i == line ? "│→ " : "│  ";
            if (i < actualLines.Length) sb.AppendLine($"{marker}{actualLines[i]}");
        }

        sb.AppendLine("└─────────────────────────────");
        return sb.ToString();
    }

    public static (string ExpectedLine, string ActualLine) GetLineAtIndex(string expected, string actual, int diffIndex)
    {
        var expectedLines = expected.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var actualLines = actual.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        int line = 0, count = 0;
        for (var i = 0; i < expectedLines.Length; i++)
        {
            var next = count + expectedLines[i].Length + (i < expectedLines.Length - 1 ? 1 : 0);
            if (next > diffIndex || i == expectedLines.Length - 1)
            {
                line = i;
                break;
            }

            count = next;
        }

        var expectedLine = line < expectedLines.Length ? expectedLines[line] : "";
        var actualLine = line < actualLines.Length ? actualLines[line] : "";
        return (expectedLine, actualLine);
    }

    public static string BuildOneLineCaret(string expectedLine, string actualLine)
    {
        var index = FirstDiffIndex(expectedLine, actualLine);
        if (index < 0) index = Math.Min(expectedLine.Length, actualLine.Length);

        StringBuilder sb = new();
        sb.AppendLine($"Expected: {expectedLine}");
        sb.AppendLine($"Actual:   {actualLine}");
        sb.AppendLine(new string('-', "Actual:   ".Length + index) + "^");
        return sb.ToString().TrimEnd();
    }
}

public static class TestFormatters
{
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) is not 0) return;

        IValueFormatter[] formatters =
        [
            new CachingReportFormatter(),
            new StepAnalysisFormatter(),
            new ForbiddenTypeViolationFormatter(),
            new DiagnosticFormatter(),
            new GeneratedSourceResultFormatter()
        ];
        foreach (var formatter in formatters) Formatter.AddFormatter(formatter);
    }

    public static void ApplyToScope(AssertionScope scope)
    {
        scope.FormattingOptions.UseLineBreaks = true;
        scope.FormattingOptions.MaxLines = 8000;
        scope.FormattingOptions.MaxDepth = 12;
    }

    private sealed class CachingReportFormatter : IValueFormatter
    {
        public bool CanHandle(object value)
        {
            return value is GeneratorCachingReport;
        }

        public void Format(object value, FormattedObjectGraph graph, FormattingContext context, FormatChild child)
        {
            var report = (GeneratorCachingReport)value;
            graph.AddFragment($"CachingReport[{report.GeneratorName}]: {(report.IsCorrect ? "✓ VALID" : "✗ FAILED")}");
        }
    }

    private sealed class StepAnalysisFormatter : IValueFormatter
    {
        public bool CanHandle(object value)
        {
            return value is GeneratorStepAnalysis;
        }

        public void Format(object value, FormattedObjectGraph graph, FormattingContext context, FormatChild child)
        {
            var step = (GeneratorStepAnalysis)value;
            var status = step.IsCachedSuccessfully ? "✓" : "✗";
            graph.AddFragment($"{status} {step.StepName}: {step.FormatBreakdown()} | Perf: {step.FormatPerformance()}");
        }
    }

    private sealed class ForbiddenTypeViolationFormatter : IValueFormatter
    {
        public bool CanHandle(object value)
        {
            return value is ForbiddenTypeViolation;
        }

        public void Format(object value, FormattedObjectGraph graph, FormattingContext context, FormatChild child)
        {
            var violation = (ForbiddenTypeViolation)value;
            graph.AddFragment($"❗ {violation.StepName}: {violation.ForbiddenType.Name} at {violation.Path}");
        }
    }

    private sealed class DiagnosticFormatter : IValueFormatter
    {
        public bool CanHandle(object value)
        {
            return value is Diagnostic;
        }

        public void Format(object value, FormattedObjectGraph graph, FormattingContext context, FormatChild child)
        {
            graph.AddFragment(DiagnosticComparable.FromDiagnostic((Diagnostic)value).Format());
        }
    }

    private sealed class GeneratedSourceResultFormatter : IValueFormatter
    {
        public bool CanHandle(object value)
        {
            return value is GeneratedSourceResult;
        }

        public void Format(object value, FormattedObjectGraph graph, FormattingContext context, FormatChild child)
        {
            var result = (GeneratedSourceResult)value;
            graph.AddFragment($"Generated[{result.HintName}] ({result.SourceText.Length} chars)");
        }
    }
}