# ASP.NET Core OpenAPI Source Generator Reference

Reference paths for exploring Microsoft's OpenAPI XML documentation generator implementation.

## Base Path
```
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/
```

## Main Generator Files

| File | Purpose |
|------|---------|
| `XmlCommentGenerator.cs` | Main `IIncrementalGenerator` - pipeline setup |
| `XmlCommentGenerator.Parser.cs` | Partial class - XML parsing logic |
| `XmlCommentGenerator.Emitter.cs` | Partial class - code emission |
| `Microsoft.AspNetCore.OpenApi.SourceGenerators.csproj` | Project file |

## XmlComments Models

| File | Purpose |
|------|---------|
| `XmlComments/XmlComment.cs` | Main XML comment model |
| `XmlComments/XmlComment.InheritDoc.cs` | `<inheritdoc>` resolution logic |
| `XmlComments/XmlParameterComment.cs` | `<param>` tag model |
| `XmlComments/XmlResponseComment.cs` | `<response>` tag model |

## Helpers

| File | Purpose |
|------|---------|
| `Helpers/AddOpenApiInvocation.cs` | Represents an `AddOpenApi()` call location |
| `Helpers/AddOpenApiInvocationComparer.cs` | Equality comparer for deduplication |
| `Helpers/AddOpenApiOverloadVariant.cs` | Enum for different `AddOpenApi()` overloads |
| `Helpers/DocumentationCommentXmlNames.cs` | XML element name constants |
| `Helpers/ISymbolExtensions.cs` | Roslyn symbol extensions |
| `Helpers/StringExtensions.cs` | String utilities |
| `Helpers/AssemblyTypeSymbolsVisitor.cs` | Visits types in assemblies |

## Quick Open Commands

```bash
# Open main generator
code /Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/XmlCommentGenerator.cs

# Open all generator files in Rider
rider /Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/

# List all files
ls -la /Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/
ls -la /Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/Helpers/
ls -la /Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/XmlComments/
```

## Full Paths (Copy-Paste Ready)

### Generator Core
```
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/XmlCommentGenerator.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/XmlCommentGenerator.Parser.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/XmlCommentGenerator.Emitter.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/Microsoft.AspNetCore.OpenApi.SourceGenerators.csproj
```

### XmlComments
```
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/XmlComments/XmlComment.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/XmlComments/XmlComment.InheritDoc.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/XmlComments/XmlParameterComment.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/XmlComments/XmlResponseComment.cs
```

### Helpers
```
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/Helpers/AddOpenApiInvocation.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/Helpers/AddOpenApiInvocationComparer.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/Helpers/AddOpenApiOverloadVariant.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/Helpers/DocumentationCommentXmlNames.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/Helpers/ISymbolExtensions.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/Helpers/StringExtensions.cs
/Users/ancplua/RiderProjects/aspnetcore/src/OpenApi/gen/Helpers/AssemblyTypeSymbolsVisitor.cs
```

## Validation Generator (Safia's Other Work)

```
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/ValidationsGenerator.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Emitters/ValidationsGenerator.Emitter.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Parsers/ValidationsGenerator.EndpointsParser.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Parsers/ValidationsGenerator.TypesParser.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Parsers/ValidationsGenerator.AttributeParser.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Parsers/ValidationsGenerator.AddValidation.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Models/ValidatableType.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Models/ValidatableProperty.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Models/ValidationAttribute.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Models/RequiredSymbols.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Models/ValidatableTypeComparer.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Extensions/ITypeSymbolExtensions.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Extensions/ISymbolExtensions.cs
/Users/ancplua/RiderProjects/aspnetcore/src/Validation/gen/Extensions/IncrementalValuesProviderExtensions.cs
```

## Key Patterns to Study

1. **Partial class splitting** - `XmlCommentGenerator.cs` + `*.Parser.cs` + `*.Emitter.cs`
2. **Interception** - `AddOpenApiInvocation.cs` captures call locations
3. **XML parsing** - `XmlComment.cs` parses documentation comments
4. **MemberKey bridging** - compile-time symbols → runtime types
