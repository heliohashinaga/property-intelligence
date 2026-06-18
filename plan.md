# Implementation Plan

## Goal
Revise `specs/001-property-intelligence-api/tasks.md` so development starts with a mock-data-first vertical slice, keeps the provider-registry architecture, and replaces commercial IPTU-oriented work with public GeoSampa/official-source tasks.

## Tasks
1. **Re-sequence `tasks.md` to introduce a mock-data-first slice before real-source integration**
   - File: `specs/001-property-intelligence-api/tasks.md`
   - Changes: Insert a new phase between current Foundational and real-provider US1 work, e.g. `Phase 3A: Mock Data MVP Slice`. This phase should deliver `POST /v1/property/analyze` end-to-end using registry-enabled mock providers and static fixtures, before any dependency on imports, public APIs, or geospatial datasets.
   - Acceptance: `tasks.md` clearly shows that the first independently shippable backend milestone is a mocked analysis response with persistence, scoring, graceful degradation, and provider registry wiring.

2. **Add test-first tasks for the mock-data MVP slice**
   - File: `specs/001-property-intelligence-api/tasks.md`
   - Changes: Add tasks ahead of real-provider implementation for:
     - contract test for `POST /v1/property/analyze` using only mock providers
     - unit tests for `PropertyEnrichmentModule` registry selection and unavailable-provider handling
     - unit/integration tests for scoring, flags, audit persistence, and `cached` behavior against mock data
     - registry-fixture-driven tests that enable subsets of providers without code changes
   - Acceptance: mock slice tasks explicitly preserve red-green-refactor order and do not require ANA/IBGE/CNES/INEP/SSP imports to get the first green end-to-end API test.

3. **Add mock provider implementation tasks as first concrete provider work**
   - File: `specs/001-property-intelligence-api/tasks.md`
   - Changes: Add tasks for a minimal set of mock adapters that implement the same abstractions as real providers, for example:
     - `src/PropertyIntelligence.Providers/Mock/MockAddressProvider.cs`
     - `src/PropertyIntelligence.Providers/Mock/MockMobilityProvider.cs`
     - `src/PropertyIntelligence.Providers/Mock/MockEnvironmentProvider.cs`
     - `src/PropertyIntelligence.Providers/Mock/MockSecurityProvider.cs`
     - `src/PropertyIntelligence.Providers/Mock/MockInfrastructureProvider.cs`
     - `src/PropertyIntelligence.Providers/Mock/MockAppreciationProvider.cs`
     - `src/PropertyIntelligence.Providers/Mock/MockUrbanContextProvider.cs`
     - `src/PropertyIntelligence.Providers/Mock/MockProviderDataLoader.cs`
     These tasks should load deterministic JSON fixtures and register through `IProviderRegistry` with provider IDs like `mock_address`, `mock_mobility`, etc.
   - Acceptance: the first runnable API path depends only on local mock fixtures plus existing DB/Redis infrastructure, not on external network calls or imported public datasets.

4. **Add fixture and registry-seed tasks required by the mock slice**
   - File: `specs/001-property-intelligence-api/tasks.md`
   - Changes: Add tasks for creating deterministic fixture files and registry seeds, for example:
     - `tests/PropertyIntelligence.Tests.Contract/Fixtures/Analyze/mock-analysis-happy-path.json`
     - `tests/PropertyIntelligence.Tests.Contract/Fixtures/Providers/*.json`
     - `tests/PropertyIntelligence.Tests.Contract/Fixtures/Registry/mock-mvp.providers.json`
     - `src/PropertyIntelligence.Api/appsettings.Mock.json` or equivalent config source for enabling mock providers locally
   - Acceptance: tasks specify that provider enable/disable behavior is driven by fixture/config data, not by changing DI registrations or endpoint code.

5. **Move dataset import execution and real-source onboarding after the mock MVP checkpoint**
   - File: `specs/001-property-intelligence-api/tasks.md`
   - Changes: Keep the existing import-script creation tasks in Foundational if desired, but move any task that requires running imports (`T014`) and any task whose validation depends on imported data to a later “Real Sources” phase after the mock MVP checkpoint. Real-source work should extend the system, not block the first end-to-end delivery.
   - Acceptance: another agent can implement and demo the API with mocked data before touching ANA/IBGE/INEP/CNES/SSP operational imports.

6. **Replace commercial IPTU tasks with public-source appreciation tasks**
   - File: `specs/001-property-intelligence-api/tasks.md`
   - Changes: Remove or rewrite the current `IptuApiProvider` and IPTU API trend tasks. Replace them with public-source providers aligned to updated research, such as:
     - `GeoSampaZoneamentoProvider` for zoning permissiveness / land-use signals
     - `GeoSampaCadastroProvider` or `GeoSampaIptuProvider` for public cadastral/IPTU fields when officially available
     - optional `TransitProjectsProvider` for confirmed future transit proximity used in appreciation signals
     Update rule tasks so `AppreciationRules` score from official public proxies (zoneamento, transit projects, urban projects, official cadastral signals) instead of a commercial wrapper API.
   - Acceptance: no remaining task in `tasks.md` makes `iptuapi.com.br` or `IPTU_API_KEY` a required MVP dependency.

7. **Align mobility and geocoding tasks with the updated public-source research**
   - File: `specs/001-property-intelligence-api/tasks.md`
   - Changes: Rewrite current `ViaCepProvider` and `OverpassPoiProvider` sequencing so tasks reflect:
     - address normalization via `ViaCEP` + municipality validation via IBGE/local rules
     - coordinates from local/open sources first where feasible
     - `Overpass` as fallback/supplementary mobility provider rather than the only planned path
     - official São Paulo mobility layers (SPTrans / GeoSampa / Metrô/CPTM) as preferred real-source tasks in the later phase
   - Acceptance: task wording no longer assumes public Nominatim as the primary production geocoder or Overpass as the sole long-term mobility source.

8. **Split current US1 into two checkpoints: Mock MVP and Real Data MVP**
   - File: `specs/001-property-intelligence-api/tasks.md`
   - Changes: Introduce explicit checkpoints such as:
     - `Checkpoint A`: mocked `POST /v1/property/analyze` works end-to-end
     - `Checkpoint B`: selected public/official providers replace mocks incrementally
     Real providers should be introduced one by one behind the same registry and test harness, with mocks remaining available for local/dev/test use.
   - Acceptance: task dependencies make clear that frontend work and core engine validation can begin after Checkpoint A, while Checkpoint B is an incremental hardening phase.

9. **Rewrite later trend/flag tasks to reference public-source providers instead of IPTU-specific trend logic**
   - File: `specs/001-property-intelligence-api/tasks.md`
   - Changes: Replace appreciation-trend tasks tied to `IptuApiProvider` with trend logic sourced from official datasets or, if historical official data is incomplete, explicitly mark appreciation trend as MVP-stable with a follow-up task once a second public snapshot/history source is available. Also update opportunity-flag tasks to reference GeoSampa/Metrô future-project layers instead of commercial zoning/IPTU payload assumptions.
   - Acceptance: Phase 4 no longer depends on `IptuData` or commercial-history fields to compute appreciation behavior.

10. **Update dependency and parallelism notes in `tasks.md` to reflect the new execution order**
   - File: `specs/001-property-intelligence-api/tasks.md`
   - Changes: Revise “Phase Dependencies,” “Within Each Phase,” “Parallel Opportunities,” and “Implementation Strategy” so they describe:
     - Foundational → Mock MVP → Real Public Sources → Trends/Flags → Frontend
     - mock-provider tasks as the first provider parallel block
     - real-provider onboarding as a second parallel block once the mock slice is green
   - Acceptance: the bottom-of-file ordering guidance matches the revised task list and no longer claims real data integration is the first path to MVP.

## Files to Modify
- `specs/001-property-intelligence-api/tasks.md` - re-sequence phases, replace IPTU/commercial-source tasks, add mock-data-first tasks, and update dependency notes
- `specs/001-property-intelligence-api/quickstart.md` - likely follow-up update so first-run validation supports a mock-only path before real imports
- `specs/001-property-intelligence-api/contracts/analyze-endpoint.md` - likely follow-up update if examples should explicitly mention mock-enabled local/dev scenarios

## New Files
- `tests/PropertyIntelligence.Tests.Contract/Fixtures/Analyze/mock-analysis-happy-path.json` - deterministic end-to-end response fixture for mock slice
- `tests/PropertyIntelligence.Tests.Contract/Fixtures/Registry/mock-mvp.providers.json` - enabled-provider fixture for registry-driven mock scenarios
- `tests/PropertyIntelligence.Tests.Contract/Fixtures/Providers/` - per-provider mock payload fixtures
- `src/PropertyIntelligence.Providers/Mock/MockProviderDataLoader.cs` - shared loader for deterministic mock provider payloads
- `src/PropertyIntelligence.Providers/Mock/*.cs` - mock provider adapters implementing the same interfaces as real providers
- `src/PropertyIntelligence.Api/appsettings.Mock.json` - optional local config enabling the mock-provider registry profile

## Dependencies
- Task 1 must happen before all other task-list edits, because it establishes the new sequencing model.
- Tasks 2–4 depend on the provider registry work already present in `tasks.md` and should be placed immediately after it.
- Task 5 depends on Task 1 because import execution can only be moved after the new mock checkpoint exists.
- Tasks 6–9 depend on the updated research direction and should be applied before rewriting dependency/parallelism notes in Task 10.
- Task 10 depends on completion of Tasks 1–9 so the summary ordering reflects the actual revised task list.

## Risks
- The exact official São Paulo replacement for all appreciation inputs is still partly underspecified; `GeoSampaZoneamentoProvider` is clear, but historical fiscal-value coverage may require a simplified MVP rule instead of a true trend.
- The line between “mock providers in production code” and “test-only fake providers” needs explicit decision in `tasks.md`; for fastest delivery, the mock slice should be runnable locally through config, not only inside tests.
- `T076` currently still references batch geocoding via Nominatim; when `tasks.md` is revised, that wording should be changed to a local/open geocoding workflow to stay aligned with research.
- If the team wants the frontend demo immediately, Checkpoint A should be declared sufficient for US3 to begin; otherwise frontend work will remain unnecessarily blocked by hard real-data integration.
- Real-source onboarding should preserve mock providers as a stable fallback for tests and demos; otherwise every provider outage will slow development again.
