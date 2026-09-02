# Deucarian Build Pipeline Agent Notes

Package ID: `com.deucarian.build-pipeline`

Canonical architecture standard:
https://github.com/Deucarian/Package-Registry/blob/main/ARCHITECTURE.md

## Ownership

This package owns editor-only Build Profile policies, build execution, managed-linker preservation policies, artifact manifests, provider contracts, generic build lifecycle contracts, ownership-safe output preparation, and the Build Pipeline Manager.

Registered capability: `build-pipeline`.

It must never own or name a consuming product, company application, scene, asset path, runtime context, content workflow, hosting system, or deployment repository. Product registration belongs in a consuming Editor package or project. Reusable domain packages may contribute product-neutral validation and reversible build preparation through `IDeucarianBuildLifecycleContributor`; they must select requests explicitly and remain free of consuming-product identifiers.

## Dependencies and policies

- Use `com.deucarian.editor` for every manager surface and menu convention.
- Use `com.deucarian.logging` for package diagnostics; direct `UnityEngine.Debug` calls are forbidden.
- Keep all assemblies editor-only and do not contribute code to player builds.
- Keep Build Profiles project-owned and modify them only through explicit synchronization or Apply Policy actions.
- Do not edit `Library/PackageCache` or copy shared Editor helpers locally.
- Work on `develop`; stable `main` changes are promotion-only.

## Validation

- Run Unity EditMode tests on Unity 6000.0 and the current Unity 6000 line.
- Run the shared Package Registry validator with `deucarian-package.json`.
- Verify source files contain no consumer-specific identifiers.
- Run `git diff --check` before committing.
