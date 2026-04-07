using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Reso360Spout2Patcher
{
    /// <summary>
    /// BepInEx 5 プリローダーパッチャー。
    /// Unity がネイティブプラグインを読み込む前に KlakSpout_send.dll を
    /// Renderite.Renderer_Data/Plugins/x86_64/ にコピーする。
    /// これにより Unity が UnityPluginLoad を呼び出して D3D11 デバイスを登録する。
    /// </summary>
    public static class Patcher
    {
        // BepInEx 5 パッチャー規約: パッチするアセンブリなし (ファイルコピーのみ)
        public static IEnumerable<string> TargetDLLs { get; } = Enumerable.Empty<string>();

        public static void Initialize()
        {
            try
            {
                // パッチャー DLL と同じフォルダにある KlakSpout_send.dll
                var patcherDir = Path.GetDirectoryName(typeof(Patcher).Assembly.Location)!;
                var sourcePath = Path.Combine(patcherDir, "KlakSpout_send.dll");

                if (!File.Exists(sourcePath))
                {
                    Console.WriteLine($"[Reso360Spout2.Patcher] KlakSpout_send.dll not found at: {sourcePath}");
                    return;
                }

                // Renderite.Renderer.exe のディレクトリ → Renderite.Renderer_Data/Plugins/x86_64/
                var gameDir = AppDomain.CurrentDomain.BaseDirectory;
                var destDir = Path.Combine(gameDir, "Renderite.Renderer_Data", "Plugins", "x86_64");

                if (!Directory.Exists(destDir))
                {
                    Console.WriteLine($"[Reso360Spout2.Patcher] Unity plugins folder not found: {destDir}");
                    return;
                }

                var destPath = Path.Combine(destDir, "KlakSpout_send.dll");

                // 同一ファイルなら不要なコピーをスキップ
                if (File.Exists(destPath) &&
                    new FileInfo(sourcePath).Length == new FileInfo(destPath).Length &&
                    File.GetLastWriteTimeUtc(sourcePath) <= File.GetLastWriteTimeUtc(destPath))
                {
                    Console.WriteLine("[Reso360Spout2.Patcher] KlakSpout_send.dll is already up to date.");
                    return;
                }

                File.Copy(sourcePath, destPath, overwrite: true);
                Console.WriteLine($"[Reso360Spout2.Patcher] Copied KlakSpout_send.dll to: {destPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Reso360Spout2.Patcher] Error: {ex}");
            }
        }

        public static void Finish() { }
    }
}
