<p align="center"><img src="docs/logo.svg" width="96" alt="" /></p>

# pkgdrift

pkgdrift is a cross-platform .NET command-line tool for finding NuGet version drift and project-file hygiene issues across repositories with many projects and no central package management.

[![CI](https://github.com/Gulliby-development/pkgdrift/actions/workflows/ci.yml/badge.svg)](https://github.com/Gulliby-development/pkgdrift/actions/workflows/ci.yml) [![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Why

When a solution grows to dozens of `.csproj` files without central package management, the same package ends up at several versions, some floating or prerelease. pkgdrift scans the tree in one pass and lists every mismatch and a few project-file hygiene problems, so you can fix them before they surprise a build.

## Install

Requires the .NET 10 SDK. Build from the repository root:

```sh
dotnet build
```

Run the tool through `dotnet run` (the arguments after `--` go to pkgdrift):

```sh
dotnet run --project src/Pkgdrift -- <dir>
```

or run the built binary directly (the assembly name is `pkgdrift`):

```sh
dotnet src/Pkgdrift/bin/Debug/net10.0/pkgdrift.dll <dir>
```

The usage examples below write `pkgdrift` for either form.

## Usage

```
pkgdrift [--format text|json] [--disable RULE]... [--ignore DIR]... DIR
```

Scan recursively for `.csproj` files. The scanner ignores `bin`, `obj`, `node_modules`, and `.git` directories, plus any directory named by `--ignore`, matched by name at any depth and ignoring case, and does not follow directory symbolic links. Paths in reports are relative to the scan directory.

Examples:

```sh
pkgdrift .
pkgdrift --format json ./src
pkgdrift --disable PD005 --disable PD004 .
pkgdrift --ignore samples --ignore legacy .
```

## Rules

| Rule | Detects |
| --- | --- |
| PD001 | A package version that differs from the first versioned reference for that package. Package names are compared without regard to case. |
| PD002 | A floating package version containing `*`. |
| PD003 | A later duplicate package reference in the same project. |
| PD004 | A project with neither `TargetFramework` nor `TargetFrameworks`, unless a `Directory.Build.props` exists under the scan directory. |
| PD005 | A prerelease version matching the numeric version and hyphenated suffix pattern. |

Disable rules by repeating `--disable RULE`. Rule IDs are case-sensitive; disabling a rule hides its findings but does not change version baselines.

## Ignoring directories

`--ignore NAME` skips every directory called NAME (any depth, case-insensitive) in addition to the built-in ones, so sample or vendored projects do not produce findings; unlike `--disable`, real projects are still checked by every rule.

For the app project and three Serilog 2.0.0 projects under `samples/demo`, `src/samples`, and `vendor/Samples`, the default scan prints six finding lines and this summary:

```text
samples/demo/demo.csproj:1: PD004 no TargetFramework or TargetFrameworks
samples/demo/demo.csproj:3: PD001 Serilog 2.0.0 differs from 3.1.0 in app/app.csproj
src/samples/inner.csproj:1: PD004 no TargetFramework or TargetFrameworks
src/samples/inner.csproj:3: PD001 Serilog 2.0.0 differs from 3.1.0 in app/app.csproj
vendor/Samples/vend.csproj:1: PD004 no TargetFramework or TargetFrameworks
vendor/Samples/vend.csproj:3: PD001 Serilog 2.0.0 differs from 3.1.0 in app/app.csproj
6 problems in 3 projects
```

## Example

Given `app/app.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
    <PackageReference Include="Serilog" Version="*" />
  </ItemGroup>
</Project>
```

and `lib/lib.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="12.0.3" />
    <PackageReference Include="Serilog" Version="3.1.0-beta.2" />
  </ItemGroup>
</Project>
```

Text output:

```text
app/app.csproj:7: PD002 floating version * of Serilog
lib/lib.csproj:1: PD004 no TargetFramework or TargetFrameworks
lib/lib.csproj:3: PD001 Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj
lib/lib.csproj:4: PD001 Serilog 3.1.0-beta.2 differs from * in app/app.csproj
lib/lib.csproj:4: PD005 prerelease version 3.1.0-beta.2 of Serilog
5 problems in 2 projects
```

## JSON output

Use `--format json` for one compact JSON line. The object contains the scanned project count, ordered findings, and total problem count:

```sh
pkgdrift --format json .
```

For the example projects above, the output is this single line (followed by a line feed):

```json
{"projects":2,"findings":[{"path":"app/app.csproj","line":7,"rule":"PD002","message":"floating version * of Serilog"},{"path":"lib/lib.csproj","line":1,"rule":"PD004","message":"no TargetFramework or TargetFrameworks"},{"path":"lib/lib.csproj","line":3,"rule":"PD001","message":"Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj"},{"path":"lib/lib.csproj","line":4,"rule":"PD001","message":"Serilog 3.1.0-beta.2 differs from * in app/app.csproj"},{"path":"lib/lib.csproj","line":4,"rule":"PD005","message":"prerelease version 3.1.0-beta.2 of Serilog"}],"problems":5}
```

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Scan completed with no findings. |
| 1 | Findings were reported. |
| 2 | Invalid arguments, missing or invalid scan directory, unknown rule, invalid `--ignore` value, or malformed project XML. |

## Development

Run the build, formatting check, and test suite from the repository root:

```sh
dotnet build
dotnet format --verify-no-changes
dotnet test
```

## License

MIT, see [LICENSE](LICENSE).

---

Built with BAKED, an open-source coding agent (bench loop round 7, BAKED 5c374a7).
