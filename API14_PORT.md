# API14 port (in progress)

The API13 branch remains the released version. This branch starts the API14
migration with Dalamud.NET.Sdk 14.0.1, .NET 10 and IObjectTable.LocalPlayer.
The build requires an explicit DALAMUD_HOME and rejects non-14 Dalamud assemblies.
Traditional Chinese resources and configuration fields are preserved.

```powershell
$env:DALAMUD_HOME = 'E:\path\to\isolated-api14-references'
dotnet build PlayerWatchlist.csproj -c Release
```

Verified on 2026-11-05: .NET SDK 10.0.302, upstream Dalamud 14.0.4.3,
Release build with 0 warnings and 0 errors. The generated ZIP manifest declares
DalamudApiLevel 14. A negative build using API13 references was rejected by
RequireApi14References as intended. No dependency lock file is tracked here.

Reference archive: goatcorp/dalamud-distrib commit
`261f5b0cd1f5746533d3d13cf01d9aa62853a746`, `latest.zip`, SHA-256
`81704d31a1690801f87c8fdb3c2c7956afcf7e7f56dd04fa37f9b1d3813082ca`.

Pending: matching TC API14 ClientStructs verification (Character ContentId/AccountId), and
in-game login/logout, territory change, list persistence, context menu,
notification and line drawing checks. No API14 release package is published yet.
