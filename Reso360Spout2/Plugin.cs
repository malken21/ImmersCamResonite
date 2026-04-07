using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using BepisResoniteWrapper;
using HarmonyLib;
using System;
using System.IO.MemoryMappedFiles;

namespace Reso360Spout2;

/// <summary>
/// ホストプロセス (Renderite.Host.exe) で動作するプラグイン。
/// FrooxEngine の Harmony パッチでカメラスロット位置を取得し、
/// 共有メモリ経由でレンダラープロセスへ転送する。
/// UnityEngine は一切参照しない。
/// </summary>
[ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
public class Reso360Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    public static ConfigEntry<string> CAMERA_SLOT_NAME = null!;

    // 共有メモリ: 10 floats × 4 bytes = 40 bytes
    //   [0-11]  position (x, y, z)
    //   [12-27] rotation (x, y, z, w)
    //   [28-39] scale    (x, y, z)
    public const string SHARED_MEM_NAME = "Reso360Spout2_Camera";
    private const int   SHARED_MEM_SIZE = 40;

    private static MemoryMappedFile?         _sharedMem;
    private static MemoryMappedViewAccessor? _sharedMemView;

    public override void Load()
    {
        Log = base.Log;

        // 共有メモリ作成（レンダラーより先に起動するため Create）
        try
        {
            _sharedMem     = MemoryMappedFile.CreateOrOpen(SHARED_MEM_NAME, SHARED_MEM_SIZE);
            _sharedMemView = _sharedMem.CreateViewAccessor();
            Log.LogInfo("[Reso360Spout2] Shared memory created.");
        }
        catch (Exception e)
        {
            Log.LogError("[Reso360Spout2] Failed to create shared memory: " + e);
        }

        CAMERA_SLOT_NAME = Config.Bind("General", "CAMERA_SLOT_NAME", "#Camera",
            "カメラ位置として参照するワールドスロット名");

        var harmony = new Harmony("dev.kokoa.Reso360Spout2");
        harmony.PatchAll();

        ResoniteHooks.OnEngineReady += OnEngineReady;
        Log.LogInfo($"Plugin {PluginMetadata.GUID} loaded.");
    }

    private void OnEngineReady()
    {
        Log.LogInfo("[Reso360Spout2] Engine ready.");
    }

    /// <summary>共有メモリにカメラ状態を書き込む（Unity スレッドから読まれる）</summary>
    private static void WriteCameraState(
        float px, float py, float pz,
        float rx, float ry, float rz, float rw,
        float sx, float sy, float sz)
    {
        if (_sharedMemView == null) return;
        _sharedMemView.Write( 0, px); _sharedMemView.Write( 4, py); _sharedMemView.Write( 8, pz);
        _sharedMemView.Write(12, rx); _sharedMemView.Write(16, ry); _sharedMemView.Write(20, rz); _sharedMemView.Write(24, rw);
        _sharedMemView.Write(28, sx); _sharedMemView.Write(32, sy); _sharedMemView.Write(36, sz);
    }

    // ---- Harmony パッチ -------------------------------------------------------
    [HarmonyPatch(typeof(FrooxEngine.Engine), "RunUpdateLoop")]
    class Patch
    {
        static void Postfix(FrooxEngine.Engine __instance)
        {
            if (__instance.WorldManager.FocusedWorld == null) return;

            __instance.WorldManager.FocusedWorld.RunSynchronously(() =>
            {
                var slot = __instance.WorldManager.FocusedWorld.RootSlot
                    .FindChildInHierarchy(CAMERA_SLOT_NAME.Value);
                if (slot == null) return;

                WriteCameraState(
                    slot.GlobalPosition.X, slot.GlobalPosition.Y, slot.GlobalPosition.Z,
                    slot.GlobalRotation.X, slot.GlobalRotation.Y, slot.GlobalRotation.Z, slot.GlobalRotation.W,
                    slot.GlobalScale.X,    slot.GlobalScale.Y,    slot.GlobalScale.Z);
            });
        }
    }
}
