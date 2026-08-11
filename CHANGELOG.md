# Changelog

## 0.4.0 - 2026-08-11

- Added target-aware Newtonsoft.Json and data-contract discovery for the exact player script assemblies reported by Unity, including inherited member contracts and safe dependency resolution.
- Added deterministic generated linker descriptors that preserve marked JSON contracts under High managed stripping.
- Made discovery and descriptor generation fail closed when reflection safety cannot be guaranteed.
- Added a fail-fast active-target check and documented `-activeBuildProfile` startup usage for reliable clean command-line builds.
- Added Mono.Cecil-based coverage for attributes, referenced converters, nested and generic contracts, resolver boundaries, deterministic XML, and invalid linker input.

## 0.3.0 - 2026-07-29

- Integrated registered workflows with Unity's native Build and Build And Run buttons.
- Added invocation-aware project callbacks that preserve Unity-selected outputs and options.
- Added a managed-profile guard that blocks direct BuildPipeline calls from bypassing Deucarian.
- Added one-time native-build guidance and manager-to-Unity Build Profile navigation.
- Stopped development policy from forcing Auto Run so the initiating build action owns launch behavior.
- Made passive Build Profile validation read the profile's serialized Player Settings override without switching Unity's active Build Profile, preventing refresh and recompilation loops while the manager is open.
- Clarified the four Build Pipeline Manager actions in the window and documentation.
- Kept the target selector and action buttons inside responsive toolbar lanes at narrow widths.
- Updated the shared Editor dependency to 1.0.5 for the responsive command-bar contract.

## 0.2.2 - 2026-07-17

- Applied the tool sample contract and aligned exact Editor and Logging dependencies.

## 0.2.1 - 2026-07-17

- Made the Build Pipeline Manager wallpaper static so an idle window does not schedule continuous UI repaints.
- Debounced project-change discovery and validation, and cancel pending refresh callbacks when the manager closes.

## 0.2.0 - 2026-07-16

- Added the shared Build Pipeline Manager and generic project workflow provider API.
- Consolidated package tooling into one Deucarian menu entry.
- Adopted Deucarian Editor and Logging and added package governance metadata.
- Removed application-specific benchmark language and added consumer-coupling guards.

## 0.1.0

- Added the platform-neutral build request, policy, runner, validation, and artifact manifest APIs.
- Added development and production WebGL policies for Unity Build Profiles.
- Added explicit profile synchronization, drift detection, editor menus, and a generic CI entry point.
- Added production artifact validation and a 20 MiB encoded bootstrap budget.
