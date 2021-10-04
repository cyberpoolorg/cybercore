using System;
using System.Runtime.InteropServices;

namespace Cybercore.Native
{
    public static unsafe class LibGrhash
    {
        [DllImport("libgrhash", EntryPoint = "ghostrider_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostrider(byte* input, void* output, uint inputLength);
    }
}