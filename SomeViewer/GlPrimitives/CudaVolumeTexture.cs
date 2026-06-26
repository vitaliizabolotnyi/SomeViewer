using ManagedCuda;
using ManagedCuda.BasicTypes;
using SomeViewer.Model;

namespace SomeViewer.GlPrimitives;

// Uploads a <see cref="VolumeData"/> into a CUDA 3D array and exposes it as a
// hardware-filtered texture object (single-channel float, trilinear filtering,
// normalized coordinates, clamp-to-edge). Pass <see cref="TexObject"/> to a
// kernel and fetch with <c>tex3D&lt;float&gt;(tex, u, v, w)</c> where each
// coordinate is in [0,1].
public sealed class CudaVolumeTexture : IDisposable
{
    private readonly CudaArray3D _array;
    private readonly CudaTexObject _texObject;

    public CudaVolumeTexture(VolumeData volume)
    {
        Width = volume.Width;
        Height = volume.Height;
        Depth = volume.Depth;

        // Single-channel float 3D array so the texture unit returns filterable floats.
        _array = new CudaArray3D(
            CUArrayFormat.Float,
            volume.Width,
            volume.Height,
            volume.Depth,
            CudaArray3DNumChannels.One,
            CUDAArray3DFlags.None);

        // z-major host layout matches the array's (x, y, z) extents.
        _array.CopyFromHostToThis(volume.Densities);

        var resourceDesc = new CudaResourceDesc(_array);
        var textureDesc = new CudaTextureDescriptor(
            CUAddressMode.Clamp,
            CUFilterMode.Linear,
            CUTexRefSetFlags.NormalizedCoordinates);

        _texObject = new CudaTexObject(resourceDesc, textureDesc);
    }

    public void Dispose()
    {
        _texObject?.Dispose();
        _array?.Dispose();
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }

    // The bindless texture handle to hand to a CUDA kernel.
    public CUtexObject TexObject => _texObject.TexObject;
}
