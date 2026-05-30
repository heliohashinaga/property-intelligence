<!--
SYNC IMPACT REPORT
==================
Version change: 1.0.0 → 1.1.0
Added sections:
  - Core Principles (6 principles: Domain-First, Data Accuracy & Auditability,
    Test-First, API-First, Observability, LGPD & Privacy by Design)
  - Technical Standards (Privacy & Compliance expanded with LGPD requirements)
  - Development Workflow
  - Governance
Removed sections: N/A
Modified principles:
  - VI: NEW — LGPD & Privacy by Design
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

### VI. LGPD & Privacy by Design (NON-NEGOTIABLE)

Esta plataforma trata dados pessoais (endereços, coordenadas geográficas,
dados cadastrais) de titulares brasileiros e está sujeita à Lei nº 13.709/2018
(LGPD). Conformidade não é opcional. Todo engenheiro e agente DEVE seguir
estas regras em cada feature e mudança.

#### Princípios do Art. 6º — vinculantes em todo tratamento de dados

| # | Princípio | Regra prática para este projeto |
|---|---|---|
| I | **Finalidade** | Dados de endereço são processados exclusivamente para gerar score e análise imobiliária. Nenhum dado pode ser reutilizado para outra finalidade sem nova base legal explícita. |
| II | **Adequação** | O tratamento deve ser compatível com a finalidade informada ao titular no momento da coleta. |
| III | **Necessidade** | Coletar o mínimo necessário. Cada campo persistido deve passar no teste: *"se remover este dado, consigo cumprir a finalidade?"* — se sim, o campo é excessivo e não pode ser coletado. |
| IV | **Livre acesso** | Titulares têm direito a consultar gratuitamente seus dados. O sistema DEVE expor mecanismo de atendimento a requisições de acesso (Art. 18). |
| V | **Qualidade** | Dados pessoais devem ser exatos, claros, relevantes e atualizados. Resultados de análise baseados em dados desatualizados DEVEM ser marcados com `data_staleness_warning`. |
| VI | **Transparência** | Logs e respostas de API NUNCA devem expor dados pessoais além do necessário. A política de privacidade deve estar acessível publicamente. |
| VII | **Segurança** | TLS 1.2+ obrigatório em trânsito. Dados pessoais em repouso DEVEM ser criptografados (AES-256 no mínimo). RBAC implementado para todo acesso a dados pessoais. |
| VIII | **Prevenção** | Avaliação de impacto (RIPD) obrigatória para qualquer feature que introduza novo tratamento de dados pessoais. Privacy by design: proteção incorporada na arquitetura, não adicionada depois. |
| IX | **Não discriminação** | Scores e análises NUNCA podem ser usados para discriminação ilícita. Viés algorítmico em dados de localização DEVE ser auditado. |
| X | **Responsabilização** | Registrar evidências de conformidade. Toda operação de tratamento de dados pessoais DEVE ser documentada no inventário de dados (`docs/data-inventory.md`). |

#### Direitos dos titulares — Art. 18 (resposta obrigatória em até 15 dias)

- **Confirmação e acesso**: titular pode saber se e quais dados são tratados.
- **Correção**: dados incompletos, inexatos ou desatualizados devem ser corrigidos.
- **Anonimização, bloqueio ou eliminação**: dados desnecessários ou excessivos devem ser removidos.
- **Portabilidade**: fornecimento dos dados em formato estruturado, mediante regulamentação da ANPD.
- **Eliminação** (quando base = consentimento): eliminação dos dados tratados com consentimento, a qualquer momento.
- **Revogação do consentimento**: procedimento gratuito, simples e imediato.
- **Oposição**: titular pode se opor a tratamento baseado em legítimo interesse.

#### Segurança técnica obrigatória — Art. 46

- TLS 1.2+ em **todas** as comunicações com dados pessoais (APIs, webhooks, e-mail).
- Criptografia em repouso (AES-256) para dados pessoais persistidos.
- Autenticação multifator (MFA) para acesso a sistemas com dados pessoais.
- RBAC: controle de acesso baseado em função implementado e auditável.
- Logs de acesso a dados pessoais ativos, protegidos contra adulteração e retidos por mínimo 5 anos.
- Gestão de vulnerabilidades: patches de segurança aplicados em até 30 dias após publicação.
- Backup regular com teste periódico de restauração documentado.

#### Notificação de incidente — Resolução ANPD nº 15/2024

- Incidentes com risco relevante aos titulares DEVEM ser comunicados à **ANPD em até 3 dias úteis**.
- Titulares afetados DEVEM ser notificados em prazo razoável.
- Todo incidente DEVE gerar registro com: data de detecção, natureza, dados afetados, medidas de contenção e lições aprendidas.
- **Ausência de comunicação de incidente é infração sancionável** (ver: caso INSS/ANPD 2024).

#### Dados sensíveis — Art. 5º, II (proibição de tratamento sem base legal específica)

Este projeto NÃO deve tratar dados sensíveis (origem racial, saúde, biometria,
opinião política, religião, orientação sexual). Se um provider externo retornar
dados nessa categoria, eles DEVEM ser descartados antes de qualquer persistência.

#### Privacy by Design — Art. 46, §2º

- Proteção de dados DEVE ser incorporada na arquitetura desde o início, nunca
  adicionada como camada posterior.
- Todo novo endpoint que trate dados pessoais DEVE passar por RIPD antes do
  merge em `main`.
- Minimização de dados nos logs: IPs, e-mails e CPFs NUNCA devem aparecer
  em logs de aplicação sem pseudonimização.
- Dados pessoais NUNCA devem ser incluídos em mensagens de erro retornadas
  ao cliente.

#### Sanções (referência) — Art. 52

Descumprimento da LGPD sujeita a multa de até **2% do faturamento**, limitada a
**R$ 50 milhões por infração**, além de suspensão do tratamento e publicização
da infração. A ANPD já aplicou sanções em 2023 e 2024 (Telekall, INSS, SEEDF).

**Rationale**: Endereços são dados pessoais. O sistema correlaciona endereços
a perfis de risco financeiro e habitacional, o que cria responsabilidade legal
direta sob a LGPD. Conformidade técnica protege os titulares, a empresa e
assegura a operação contínua da plataforma.

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
  financial information MUST include a privacy and compliance note AND a
  RIPD (Relatório de Impacto à Proteção de Dados) in the feature specification,
  per LGPD Art. 38 and Constitution Principle VI. The data inventory at
  `docs/data-inventory.md` MUST be updated for any new data collection.
  Personal data in logs MUST be pseudonymised. Sensitive data (Art. 5º, II)
  MUST be discarded before persistence.

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

**Version**: 1.1.0 | **Ratified**: 2026-05-28 | **Last Amended**: 2026-05-30
