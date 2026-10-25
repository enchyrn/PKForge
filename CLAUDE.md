# PKForge Fork

This is the owner's personal fork (https://github.com/gabzidevs/PKForge) of sofianeelhor/PKForge. Read `docs/fork/README.md` for why it exists and what changed, and `docs/fork/ROADMAP.md` for the decisions and ideas about where it goes next.

- Never push to upstream. The `upstream` remote is fetch-only; push to `origin`.
- Tests (`dotnet` is only available through mise):
  - `mise x dotnet@10 -- dotnet test tests/PKForge.Engine.Tests/PKForge.Engine.Tests.csproj -c Release`
  - `mise x dotnet@10 -- dotnet test tests/PKForge.Domain.Tests/PKForge.Domain.Tests.csproj -c Release`
- `src/PKForge.App` (MAUI Android) can't be built locally; keep App changes thin, put logic in Engine/Infrastructure/Domain, and let CI compile-check the rest.
