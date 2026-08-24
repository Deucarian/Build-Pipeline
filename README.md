# Deucarian Build Pipeline

`com.deucarian.build-pipeline` is an editor-only Unity package for repeatable development and production builds. It keeps Build Profiles project-owned while centralizing the settings that should be consistent across Deucarian projects.

Version 0.4.0 provides a single provider-driven Build Pipeline Manager with a static idle surface and debounced project-change validation. Registered Build Profiles also route Unity's native Build and Build And Run buttons through the same project callback. Target-specific Newtonsoft.Json contracts are preserved automatically when managed stripping runs. The public policy and provider interfaces are platform-neutral so Windows, Android, and iOS policies can be added without redesigning project integrations.

## Install

Reference the stable package channel in `Packages/manifest.json`:

```json
"com.deucarian.build-pipeline": "https://github.com/Deucarian/Build-Pipeline.git#main"
```

Unity 6.0 or newer is required. The package contains Editor assemblies only and contributes nothing to a player build. It depends directly on `com.deucarian.editor` 1.0.5, `com.deucarian.logging` 1.0.2, and Unity's Editor-only `com.unity.nuget.mono-cecil` package.

## Build Pipeline Manager

Open `Tools > Deucarian > Build Pipeline`. The manager discovers project providers through Unity `TypeCache`, presents their registered workflows, validates profile drift and project preflight rules, and dispatches builds through project-owned callbacks.

Registered workflows also integrate with Unity's native `File > Build Profiles` window.
When a registered profile is active, Unity's **Build** and **Build And Run** buttons
dispatch through the same project callback as the manager. The selected output path and
build options are preserved, while Deucarian policy validation, project preparation,
manifest generation, and artifact validation remain active. A one-time notice explains
the integration; unrelated profiles continue through Unity's default build behavior.

Use **Open in Unity** beside a registered profile to activate it and open Unity's Build
Profiles window. Direct `BuildPipeline.BuildPlayer` calls for an active registered profile
are rejected unless they run through `DeucarianBuildRunner`.

The manager also includes a `Custom Build Profile` mode. Select a profile, environment, and project-relative output path to apply a policy explicitly, validate it, or build directly through `DeucarianBuildRunner`.

Profile changes are always explicit. Sync Profiles and Apply Policy ask for confirmation because they modify version-controlled Build Profile assets. Ordinary validation and build actions do not silently edit profiles.

The manager distinguishes the four actions directly in the window:

- **Sync Profiles** creates or refreshes every profile registered by the selected project provider.
- **Apply Policy** updates only the selected profile with its environment policy.
- **Validate** checks policy drift and project preflight rules without changing assets.
- **Build** validates and then runs the selected build workflow.

The first two actions modify version-controlled profile assets. The normal build path is Validate, then Build.

## Project provider API

Implement `IDeucarianBuildManagerProvider` in an Editor assembly with a public parameterless constructor. A provider supplies a stable ID, display name, order, optional synchronization action, and immutable `DeucarianBuildManagerTarget` descriptors.

Each registered target provides:

- A stable target ID, label, and generic description.
- A project-owned Build Profile asset path.
- An environment and project-relative output path.
- Optional default `BuildOptions` used by the manager and programmatic default builds.
- An optional side-effect-free project validation callback.
- An invocation-aware build callback returning `DeucarianBuildResult`.

The callback receives a `DeucarianBuildInvocation` containing the selected Build Profile,
output path, additional options, and invocation source. It owns project preflight,
temporary state, output cleanup, and project artifact validation. The manager and Unity
native bridge never bypass it.

## Core API

- `DeucarianBuildEnvironment`: `Development` or `Production`.
- `DeucarianBuildRequest`: Build Profile, environment, output path, and additional build options.
- `DeucarianBuildDispatcher`: validates and invokes registered targets consistently from the manager, Unity Build Profiles, CI, or project code.
- `IDeucarianPlatformBuildPolicy`: applies settings, detects drift, and validates generated artifacts.
- `DeucarianBuildRunner.Build(request)`: validates and calls `BuildPipeline.BuildPlayer(BuildPlayerWithProfileOptions)`.
- `DeucarianBuildArtifactManifest`: artifact paths and encoded/raw sizes, versions, build GUID, duration, settings fingerprint, and budget result.

The WebGL policy maps development to an inspectable build and production to Brotli, hashed filenames, data caching, High managed stripping, size-optimized IL2CPP, engine stripping, and WebAssembly 2023. Build And Run supplies `AutoRunPlayer` through its invocation instead of making every development build launch automatically. The policy leaves scenes, memory, rendering and quality assets, templates, identifiers, icons, and runtime content-loading behavior project-owned.

Package-owned WebGL template sources can be synchronized into Unity's required
project location with `DeucarianWebGLTemplateUtility.SynchronizePackageTemplate`.
Consumer providers remain responsible for choosing the template: call
`ApplyTemplate` while synchronizing each project-owned WebGL Build Profile and
compose `ValidateTemplate` into the provider's project validation callback.

## Newtonsoft.Json and managed stripping

Before Unity's managed linker runs, the package inspects the exact target player assemblies with Mono.Cecil. Types carrying Newtonsoft.Json serialization attributes, supported `System.Runtime.Serialization` contract attributes, and types referenced by those attributes are written to a deterministic descriptor under `Library/Deucarian/BuildPipeline/NewtonsoftLinker`. Each discovered contract uses `preserve="all"`, protecting constructors, accessors, fields, callbacks, and converters used through reflection. Missing or unreadable linker input stops the build instead of producing a potentially broken player.

Automatic discovery is annotation-driven: every application POCO serialized or deserialized through Newtonsoft.Json must declare `[JsonObject]` or at least one Newtonsoft serialization attribute. Compiled code does not expose enough information to infer arbitrary objects passed dynamically to Json.NET. The scan covers target script assemblies that Unity classifies as `ManagedLibrary`; precompiled dependency contracts, unannotated contracts, and dynamic contracts still require their package or project to provide an explicit `link.xml` rule. An installed project that does not use Newtonsoft.Json produces a valid empty linker descriptor.

## Command line

The command-line API remains stable:

```text
-batchmode -quit \
-activeBuildProfile "Assets/Settings/Build Profiles/Web Production.asset" \
-executeMethod Deucarian.BuildPipeline.DeucarianBuildCommandLine.Build \
-deucarianProfile "Assets/Settings/Build Profiles/Web Production.asset" \
-deucarianEnvironment Production \
-deucarianOutput "Builds/WebGL/Production"
```

`-activeBuildProfile` is a Unity startup argument: it selects the target and profile-specific scripting defines before package and project scripts compile. `-deucarianProfile` tells this package which profile to validate and build, so both arguments must name the same asset. A target-only `-buildTarget WebGL` is a valid fallback, but `-activeBuildProfile` is the canonical Unity 6 invocation. The runner also rejects target mismatches with this guidance before starting a build.

Optional `-deucarianOptions` accepts a comma-separated list of `BuildOptions` names. Environment-required options are added by the runner.

## Web deployment boundary

Production output uses Brotli with decompression fallback disabled. The external host must serve over HTTPS, send `Content-Encoding: br` for Brotli files, and send `application/wasm` for WebAssembly streaming compilation. This package does not inspect or modify hosting, CDN, deployment, or backoffice configuration.

## Production gates

The WebGL production gate rejects development options, drifted profiles, raw generated payload files, debug-symbol artifacts, development-context artifacts, non-hashed compressed payload names, and an encoded pre-engine bootstrap above 20 MiB. `StreamingAssets` is excluded because streamed project data loads after the engine boundary.

For startup benchmarking, use seven cold evergreen-Chromium runs at 20 Mbps, 40 ms RTT, and 4x CPU throttling. Compare median `page-start` to `engine-ready`; post-engine content belongs to a separate project-owned measurement. A production candidate must improve the preserved baseline by at least 40%.

Before changing production IL2CPP from Optimize Size to Faster Runtime, compare a representative workload over the same scripted 30-second interaction sequence. Switch only when Optimize Size increases p95 frame time by more than 5%.
