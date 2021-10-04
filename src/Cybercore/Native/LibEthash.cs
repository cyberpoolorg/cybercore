using System;
using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming

namespace Cybercore.Native
{
    public static unsafe class LibEthash
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ethash_h256_t
        {
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U8, SizeConst = 32)] public byte[] value;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ethash_return_value
        {
            public ethash_h256_t result;
            public ethash_h256_t mix_hash;

            [MarshalAs(UnmanagedType.U1)] public bool success;
        }

        public delegate int ethash_callback_t(uint progress);

        [DllImport("libethash", EntryPoint = "ethash_light_new_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ethash_light_new(ulong block_number);

        [DllImport("libethash", EntryPoint = "ethash_light_delete_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ethash_light_delete(IntPtr handle);

        [DllImport("libethash", EntryPoint = "ethash_light_compute_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ethash_light_compute(IntPtr handle, byte* header_hash, ulong nonce, ref ethash_return_value result);

        [DllImport("libethash", EntryPoint = "ethash_full_new_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ethash_full_new(string dagDir, IntPtr light, ethash_callback_t callback);

        [DllImport("libethash", EntryPoint = "ethash_full_delete_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ethash_full_delete(IntPtr handle);

        [DllImport("libethash", EntryPoint = "ethash_full_compute_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ethash_full_compute(IntPtr handle, byte* header_hash, ulong nonce, ref ethash_return_value result);

        [DllImport("libethash", EntryPoint = "ethash_full_dag_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ethash_full_dag(IntPtr handle);

        [DllImport("libethash", EntryPoint = "ethash_full_dag_size_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong ethash_full_dag_size(IntPtr handle);

        [DllImport("libethash", EntryPoint = "ethash_get_seedhash_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern ethash_h256_t ethash_get_seedhash(ulong block_number);

        [DllImport("libethash", EntryPoint = "ethash_get_default_dirname_export", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool ethash_get_default_dirname(byte* data, int length);
    }
}