# Third-party components

Lantern is built with Microsoft's .NET 10 / WPF and uses the following NuGet components:

- Microsoft.Data.Sqlite 10.0.15 and Microsoft.Data.Sqlite.Core: Microsoft, MIT license. https://github.com/dotnet/efcore
- SQLitePCLRaw.bundle_e_sqlite3 3.0.5 and its native/provider dependencies: Eric Sink and contributors, Apache-2.0 for the managed wrapper; SQLite's own licensing applies to the SQLite engine. https://github.com/ericsink/SQLitePCL.raw
- System.Security.Cryptography.ProtectedData 10.0.15: .NET contributors, MIT license. https://github.com/dotnet/runtime
- Microsoft Windows SDK .NET projections / CsWinRT: Microsoft, licenses in the corresponding packages. https://github.com/microsoft/CsWinRT
- .NET runtime and WPF runtime: Microsoft and .NET contributors. https://github.com/dotnet/runtime and https://github.com/dotnet/wpf

Exact transitive dependencies are recorded by NuGet restore in project.assets.json. The source project pins direct dependencies. NuGet's vulnerability audit is enabled and warnings are treated as errors.

Microsoft.Web.WebView2 1.0.4314.35, copyright Microsoft Corporation, is used for the isolated HTML attachment viewer. Source/package: https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35. The SDK license is included in `licenses/WEBVIEW2-LICENSE.txt`; the portable distribution includes its full text at the end of this notices file. The separately installed Microsoft Edge WebView2 Runtime retains its own terms.

The UI uses Windows-provided Segoe typography and icon fonts; those fonts are not redistributed. The Lantern icon and UI composition were created for this project. QA media is not included in the release. The public W3C Sintel trailer was used only for playback verification in a temporary QA workspace.

## Zalo PC emoji

The desktop catalog includes emoji artwork and insertion-code mappings imported from Zalo PC 26.11.12 (`resources/app.asar`, `pc-dist/emoji`). Original archive member paths and SHA-256 hashes are recorded in `src/Lantern.App/Assets/Emoji/Zalo/source.json`; the import procedure is in `scripts/Import-ZaloEmoji.mjs`. These imported assets retain their original owners' rights.

## Zalo PC stickers

The desktop sticker library contains artwork imported from the local Zalo PC sticker cache. Original owners retain their rights. The cache inventory and conversion settings are recorded in `src/Lantern.App/Assets/Stickers/source.json`; the importer is `scripts/Import-ZaloStickers.py`. Cached previews are not a complete copy of Zalo's online store. Horizontal PNG frame strips were converted to GIF with an assumed 100 ms frame interval when source timing metadata was unavailable. Existing GIF timings were preserved; WebP assets were decoded by file signature.

## QRCoder 1.6.0

Source: https://github.com/codebude/QRCoder/tree/v1.6.0

The MIT License (MIT)

Copyright (c) 2013-2018 Raffael Herrmann

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## jsQR 1.4.0

PWA sử dụng jsQR (Apache-2.0), copyright các tác giả jsQR. Toàn văn giấy phép: pwa/licenses/jsqr.txt; bản web phân phối tại /licenses/jsqr.txt.
