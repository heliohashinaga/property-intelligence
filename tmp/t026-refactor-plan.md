# T026 Raw Payload Refactor Impact Plan

## Summary
Changing `IDataProvider<T>.FetchAsync` to return a result that carries both typed data and raw payload is feasible with a small, contained refactor. The smallest safe path is to add a generic result type in Core, update the `IDataProvider<T>` contract, adapt `PropertyEnrichmentModule` invoker plumbing, and update the 8 mock providers plus the few test doubles that implement `IDataProvider<T>`.

## Why this refactor is needed
Current `PropertyEnrichmentModule` writes `DataProviderRawLog.RawPayload` from `JsonSerializer.Serialize(payload)` of the transformed DTO, not from the provider's original payload. That breaks the auditability intent documented in:

- `src/PropertyIntelligence.Core/Domain/DataProviderRawLog.cs`
- `src/PropertyIntelligence.Core/Interfaces/IDataProvider.cs`
- `src/PropertyIntelligence.Core/Services/PropertyEnrichmentModule.cs`

## Smallest safe implementation path

### 1) Add a shared provider result type in Core
Add a new type such as:
- `src/PropertyIntelligence.Core/Domain/ProviderFetchResult.cs`

Recommended shape:
- `Data` / typed result
- `RawPayload` / original raw response text

Keep it generic so providers stay strongly typed.

### 2) Change the provider contract once
Update:
- `src/PropertyIntelligence.Core/Interfaces/IDataProvider.cs`

From:
- `Task<TResult> FetchAsync(...)`

To:
- `Task<ProviderFetchResult<TResult>> FetchAsync(...)`

Leave `ProviderName` and `CacheTtl` unchanged.

### 3) Refactor `PropertyEnrichmentModule` plumbing only where needed
Update:
- `src/PropertyIntelligence.Core/Services/PropertyEnrichmentModule.cs`

Preserve:
- registry-driven selection via `IProviderRegistry.GetEnabled()`
- per-provider timeout behavior
- per-provider exception handling
- `ProvidersUnavailable` population
- `PropertyProfile` aggregation

Minimal code-shape recommendation:
- keep `ExecuteProviderAsync(...)` as the timeout/exception boundary
- change `_providerInvokers` so each invoker returns both typed data and raw payload
- stop synthesizing success raw payload via `JsonSerializer.Serialize(payload)`
- keep timeout/exception failure payloads as synthetic JSON error envelopes (`timeout`, exception message), since there is no source payload on failure

Practical implementation option:
- keep the internal `ProviderExecutionResult`
- change `RegisterProviders<T>(...)` to wrap `provider.FetchAsync(...)` and adapt `ProviderFetchResult<T>` into the internal execution result
- avoid leaking the new generic result type throughout the rest of Core

### 4) Minimize churn in mock providers by using the existing raw fixture loader
Best leverage point:
- `src/PropertyIntelligence.Providers/Mock/MockProviderDataLoader.cs`

It already has:
- `LoadRaw(...)` returning `JsonElement?`

Smallest safe provider-side helper:
- add a new loader helper that returns both deserialized typed data and `JsonElement.GetRawText()`
- then update each mock provider to return `ProviderFetchResult<T>` using that helper

Affected mock providers:
- `src/PropertyIntelligence.Providers/Mock/MockAddressProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockMobilityProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockEnvironmentProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockSecurityProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockHealthProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockSchoolProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockAppreciationProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockUrbanContextProvider.cs`

For fallback branches where no fixture exists/matches:
- return the fallback DTO as `Data`
- use serialized fallback DTO as `RawPayload`
- document that this is synthetic mock payload, not external-source raw payload

## Likely breakpoints

### Compile-breaks from interface change
Any `IDataProvider<T>` implementation or fake will fail to compile until updated.

Affected implementations/fakes found in the repo:
- `src/PropertyIntelligence.Providers/Mock/*.cs` (8 files)
- `tests/PropertyIntelligence.Tests.Unit/PropertyEnrichmentModuleTests.cs`
- `tests/PropertyIntelligence.Tests.Contract/AnalyzeEndpointTests.cs` (`FailingSecurityProvider`)

### `PropertyEnrichmentModule` invoker dictionary
Current invoker shape only carries `object?` payload. That is the main orchestration breakpoint.

Affected file:
- `src/PropertyIntelligence.Core/Services/PropertyEnrichmentModule.cs`

### Unit test expectations around raw logs
Current unit tests assert provider names and unavailable behavior, but do not yet pin exact `RawPayload` values. After the refactor, that is the best place to add the strongest auditability assertions.

Affected test file:
- `tests/PropertyIntelligence.Tests.Unit/PropertyEnrichmentModuleTests.cs`

## Recommended test updates

### Update existing unit tests
File:
- `tests/PropertyIntelligence.Tests.Unit/PropertyEnrichmentModuleTests.cs`

Keep existing assertions and add:
- successful enabled provider stores expected raw payload text
- disabled provider does not generate a raw log entry
- failing enabled provider still generates a raw log entry with an error envelope

### Update contract-test fake only for compilation
File:
- `tests/PropertyIntelligence.Tests.Contract/AnalyzeEndpointTests.cs`

Only the fake `FailingSecurityProvider` should need a return type/signature update. Endpoint assertions should remain unchanged.

## Files most likely to change

### Core
- `src/PropertyIntelligence.Core/Interfaces/IDataProvider.cs`
- `src/PropertyIntelligence.Core/Services/PropertyEnrichmentModule.cs`
- `src/PropertyIntelligence.Core/Domain/ProviderFetchResult.cs` (new)

### Providers
- `src/PropertyIntelligence.Providers/Mock/MockProviderDataLoader.cs`
- `src/PropertyIntelligence.Providers/Mock/MockAddressProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockMobilityProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockEnvironmentProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockSecurityProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockHealthProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockSchoolProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockAppreciationProvider.cs`
- `src/PropertyIntelligence.Providers/Mock/MockUrbanContextProvider.cs`

### Tests
- `tests/PropertyIntelligence.Tests.Unit/PropertyEnrichmentModuleTests.cs`
- `tests/PropertyIntelligence.Tests.Contract/AnalyzeEndpointTests.cs`

## Files unlikely to need changes
- `src/PropertyIntelligence.Core/Domain/DataProviderRawLog.cs`
- `src/PropertyIntelligence.Core/Interfaces/IDataProviderRawLogStore.cs`
- `src/PropertyIntelligence.Core/Services/EfCoreDataProviderRawLogStore.cs`
- `src/PropertyIntelligence.Api/Program.cs`
- `src/PropertyIntelligence.Providers/Mock/MockProviderServiceCollectionExtensions.cs`

## Validation commands for the writer
Run after the refactor:

1. `dotnet build`
2. `dotnet test tests/PropertyIntelligence.Tests.Unit`
3. `dotnet test tests/PropertyIntelligence.Tests.Contract`

Optional focused checks:
4. `dotnet test tests/PropertyIntelligence.Tests.Unit --filter "FullyQualifiedName~PropertyEnrichmentModuleTests"`
5. `dotnet test tests/PropertyIntelligence.Tests.Contract --filter "FullyQualifiedName~AnalyzeEndpointTests"`

## Residual risks
- Any future non-mock provider added later will also need to implement the new `FetchAsync` contract.
- Mock fallback branches will still produce synthetic raw payloads when there is no fixture match; this is acceptable for mock mode if documented.
- If the writer changes the invoker abstraction too broadly, it could accidentally alter timeout or unavailable-provider behavior. Keep those paths intact.

```acceptance-report
{
  "criteriaSatisfied": [
    {
      "id": "criterion-1",
      "status": "satisfied",
      "evidence": "Identified affected Core, Mock provider, and test files; recommended the minimal safe path: add a generic ProviderFetchResult, update IDataProvider<T>.FetchAsync, adapt PropertyEnrichmentModule invokers, and update the 8 mock providers plus 2 test files."
    }
  ],
  "changedFiles": [
    "/home/helio/repos/property-intelligence/tmp/t026-refactor-plan.md"
  ],
  "testsAddedOrUpdated": [],
  "commandsRun": [
    {
      "command": "grep -R \"Task<[^>]+>\\s+FetchAsync\\(\" **/*.cs",
      "result": "passed",
      "summary": "Found FetchAsync implementations in IDataProvider, 8 mock providers, and test doubles in unit/contract tests."
    },
    {
      "command": "grep -R \"IDataProvider<\" **/*.cs",
      "result": "passed",
      "summary": "Found all injection points and fake implementations affected by the interface change."
    },
    {
      "command": "read src/PropertyIntelligence.Core/Services/PropertyEnrichmentModule.cs",
      "result": "passed",
      "summary": "Confirmed registry-driven fan-out, timeout handling, and current raw-payload serialization behavior."
    },
    {
      "command": "read src/PropertyIntelligence.Providers/Mock/MockProviderDataLoader.cs",
      "result": "passed",
      "summary": "Confirmed LoadRaw(JsonElement?) already exists and can be reused to minimize provider changes."
    },
    {
      "command": "read tests/PropertyIntelligence.Tests.Unit/PropertyEnrichmentModuleTests.cs and tests/PropertyIntelligence.Tests.Contract/AnalyzeEndpointTests.cs",
      "result": "passed",
      "summary": "Confirmed the exact unit-test and contract-test doubles that must be updated for the new FetchAsync return type."
    }
  ],
  "validationOutput": [
    "Repository scan found 8 mock IDataProvider implementations in src/PropertyIntelligence.Providers/Mock.",
    "Repository scan found 2 test files with IDataProvider fakes that will compile-break after the interface change.",
    "PropertyEnrichmentModule currently stores success raw payloads by serializing transformed DTOs, which is the auditability gap this refactor fixes."
  ],
  "residualRisks": [
    "Any future real provider implementation will need the new contract as soon as non-mock providers are added.",
    "Mock fallback branches without fixture matches will still require a documented synthetic raw payload strategy.",
    "Over-broad refactoring of invoker plumbing could accidentally change timeout or ProvidersUnavailable behavior; keep the current control flow intact."
  ],
  "noStagedFiles": true,
  "notes": "Read-only planning only. No repository source files were modified; only the requested planning artifact was written. Validation commands listed for the writer were not executed in this planning task."
}
```