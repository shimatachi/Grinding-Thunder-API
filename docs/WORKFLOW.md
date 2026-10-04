# Development Workflow

This document defines how coding agents should continue the project roadmap.

## Project type

Grinding Thunder is an ASP.NET Core 10 HTTP REST API backed by PostgreSQL through Entity Framework Core.

It is not:

- a desktop application;
- a GUI application;
- a CLI product;
- a Windows executable workflow;
- a webhook-only service.

The executable produced by .NET is the ASP.NET Core server host. Do not treat launching an `.exe` as a generic acceptance test.

The future public/admin frontend is separate from this backend.

## Source of truth

Before substantial work:

1. Read `AGENTS.md`.
2. Read `docs/ROADMAP.md` to determine current batch status.
3. Read `docs/DECISIONS.md` when historical reasoning matters.
4. Read only the current-state documentation relevant to the task:
   - `PRODUCT.md`
   - `DOMAIN.md`
   - `DATABASE.md`
   - `ARCHITECTURE.md`
5. Inspect the relevant implementation and tests.

Do not assume roadmap text proves implementation exists. Verify completed behavior against code/tests when necessary.

## Batch lifecycle

For each planned batch:

### 1. Assess

Before implementation, determine whether the previous completed batch needs additional verification.

Check for:

- failing tests;
- stale documentation;
- known unresolved defects;
- violated database invariants;
- migration/data-loss risk;
- mismatch between ROADMAP, DECISIONS, current-state docs, and code.

If a blocking correctness issue exists, fix and verify it before starting the next batch.

Do not create verification work merely for ceremony.

### 2. Plan

Produce a bounded implementation brief containing:

- Goal
- Current state
- Scope
- Out of scope
- Domain/database invariants
- Migration requirements if applicable
- Tests
- Acceptance criteria
- Verification

Do not implement future batches early.

### 3. Implement

Prefer surgical changes.

Do not introduce:

- generic repositories;
- UnitOfWork wrappers;
- CQRS;
- MediatR;
- AutoMapper;
- additional production projects;
- new dependencies;

unless the batch has a concrete requirement that justifies them.

### 4. Verify

Verification must be proportional to the change.

Always for production-code changes:

- `dotnet build`
- `dotnet test`

For EF/schema changes:

- inspect the generated migration;
- verify migration data preservation;
- apply/test the migration against the development PostgreSQL database when required.

For REST API/controller contract changes:

- start the ASP.NET Core API only when manual HTTP verification adds value;
- send requests to the affected endpoint(s);
- verify status codes and JSON responses.

Do not launch or test a generated `.exe` as though this were a desktop application.

Do not manually smoke-test unrelated endpoints.

For documentation-only changes, do not start the API unless there is another reason.

### 5. Review

Before marking a batch complete, report:

- changed files;
- behavior changes;
- schema/migration changes;
- tests added/changed;
- verification results;
- deferred work;
- anything implemented outside scope.

If a defect is found, report/fix it before advancing.

### 6. Document

After successful verification:

- update current-state docs that became inaccurate;
- append significant decisions/tradeoffs to `DECISIONS.md`;
- change the batch status in `ROADMAP.md` to Completed.

Do not document planned behavior as implemented.

### 7. Commit

Use one coherent commit per batch when practical.

Examples:

- `feat: model vehicle acquisition types`
- `refactor: version vehicle prerequisites`
- `docs: synchronize project documentation`

### 8. Advance

Determine the next Planned batch from `ROADMAP.md`.

Repeat from Assess.

## Prompt generation workflow

When Prompt Master is available, use it in Plan mode to:

1. read the project instructions and relevant documentation;
2. inspect the current batch status;
3. decide whether a blocking verification/fix is needed;
4. if blocked, generate a verification/fix prompt;
5. otherwise generate a bounded implementation prompt for the next batch.

Prompt Master should generate instructions for the coding agent; it should not silently expand the roadmap or implement unrelated future batches.

## Stop conditions

Stop and ask for human review before:

- destructive migration/data-loss choices;
- changing a documented domain invariant;
- adding major infrastructure/dependencies;
- changing roadmap direction substantially;
- making assumptions about War Thunder mechanics not documented in the project.
