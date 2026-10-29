# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 1.5.x   | :white_check_mark: |
| < 1.5   | :x: |

Only the latest released minor line receives security fixes. The library targets
`net8.0`, `net9.0`, and `net10.0`; the CLI ships as framework-dependent bundles for
Windows, Linux, and macOS (x64/arm64).

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Use GitHub's private vulnerability reporting:

<https://github.com/purelogiccode/CHDSharp/security/advisories/new>

If you cannot use that form, contact the maintainer through the repository's
[security page](https://github.com/purelogiccode/CHDSharp/security) and a private
channel will be arranged.

Please include:

- the affected version, target framework, and OS/architecture;
- a minimal reproducer (input file, command line, or API call sequence);
- the observed impact and any known mitigations;
- whether you are willing to be credited in the advisory.

## What to expect

- **Acknowledgement** within 3 business days.
- **Triage** within 10 business days, including a severity assessment (CVSS).
- **Fix and advisory** coordinated with you; credit is given unless you prefer to
  remain anonymous.
- **Disclosure** after a patched release is available, or after 90 days, whichever
  comes first, unless an extension is agreed.

## Scope

In scope:

- `CHDSharpLib` (NuGet package `CHDSharp`), `CHDSharpCli`, and the shipped bundles;
- memory-safety issues in the vendored codec ports (`VendoredZLib`, `VendoredLZMA`,
  `VendoredFlac`, `VendoredZSTD`);
- denial of service via malformed CHD/CUE/GDI/AVI input;
- path traversal or arbitrary file write during extraction/creation;
- supply-chain issues in the build, test, and release workflows.

Out of scope:

- vulnerabilities in MAME `chdman` itself (report them upstream);
- issues that require a malicious local user with write access to the input files
  and the output directory;
- missing hardening that has no demonstrated impact;
- social engineering and spam.

## Security measures in this repository

- **CodeQL** code scanning for C# and GitHub Actions on every push to `main`, every
  pull request, and weekly (`.github/workflows/codeql.yml`).
- **Dependabot** alerts, security updates, and weekly version-update PRs for NuGet
  and GitHub Actions (`.github/dependabot.yml`).
- **Secret scanning** with push protection enabled for the repository.
- **Private vulnerability reporting** enabled (the link above).
- **Dependency review** on pull requests, failing on moderate-or-higher advisories
  (`.github/workflows/dependency-review.yml`).
- **OpenSSF Scorecard** results published to the code-scanning dashboard
  (`.github/workflows/scorecard.yml`).
- **NuGet audit** of direct and transitive packages during restore/build
  (`NuGetAuditMode=all` in `Directory.Build.props`); CI builds with
  `-warnaserror`, so a known vulnerable package fails the build.
- **ReSharper InspectCode** runs in CI and uploads SARIF to code scanning.
- GitHub Actions are pinned to full commit SHAs; the repository requires SHA
  pinning for all actions.
- Release bundles are built from a version tag and attested with GitHub artifact
  provenance (`actions/attest-build-provenance`).

## Verifying a release

```bash
gh attestation verify CHDSharp_win-x64_v1.5.2.zip --repo purelogiccode/CHDSharp
```

NuGet packages are published from the tag workflow; compare the package version,
the release tag, and `Directory.Build.props`.
