using System;
using System.Runtime.InteropServices;

namespace Cybercore.Native
{
    public static unsafe class LibMinotaurX
    {
        [DllImport("libminotaurx", EntryPoint = "minotaurx_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void minotaurx(byte* input, void* output, uint inputLength);
    }
}