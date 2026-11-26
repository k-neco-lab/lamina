// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Abstractions;

/// <summary>
/// UI-engine-agnostic Layer 1 contract for a 3D spatial overlay panel that accepts shared GPU textures.
/// </summary>
public interface IVrOverlayFrameSurface
{
    string Id { get; }
    int PixelWidth { get; }
    int PixelHeight { get; }
    uint SortOrder { get; }
    bool IsVisible { get; }
    bool IsDirty { get; }
    Matrix4x4 Pose { get; }
    float WidthMeters { get; }
    float HeightMeters { get; }

    void MarkDirty();

    /// <summary>
    /// Submits a cross-process shared GPU texture handle (e.g. D3D11 ID3D11Texture2D with ResourceOptionFlags.Shared).
    /// </summary>
    void SubmitSharedTexture(nint sharedTexturePointer, int width, int height);
}
