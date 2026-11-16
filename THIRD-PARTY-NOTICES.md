# Third-party components

Lantern is built with Microsoft's .NET 10 / WPF and uses the following NuGet components:

- Microsoft.Data.Sqlite 10.0.15 and Microsoft.Data.Sqlite.Core: Microsoft, MIT license. https://github.com/dotnet/efcore
- SQLitePCLRaw.bundle_e_sqlite3 3.0.5 and its native/provider dependencies: Eric Sink and contributors, Apache-2.0 for the managed wrapper; SQLite's own licensing applies to the SQLite engine. https://github.com/ericsink/SQLitePCL.raw
- System.Security.Cryptography.ProtectedData 10.0.15: .NET contributors, MIT license. https://github.com/dotnet/runtime
- Microsoft Windows SDK .NET projections / CsWinRT: Microsoft, licenses in the corresponding packages. https://github.com/microsoft/CsWinRT
- .NET runtime and WPF runtime: Microsoft and .NET contributors. https://github.com/dotnet/runtime and https://github.com/dotnet/wpf

Exact transitive dependencies are recorded by NuGet restore in project.assets.json. The source project pins direct dependencies. NuGet's vulnerability audit is enabled and warnings are treated as errors.

The UI uses Windows-provided Segoe typography and icon fonts; those fonts are not redistributed. The Lantern icon and UI composition were created for this project. QA media is not included in the release. The public W3C Sintel trailer was used only for playback verification in a temporary QA workspace.
