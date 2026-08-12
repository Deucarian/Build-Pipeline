# Roslyn analyzer development

Unity ignores this tilde-suffixed directory. The installable analyzer is the compiled `Analyzers/Deucarian.BuildPipeline.Analyzers.dll` asset at the package root.

The project targets .NET Standard 2.0 and Microsoft.CodeAnalysis 4.3.1 to match Unity 6000.0's compiler line. It contains both the diagnostic analyzer and standard Roslyn `CodeFixProvider`; the compiler ignores the code-fix export while IDE language services discover it from the same analyzer assembly.

Run the focused suite with:

```text
dotnet test Analyzers~/Tests/Deucarian.BuildPipeline.Analyzers.Tests.csproj --configuration Release
```

Refresh the packaged DLL after changing analyzer sources with:

```text
dotnet build Analyzers~/Deucarian.BuildPipeline.Analyzers.csproj --configuration Release -p:UpdateUnityAnalyzer=true
```

`SourceFingerprint.txt` contains the canonical, line-ending-normalized SHA-256 of the analyzer project and its source files. The packaged DLL embeds that value as assembly metadata. CI runs the analyzer/code-fix suite on Windows and Linux, recomputes the source fingerprint, and rejects a stale committed DLL without depending on machine-specific PE bytes.
