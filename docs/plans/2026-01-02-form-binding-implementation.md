# Form Binding v2.0 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add complete multipart/form-data binding support to ErrorOr.Http source generator with [FromForm], IFormFile, and IFormFileCollection.

**Architecture:** Extend the existing parameter classification pipeline with new source types (Form, FormFile, FormFiles). Use constructor-based DTO analysis matching the [AsParameters] pattern. Generate AOT-safe code using ReadFormAsync and explicit TryParse calls.

**Tech Stack:** C# 13, .NET 10, Roslyn Source Generators, Microsoft.AspNetCore.Http

---

## Phase 0: Diagnostics Foundation

### Task 0.1: Add EOE006 - Multiple Body Sources

**Files:**
- Modify: `ErrorOr.Http/Generators/Diagnostics.cs:33` (after MultipleBodyParameters)

**Step 1: Add diagnostic descriptor**

Add after `MultipleBodyParameters`:

```csharp
public static readonly DiagnosticDescriptor MultipleBodySources = new(
    "EOE006",
    "Multiple body sources",
    "Endpoint '{0}' has both [FromBody] and [FromForm] parameters. Only one body source is allowed.",
    "Usage",
    DiagnosticSeverity.Error,
    true);
```

**Step 2: Build to verify no errors**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/Diagnostics.cs
git commit -m "feat(diagnostics): add EOE006 multiple body sources"
```

---

### Task 0.2: Add EOE007 - Multiple FromForm Parameters

**Files:**
- Modify: `ErrorOr.Http/Generators/Diagnostics.cs`

**Step 1: Add diagnostic descriptor**

```csharp
public static readonly DiagnosticDescriptor MultipleFromFormParameters = new(
    "EOE007",
    "Multiple [FromForm] parameters",
    "Endpoint '{0}' has multiple [FromForm] parameters. Only one is allowed.",
    "Usage",
    DiagnosticSeverity.Error,
    true);
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/Diagnostics.cs
git commit -m "feat(diagnostics): add EOE007 multiple FromForm parameters"
```

---

### Task 0.3: Add EOE008 - Unsupported Form DTO Shape

**Files:**
- Modify: `ErrorOr.Http/Generators/Diagnostics.cs`

**Step 1: Add diagnostic descriptor**

```csharp
public static readonly DiagnosticDescriptor UnsupportedFormDtoShape = new(
    "EOE008",
    "Unsupported [FromForm] DTO shape",
    "[FromForm] parameter '{0}' on '{1}' has unsupported shape: {2}",
    "Usage",
    DiagnosticSeverity.Error,
    true);
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/Diagnostics.cs
git commit -m "feat(diagnostics): add EOE008 unsupported form DTO shape"
```

---

### Task 0.4: Add EOE009 - FormFile Nullability Warning

**Files:**
- Modify: `ErrorOr.Http/Generators/Diagnostics.cs`

**Step 1: Add diagnostic descriptor**

```csharp
public static readonly DiagnosticDescriptor FormFileNotNullable = new(
    "EOE009",
    "IFormFile nullability",
    "IFormFile parameter '{0}' is non-nullable but file may be missing. Use IFormFile? for optional files.",
    "Usage",
    DiagnosticSeverity.Warning,
    true);
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/Diagnostics.cs
git commit -m "feat(diagnostics): add EOE009 FormFile nullability warning"
```

---

### Task 0.5: Add EOE010 - Form Content Type Info

**Files:**
- Modify: `ErrorOr.Http/Generators/Diagnostics.cs`

**Step 1: Add diagnostic descriptor**

```csharp
public static readonly DiagnosticDescriptor FormContentTypeRequired = new(
    "EOE010",
    "Form content type required",
    "Endpoint '{0}' uses form binding but may receive non-form requests at runtime.",
    "Usage",
    DiagnosticSeverity.Info,
    true);
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/Diagnostics.cs
git commit -m "feat(diagnostics): add EOE010 form content type info"
```

---

## Phase 1: Classification Extension

### Task 1.1: Extend EndpointParameterSource Enum

**Files:**
- Modify: `ErrorOr.Http/Generators/Models.cs:8-19`

**Step 1: Add new enum values**

Replace the enum:

```csharp
internal enum EndpointParameterSource
{
    Route,
    Body,
    Query,
    Header,
    Service,
    KeyedService,
    AsParameters,
    HttpContext,
    CancellationToken,
    Form,       // [FromForm] primitive or DTO
    FormFile,   // IFormFile
    FormFiles   // IFormFileCollection
}
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/Models.cs
git commit -m "feat(models): add Form, FormFile, FormFiles parameter sources"
```

---

### Task 1.2: Add Form Types to KnownSymbols

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Extractor.cs:896-919`

**Step 1: Extend KnownSymbols record**

Replace the record definition:

```csharp
internal sealed record KnownSymbols(
    INamedTypeSymbol? FromBody,
    INamedTypeSymbol? FromServices,
    INamedTypeSymbol? FromKeyedServices,
    INamedTypeSymbol? FromRoute,
    INamedTypeSymbol? FromQuery,
    INamedTypeSymbol? FromHeader,
    INamedTypeSymbol? AsParameters,
    INamedTypeSymbol? Obsolete,
    INamedTypeSymbol? FromForm,
    INamedTypeSymbol? IFormFile,
    INamedTypeSymbol? IFormFileCollection)
{
    public static KnownSymbols Create(Compilation compilation)
    {
        return new KnownSymbols(
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromBodyAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromServicesAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromRouteAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromQueryAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromHeaderAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.AsParametersAttribute"),
            compilation.GetTypeByMetadataName("System.ObsoleteAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromFormAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IFormFile"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IFormFileCollection")
        );
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Extractor.cs
git commit -m "feat(extractor): add FromForm and IFormFile to KnownSymbols"
```

---

### Task 1.3: Extend ParameterMeta with Form Fields

**Files:**
- Modify: `ErrorOr.Http/Generators/Models.cs:59-81`

**Step 1: Add form-related fields to ParameterMeta**

Add after `CollectionItemPrimitiveKind`:

```csharp
internal readonly record struct ParameterMeta(
    IParameterSymbol Symbol,
    string Name,
    string TypeFqn,
    RoutePrimitiveKind? RouteKind,
    bool HasFromServices,
    bool HasFromKeyedServices,
    string? KeyedServiceKey,
    bool HasFromBody,
    bool HasFromRoute,
    bool HasFromQuery,
    bool HasFromHeader,
    bool HasAsParameters,
    string RouteName,
    string QueryName,
    string HeaderName,
    bool IsCancellationToken,
    bool IsHttpContext,
    bool IsNullable,
    bool IsNonNullableValueType,
    bool IsCollection,
    string? CollectionItemTypeFqn,
    RoutePrimitiveKind? CollectionItemPrimitiveKind,
    bool HasFromForm,
    string FormName,
    bool IsFormFile,
    bool IsFormFileCollection);
```

**Step 2: Build - expect errors (CreateParameterMeta not updated yet)**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`
Expected: Errors about missing arguments

---

### Task 1.4: Update CreateParameterMeta for Form Detection

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Extractor.cs:546-579`

**Step 1: Add form attribute constant**

Add after `AsParametersAttrName`:

```csharp
private const string FromFormAttrName = "Microsoft.AspNetCore.Mvc.FromFormAttribute";
```

**Step 2: Update CreateParameterMeta method**

```csharp
private static ParameterMeta CreateParameterMeta(IParameterSymbol parameter, KnownSymbols knownSymbols)
{
    var type = parameter.Type;
    var typeFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    var hasFromRoute = HasParameterAttribute(parameter, knownSymbols.FromRoute, FromRouteAttrName);
    var hasFromQuery = HasParameterAttribute(parameter, knownSymbols.FromQuery, FromQueryAttrName);
    var hasFromHeader = HasParameterAttribute(parameter, knownSymbols.FromHeader, FromHeaderAttrName);
    var hasFromKeyedServices =
        HasParameterAttribute(parameter, knownSymbols.FromKeyedServices, FromKeyedServicesAttrName);
    var hasAsParameters = HasParameterAttribute(parameter, knownSymbols.AsParameters, AsParametersAttrName);
    var hasFromForm = HasParameterAttribute(parameter, knownSymbols.FromForm, FromFormAttrName);

    var routeName = hasFromRoute ? TryGetFromRouteName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
    var queryName = hasFromQuery ? TryGetFromQueryName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
    var headerName = hasFromHeader ? TryGetFromHeaderName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
    var formName = hasFromForm ? TryGetFromFormName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
    var keyedServiceKey = hasFromKeyedServices ? ExtractKeyFromKeyedServiceAttribute(parameter, knownSymbols) : null;

    var (isNullable, isNonNullableValueType) = GetParameterNullability(type, parameter.NullableAnnotation);
    var (isCollection, itemType, itemPrimitiveKind) = AnalyzeCollectionType(type);

    var isFormFile = typeFqn == "global::Microsoft.AspNetCore.Http.IFormFile";
    var isFormFileCollection = typeFqn == "global::Microsoft.AspNetCore.Http.IFormFileCollection" ||
        (type is INamedTypeSymbol { IsGenericType: true } named &&
         named.ConstructedFrom.ToDisplayString().StartsWith("System.Collections.Generic.IReadOnlyList") &&
         named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Microsoft.AspNetCore.Http.IFormFile");

    return new ParameterMeta(
        parameter, parameter.Name, typeFqn, TryGetRoutePrimitiveKind(type),
        HasParameterAttribute(parameter, knownSymbols.FromServices, FromServicesAttrName),
        hasFromKeyedServices, keyedServiceKey,
        HasParameterAttribute(parameter, knownSymbols.FromBody, FromBodyAttrName),
        hasFromRoute, hasFromQuery, hasFromHeader, hasAsParameters,
        routeName, queryName, headerName,
        typeFqn == "global::System.Threading.CancellationToken",
        typeFqn == "global::Microsoft.AspNetCore.Http.HttpContext",
        isNullable, isNonNullableValueType,
        isCollection, itemType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), itemPrimitiveKind,
        hasFromForm, formName, isFormFile, isFormFileCollection);
}
```

**Step 3: Add TryGetFromFormName helper**

Add after `TryGetFromHeaderName`:

```csharp
private static string? TryGetFromFormName(IParameterSymbol parameter, KnownSymbols knownSymbols)
{
    var attr = parameter.GetAttributes()
        .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, knownSymbols.FromForm));
    return ExtractNameFromAttribute(attr);
}
```

**Step 4: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Extractor.cs ErrorOr.Http/Generators/Models.cs
git commit -m "feat(extractor): detect [FromForm], IFormFile, IFormFileCollection"
```

---

### Task 1.5: Add Form Classification in ClassifyParameter

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Extractor.cs:698-738`

**Step 1: Add form classification cases**

In `ClassifyParameter` method, add after `if (meta.HasFromBody)`:

```csharp
// Explicit [FromForm]
if (meta.HasFromForm)
    return ClassifyFromFormParameter(in meta, method, knownSymbols);

// Implicit form file types (auto-detected like HttpContext)
if (meta.IsFormFile)
    return ParameterSuccess(in meta, EndpointParameterSource.FormFile, formName: meta.Name);
if (meta.IsFormFileCollection)
    return ParameterSuccess(in meta, EndpointParameterSource.FormFiles, formName: meta.Name);
```

**Step 2: Add formName parameter to ParameterSuccess**

Update the signature and implementation:

```csharp
private static DiagnosticFlow<EndpointParameter?> ParameterSuccess(
    in ParameterMeta meta,
    EndpointParameterSource source,
    string? routeName = null,
    string? headerName = null,
    string? queryName = null,
    string? keyedServiceKey = null,
    string? formName = null)
{
    var param = new EndpointParameter(
        meta.Name,
        meta.TypeFqn,
        source,
        routeName ?? queryName ?? headerName ?? keyedServiceKey ?? formName,
        meta.IsNullable,
        meta.IsNonNullableValueType,
        meta.IsCollection,
        meta.CollectionItemTypeFqn,
        EquatableArray<EndpointParameter>.Empty);

    return DiagnosticFlow<EndpointParameter?>.Ok(param);
}
```

**Step 3: Build - expect error (ClassifyFromFormParameter not defined yet)**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`
Expected: Error about missing ClassifyFromFormParameter

---

### Task 1.6: Implement ClassifyFromFormParameter

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Extractor.cs`

**Step 1: Add ClassifyFromFormParameter method**

Add after `ClassifyAsParameters`:

```csharp
/// <summary>
///     Classifies a [FromForm] parameter - primitive, collection, or DTO.
/// </summary>
private static DiagnosticFlow<EndpointParameter?> ClassifyFromFormParameter(
    in ParameterMeta meta,
    IMethodSymbol method,
    KnownSymbols knownSymbols)
{
    // Primitive: direct form field binding
    if (meta.RouteKind is not null)
        return ParameterSuccess(in meta, EndpointParameterSource.Form, formName: meta.FormName);

    // Primitive collection: form field array
    if (meta.IsCollection && meta.CollectionItemPrimitiveKind is not null)
        return ParameterSuccess(in meta, EndpointParameterSource.Form, formName: meta.FormName);

    // Complex type: validate DTO shape and extract constructor parameters
    if (meta.Symbol.Type is INamedTypeSymbol typeSymbol)
        return ClassifyFormDtoParameter(in meta, typeSymbol, method, knownSymbols);

    // Unsupported type
    var error = DiagnosticInfo.Create(DiagnosticDescriptors.UnsupportedFormDtoShape,
        meta.Symbol, meta.Name, method.Name, "must be a primitive, collection, or class/struct");
    return DiagnosticFlow<EndpointParameter?>.Fail(error);
}
```

**Step 2: Build - expect error (ClassifyFormDtoParameter not defined)**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

---

### Task 1.7: Implement ClassifyFormDtoParameter

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Extractor.cs`

**Step 1: Add ClassifyFormDtoParameter method**

```csharp
/// <summary>
///     Validates DTO shape for [FromForm] and extracts constructor parameters.
/// </summary>
private static DiagnosticFlow<EndpointParameter?> ClassifyFormDtoParameter(
    in ParameterMeta meta,
    INamedTypeSymbol typeSymbol,
    IMethodSymbol method,
    KnownSymbols knownSymbols)
{
    // Find usable constructor (prefer primary/longest)
    var constructor = typeSymbol.Constructors
        .Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length > 0)
        .OrderByDescending(c => c.Parameters.Length)
        .FirstOrDefault();

    if (constructor is null || constructor.Parameters.Length == 0)
    {
        var error = DiagnosticInfo.Create(DiagnosticDescriptors.UnsupportedFormDtoShape,
            meta.Symbol, meta.Name, method.Name, "DTO must have a constructor with parameters");
        return DiagnosticFlow<EndpointParameter?>.Fail(error);
    }

    // Validate all constructor parameters are primitives
    var children = ImmutableArray.CreateBuilder<EndpointParameter>(constructor.Parameters.Length);

    foreach (var param in constructor.Parameters)
    {
        var paramKind = TryGetRoutePrimitiveKind(param.Type);
        if (paramKind is null && param.Type.SpecialType != SpecialType.System_String)
        {
            var error = DiagnosticInfo.Create(DiagnosticDescriptors.UnsupportedFormDtoShape,
                meta.Symbol, meta.Name, method.Name,
                $"constructor parameter '{param.Name}' must be a primitive type");
            return DiagnosticFlow<EndpointParameter?>.Fail(error);
        }

        var isNullable = param.Type.NullableAnnotation == NullableAnnotation.Annotated ||
            (param.Type is INamedTypeSymbol { IsGenericType: true } nullable &&
             nullable.ConstructedFrom.ToDisplayString() == "System.Nullable<T>");

        children.Add(new EndpointParameter(
            param.Name,
            param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EndpointParameterSource.Form,
            param.Name, // form field name
            isNullable,
            param.Type.IsValueType && !isNullable,
            false,
            null,
            EquatableArray<EndpointParameter>.Empty));
    }

    return DiagnosticFlow<EndpointParameter?>.Ok(new EndpointParameter(
        meta.Name,
        meta.TypeFqn,
        EndpointParameterSource.Form,
        null,
        meta.IsNullable,
        meta.IsNonNullableValueType,
        false,
        null,
        new EquatableArray<EndpointParameter>(children.ToImmutable())));
}
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Extractor.cs
git commit -m "feat(extractor): implement form parameter classification"
```

---

### Task 1.8: Add Body Source Conflict Validation

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Extractor.cs:480-499`

**Step 1: Update BindMethodParameters to check conflicts**

Add after the multiple body check:

```csharp
// Check for body + form conflict
var hasBody = metas.Any(static m => m.HasFromBody);
var hasForm = metas.Any(static m => m.HasFromForm || m.IsFormFile || m.IsFormFileCollection);

if (hasBody && hasForm)
{
    var error = DiagnosticInfo.Create(DiagnosticDescriptors.MultipleBodySources, method, method.Name);
    return DiagnosticFlow<ImmutableArray<EndpointParameter>>.Fail(error);
}

// Check for multiple [FromForm] parameters
if (metas.Count(static m => m.HasFromForm) > 1)
{
    var error = DiagnosticInfo.Create(DiagnosticDescriptors.MultipleFromFormParameters, method, method.Name);
    return DiagnosticFlow<ImmutableArray<EndpointParameter>>.Fail(error);
}
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Extractor.cs
git commit -m "feat(extractor): add body+form conflict and multiple form validation"
```

---

## Phase 2: Snapshot Tests for Classification

### Task 2.1: Add Form Binding Snapshot Test

**Files:**
- Modify: `ErrorOr.Http.SnapShot/SnapshotTests.cs`

**Step 1: Add test method**

```csharp
[Fact]
public async Task FormBinding_PrimitiveAndFile()
{
    var source = """
        using ErrorOr;
        using ErrorOr.Http;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Mvc;

        public static class Handlers
        {
            [ErrorOrEndpoint("POST", "/upload")]
            public static ErrorOr<Success> Upload(
                [FromForm] string title,
                [FromForm] int version,
                IFormFile document)
                => Result.Success;
        }
        """;

    await Verify(source);
}
```

**Step 2: Run test to generate snapshot**

Run: `dotnet test ErrorOr.Http.SnapShot --filter "FormBinding_PrimitiveAndFile"`

**Step 3: Verify generated snapshot file exists**

Check: `ErrorOr.Http.SnapShot/SnapshotTests.FormBinding_PrimitiveAndFile#ErrorOrEndpointAttribute.g.verified.cs`

**Step 4: Commit**

```bash
git add ErrorOr.Http.SnapShot/
git commit -m "test(snapshot): add form binding classification test"
```

---

## Phase 3: Binding Code Emitter

### Task 3.1: Add Form Content Type Guard Emission

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Emitter.cs`

**Step 1: Add EmitFormContentTypeGuard method**

```csharp
private static void EmitFormContentTypeGuard(StringBuilder code)
{
    code.AppendLine("            if (!ctx.Request.HasFormContentType)");
    code.AppendLine("            {");
    code.AppendLine("                ctx.Response.StatusCode = 400;");
    code.AppendLine("                return;");
    code.AppendLine("            }");
    code.AppendLine();
    code.AppendLine("            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);");
    code.AppendLine();
}
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Emitter.cs
git commit -m "feat(emitter): add form content type guard emission"
```

---

### Task 3.2: Add Form Primitive Binding Emission

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Emitter.cs`

**Step 1: Add EmitFormPrimitiveBinding method**

```csharp
private static void EmitFormPrimitiveBinding(StringBuilder code, in EndpointParameter param, int index)
{
    var varName = $"p{index}";
    var fieldName = param.KeyName ?? param.Name;
    var rawVar = $"{varName}Raw";

    code.AppendLine($"            {param.TypeFqn} {varName};");
    code.AppendLine($"            if (!form.TryGetValue(\"{fieldName}\", out var {rawVar}) || {rawVar}.Count == 0)");
    code.AppendLine("            {");

    if (param.IsNullable)
    {
        code.AppendLine($"                {varName} = default;");
    }
    else
    {
        code.AppendLine("                ctx.Response.StatusCode = 400;");
        code.AppendLine("                return;");
    }

    code.AppendLine("            }");
    code.AppendLine("            else");
    code.AppendLine("            {");

    if (param.TypeFqn == "global::System.String" || param.TypeFqn == "string")
    {
        code.AppendLine($"                {varName} = {rawVar}.ToString();");
    }
    else
    {
        var tempVar = $"{varName}Temp";
        var baseType = param.IsNullable ? param.TypeFqn.TrimEnd('?') : param.TypeFqn;
        code.AppendLine($"                if (!{baseType}.TryParse({rawVar}.ToString(), out var {tempVar}))");
        code.AppendLine("                {");
        code.AppendLine("                    ctx.Response.StatusCode = 400;");
        code.AppendLine("                    return;");
        code.AppendLine("                }");
        code.AppendLine($"                {varName} = {tempVar};");
    }

    code.AppendLine("            }");
}
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Emitter.cs
git commit -m "feat(emitter): add form primitive binding emission"
```

---

### Task 3.3: Add Form File Binding Emission

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Emitter.cs`

**Step 1: Add EmitFormFileBinding method**

```csharp
private static void EmitFormFileBinding(StringBuilder code, in EndpointParameter param, int index)
{
    var varName = $"p{index}";
    var fieldName = param.KeyName ?? param.Name;

    code.AppendLine($"            var {varName} = form.Files.GetFile(\"{fieldName}\");");

    if (!param.IsNullable)
    {
        code.AppendLine($"            if ({varName} is null)");
        code.AppendLine("            {");
        code.AppendLine("                ctx.Response.StatusCode = 400;");
        code.AppendLine("                return;");
        code.AppendLine("            }");
    }
}

private static void EmitFormFilesBinding(StringBuilder code, in EndpointParameter param, int index)
{
    var varName = $"p{index}";
    var fieldName = param.KeyName ?? param.Name;

    if (param.TypeFqn.Contains("IFormFileCollection"))
    {
        code.AppendLine($"            var {varName} = form.Files;");
    }
    else
    {
        code.AppendLine($"            var {varName} = form.Files.GetFiles(\"{fieldName}\");");
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Emitter.cs
git commit -m "feat(emitter): add form file binding emission"
```

---

### Task 3.4: Add Form DTO Binding Emission

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Emitter.cs`

**Step 1: Add EmitFormDtoBinding method**

```csharp
private static void EmitFormDtoBinding(StringBuilder code, in EndpointParameter param, int index)
{
    var varName = $"p{index}";

    // Emit binding for each child field
    for (var i = 0; i < param.Children.Items.Length; i++)
    {
        var child = param.Children.Items[i];
        var childVarName = $"{varName}_f{i}";
        EmitFormPrimitiveBindingForChild(code, child, childVarName);
    }

    // Construct the DTO
    var args = string.Join(", ", param.Children.Items.Select((_, i) => $"{varName}_f{i}"));
    code.AppendLine($"            var {varName} = new {param.TypeFqn}({args});");
}

private static void EmitFormPrimitiveBindingForChild(StringBuilder code, EndpointParameter param, string varName)
{
    var fieldName = param.KeyName ?? param.Name;
    var rawVar = $"{varName}Raw";

    code.AppendLine($"            {param.TypeFqn} {varName};");
    code.AppendLine($"            if (!form.TryGetValue(\"{fieldName}\", out var {rawVar}) || {rawVar}.Count == 0)");
    code.AppendLine("            {");

    if (param.IsNullable)
    {
        code.AppendLine($"                {varName} = default;");
    }
    else
    {
        code.AppendLine("                ctx.Response.StatusCode = 400;");
        code.AppendLine("                return;");
    }

    code.AppendLine("            }");
    code.AppendLine("            else");
    code.AppendLine("            {");

    if (param.TypeFqn == "global::System.String" || param.TypeFqn == "string")
    {
        code.AppendLine($"                {varName} = {rawVar}.ToString();");
    }
    else
    {
        var tempVar = $"{varName}Temp";
        var baseType = param.IsNullable ? param.TypeFqn.TrimEnd('?') : param.TypeFqn;
        code.AppendLine($"                if (!{baseType}.TryParse({rawVar}.ToString(), out var {tempVar}))");
        code.AppendLine("                {");
        code.AppendLine("                    ctx.Response.StatusCode = 400;");
        code.AppendLine("                    return;");
        code.AppendLine("                }");
        code.AppendLine($"                {varName} = {tempVar};");
    }

    code.AppendLine("            }");
}
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Commit**

```bash
git add ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Emitter.cs
git commit -m "feat(emitter): add form DTO binding emission"
```

---

### Task 3.5: Integrate Form Emission into EmitInvoker

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Emitter.cs`

**Step 1: Find EmitInvoker and add form handling**

In the parameter binding loop, add cases for form sources:

```csharp
// Check if endpoint has form parameters
var hasFormParams = ep.HandlerParameters.Items.Any(p =>
    p.Source is EndpointParameterSource.Form or
                EndpointParameterSource.FormFile or
                EndpointParameterSource.FormFiles);

if (hasFormParams)
{
    EmitFormContentTypeGuard(code);
}

// In the parameter loop switch:
case EndpointParameterSource.Form:
    if (param.Children.IsDefaultOrEmpty)
        EmitFormPrimitiveBinding(code, param, i);
    else
        EmitFormDtoBinding(code, param, i);
    break;
case EndpointParameterSource.FormFile:
    EmitFormFileBinding(code, param, i);
    break;
case EndpointParameterSource.FormFiles:
    EmitFormFilesBinding(code, param, i);
    break;
```

**Step 2: Build to verify**

Run: `dotnet build ErrorOr.Http/ErrorOr.Http.csproj --no-restore`

**Step 3: Run snapshot tests**

Run: `dotnet test ErrorOr.Http.SnapShot`

**Step 4: Review and accept snapshot changes**

**Step 5: Commit**

```bash
git add ErrorOr.Http/
git commit -m "feat(emitter): integrate form binding into invoker generation"
```

---

## Phase 4: OpenAPI Metadata

### Task 4.1: Add Multipart Accept Metadata

**Files:**
- Modify: `ErrorOr.Http/Generators/ErrorOrEndpointGenerator.Emitter.cs`

**Step 1: Update EmitMapCall for form endpoints**

In the metadata emission section:

```csharp
if (hasFormParams)
{
    code.AppendLine("            .Accepts<Microsoft.AspNetCore.Http.IFormCollection>(\"multipart/form-data\")");
}
```

**Step 2: Build and run snapshot tests**

Run: `dotnet build && dotnet test ErrorOr.Http.SnapShot`

**Step 3: Accept snapshots and commit**

```bash
git add ErrorOr.Http/
git commit -m "feat(openapi): add multipart/form-data accept metadata"
```

---

## Phase 5-7: Integration Tests and Polish

### Task 5.1: Add Integration Test Project (if needed)

**Files:**
- Create: `ErrorOr.Http.IntegrationTests/ErrorOr.Http.IntegrationTests.csproj`

### Task 5.2: Add Form Binding Integration Tests

**Test scenarios:**
- Missing required form field → 400
- Missing optional form field → Success with null
- Invalid primitive parse → 400
- Missing required file → 400
- Missing optional file → Success with null
- Valid form + file → Success

### Task 5.3: Run Full Test Suite

Run: `dotnet test`
Expected: All tests pass

### Task 5.4: Update Documentation

**Files:**
- Modify: `README.md` - Add form binding examples
- Modify: `CLAUDE.md` - Mark phase complete

### Task 5.5: Final Commit

```bash
git add .
git commit -m "feat: complete Form Binding v2.0 implementation"
```

---

## Definition of Done Checklist

- [ ] All Phase 0-4 tasks complete
- [ ] EOE006-EOE010 diagnostics fire at compile-time
- [ ] Form primitives bind via TryParse
- [ ] Form DTOs bind via constructor
- [ ] IFormFile/IFormFileCollection bind correctly
- [ ] Generated code has no reflection
- [ ] Snapshot tests pass
- [ ] OpenAPI metadata includes multipart/form-data
- [ ] README updated with examples
