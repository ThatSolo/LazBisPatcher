# Runtime licenses and notices

This page records the runtime components and terms reviewed for the Windows x64 v0.1.5 candidate on November 13, 2026. The release notes link to this documentation. The EXE also carries the four text files listed below as silent resources. These runtime terms do not choose a license for the patcher source.

## Exact runtime inputs

The app and its bundled helper are self-contained .NET 10 builds using SDK 10.0.301. Both runtime packs resolved to **10.0.9**, source revision `901ca941248413c79832d2fdbd709da0c4386353`:

- `Microsoft.NETCore.App.Runtime.win-x64/10.0.9`
- `Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.9`

Both local package manifests declare MIT, but that metadata alone does not describe all Windows native components. Microsoft's [Windows licensing map](https://github.com/dotnet/core/blob/33918e8affbb517bf654c15559ea6f813c08bbae/license-information-windows.md) identifies the additional terms below. The app's resolved dependency manifest contains these native binaries; no additional third-party application package is referenced. The helper uses the same .NET Core runtime without WPF.

| Bundled component | Exact file version | Applicable terms |
| --- | --- | --- |
| Single-file .NET runtime, including `coreclr.dll` | 10.0.9, revision `901ca941248413c79832d2fdbd709da0c4386353` | Microsoft .NET Library License |
| `Microsoft.DiaSymReader.Native.amd64.dll` | 14.42.34436.0 | Microsoft .NET Library License |
| `PresentationNative_cor3.dll` | 10.0.526.25302, revision `02c06974071d84bc2ea31864821c8fe345000bf1` | Microsoft .NET Library License |
| `vcruntime140_cor3.dll` | 14.44.35211.0 | Microsoft .NET Library License |
| `wpfgfx_cor3.dll` | 10.0.926.27113 | Microsoft .NET Library License |
| `D3DCompiler_47_cor3.dll` | 10.0.26100.7705 | [Windows SDK terms](https://learn.microsoft.com/en-us/legal/windows-sdk/license), section 2(a) |
| Other runtime/WPF files, including `PenImc_cor3.dll` | Runtime pack 10.0.9 | MIT and the included third-party notices |

The WPF project also [explains its redistributed VC runtime and D3D compiler](https://github.com/dotnet/wpf/blob/main/Documentation/redistributables.md). Their bytes are supplied by the Windows Desktop runtime pack, not copied from a separate workstation SDK or installation.

## Full license and notice texts

- [.NET MIT license](notices/DOTNET-LICENSE.txt)
- [.NET third-party notices](notices/DOTNET-THIRD-PARTY-NOTICES.txt)
- [Windows Desktop MIT license](notices/WINDOWSDESKTOP-LICENSE.txt)
- [Microsoft .NET Library License](notices/DOTNET-LIBRARY-LICENSE.txt)
- [Windows SDK license](https://learn.microsoft.com/en-us/legal/windows-sdk/license)

The first three files are unchanged copies from the resolved 10.0.9 packs. The .NET Library License is copied from the [same runtime source revision](https://github.com/dotnet/dotnet/blob/901ca941248413c79832d2fdbd709da0c4386353/src/runtime/src/installer/pkg/LICENSE-MSFT.TXT), Git blob `c69e3631ee3c942df5751003abd6f6b7051f09a2`. Retain these texts verbatim when redistributing. When changing the runtime, inspect the newly resolved packages and component terms again.

## How the notices accompany the distribution

The build includes the four full text files above as silent resources inside `LazBisPatcher.exe`. The same texts are readable in this repository. Release notes must link to this document at the exact source revision used for the build. The distribution has one downloadable `LazBisPatcher.exe`. There is no separate notice asset, notices popup, Notices control, or runtime notice extraction.

A web link alone was not established as sufficient to replace the copy-included texts. The packaged MIT terms require the copyright and permission notice with copies or substantial portions. Microsoft's [.NET asset licensing model](https://github.com/dotnet/runtime/blob/main/docs/project/licensing-assets.md) says binary distributions must carry their license and third-party notices. Some included terms allow accompanying documentation, but that does not waive the other included conditions. The [general .NET license information](https://github.com/dotnet/core/blob/33918e8affbb517bf654c15559ea6f813c08bbae/license-information.md) also covers single-file hosts and self-contained application redistribution.

This release preserves that reviewed distribution arrangement. Removing the embedded texts would require a new assessment of the included terms.

## Redistribution questions

The Microsoft .NET Library License, section 3(a)(ii), and Windows SDK terms, section 2(a)(ii), include downstream protective-terms, valid program copyright-notice and other distribution obligations. Whether the planned end-user/distributor terms and copyright display satisfy those requirements remains unverified and needs review before public distribution. A download-page link alone has not been established as sufficient for those obligations.

The component inventory and license texts above describe the reviewed build. This record does not establish complete redistribution compliance or add a new patcher license or acceptance popup. [Verification](../VERIFICATION.md) records the candidate's source provenance and test limits; [MAINTAINING.md](../MAINTAINING.md) records its authorship and release workflow.
