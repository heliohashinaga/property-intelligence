<!--
SYNC IMPACT REPORT
==================
Version change: (none — initial ratification) → 1.0.0
Added sections:
  - Core Principles (5 principles: Domain-First, Data Accuracy & Auditability,
    Test-First, API-First, Observability)
  - Technical Standards
  - Development Workflow
  - Governance
Removed sections: N/A (initial ratification)
Modified principles: N/A (new document)
Templates reviewed:
  ✅ .specify/templates/plan-template.md — Constitution Check gate is dynamic
     ([Gates determined based on constitution file]); no update required
  ✅ .specify/templates/spec-template.md — requirements structure aligns
     with Domain-First and API-First principles; no update required
  ✅ .specify/templates/tasks-template.md — task phases align with
     Test-First (tests before implementation) and Observability
     (logging tasks in foundational phase); no update required
Deferred TODOs: None
-->

# Property Intelligence Constitution

## Core Principles

### I. Domain-First Modeling

Every feature MUST be expressed in terms of the real estate domain
(Property, Valuation, RiskProfile, Market, Listing, etc.) before any
implementation detail is chosen. Domain entities and their relationships
MUST be defined in the feature specification before development begins.
No feature may be shipped without explicit domain entity definitions
committed to `specs/[###-feature-name]/`.

**Rationale**: Property valuation and risk assessment are expert domains.
Leaking infrastructure concerns into domain logic degrades correctness,
makes auditing harder, and obscures business rules from domain experts.

### II. Data Accuracy & Auditability

All valuations, risk scores, and derived metrics MUST be fully traceable:
every output MUST carry a reference to its source data and the
algorithm/model version that produced it. Historical records MUST never
be mutated in place; corrections MUST be appended, not overwritten.
Every data ingestion pipeline MUST preserve the raw external payload
before any transformation is applied.

**Rationale**: Financial and legal decisions depend on these valuations.
Incorrect or untraceable outputs create regulatory and liability exposure
that is unacceptable for a property risk platform.

### III. Test-First Development (NON-NEGOTIABLE)

Tests MUST be written and confirmed failing before implementation begins.
The mandatory cycle is: write test → confirm red → implement →
confirm green → refactor. No production code ships without an associated
automated test at the contract or integration level covering its primary
behavior.

**Rationale**: The property risk domain demands correctness that cannot
be guaranteed by retrofitting tests. Pre-written tests encode the
domain contract and protect against silent regressions.

### IV. API-First Interface

Every feature MUST produce a documented, versioned API contract (request/
response schemas, error codes, authentication requirements) before
implementation begins. Contracts are committed to `specs/[###-feature]/
contracts/` and reviewed before development starts. Breaking changes to
existing contracts MUST be versioned and communicated.

**Rationale**: A Property Intelligence platform integra-se a provedores externos de dados, fontes de mercado e clientes consumidores. Contratos estáveis e bem documentados reduzem risco de integração e permitem evolução paralela.

### V. Observability by Default

Every service and background job MUST emit structured logs (JSON) with
correlation IDs, operation names, and latency measurements. Risk scoring
and valuation pipelines MUST log inputs, intermediate computed values,
and outputs for post-hoc auditing. Every HTTP service MUST expose a
health endpoint. Silent failures are a constitution violation.

**Rationale**: Valuation errors in production must be diagnosable
rapidly. In a financial risk context, inadequate observability converts
bugs into undetectable, compounding harm.

## Technical Standards

- **Stack**: Declared per feature in `specs/[###-feature]/plan.md`;
  the plan MUST justify any deviation from the existing project stack.
- **Storage**: Append-only writes for valuation and risk records.
  Read-optimized projections (read models, caches) are permitted for
  query performance but MUST NOT replace the source-of-truth records.
- **External Data**: All third-party property and market data MUST be
  ingested through a dedicated adapter layer. Raw external payloads
  MUST be persisted before any transformation is applied.
- **Secrets**: It is strictly forbidden to commit (directly or indirectly) any secrets, passwords, private keys, tokens, API keys, or sensitive data to the repository. This includes code, configuration files (such as `.env`, `appsettings.Production.json`, `secrets.yml`, `credentials.json`, etc.), git history, and documentation examples. Always use only placeholder or clearly fake values (e.g. `API_KEY=your-key-here`) in any sample or shared configuration. All environment/secrets files (`.env`, `.env.*`, `.env.production` etc.) MUST be in `.gitignore` and must never be versioned. Any commit or PR introducing real secrets will be blocked, all affected keys must be rotated, and any secrets leaked in history MUST be purged using appropriate tooling before continuing development.
- **Privacy & Compliance**: Features touching personal property data or
  financial information MUST include a privacy and compliance note in
  the feature specification.

## Development Workflow

- All work begins with a feature specification (`/speckit.specify`).
- Implementation planning (`/speckit.plan`) is required before tasks
  are generated with `/speckit.tasks`.
- Every pull request MUST pass the Constitution Check defined in the
  active `plan.md` before code review is requested.
- Code review is mandatory; self-merge to main/master is prohibited.
- Specs, plans, contracts, and tasks live in `specs/[###-feature-name]/`
  and are committed alongside source code changes.
- The `AGENTS.md` file at the repository root provides runtime guidance
  for AI agents and MUST be kept current with major workflow changes.

## Governance

This Constitution supersedes all prior ad-hoc conventions and decisions.
Any amendment requires:

1. A documented rationale (update this file with the change).
2. A version increment per the Versioning Policy below.
3. Updates to all affected templates under `.specify/templates/`.
4. A commit message of the form:
   `docs: amend constitution to vX.Y.Z (<summary of change>)`

**Versioning Policy**:
- MAJOR: Removal or fundamental redefinition of a Core Principle.
- MINOR: New principle added, new section introduced, or materially
  expanded guidance that changes expected behavior.
- PATCH: Clarifications, wording corrections, typo fixes, non-semantic
  refinements.

All feature specifications and implementation plans MUST reference the
constitution version in force at the time of creation. Compliance review
occurs as part of the pull request code review process.

**Version**: 1.0.0 | **Ratified**: 2026-05-28 | **Last Amended**: 2026-05-28
