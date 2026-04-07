using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reso360Spout2Renderer
{
    static class SpoutUtil
    {
        static CommandBuffer? _commandBuffer;

        internal static void IssueSenderPluginEvent(PluginEntry.Event pluginEvent, IntPtr ptr)
        {
            if (_commandBuffer == null) _commandBuffer = new CommandBuffer();
            _commandBuffer.IssuePluginEventAndData(
                PluginEntry.Sender_GetRenderEventFunc(), (int)pluginEvent, ptr);
            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();
        }
    }
}
