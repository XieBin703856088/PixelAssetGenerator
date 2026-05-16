#if VORTICE
using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using ResourceUsage = Vortice.Direct3D11.Usage;

namespace PixelAssetGenerator.Core.Gpu
{
    /// <summary>
    /// Helper utilities for creating D3D11 buffers used by GPU nodes.
    /// </summary>
    internal static class GpuBufferHelpers
    {
        /// <summary>
        /// Create a constant buffer from a mixed sequence of ints/floats/bools.
        /// Each slot is 32-bit; floats are reinterpreted as their IEEE-754 bit pattern
        /// so HLSL can correctly read int and float slots from the same buffer layout.
        /// </summary>
        public static ID3D11Buffer CreatePackedConstantBuffer(ID3D11Device device, object[] slots)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(slots);

            // Ensure buffer size is a multiple of 16 bytes (4 ints) to match HLSL cbuffer packing.
            var slotCount = slots.Length;
            var paddedCount = ((slotCount + 3) / 4) * 4; // round up to multiple of 4 ints
            if (paddedCount == 0) paddedCount = 4; // ensure at least 16 bytes
            var raw = new int[paddedCount];
            for (int i = 0; i < slotCount; i++)
            {
                var v = slots[i];
                if (v is int vi)
                {
                    raw[i] = vi;
                }
                else if (v is float vf)
                {
                    raw[i] = BitConverter.SingleToInt32Bits(vf);
                }
                else if (v is bool vb)
                {
                    raw[i] = vb ? 1 : 0;
                }
                else
                {
                    throw new ArgumentException($"Unsupported slot type: {v?.GetType()}", nameof(slots));
                }
            }

            var h = GCHandle.Alloc(raw, GCHandleType.Pinned);
            try
            {
                var cbd = new BufferDescription
                {
                    Usage = ResourceUsage.Default,
                    SizeInBytes = sizeof(int) * raw.Length,
                    BindFlags = BindFlags.ConstantBuffer,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None,
                    StructureByteStride = 0
                };
                var init = new SubresourceData(h.AddrOfPinnedObject(), 0, 0);
                return device.CreateBuffer(cbd, init);
            }
            finally
            {
                h.Free();
            }
        }
    }
}
#endif
