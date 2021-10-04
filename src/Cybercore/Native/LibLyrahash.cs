using System;
using System.Runtime.InteropServices;

namespace Cybercore.Native
{
    public static unsafe class LibLyrahash
    {
        [DllImport("liblyrahash", EntryPoint = "lyra2re_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void lyra2re(byte* input, void* output, uint inputLength);

        [DllImport("liblyrahash", EntryPoint = "lyra2rev2_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void lyra2rev2(byte* input, void* output, uint inputLength);

        [DllImport("liblyrahash", EntryPoint = "lyra2rev3_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void lyra2rev3(byte* input, void* output, uint inputLength);

        [DllImport("liblyrahash", EntryPoint = "lyra2vc0ban_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void lyra2vc0ban(byte* input, void* output, uint inputLength);

        [DllImport("liblyrahash", EntryPoint = "lyra2z_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void lyra2z(byte* input, void* output, uint inputLength);

        [DllImport("liblyrahash", EntryPoint = "lyra2z330_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void lyra2z330(byte* input, void* output, uint inputLength);
    }
}