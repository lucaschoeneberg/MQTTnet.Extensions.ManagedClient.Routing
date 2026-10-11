# Contributing to MQTTnet.Extensions.ManagedClient.Routing

Thank you for considering contributing to this project!

## Reporting issues

Please use the issue templates located in [.github/ISSUE_TEMPLATE](.github/ISSUE_TEMPLATE) when opening a new issue. Choose the template that best matches your request (bug report, feature request, question or other).

## Submitting pull requests

1. Fork the repository and create your branch from `master`.
2. Install the local gates once per clone: `lefthook install` (runs
   `gitleaks` secret scan and `dotnet format` check on every commit).
3. Add your changes with clear commit messages.
4. Ensure build and test suite pass warning-free by running
   `dotnet build` and `dotnet test` (CI builds with warnings as errors).
5. Open a pull request against this repository and describe your changes.

## Running the tests

The test suite can be executed using the [.NET CLI](https://docs.microsoft.com/dotnet/core/tools/). From the repository root run:

```bash
dotnet test
```

## Releasing a new version

Publishing to nuget.org is automated via Trusted Publishing (OIDC, no API
key) in `.github/workflows/publish.yml`, which runs when a GitHub Release
is published. One-time setup (package owner only):

1. nuget.org -> sign in -> username -> Trusted Publishing -> add policy
   (owner `lucaschoeneberg`, repo
   `MQTTnet.Extensions.ManagedClient.Routing`, workflow `publish.yml`,
   environment `release`).
2. Repo Settings -> Secrets and variables -> Actions -> repository secret
   `NUGET_USER` with the nuget.org profile name (not the email).

To release:

1. Add a `CHANGELOG.md` entry (no manual version bump: the version is
   derived from `v*` git tags via MinVer).
2. Commit, tag `vX.Y.Z`, push tag and create the GitHub Release.
3. The publish workflow builds, tests, verifies tag == packed version
   and pushes to nuget.org automatically (`--skip-duplicate`).


