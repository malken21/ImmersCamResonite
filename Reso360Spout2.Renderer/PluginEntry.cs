// KlakSpout - Spout video frame sharing plugin for Unity
// https://github.com/keijiro/KlakSpout

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Reso360Spout2Renderer
{
    static class PluginEntry
    {
        internal enum Event { Update, Dispose }

        internal static bool IsAvailable =>
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11;

        // Sender 関数 (KlakSpout_send.dll)
        // [DllImport] 経由でロードすると Unity が UnityPluginLoad を呼び出し D3D11 デバイスが登録される
        [DllImport("KlakSpout_send", EntryPoint = "GetRenderEventFunc")]
        internal static extern IntPtr Sender_GetRenderEventFunc();

        [DllImport("KlakSpout_send", EntryPoint = "CreateSender")]
        internal static extern IntPtr CreateSender(string name, int width, int height);

        [DllImport("KlakSpout_send", EntryPoint = "GetTexturePointer")]
        internal static extern IntPtr GetTexturePointer(IntPtr ptr);

        [DllImport("KlakSpout_send", EntryPoint = "GetTextureWidth")]
        internal static extern int GetTextureWidth(IntPtr ptr);

        [DllImport("KlakSpout_send", EntryPoint = "GetTextureHeight")]
        internal static extern int GetTextureHeight(IntPtr ptr);

        // Receiver / Utility 関数 (KlakSpout.dll)
        [DllImport("KlakSpout")] internal static extern IntPtr CreateReceiver(string name);

        [DllImport("KlakSpout")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CheckValid(IntPtr ptr);

        [DllImport("KlakSpout")] internal static extern int    ScanSharedObjects();
        [DllImport("KlakSpout")] internal static extern IntPtr GetSharedObjectName(int index);

        internal static string? GetSharedObjectNameString(int index)
        {
            var ptr = GetSharedObjectName(index);
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : null;
        }
    }
}
