# Roslyn analyzer development

Unity ignores this tilde-suffixed directory. The installable analyzer is the compiled `Analyzers/Deucarian.BuildPipeline.Analyzers.dll` asset at the package root.

The project targets .NET Standard 2.0 and Microsoft.CodeAnalysis 4.3.1 to match Unity 6000.0's compiler line. It contains both the diagnostic analyzer and standard Roslyn `CodeFixProvider`; the compiler ignores the code-fix export while IDE language services discover it from the same analyzer assembly.

Run the focused suite and refresh the packaged DLL with:

```text
dotnet test Analyzers~/Tests/Deucarian.BuildPipeline.Analyzers.Tests.csproj --configuration Release
```

Release builds copy the deterministic analyzer DLL into `Analyzers`. CI repeats that build, runs the analyzer/code-fix tests, and rejects a stale committed DLL.
