# GitHub Actions

The workflows provide two paths:

- `pull-request.yml` calls `quality-gates.yml` with read-only permissions. It
  builds and tests the .NET, Next.js, Angular, and infrastructure projects. It
  does not publish artifacts or push containers.
- `publish.yml` runs on pushes to `main` and version tags. It waits for the same
  quality gates, uploads the .NET and web build artifacts, then builds and pushes
  six images to GHCR. Images receive a commit SHA tag; version tags also receive
  a semantic-version tag.

GHCR publishing uses the repository-scoped `GITHUB_TOKEN`. Only the image job
receives `packages: write` permission. The pull-request workflow never logs in
to GHCR.

`quality-gates.yml` can also be run manually. It runs the same checks as the
pull-request and publish workflows.

The CI workflows do not create release manifests or run automated promotion or
rollback. The Week 6 deployment scripts still require a separately supplied
release manifest.
