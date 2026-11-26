// Copyright (c) K Lab. Licensed under the MIT License.

using KLab.Lamina.Ui;
using Vortice.Direct3D11;

namespace KLab.Lamina.D2D;

/// <summary>
/// Wraps a D3D11 texture as an opaque <see cref="ITextureHandle"/> consumable by <see cref="KLab.Lamina.Ui"/>.
/// </summary>
public sealed class D2DTextureHandle : ITextureHandle, IDisposable
{
    private readonly bool ownsTexture;
    private bool disposed;

    public D2DTextureHandle(ID3D11Texture2D texture, bool ownsTexture = false)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Texture = texture;
        this.ownsTexture = ownsTexture;
        var description = texture.Description;
        Width = (int)description.Width;
        Height = (int)description.Height;
    }

    public ID3D11Texture2D Texture { get; }
    public int Width { get; }
    public int Height { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (ownsTexture)
        {
            Texture.Dispose();
        }
    }
}
