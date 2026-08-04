# ADR 005: Establish the repository boundary before Week 4 CI

**Status:** Proposed — owner decision required before Week 4 workflow work  
**Date:** 2026-07-20  
**Scope:** Git ownership of the .NET API/worker, Next.js rendering lab, Angular OnPush demo, and future CI/deployment assets

## Context

The supplied training plan defines one anchor repository carried through all six weeks. The workspace currently has this shape:

```text
notification/
├── notification-api/       # the existing Git repository
├── rendering-lab/          # Next.js workspace sibling; outside that Git repository
└── angular-onpush-demo/     # Angular workspace sibling; outside that Git repository
```

The sibling locations intentionally preserve the project-owner request to place both frontend applications in the project root. They also create a delivery boundary: a workflow committed inside `notification-api/.github/workflows` cannot version, test, build, or publish the sibling applications. Local builds prove the applications work, but they cannot produce a single reviewable Week 4 commit or one pipeline for all three applications.

Changing the Git root can rewrite paths and history, while moving the applications back under `notification-api` would contradict the requested workspace layout. Neither operation should be inferred from the technical requirements.

## Decision drivers

- Preserve the requested root-level locations for the Next.js and Angular applications.
- Make the Week 4 build/test/container workflow capable of seeing every deliverable.
- Keep review, rollback, tags, and evidence tied to one immutable commit.
- Preserve existing backend history and remotes without an unreviewed rewrite.
- Avoid nested repositories, copied frontend trees, and CI scripts that reach outside their checkout.

## Considered options

| Option | Benefits | Costs and risks | Outcome |
| --- | --- | --- | --- |
| Keep the current nested backend Git root | No repository migration | Frontends remain untracked by the anchor repository; one CI checkout cannot build all deliverables | Rejected for Week 4 completion |
| Move both frontends under `notification-api` | Simple existing Git boundary | Reverses the explicit root-folder placement request | Not selected without owner approval |
| Create a new parent monorepo and import the backend history under `notification-api/` | Preserves root-level app layout and gives CI one commit boundary | Requires a reviewed history/import procedure and remote/branch coordination | **Recommended** |
| Use Git submodules or separate repositories | Independent histories and permissions | Contradicts the one-anchor-repository intent and adds cross-repository CI/version coordination | Rejected for this training project |

## Proposed decision

Before implementing Week 4 GitHub Actions, promote `notification/` to the anchor repository and retain these three top-level application folders. Import the existing backend history under `notification-api/` with a reviewed, recoverable procedure; then configure one solution/workspace validation command and one workflow rooted at `notification/`.

The migration must be performed only after the owner confirms the repository and remote strategy. Until then:

- do not move `.git`, rewrite history, or duplicate application folders;
- continue to run and record local validation in all three directories;
- treat GitHub PR, workflow, GHCR, and single-commit frontend evidence as pending;
- keep backend implementation/evidence inside `notification-api` and clearly identify sibling paths.

## Consequences

The recommended monorepo makes Week 4–6 CI, Compose, Vault, blue/green deployment, and final evidence reproducible from one checkout and one SHA. It also makes atomic changes across contracts and frontends possible.

The cost is a one-time repository migration that needs an agreed remote, default branch, history-retention method, and rollback point. Until that decision is made, the current code is locally buildable but the training plan's single-repository publication requirement is not satisfied.

## Validation

After approval and migration, verify all of the following from a fresh clone:

1. Backend history and tags are reachable and the remote/default branch are correct.
2. `dotnet build` and `dotnet test` cover the API, WorkerService, and tests.
3. `npm ci`, tests, and production builds run for both frontend applications.
4. Docker build contexts do not escape the checkout.
5. A pull-request workflow can validate all applications from one commit.

Revisit this record if the owner intentionally chooses separate repositories and updates the training-plan anchor-repository requirement.
