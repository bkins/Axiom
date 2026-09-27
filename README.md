# Axiom

Axiom is a Windows-first personal Markdown editor. Ordinary Markdown files remain authoritative; Axiom protects dirty buffers and workspace-owned state in local SQLite without silently saving document files.

## Develop

```powershell
dotnet restore Axiom.slnx
dotnet test Axiom.slnx
dotnet run --project src\Axiom.App\Axiom.App.csproj
```

## Release checks

```powershell
.\scripts\Test-ForcedRecovery.ps1
.\scripts\Publish-PortableWindows.ps1
```

Application state and rotating logs are stored under `%LOCALAPPDATA%\Axiom`, separate from portable binaries.

