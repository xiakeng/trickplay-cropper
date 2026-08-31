# Jellyfin plugin contract for Trickplay Cropper

Status date: 2026-09-01

## Decision

Trickplay Cropper v1 must target Jellyfin Server **10.11.11** only, compile for **`net9.0`**, and use the Jellyfin 10.11 plugin ABI **`10.11.0.0`**. Compile against exact `10.11.11` Jellyfin packages, use the SkiaSharp version shipped by that server, and produce a flat, manually installable plugin ZIP containing `meta.json` and `Jellyfin.Plugin.TrickplayCropper.dll`.

The compatibility baseline is:

| Concern | Pinned contract |
| --- | --- |
| Supported and tested server | Jellyfin Server `10.11.11` |
| Target framework / CI SDK | `net9.0` / .NET SDK `9.0.x` |
| Plugin ABI in `build.yaml` | `10.11.0.0` |
| Jellyfin packages | `Jellyfin.Controller` `10.11.11`; `Jellyfin.Model` `10.11.11` |
| ASP.NET surface | `FrameworkReference` to `Microsoft.AspNetCore.App` |
| Image library | `SkiaSharp` `3.116.1`, compile-time only; use the copy and native assets shipped by Jellyfin |
| Initial plugin/assembly version | `1.0.0.0` |
| Package contents | `meta.json`; `Jellyfin.Plugin.TrickplayCropper.dll` |

## Supported Jellyfin release

The official GitHub `latest` release is [Jellyfin 10.11.11](https://github.com/jellyfin/jellyfin/releases/tag/v10.11.11), published on 2026-06-06. The newer 12.0 tags are release candidates rather than stable releases, so they are not the v1 target.

At the `v10.11.11` tag, both [`Jellyfin.Controller` declares package version `10.11.11` and targets `net9.0`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/MediaBrowser.Controller/MediaBrowser.Controller.csproj#L8-L14), and [`Jellyfin.Model` declares the same package version and target](https://github.com/jellyfin/jellyfin/blob/v10.11.11/MediaBrowser.Model/MediaBrowser.Model.csproj#L8-L24). The plugin must therefore pin the package patch version rather than using a `10.*-*` floating range.

`targetAbi` and the tested server version have different meanings. The official Jellyfin Reports build for the 10.11 line uses [`targetAbi: "10.11.0.0"` with `framework: "net9.0"`](https://github.com/jellyfin/jellyfin-plugin-reports/blob/b4f7ab463f5b0dba3f97ca7212eb532b21a6e451/build.yaml#L1-L13). Jellyfin treats `targetAbi` as a minimum version check (`server version >= target ABI`), not as an exact-version or maximum-version constraint, in [`PluginManager.LoadManifest`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/Plugins/PluginManager.cs#L667-L703). Consequently, the manifest follows the official 10.11 ABI value, while the project support statement and CI/manual smoke test remain explicitly limited to Server 10.11.11. A later Jellyfin release may load the assembly, but is unsupported until deliberately re-pinned and tested.

### Template warning

The official plugin template is useful for structure but must not be copied verbatim for version pins. At its current commit, its project targets `net9.0` but still references Jellyfin `10.9.11`, while its [`build.yaml` still says ABI `10.9.0.0` and `net8.0`](https://github.com/jellyfin/jellyfin-plugin-template/blob/7a9dbdafcced0bf6ccf1ca5aa404e404c76d5b04/build.yaml#L1-L14); its README separately demonstrates Jellyfin `10.11.3`. The README itself says package versions must match the installed server and that Jellyfin runtime assets must be excluded ([package setup](https://github.com/jellyfin/jellyfin-plugin-template/blob/7a9dbdafcced0bf6ccf1ca5aa404e404c76d5b04/README.md#L38-L66)). Use the structural conventions, but use the pins in this decision.

## Project and package references

The production project contract is:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <RootNamespace>Jellyfin.Plugin.TrickplayCropper</RootNamespace>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Jellyfin.Controller" Version="10.11.11">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="Jellyfin.Model" Version="10.11.11">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="SkiaSharp" Version="3.116.1">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
```

Reasons:

- The Jellyfin packages provide `ITrickplayManager`, plugin APIs, application paths, scheduled-task types, and other server contracts. They are host assemblies and must not be copied into the plugin ZIP. The official template uses `ExcludeAssets=runtime` for exactly this reason ([template project](https://github.com/jellyfin/jellyfin-plugin-template/blob/7a9dbdafcced0bf6ccf1ca5aa404e404c76d5b04/Jellyfin.Plugin.Template/Jellyfin.Plugin.Template.csproj#L13-L20)). NuGet defines `runtime` assets as the assemblies and runtime-specific files copied to output ([PackageReference asset rules](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#controlling-dependency-assets)).
- The explicit `Microsoft.AspNetCore.App` framework reference is the established 10.11 plugin pattern for an MVC controller; the official Reports plugin combines `net9.0`, `Jellyfin.Controller`, and that framework reference ([Reports project](https://github.com/jellyfin/jellyfin-plugin-reports/blob/b4f7ab463f5b0dba3f97ca7212eb532b21a6e451/Jellyfin.Plugin.Reports/Jellyfin.Plugin.Reports.csproj#L1-L27)).
- Server 10.11.11 pins [`SkiaSharp` to `3.116.1`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Directory.Packages.props#L72-L80), its [Skia drawing project references the managed and native Skia packages](https://github.com/jellyfin/jellyfin/blob/v10.11.11/src/Jellyfin.Drawing.Skia/Jellyfin.Drawing.Skia.csproj#L19-L27), and [Jellyfin.Server includes that project](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Server/Jellyfin.Server.csproj#L61-L69). Trickplay Cropper must compile against that exact managed API and exclude its runtime assets. This avoids shipping a second managed SkiaSharp assembly or platform-specific native assets and honors the decision not to maintain a separate OS/CPU matrix.
- Jellyfin's plugin load context first resolves plugin-local dependencies and returns `null` when they are absent ([`PluginLoadContext`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/Plugins/PluginLoadContext.cs)); .NET then falls back to the default load context ([Microsoft `AssemblyLoadContext` loading behavior](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability#create-a-collectible-assemblyloadcontext)). Thus the compile-only Skia reference resolves to Jellyfin's loaded copy. The release smoke test must exercise one real decode to catch any host packaging regression.

Do not add `SkiaSharp.NativeAssets.*`, `Jellyfin.Common`, `Jellyfin.Data`, or `Jellyfin.Api` as direct runtime artifacts. `Jellyfin.Controller` brings its Jellyfin contract dependencies, and the server owns their runtime copies. `Jellyfin.Api` is not published as the plugin controller surface; plugins use ASP.NET `ControllerBase`.

Analyzer packages are development-only and are not part of the runtime contract. If adopted, mark them `PrivateAssets="all"` and pin them separately.

## Plugin identity, manifest, and ZIP

Use `build.yaml` as the package source of truth:

```yaml
---
name: "Trickplay Cropper"
guid: "<one generated UUID, kept stable forever>"
version: "1.0.0.0"
targetAbi: "10.11.0.0"
framework: "net9.0"
overview: "Return cropped trickplay preview images through an authenticated API."
description: "Provides authenticated single-frame JPEG previews from Jellyfin-generated trickplay sprites."
category: "General"
owner: "xiakeng"
artifacts:
  - "Jellyfin.Plugin.TrickplayCropper.dll"
changelog: "Initial v1 release."
```

The implementation issue must generate the UUID once. The same UUID must be returned by `Plugin.Id`, as illustrated by the official template's [`Plugin` class](https://github.com/jellyfin/jellyfin-plugin-template/blob/7a9dbdafcced0bf6ccf1ca5aa404e404c76d5b04/Jellyfin.Plugin.Template/Plugin.cs#L12-L33). The assembly, file, and package versions must all be `1.0.0.0` for the first artifact and must advance together for later artifacts. There is no configuration page and therefore no embedded dashboard HTML or `IHasWebPages` implementation.

The package builder generates `meta.json` from `build.yaml`. Jellyfin's manifest model recognizes the name, GUID, version, target ABI, owner, overview, description, category, timestamp, status, and optional assembly whitelist ([`PluginManifest`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/MediaBrowser.Common/Plugins/PluginManifest.cs#L11-L115)). Jellyfin reads `meta.json` from a plugin directory and evaluates its target ABI on startup ([manifest loading](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/Plugins/PluginManager.cs#L667-L703)).

The ZIP must be flat at its root:

```text
trickplay-cropper_1.0.0.0.zip
├── Jellyfin.Plugin.TrickplayCropper.dll
└── meta.json
```

This matches the official 10.11 packaging contract: the [official Reports manifest entry](https://repo.jellyfin.org/files/plugin/manifest.json) points to a [10.11 Reports ZIP](https://repo.jellyfin.org/files/plugin/reports/reports_18.0.0.0.zip) whose root contains `meta.json`, the plugin DLL, and only the explicitly listed third-party DLLs. Trickplay Cropper has no plugin-owned runtime dependency because SkiaSharp and Jellyfin assemblies are host-provided, so its `artifacts` list contains only its own DLL.

For manual installation, extract the two root files into one dedicated direct child directory under Jellyfin's plugins directory, then restart Jellyfin. Do not put an additional archive directory layer inside that plugin directory. The server enumerates direct child directories, reads their manifests, and loads their DLLs ([discovery](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/Plugins/PluginManager.cs#L738-L813)).

## Controller discovery and dependency injection

The controller contract is:

- Define a `public`, concrete, non-generic type deriving from ASP.NET `ControllerBase`.
- Apply `[ApiController]`, `[Authorize]`, and an explicit route matching `TrickplayCropper/Videos/{ItemId}/Preview`; expose the action with `[HttpGet]`.
- Keep authentication on the controller/action itself. Do not rely on route placement or a custom token parameter.
- Use constructor injection for Jellyfin interfaces and plugin-owned services.
- Do not register the controller with MVC and do not call `AddControllers` from the plugin.

Jellyfin scans exported public concrete types from loaded plugin assemblies ([type discovery](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/ApplicationHost.cs#L692-L730)), selects assemblies containing a `ControllerBase` subclass ([API plugin assembly discovery](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/ApplicationHost.cs#L950-L962)), adds those assemblies as MVC application parts, and registers controllers as services ([MVC setup](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs#L114-L166)). The official Reports controller demonstrates `[ApiController]`, `[Route]`, `[Authorize]`, `ControllerBase`, and constructor injection in a plugin ([Reports controller](https://github.com/jellyfin/jellyfin-plugin-reports/blob/b4f7ab463f5b0dba3f97ca7212eb532b21a6e451/Jellyfin.Plugin.Reports/Api/ReportsController.cs#L14-L31)).

Plugin-owned services must be registered through one public, parameterless `IPluginServiceRegistrator`. Its `RegisterServices(IServiceCollection, IServerApplicationHost)` method adds those services before the service provider is constructed; the interface explicitly requires a parameterless constructor ([contract](https://github.com/jellyfin/jellyfin/blob/v10.11.11/MediaBrowser.Controller/Plugins/IPluginServiceRegistrator.cs#L1-L18)), and the server discovers and invokes it ([registration](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/Plugins/PluginManager.cs#L201-L227)). This registrator is for the crop/cache services, not for the controller itself.

## Scheduled-task registration

Implement cache cleanup as a `public sealed` concrete class implementing `IScheduledTask`. It must provide stable `Name`, `Key`, `Description`, and `Category` values, `ExecuteAsync(IProgress<double>, CancellationToken)`, and `GetDefaultTriggers()`; these are the complete interface members in Server 10.11.11 ([`IScheduledTask`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/MediaBrowser.Model/Tasks/IScheduledTask.cs#L11-L48)). Use:

```csharp
public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
{
    yield return new TaskTriggerInfo
    {
        Type = TaskTriggerInfoType.DailyTrigger,
        TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
    };
}
```

Recommended stable metadata is:

```text
Name: Clear Trickplay Cropper Cache
Key: ClearTrickplayCropperCache
Description: Delete preview images created by Trickplay Cropper.
Category: Trickplay Cropper
```

No explicit scheduled-task DI registration is needed. At startup Jellyfin instantiates every discovered `IScheduledTask` through DI and adds it to the task manager ([discovery and startup registration](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/ApplicationHost.cs#L347-L412)). A Jellyfin built-in task shows the same `DailyTrigger`/`TimeOfDayTicks` shape ([example](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/ScheduledTasks/Tasks/ChapterImagesTask.cs#L59-L83)). The trigger uses `DateTime.Now`, so `03:00` means server-local time ([`DailyTrigger.Start`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Emby.Server.Implementations/ScheduledTasks/Triggers/DailyTrigger.cs#L35-L48)).

The task may receive a plugin-owned cache service through constructor injection, provided that service is registered by `IPluginServiceRegistrator`. `ExecuteAsync` must honor cancellation and report progress; exception/logging behavior belongs to the implementation design.

## CI build and artifact contract

CI runs on pull requests to and pushes on `main`. It must use an Ubuntu GitHub-hosted runner as a build environment, not as a statement that the plugin only supports Linux.

Required gates:

1. Check out the repository and install .NET SDK `9.0.x`.
2. Run `dotnet restore`.
3. Run `dotnet build --configuration Release --no-restore`; warnings are errors in the plugin project.
4. Run all unit and controller/component tests in Release configuration. A live Jellyfin server is not required in CI.
5. Package with the same JPRM action and `dotnet-target: net9.0` used by Jellyfin's official reusable build workflow.
6. Upload the resulting ZIP as a workflow artifact and fail when no artifact is produced.
7. Inspect the ZIP in CI: exactly the plugin DLL and `meta.json` must be present; `meta.json` must contain the stable GUID, version `1.0.0.0`, and `targetAbi` `10.11.0.0`. Fail if `Jellyfin.*`, `SkiaSharp*`, native assets, PDBs, or a nested top-level directory are packaged.

Jellyfin's official reusable test workflow establishes .NET `9.0.x`, restore, Release build, and test as the current baseline ([test workflow](https://github.com/jellyfin/jellyfin-meta-plugins/blob/2d1d8651c878e11ce83de5ecdbedb31e70ebc6f0/.github/workflows/test.yaml#L1-L29)). Its build workflow uses .NET `9.0.x`, JPRM with `net9.0`, a commit-pinned JPRM action, and artifact upload with `if-no-files-found: error` ([build workflow](https://github.com/jellyfin/jellyfin-meta-plugins/blob/2d1d8651c878e11ce83de5ecdbedb31e70ebc6f0/.github/workflows/build.yaml#L1-L39)). The target repository may call those reusable workflows as the official template does, but a repo-local workflow should pin third-party action commits equivalently and must retain the explicit ZIP validation above.

## Implementation handoff checklist

- Pin server support, package references, and manual smoke testing to 10.11.11.
- Target `net9.0`; use .NET SDK `9.0.x` in CI.
- Use `targetAbi: 10.11.0.0`; do not interpret it as an exact or maximum server version.
- Generate one plugin UUID and use it unchanged in `build.yaml`, `Plugin.Id`, tests, and artifact validation.
- Exclude runtime assets for Jellyfin packages and SkiaSharp; package only the plugin DLL plus generated `meta.json`.
- Let Jellyfin discover `ControllerBase` and `IScheduledTask` types; use `IPluginServiceRegistrator` only for plugin-owned services.
- Default the cleanup task to server-local 03:00 with `DailyTrigger`.
- Build, test, package, inspect, and upload the ZIP in CI; retain a real-server 10.11.11 install/API/decode smoke test as a release checklist item.

## Scope boundary

This decision pins the Jellyfin host/plugin contract. It does not resolve authenticated item/media-source authorization flow, the exact `ITrickplayManager` call sequence, Skia partial-decode behavior, cache concurrency, or API error mapping; those belong to their dedicated Wayfinder tickets.
