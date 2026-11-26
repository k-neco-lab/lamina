# Lamina

**Lamina** is an MIT-licensed .NET 10 OpenVR overlay runtime and native multi-pointer Direct2D UI toolkit developed by **K Lab**.

Designed for high-refresh-rate SteamVR overlay applications, Lamina decouples 3D spatial overlay hosting from 2D UI rendering and ships a lightweight retained-mode UI tree built from the ground up for independent dual-hand (multi-pointer) interaction.

## Package Architecture

Lamina is organized into four focused packages:

| Package | Target | Purpose |
| --- | --- | --- |
| `KLab.Lamina.Abstractions` | `net10.0` | UI-engine-agnostic overlay surface contracts (`IVrOverlayFrameSurface`), multi-pointer event types (`VrPointerEvent`), and per-pointer panel routing (`MultiPointerPanelRouter`). |
| `KLab.Lamina` | `net10.0` | OpenVR overlay host (`VrOverlayBackend`, `OpenVrOverlayDriver`), 3D spatial pose math (`SpatialPoseMath`, `OverlayNodePose`), deterministic dual-hand ray arbitration (`SpatialHitArbiter`), action-set grace tracking (`VrHandActionTracker`), and frame coordination (`VrOverlaySceneCoordinator`). |
| `KLab.Lamina.Ui` | `net10.0` | Retained-mode UI control tree (`UiRoot`, `Panel`, `Stack`, `Grid`, `Button`, `Toggle`, `Slider`, `ScrollView`, `ListView`, `TextBlock`, `Icon`, `Thumbnail`) with per-pointer hover/capture state and VR click-slop tolerance. |
| `KLab.Lamina.D2D` | `net10.0-windows10.0.19041.0` | Zero-copy Direct3D 11 / Direct2D 1 / DirectWrite renderer (`VrD3D11Device`, `D2DDrawContext`, `TextLayoutCache`) and double-buffered shared-texture swap chain (`SharedTextureSwapChain`, `D2DPanelSurfaceHost`). |

```text
KLab.Lamina.D2D ──► KLab.Lamina.Ui ──► KLab.Lamina.Abstractions ◄── KLab.Lamina
```

`KLab.Lamina` never depends on `KLab.Lamina.Ui` or `KLab.Lamina.D2D`, so you can pair the OpenVR spatial runtime with Lamina's Direct2D UI or with any custom texture-producing UI backend.

## Quickstart

### 1. Consume via NuGet

Lamina publishes prebuilt packages to a static NuGet V3 feed on the `feed` branch of this repository. Add a `NuGet.config` to your solution:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="klab-lamina" value="https://raw.githubusercontent.com/k-neco-lab/lamina/feed/index.json" />
  </packageSources>
</configuration>
```

Then reference the packages you need:

```xml
<ItemGroup>
  <PackageReference Include="KLab.Lamina.Abstractions" Version="0.1.0" />
  <PackageReference Include="KLab.Lamina" Version="0.1.0" />
</ItemGroup>
```

### 2. Build, Test & Publish Locally

Requires the .NET 10 SDK on Windows (Direct3D 11 / Direct2D GPU tests run directly on the local adapter or WARP):

```bash
dotnet build Lamina.sln -c Release
dotnet test Lamina.sln -c Release
dotnet pack Lamina.sln -c Release -o artifacts/nupkgs
```

To update the static NuGet V3 `feed` branch locally (no GitHub Actions required):

```bash
python3 scripts/publish-feed.py --nupkgs artifacts/nupkgs --branch feed --push
```

### 3. Render a Multi-Pointer Direct2D Panel into SteamVR

```csharp
using KLab.Lamina;
using KLab.Lamina.Abstractions;
using KLab.Lamina.D2D;
using KLab.Lamina.Ui;

using var gpu = new VrD3D11Device();
using var surface = new D2DPanelSurfaceHost(gpu, width: 640, height: 360);

var root = surface.UiRoot;
root.Root = new Stack
{
    Direction = StackDirection.Vertical,
    Spacing = 12,
    Children =
    {
        new TextBlock { Text = "Hello from Lamina" },
        new Button { Text = "Click Me", OnClick = () => Console.WriteLine("Clicked!") }
    }
};

using var backend = new VrOverlayBackend();
var coordinator = new VrOverlaySceneCoordinator(backend: backend);
coordinator.RegisterPanel("main", surface, new VrOverlayPanelOptions
{
    WidthMeters = 0.64f,
    SortOrder = 10
});

// Per frame: update poses, route dual-hand pointer rays, render dirty surfaces, and submit to OpenVR.
surface.RenderIfDirty();
```

## Key Capabilities

- **Native Dual-Hand Multi-Pointer Routing**: Left and right controllers maintain isolated hover, press, drag, and capture state (`PointerId`) both across panels (`MultiPointerPanelRouter`) and within a single panel (`UiRoot`).
- **Deterministic Spatial Hit Arbitration**: `SpatialHitArbiter` resolves overlapping overlays using a 2.5 cm coplanar depth band (`SortOrder` tie-breaking) and a 6 mm foreground seam lock to prevent accidental ray click-through.
- **Transient Action-Set Grace Window**: `VrHandActionTracker` absorbs short action-binding dropouts during SteamVR action-set switches without releasing held triggers or drags.
- **Zero-Copy Shared-Texture Double Buffering**: `SharedTextureSwapChain` renders Direct2D directly onto `IDXGISurface` backed by `D3D11` shared textures (`BGRA8`, premultiplied alpha) and defers retirement of resized buffers by one frame.

## License

MIT License — Copyright (c) 2026 K Lab. See [LICENSE](LICENSE).

Third-party OpenVR headers and binaries in `vendor/openvr/` are licensed under their respective BSD-3-Clause terms (see [vendor/openvr/LICENSE](vendor/openvr/LICENSE)).
