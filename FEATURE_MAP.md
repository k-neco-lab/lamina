# Lamina Feature Map

## Packages

### `KLab.Lamina.Abstractions` (`src/KLab.Lamina.Abstractions/`)
- `IVrOverlayFrameSurface`: Frame surface contract for dirty-checked rendering and shared GPU texture handoff.
- `IVrPanelInputSink`: Multi-pointer input receiver contract (`PointerMove`, `PointerDown`, `PointerUp`, `PointerScroll`, `PointerLeave`, `PointerCancel`).
- `VrPointerEvent`: Immutable pointer event payload with `PointerId`, UV/pixel coordinates, and scroll delta.
- `MultiPointerPanelRouter`: Per-pointer cross-panel hover, press capture, 28 px VR click-slop anchor, and tracking-loss cancellation.

### `KLab.Lamina` (`src/KLab.Lamina/`)
- `SpatialPoseMath`: OpenVR 3x4 / `Matrix4x4` conversions, world-space ray generation, and attached/docked child pose transforms.
- `OverlayNodePose`: Hierarchical spatial panel pose representation.
- `IOverlayRaycaster` / `GeometricOverlayRaycaster` / `OpenVrOverlayRaycaster`: Pluggable ray-panel intersection backends.
- `SpatialHitArbiter`: Authoritative per-frame ray arbitration with 2.5 cm coplanar depth banding (`SortOrder`) and 6 mm foreground seam guard.
- `VrHandActionTracker`: Hand pose/action sampler with 3-tick deactivation grace window to prevent transient binding dropouts.
- `IVrOverlayDriver` / `OpenVrOverlayDriver` / `VrOverlayBackend`: SteamVR `IVROverlay` lifecycle, transform, sort order, curvature, and D3D11 shared texture submission.
- `VrOverlaySceneCoordinator`: End-to-end per-frame coordinator combining pose resolution, dual-hand ray arbitration, pointer routing, and overlay submission.

### `KLab.Lamina.Ui` (`src/KLab.Lamina.Ui/`)
- `UiRoot`: Retained-mode UI root with per-pointer hover/capture maps, layout pass, dirty tracking, and VR click-slop handling.
- `Controls/`: `Control`, `ContainerControl`, `Panel`, `Stack`, `Grid`, `ScrollView`, `ScrollState`, `ListView`, `Button`, `Toggle`, `Slider`, `TextBlock`, `Icon`, `Thumbnail`.
- `Drawing/`: `IDrawContext`, `Color`, `Geometry`, `Text`, `VectorPath`.
- `Icons/`: `IconCatalog`, `IconShapes`.
- `Animation/`: `Tween`.
- `Theme` / `ThemeService`: Light/dark semantic color tokens and theme switching.

### `KLab.Lamina.D2D` (`src/KLab.Lamina.D2D/`)
- `VrD3D11Device`: D3D11 + D2D1 + DirectWrite device initialization (hardware with WARP fallback).
- `SharedTextureSwapChain`: Double-buffered `BGRA8` `ResourceOptionFlags.Shared` swap chain with deferred retirement on resize.
- `D2DDrawContext` & `TextLayoutCache`: Zero-copy Direct2D/DirectWrite `IDrawContext` implementation with premultiplied alpha and cached text layouts.
- `D2DTextureHandle`: Opaque `ITextureHandle` wrapper over D3D11 textures.
- `D2DPanelSurfaceHost`: Combines `UiRoot`, `SharedTextureSwapChain`, and `D2DDrawContext` into an `IVrOverlayFrameSurface` + `IVrPanelInputSink`.

## Verification (`tests/KLab.Lamina.Tests/`)
- `Ui/MultiPointerPanelRouterTests.cs` & `Ui/MultiPointerUiTests.cs`: Cross-panel and in-panel multi-pointer hover, capture, click-slop, and cancel semantics.
- `D2D/D2DGpuTests.cs`: Real D3D11/D2D1 GPU rendering, premultiplied alpha readback, shared texture swap-chain double buffering, and resize retirement.
- `Spatial/SpatialHitArbiterTests.cs`, `SpatialPoseMathTests.cs`, `VrHandActionTrackerTests.cs`, `VrOverlaySceneCoordinatorTests.cs`: Coplanar arbitration, seam lock, pose math, action grace ticks, and full scene coordination.
