# Third-party components

Lantern is built with Microsoft's .NET 10 / WPF and uses the following NuGet components:

- Microsoft.Data.Sqlite 10.0.15 and Microsoft.Data.Sqlite.Core: Microsoft, MIT license. https://github.com/dotnet/efcore
- SQLitePCLRaw.bundle_e_sqlite3 3.0.5 and its native/provider dependencies: Eric Sink and contributors, Apache-2.0 for the managed wrapper; SQLite's own licensing applies to the SQLite engine. https://github.com/ericsink/SQLitePCL.raw
- System.Security.Cryptography.ProtectedData 10.0.15: .NET contributors, MIT license. https://github.com/dotnet/runtime
- Microsoft Windows SDK .NET projections / CsWinRT: Microsoft, licenses in the corresponding packages. https://github.com/microsoft/CsWinRT
- .NET runtime and WPF runtime: Microsoft and .NET contributors. https://github.com/dotnet/runtime and https://github.com/dotnet/wpf

Exact transitive dependencies are recorded by NuGet restore in project.assets.json. The source project pins direct dependencies. NuGet's vulnerability audit is enabled and warnings are treated as errors.

The UI uses Windows-provided Segoe typography and icon fonts; those fonts are not redistributed. The Lantern icon and UI composition were created for this project. QA media is not included in the release. The public W3C Sintel trailer was used only for playback verification in a temporary QA workspace.

## Microsoft Fluent Emoji

34 bundled 3D PNG assets, copyright Microsoft Corporation, MIT license.
Source: https://github.com/microsoft/fluentui-emoji/tree/1ffb34c752ecf5d402f04cfb4b392c77f57c54bc
Per-asset upstream paths are recorded in src/Lantern.App/Assets/Emoji/catalog.json.

    MIT License

    Copyright (c) Microsoft Corporation.

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE
