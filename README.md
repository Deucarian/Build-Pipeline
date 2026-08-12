# Deucarian Build Pipeline

`com.deucarian.build-pipeline` is an Editor-only Unity package for repeatable development and production builds. It keeps Build Profiles and project preparation project-owned while centralizing build policy, AOT and stripping safety, artifact evidence, and headless entry points.

Version 0.5.0 adds final-player assembly inspection, generated linker evidence, strict runtime-dynamic-code enforcement, stable registered target IDs, and machine-readable command results. The existing Newtonsoft preservation processor remains as a migration safety net, but strict mode deliberately reports reflection-based object mapping so application-owned runtime reflection can be removed instead of permanently hidden behind preservation rules.

## Install

Reference the stable package channel in `Packages/manifest.json`:

```json
"com.deucarian.build-pipeline": "https://github.com/Deucarian/Build-Pipeline.git#main"
```

Unity 6.0 or newer is required. The package contains Editor assemblies only and contributes no runtime assembly to the player. It depends directly on `com.deucarian.editor` 1.0.5, `com.deucarian.logging` 1.0.2, and Unity's Editor-only `com.unity.nuget.mono-cecil` package.

## Build Pipeline Manager

Open `Tools > Deucarian > Build Pipeline`. The manager discovers project providers through Unity `TypeCache`, presents their registered workflows, validates profile drift and project preflight rules, and dispatches builds through project-owned callbacks.

Registered workflows also integrate with Unity's native `File > Build Profiles` window. When a registered profile is active, Unity's **Build** and **Build And Run** buttons dispatch through the same project callback as the manager. The selected output path and build options are preserved, while Deucarian policy validation, project preparation, manifest generation, AOT inspection, and artifact validation remain active.

Use **Open in Unity** beside a registered profile to activate it and open Unity's Build Profiles window. Direct `BuildPipeline.BuildPlayer` calls for an active registered profile are rejected unless they run through `DeucarianBuildRunner`.

Profile changes are explicit:

- **Sync Profiles** creates or refreshes every profile registered by the selected project provider.
- **Apply Policy** updates only the selected profile with its environment policy.
- **Validate** checks policy drift and project preflight rules without changing assets.
- **Build** validates and then runs the selected project workflow.

## Project provider API

Implement `IDeucarianBuildManagerProvider` in an Editor assembly with a public parameterless constructor. A provider supplies a stable ID, display name, order, optional synchronization action, and immutable `DeucarianBuildManagerTarget` descriptors.

Each registered target provides:

- A stable target ID, label, and description.
- A project-owned Build Profile asset path.
- An environment and project-relative output path.
- Optional default `BuildOptions`.
- An optional side-effect-free project validation callback.
- An invocation-aware build callback returning `DeucarianBuildResult`.

The callback receives a `DeucarianBuildInvocation` containing the selected Build Profile, output path, additional options, invocation source, and requested AOT safety mode. It owns project preflight, temporary state, output cleanup, and project artifact validation. The manager, native Unity bridge, and CI registry all dispatch through this same callback.

## Core API

- `DeucarianBuildEnvironment`: `Development` or `Production`.
- `DeucarianAotSafetyMode`: `Inherit`, `Audit`, or `Enforce`.
- `DeucarianBuildRequest`: Build Profile, environment, output path, additional options, and AOT mode.
- `DeucarianBuildDispatcher`: validates and invokes registered targets consistently.
- `DeucarianBuildTargetRegistry`: lists, validates, resolves, and builds registered workflows through stable `provider/target` keys.
- `IDeucarianPlatformBuildPolicy`: applies settings, detects drift, and validates generated artifacts.
- `DeucarianBuildRunner.Build(request)`: validates and calls `BuildPipeline.BuildPlayer(BuildPlayerWithProfileOptions)`.
- `DeucarianBuildArtifactManifest`: artifact evidence, build identity, size budget, and AOT safety report.

The WebGL policy maps development to an inspectable build and production to Brotli, hashed filenames, data caching, High managed stripping, size-optimized IL2CPP, engine stripping, and WebAssembly 2023. Scenes, memory, rendering, quality assets, templates, identifiers, icons, and runtime content-loading behavior remain project-owned.

## AOT and stripping safety

Before UnityLinker removes managed code, Build Pipeline inspects the exact managed assemblies reported for that player build with Mono.Cecil. It reports runtime patterns whose targets cannot be proven through ordinary static reachability, including:

- Assembly and type discovery.
- `Activator.CreateInstance` and reflective invocation.
- Reflection-based Newtonsoft, System.Text.Json, XML, and data-contract object mapping.
- Runtime expression compilation.
- Unity string dispatch such as `SendMessage`, `Invoke(string)`, `StartCoroutine(string)`, and string-based component creation or lookup.

Editor-only reflection is not part of the player assemblies and is therefore outside this rule.

### Compiler-backed AOT diagnostics

The package ships `Deucarian.BuildPipeline.Analyzers.dll` as a standard Roslyn analyzer. Unity imports that asset with the `RoslynAnalyzer` label, so the same diagnostic IDs and messages are available to Unity compilation, the Unity Console, CI, and Roslyn-aware editors that consume Unity's generated C# projects. This is not a Rider plugin and contains no IDE-specific inspection API.

| ID | Default | Meaning | Action |
|---|---|---|---|
| `DBP1001` | Warning | Runtime type, member, or assembly discovery | Replace a constant lookup with `typeof(...)`, or introduce generated/explicit registration. |
| `DBP1002` | Warning | Reflective invocation or dynamic construction | Use a direct call/constructor, or an explicit factory keyed by closed types. |
| `DBP1003` | Error | Runtime IL/code generation such as `System.Reflection.Emit` | Replace it with generated or statically compiled code; linker preservation cannot make this AOT-safe. |
| `DBP1004` | Warning | Unity string dispatch | Use a strongly typed component, direct method call, or explicit delegate. |
| `DBP1005` | Warning | Runtime expression compilation | Generate or statically compile the path, or isolate an explicitly audited interpreter boundary. |

Warnings are the right migration default because source analysis cannot see every project compatibility boundary. `Enforce` remains the authoritative production gate: it inspects the compiled player assemblies and fails any remaining undeclared hazard even if a source diagnostic was suppressed. Teams can promote warning diagnostics to errors with normal Roslyn severity configuration, for example:

```ini
[*.cs]
dotnet_diagnostic.DBP1001.severity = error
dotnet_diagnostic.DBP1002.severity = error
dotnet_diagnostic.DBP1004.severity = error
dotnet_diagnostic.DBP1005.severity = error
```

The analyzer skips generated code plus assemblies that are recognizably Editor-only or test-only. A global analyzer configuration can explicitly set `deucarian_aot_analysis = enabled` or `disabled` when an assembly does not follow those conventions.

Quick actions are offered only when the replacement target and syntax are deterministic:

```csharp
Activator.CreateInstance<Widget>()  // becomes: new Widget()
Type.GetType("Example.Widget")      // becomes: typeof(Widget)
```

The direct-construction fix is offered only when the target has an accessible parameterless constructor (or a valid generic constructor constraint). The `typeof` fix is offered only when the constant type name resolves unambiguously. Runtime-selected factories, reflection loops, and string-dispatched methods receive actionable guidance but no speculative rewrite.

Build Pipeline owns these generic reflection/AOT diagnostics. It intentionally does not diagnose or rewrite Newtonsoft or other JSON APIs at source level. The future Serialization package owns JSON contract generation, serializer-specific diagnostics, and JSON-specific code fixes; the final-player verifier here remains the independent backstop.

### Modes

`Audit` records findings in `deucarian-build-manifest.json` without blocking the build. This is the migration mode and the default for projects without settings.

`Enforce` fails closed when:

- An unbounded runtime-dynamic call remains.
- A declared exception is incomplete.
- A generated preserve declaration references a missing assembly or type.
- The managed linker did not run the final assembly inspection.
- A project-owned `Assets/**/link.xml` file exists.

A command-line build can force enforcement without changing project settings:

```text
-deucarianAotMode Enforce
```

Projects can version a human-readable policy at `ProjectSettings/DeucarianAotSafety.json`:

```json
{
  "developmentMode": "Audit",
  "productionMode": "Enforce",
  "rejectManualProjectLinkXml": true,
  "preserveTypes": [],
  "exceptions": []
}
```

### Exact compatibility exceptions

Application-owned runtime code should be generated or explicitly composed. An exception is reserved for a framework or vendor boundary that cannot yet be rewritten. It must identify the exact assembly, declaring type, method, called API, strategy, and reason.

A `Declared` strategy also supplies exact hidden target types:

```json
{
  "assemblyName": "Vendor.Integration",
  "declaringType": "Vendor.Factory",
  "method": "Create",
  "calledApi": "System.Activator::CreateInstance",
  "strategy": "Declared",
  "reason": "Vendor SDK compatibility boundary.",
  "preserveTypes": [
    {
      "assemblyName": "Vendor.Runtime",
      "typeName": "Vendor.CallbackReceiver",
      "reason": "Constructed by the vendor boundary."
    }
  ]
}
```

Build Pipeline verifies every declared assembly and type against the final player inputs, then writes a deterministic descriptor under `Library/Deucarian/BuildPipeline/AotSafety`. Nobody maintains the resulting `link.xml` by hand.

`Generated` means normal direct calls were emitted and should usually leave no dynamic call to exempt. `Framework` is reserved for a narrowly audited framework boundary that owns its own AOT behavior.

### Package-owned evidence

Packages and source generators can carry neutral evidence in their compiled assembly without depending on Build Pipeline:

```csharp
[assembly: AssemblyMetadata(
    "Deucarian.AOT.Feature",
    "serialization-json")]

[assembly: AssemblyMetadata(
    "Deucarian.AOT.Exception",
    "Vendor.Factory|Create|System.Activator::CreateInstance|Declared|Vendor compatibility boundary.")]

[assembly: AssemblyMetadata(
    "Deucarian.AOT.PreserveType",
    "Vendor.Runtime|Vendor.CallbackReceiver|Constructed by the vendor boundary.")]
```

Build Pipeline reads this evidence from the final player assemblies, verifies it, generates linker input for exact declarations, and records the result in the build manifest.

## Newtonsoft migration boundary

Version 0.4.0 introduced automatic annotation-driven Newtonsoft preservation so current projects would stop failing silently under High stripping. That processor remains active during migration and generates its own deterministic descriptor.

Version 0.5.0 does not treat preservation as the final architecture. In `Enforce` mode, application-owned calls to reflection-based JSON object mapping remain findings. The intended end state is generated serialization with normal constructor and member calls, at which point no DTO preservation rule is needed.

Low-level JSON token parsing such as an explicitly implemented protocol codec is not treated as reflective object mapping.

## Target-aware command line

Use the public `Run` entry point for new automation.

List registered targets:

```text
-batchmode -quit \
-executeMethod Deucarian.BuildPipeline.DeucarianBuildCommandLine.Run \
-deucarianAction list \
-deucarianResult "Artifacts/build-targets.json"
```

Validate a target without building:

```text
-batchmode -quit \
-activeBuildProfile "Assets/Settings/Build Profiles/Web Production.asset" \
-executeMethod Deucarian.BuildPipeline.DeucarianBuildCommandLine.Run \
-deucarianAction validate \
-deucarianTarget "viewer/webgl-production" \
-deucarianAotMode Enforce \
-deucarianResult "Artifacts/validation-result.json"
```

Build through the registered project workflow:

```text
-batchmode -quit \
-activeBuildProfile "Assets/Settings/Build Profiles/Web Production.asset" \
-executeMethod Deucarian.BuildPipeline.DeucarianBuildCommandLine.Run \
-deucarianAction build \
-deucarianTarget "viewer/webgl-production" \
-deucarianOutput "Builds/WebGL/Production" \
-deucarianAotMode Enforce \
-deucarianResult "Artifacts/build-result.json"
```

`-deucarianOptions` accepts a comma-separated list of `BuildOptions` names. Target defaults and environment-required options are added automatically.

`-activeBuildProfile` is a Unity startup argument. It selects target-specific scripting defines before package and project scripts compile, so it must reference the profile registered by the chosen target. A target-only `-buildTarget` is a fallback, but `-activeBuildProfile` is canonical for Unity 6.

The profile-based `DeucarianBuildCommandLine.Build` entry point remains available for existing automation and now accepts `-deucarianAotMode` and `-deucarianResult` as optional arguments.

## CI and deployment boundary

Build Pipeline owns deterministic validation and player build execution. An external CI system should orchestrate separate Unity invocations for compilation, EditMode tests, PlayMode tests, player build, and stripped-player smoke testing.

The build output contains:

```text
player files
deucarian-build-manifest.json
```

The command writes a separate result JSON even on failure. Deployment should consume the already-tested immutable artifact after Unity exits. Hosting credentials, SSH/CDN clients, release promotion, health checks, and rollback remain outside the Unity package.

## Web deployment boundary

Production output uses Brotli with decompression fallback disabled. The external host must serve over HTTPS, send `Content-Encoding: br` for Brotli files, and send `application/wasm` for WebAssembly streaming compilation. This package does not inspect or modify hosting, CDN, deployment, or backoffice configuration.

## Production gates

The WebGL production gate rejects development options, drifted profiles, raw generated payload files, debug-symbol artifacts, development-context artifacts, non-hashed compressed payload names, and an encoded pre-engine bootstrap above 20 MiB. `StreamingAssets` is excluded because streamed project data loads after the engine boundary.
