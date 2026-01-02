# Form Binding Epic Proposal

> **Version:** 2.0.0-preview
> **Status:** Design Approved
> **Target Framework:** .NET 10
> **Prerequisites:** ErrorOr.Http 1.0.0 (Route, Query, Header, Body, AsParameters)
> **Author:** Claude Code
> **Date:** 2026-01-02

---

## 1. Goals (Non-Negotiable)

| # | Goal | Rationale |
|---|------|-----------|
| 1 | **Explicit binding only** | No implicit form detection. All form parameters require `[FromForm]` or are special types (`IFormFile`, `IFormFileCollection`). |
| 2 | **Validated at compile time** | All form binding errors emit diagnostics during source generation. No runtime surprises. |
| 3 | **Generated code is readable** | Emitted code must be debuggable, step-through-able, and obvious to a junior developer. |
| 4 | **Predictable precedence** | Explicit attributes always win. Special types auto-bind. Ambiguous parameters error. |
| 5 | **AOT-safe always** | No reflection, no `Activator.CreateInstance`, no runtime model binding. |
| 6 | **Single structured source** | One `[FromForm]` DTO or `[FromForm] IFormCollection` per endpoint. Never both. |
| 7 | **IFormCollection is explicit-only** | `IFormCollection` without `[FromForm]` is a compile error. No magic. |

---

## 2. Classification Rules

### 2.1 Extended Parameter Source Enum

```csharp
internal enum EndpointParameterSource
{
    // Existing (v1.0)
    Route,
    Body,
    Query,
    Header,
    Service,
    KeyedService,
    AsParameters,
    HttpContext,
    CancellationToken,

    // New (v2.0 - Form Binding)
    Form,               // [FromForm] primitives, collections, DTOs
    FormFile,           // IFormFile (auto-detected)
    FormFileCollection  // IFormFileCollection (auto-detected)
}
```

### 2.2 Classification Precedence

```
1. Explicit attributes (win immediately)
   ├─ [FromRoute]
   ├─ [FromQuery]
   ├─ [FromHeader]
   ├─ [FromBody]
   ├─ [FromForm]           ← NEW
   ├─ [FromServices]
   ├─ [FromKeyedServices]
   └─ [AsParameters]

2. Special framework types (auto-detected)
   ├─ HttpContext
   ├─ CancellationToken
   ├─ IFormFile            ← NEW
   └─ IFormFileCollection  ← NEW

3. Explicit-only raw types (require attribute)
   └─ IFormCollection      ← NEW (must have [FromForm])

4. Implicit inference (existing brutal safety)
   ├─ Route match → Route
   ├─ Primitive → Query
   └─ Otherwise → EOE004 (Ambiguous)
```

### 2.3 Body Source Conflict Matrix

| Has `[FromBody]` | Has `[FromForm]` | Has `IFormFile` | Result |
|------------------|------------------|-----------------|--------|
| ✓ | ✗ | ✗ | Valid (JSON body) |
| ✗ | ✓ | ✗ | Valid (Form body) |
| ✗ | ✓ | ✓ | Valid (Form + files) |
| ✗ | ✗ | ✓ | Valid (Files only) |
| ✓ | ✓ | ✗ | **EOE010** (Conflict) |
| ✓ | ✗ | ✓ | **EOE010** (Conflict) |

### 2.4 Structured Form Source Rules

| Scenario | Result |
|----------|--------|
| One `[FromForm]` DTO | Valid |
| One `[FromForm] IFormCollection` | Valid |
| Two `[FromForm]` DTOs | **EOE011** |
| `[FromForm]` DTO + `[FromForm] IFormCollection` | **EOE012** |
| `IFormCollection` without `[FromForm]` | **EOE013** |
| `[FromForm]` DTO + `IFormFile` | Valid (coexist) |
| `[FromForm] IFormCollection` + `IFormFile` | Valid (coexist) |

---

## 3. Binding Semantics

### 3.1 Primitive Form Field

**Handler:**
```csharp
[Post("/contact")]
public static ErrorOr<Success> Contact(
    [FromForm] string name,
    [FromForm] string email,
    [FromForm] int? priority)
```

**Generated Code:**
```csharp
static async Task Invoke_Contact(HttpContext context)
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);

    // Required string
    if (!form.TryGetValue("name", out var nameValues) || nameValues.Count == 0)
        return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["name"] = ["The name field is required."] });
    var name = nameValues[0]!;

    // Required string
    if (!form.TryGetValue("email", out var emailValues) || emailValues.Count == 0)
        return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["email"] = ["The email field is required."] });
    var email = emailValues[0]!;

    // Optional int
    int? priority = null;
    if (form.TryGetValue("priority", out var priorityValues) && priorityValues.Count > 0)
    {
        if (!int.TryParse(priorityValues[0], out var priorityParsed))
            return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["priority"] = ["The priority field must be a valid integer."] });
        priority = priorityParsed;
    }

    var result = Handlers.Contact(name, email, priority);
    return result.Match(
        success => Results.NoContent(),
        errors => ErrorOrEndpoint.ToProblemDetails(errors));
}
```

### 3.2 Complex DTO (Constructor-Based)

**Handler:**
```csharp
public record CreateUserRequest(
    string Username,
    string Email,
    [FromForm(Name = "dob")] DateOnly? DateOfBirth);

[Post("/users")]
public static ErrorOr<Created> CreateUser([FromForm] CreateUserRequest request)
```

**Generated Code:**
```csharp
static async Task Invoke_CreateUser(HttpContext context)
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var errors = new Dictionary<string, string[]>();

    // Username (required)
    string? username = null;
    if (form.TryGetValue("Username", out var usernameValues) && usernameValues.Count > 0)
        username = usernameValues[0];
    else
        errors["Username"] = ["The Username field is required."];

    // Email (required)
    string? email = null;
    if (form.TryGetValue("Email", out var emailValues) && emailValues.Count > 0)
        email = emailValues[0];
    else
        errors["Email"] = ["The Email field is required."];

    // DateOfBirth (optional, custom name)
    DateOnly? dateOfBirth = null;
    if (form.TryGetValue("dob", out var dobValues) && dobValues.Count > 0)
    {
        if (!DateOnly.TryParse(dobValues[0], out var dobParsed))
            errors["dob"] = ["The dob field must be a valid date."];
        else
            dateOfBirth = dobParsed;
    }

    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var request = new CreateUserRequest(username!, email!, dateOfBirth);
    var result = Handlers.CreateUser(request);
    return result.Match(
        success => Results.Created(),
        errs => ErrorOrEndpoint.ToProblemDetails(errs));
}
```

### 3.3 File Upload

**Handler:**
```csharp
[Post("/upload")]
public static ErrorOr<Created> Upload(
    [FromForm] string description,
    IFormFile file,
    IFormFileCollection? additionalFiles)
```

**Generated Code:**
```csharp
static async Task Invoke_Upload(HttpContext context)
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var errors = new Dictionary<string, string[]>();

    // Description (required)
    string? description = null;
    if (form.TryGetValue("description", out var descValues) && descValues.Count > 0)
        description = descValues[0];
    else
        errors["description"] = ["The description field is required."];

    // IFormFile (required, auto-detected)
    var file = form.Files.GetFile("file");
    if (file is null)
        errors["file"] = ["The file field is required."];

    // IFormFileCollection (optional)
    var additionalFiles = form.Files.GetFiles("additionalFiles");

    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var result = Handlers.Upload(description!, file!, additionalFiles);
    return result.Match(
        success => Results.Created(),
        errs => ErrorOrEndpoint.ToProblemDetails(errs));
}
```

### 3.4 Raw Form Collection

**Handler:**
```csharp
[Post("/webhook")]
public static ErrorOr<Success> Webhook([FromForm] IFormCollection formData)
```

**Generated Code:**
```csharp
static async Task Invoke_Webhook(HttpContext context)
{
    var formData = await context.Request.ReadFormAsync(context.RequestAborted);

    var result = Handlers.Webhook(formData);
    return result.Match(
        success => Results.NoContent(),
        errors => ErrorOrEndpoint.ToProblemDetails(errors));
}
```

---

## 4. OpenAPI Schema Generation

### 4.1 Multipart Form Data

**Handler:**
```csharp
[Post("/submit")]
public static ErrorOr<Created> Submit(
    [FromForm] string title,
    [FromForm] int count,
    IFormFile document)
```

**Generated OpenAPI (YAML):**
```yaml
/submit:
  post:
    requestBody:
      required: true
      content:
        multipart/form-data:
          schema:
            type: object
            required:
              - title
              - count
              - document
            properties:
              title:
                type: string
              count:
                type: integer
                format: int32
              document:
                type: string
                format: binary
    responses:
      '201':
        description: Created
      '400':
        description: Validation Error
        content:
          application/problem+json:
            schema:
              $ref: '#/components/schemas/HttpValidationProblemDetails'
```

### 4.2 Generated Metadata

```csharp
app.MapMethods("/submit", new[] { "POST" }, (RequestDelegate)Invoke_Submit)
    .WithMetadata(new AcceptsAttribute(["multipart/form-data"]))
    .WithMetadata(new ProducesResponseTypeAttribute(201))
    .WithMetadata(new ProducesResponseTypeAttribute(typeof(HttpValidationProblemDetails), 400))
    .WithOpenApi(op =>
    {
        op.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Required = new HashSet<string> { "title", "count", "document" },
                        Properties = new Dictionary<string, OpenApiSchema>
                        {
                            ["title"] = new() { Type = "string" },
                            ["count"] = new() { Type = "integer", Format = "int32" },
                            ["document"] = new() { Type = "string", Format = "binary" }
                        }
                    }
                }
            }
        };
        return op;
    });
```

---

## 5. Diagnostics Contract

### 5.1 New Diagnostic IDs

| ID | Name | Severity | Message Template |
|----|------|----------|------------------|
| **EOE010** | `FormAndBodyConflict` | Error | `Endpoint '{0}' has both [FromForm] and [FromBody] parameters. An endpoint can read from form data OR JSON body, not both.` |
| **EOE011** | `MultipleStructuredFormSources` | Error | `Endpoint '{0}' has multiple [FromForm] parameters. Only one structured form body is allowed.` |
| **EOE012** | `MixedFormCollectionAndDto` | Error | `Endpoint '{0}' mixes [FromForm] IFormCollection with [FromForm] DTO. Use either raw IFormCollection or structured DTO binding, not both.` |
| **EOE013** | `FormCollectionRequiresAttribute` | Error | `Parameter '{0}' on '{1}' uses IFormCollection but lacks [FromForm]. IFormCollection does not auto-bind.` |
| **EOE014** | `UnsupportedFormType` | Error | `Parameter '{0}' on '{1}' cannot be form-bound: {2}` |

### 5.2 Philosophy: No Warnings, No Fallback

- All form binding issues are **errors**, not warnings
- No "best effort" binding — if we can't bind, we fail compilation
- No implicit fallback to query string or body
- Explicit is always better than implicit

### 5.3 Diagnostic Descriptor Definitions

```csharp
public static readonly DiagnosticDescriptor FormAndBodyConflict = new(
    "EOE010",
    "Form and body conflict",
    "Endpoint '{0}' has both [FromForm] and [FromBody] parameters. " +
    "An endpoint can read from form data OR JSON body, not both.",
    "Usage",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor MultipleStructuredFormSources = new(
    "EOE011",
    "Multiple structured form sources",
    "Endpoint '{0}' has multiple [FromForm] parameters. " +
    "Only one structured form body is allowed.",
    "Usage",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor MixedFormCollectionAndDto = new(
    "EOE012",
    "Mixed form collection and DTO",
    "Endpoint '{0}' mixes [FromForm] IFormCollection with [FromForm] DTO. " +
    "Use either raw IFormCollection or structured DTO binding, not both.",
    "Usage",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor FormCollectionRequiresAttribute = new(
    "EOE013",
    "IFormCollection requires explicit attribute",
    "Parameter '{0}' on '{1}' uses IFormCollection but lacks [FromForm]. " +
    "IFormCollection does not auto-bind.",
    "Usage",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor UnsupportedFormType = new(
    "EOE014",
    "Unsupported form type",
    "Parameter '{0}' on '{1}' cannot be form-bound: {2}",
    "Usage",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

---

## 6. AOT Traps and Patterns

### 6.1 DO NOT USE (Reflection-Based)

```csharp
// ❌ Runtime model binding
var model = await context.Request.ReadFromJsonAsync<T>();

// ❌ Activator pattern
var instance = Activator.CreateInstance(typeof(T));

// ❌ Reflection-based property setting
foreach (var prop in typeof(T).GetProperties())
    prop.SetValue(instance, formValue);

// ❌ Dynamic expression compilation
var compiled = Expression.Lambda<Func<IFormCollection, T>>(body).Compile();
```

### 6.2 ALLOWED (AOT-Safe)

```csharp
// ✅ Direct form access
var form = await context.Request.ReadFormAsync(ct);
var value = form["fieldName"];

// ✅ Static TryParse
if (int.TryParse(form["count"], out var count)) { }

// ✅ IParsable<T> (see Section 7)
if (T.TryParse(form["value"], null, out var result)) { }

// ✅ Constructor invocation
var dto = new CreateUserRequest(username, email, dateOfBirth);

// ✅ File access
var file = form.Files.GetFile("document");
var files = form.Files.GetFiles("attachments");
```

---

## 7. IParsable<TSelf> Integration (AOT-Safe Custom Parsing)

### 7.1 Background

.NET 7+ introduced `IParsable<TSelf>` and `ISpanParsable<TSelf>` as static abstract interface members. These provide an AOT-safe parsing surface that avoids the reflection-based `TryParse` discovery pattern.

### 7.2 Detection Strategy

```csharp
// Check if type implements IParsable<TSelf>
private static bool ImplementsIParsable(ITypeSymbol type)
{
    return type.AllInterfaces.Any(i =>
        i.OriginalDefinition.ToDisplayString() == "System.IParsable<TSelf>" &&
        SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type));
}
```

### 7.3 Generated Binding Code

**For `IParsable<T>` types:**
```csharp
// Handler parameter: [FromForm] Money amount
// where Money : IParsable<Money>

if (form.TryGetValue("amount", out var amountValues) && amountValues.Count > 0)
{
    if (!Money.TryParse(amountValues[0], null, out var amountParsed))
        errors["amount"] = ["The amount field must be a valid Money value."];
    else
        amount = amountParsed;
}
```

### 7.4 Priority in Form Binding

```
1. Primitive types (int, string, Guid, etc.) → Built-in TryParse
2. IParsable<TSelf> implementers → Static TryParse via interface
3. Complex types (records/classes) → Constructor-based binding
4. Unsupported → EOE014 diagnostic
```

### 7.5 Example: Custom Value Object

```csharp
public readonly record struct Money(decimal Amount, string Currency)
    : IParsable<Money>
{
    public static Money Parse(string s, IFormatProvider? provider)
        => TryParse(s, provider, out var result)
            ? result
            : throw new FormatException($"Invalid money format: {s}");

    public static bool TryParse(string? s, IFormatProvider? provider, out Money result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        // Format: "100.00 USD"
        var parts = s.Split(' ');
        if (parts.Length != 2) return false;

        if (!decimal.TryParse(parts[0], out var amount)) return false;

        result = new Money(amount, parts[1]);
        return true;
    }
}

// Usage
[Post("/payment")]
public static ErrorOr<Success> Pay([FromForm] Money amount)
```

---

## 8. DiagnosticFlow → ValidationProblemDetails Integration

### 8.1 RFC 9457 Problem Details Format

Form binding errors must accumulate into a standard `ValidationProblemDetails` response with per-field error arrays, matching RFC 9457 (Problem Details for HTTP APIs).

### 8.2 Error Accumulation Pattern

```csharp
// During form binding, errors accumulate into a dictionary
var errors = new Dictionary<string, List<string>>();

// Each field validation adds to the list
if (!form.TryGetValue("email", out var emailValues) || emailValues.Count == 0)
{
    if (!errors.ContainsKey("email"))
        errors["email"] = new List<string>();
    errors["email"].Add("The email field is required.");
}

// At the end, convert to string[] for ValidationProblemDetails
if (errors.Count > 0)
{
    var problemErrors = errors.ToDictionary(
        kvp => kvp.Key,
        kvp => kvp.Value.ToArray());
    return Results.ValidationProblem(problemErrors);
}
```

### 8.3 Generated Response Format

**HTTP Response:**
```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "email": ["The email field is required."],
    "dob": ["The dob field must be a valid date."],
    "file": ["The file field is required."]
  }
}
```

### 8.4 DiagnosticFlow Extension (Optional)

For complex DTOs with nested properties, errors should use dot notation:

```csharp
// Nested DTO
public record Address(string Street, string City, string Zip);
public record CreateUserRequest(string Name, Address BillingAddress);

// Error output
{
  "errors": {
    "Name": ["The Name field is required."],
    "BillingAddress.Street": ["The Street field is required."],
    "BillingAddress.Zip": ["The Zip field must be a valid postal code."]
  }
}
```

### 8.5 Implementation in Generated Code

```csharp
private static void AddError(
    Dictionary<string, List<string>> errors,
    string fieldPath,
    string message)
{
    if (!errors.TryGetValue(fieldPath, out var list))
    {
        list = new List<string>();
        errors[fieldPath] = list;
    }
    list.Add(message);
}

// Usage in nested binding
AddError(errors, $"{parentPath}.{fieldName}", "The field is required.");
```

---

## 9. Test Matrix

| # | Scenario | Input | Expected Outcome |
|---|----------|-------|------------------|
| 1 | Primitive form fields | `[FromForm] string name, [FromForm] int age` | Binds from `form["name"]`, `form["age"]` |
| 2 | Optional primitive | `[FromForm] int? priority` | Binds if present, null if missing |
| 3 | DTO with constructor | `[FromForm] CreateUserRequest request` | Recursive property binding |
| 4 | IFormFile (auto) | `IFormFile document` | Binds from `form.Files.GetFile("document")` |
| 5 | IFormFileCollection | `IFormFileCollection attachments` | Binds from `form.Files.GetFiles("attachments")` |
| 6 | IFormCollection explicit | `[FromForm] IFormCollection data` | Binds full form collection |
| 7 | IFormCollection implicit | `IFormCollection data` (no attr) | **EOE013** compile error |
| 8 | Mixed body + form | `[FromBody] T x, [FromForm] string y` | **EOE010** compile error |
| 9 | Multiple [FromForm] DTOs | `[FromForm] A a, [FromForm] B b` | **EOE011** compile error |
| 10 | IParsable<T> field | `[FromForm] Money amount` | Calls `Money.TryParse(...)` |
| 11 | Nested DTO errors | Missing nested property | Error path: `"Parent.Child.Field"` |
| 12 | File + DTO coexist | `[FromForm] T dto, IFormFile file` | Valid, both bound |

---

## 10. Implementation Roadmap

### Phase 1: Infrastructure (Day 1)

- [ ] Add `Form`, `FormFile`, `FormFileCollection` to `EndpointParameterSource`
- [ ] Add form-related fields to `ParameterMeta`
- [ ] Add `FromForm` to `KnownSymbols`
- [ ] Add diagnostic descriptors EOE010-EOE014

### Phase 2: Classification (Day 2)

- [ ] Implement `ClassifyFromFormParameter`
- [ ] Implement `ClassifyFormComplexType` (recursive)
- [ ] Add `IFormFile`/`IFormFileCollection` auto-detection
- [ ] Add `IFormCollection` explicit-only check
- [ ] Implement conflict validation (body + form, multiple structured)

### Phase 3: Code Emission (Day 3-4)

- [ ] Emit `ReadFormAsync` call
- [ ] Emit primitive field binding with `TryParse`
- [ ] Emit DTO constructor binding (recursive)
- [ ] Emit file binding (`GetFile`, `GetFiles`)
- [ ] Emit error accumulation pattern
- [ ] Emit `ValidationProblemDetails` response

### Phase 4: OpenAPI (Day 5)

- [ ] Emit `AcceptsAttribute(["multipart/form-data"])`
- [ ] Emit form schema in OpenAPI metadata
- [ ] Handle file fields as `format: binary`

### Phase 5: Testing & Polish (Day 6-7)

- [ ] Snapshot tests for all 12 test matrix scenarios
- [ ] Integration tests with actual HTTP requests
- [ ] Update CLAUDE.md with form binding documentation
- [ ] Update README with examples

---

## 11. Appendix: ParameterMeta Extensions

```csharp
internal readonly record struct ParameterMeta(
    // ... existing fields ...

    // NEW: Form binding fields
    bool HasFromForm,
    string FormName,
    bool IsFormFile,
    bool IsFormFileCollection,
    bool IsFormCollection,
    bool IsIParsable);
```

---

## 12. Appendix: Classification Flow Diagram

```
BindMethodParameters(method, pattern, knownSymbols)
    │
    ├─► Validate: No [FromBody] + [FromForm] conflict → EOE010
    │
    ├─► Validate: No multiple structured form sources → EOE011/EOE012
    │
    ├─► Validate: IFormCollection has [FromForm] → EOE013
    │
    └─► For each parameter:
           │
           ├─► [FromForm]?
           │      ├─► Primitive/IParsable → Source.Form
           │      ├─► Collection → Source.Form
           │      ├─► IFormCollection → Source.Form (raw)
           │      └─► Complex → Recursive property binding
           │
           ├─► IFormFile? → Source.FormFile (auto)
           │
           ├─► IFormFileCollection? → Source.FormFileCollection (auto)
           │
           └─► (existing classification continues...)
```

---

**Document Status:** Ready for Implementation
**Next Step:** Phase 1 - Infrastructure
