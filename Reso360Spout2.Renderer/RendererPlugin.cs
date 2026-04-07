using BepInEx;
using BepInEx.Configuration;
using System;
using System.IO;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reso360Spout2Renderer
{
    /// <summary>
    /// レンダラープロセス (Renderite.Renderer) で動作する BepInEx 5 プラグイン。
    /// 共有メモリからカメラ状態を受け取り、360度/VR180映像をSpout送信する。
    /// </summary>
    [BepInPlugin("dev.kokoa.Reso360Spout2.Renderer", "Reso360Spout2 Renderer", "1.0.0")]
    public class RendererPlugin : BaseUnityPlugin
    {
        public static RendererPlugin? Instance { get; private set; }

        // ---- 設定 -------------------------------------------------------------
        public static ConfigEntry<bool>          SPOUT_ENABLE     = null!;
        public static ConfigEntry<ProjectionType> PROJECTION_TYPE  = null!;
        public static ConfigEntry<CubeMapSize>   CUBEMAP_SIZE     = null!;
        public static ConfigEntry<int>           OUTPUT_WIDTH     = null!;
        public static ConfigEntry<int>           OUTPUT_HEIGHT    = null!;
        public static ConfigEntry<bool>          RENDER_IN_STEREO = null!;
        public static ConfigEntry<float>         NEAR_CLIP        = null!;
        public static ConfigEntry<float>         FAR_CLIP         = null!;
        public static ConfigEntry<bool>          HIDE_LOCAL       = null!;

        public enum CubeMapSize { Low = 512, Mid = 1024, High = 2048, Ultra = 3072 }

        private string _pluginDir = "";
        private UnityEntry? _unityEntry;
        private GameObject? _entryGo;

        // ---- Awake: BepInEx 5 初期化 ------------------------------------------
        void Awake()
        {
            Instance = this;
            _pluginDir = Path.GetDirectoryName(GetType().Assembly.Location) ?? "";

            // 設定バインド
            const string s = "General";
            SPOUT_ENABLE     = Config.Bind(s, "SPOUT_ENABLE",     true,                        "Spout 出力を有効にする");
            PROJECTION_TYPE  = Config.Bind(s, "PROJECTION_TYPE",  ProjectionType.Equirectangular_180, "投影方式");
            CUBEMAP_SIZE     = Config.Bind(s, "CUBEMAP_SIZE",     CubeMapSize.High,            "キューブマップサイズ");
            OUTPUT_WIDTH     = Config.Bind(s, "OUTPUT_WIDTH",     6144,                        "出力幅 (px)");
            OUTPUT_HEIGHT    = Config.Bind(s, "OUTPUT_HEIGHT",    3072,                        "出力高 (px)");
            RENDER_IN_STEREO = Config.Bind(s, "RENDER_IN_STEREO", true,                        "ステレオレンダリング");
            NEAR_CLIP        = Config.Bind(s, "NEAR_CLIP",        0.01f,                       "ニアクリップ");
            FAR_CLIP         = Config.Bind(s, "FAR_CLIP",         3000f,                       "ファークリップ");
            HIDE_LOCAL       = Config.Bind(s, "HIDE_LOCAL",       true,                        "ローカルユーザーを非表示");

            // UnityEntry を持つ GameObject を生成
            _entryGo = new GameObject("___Reso360Spout2");
            DontDestroyOnLoad(_entryGo);
            _unityEntry = _entryGo.AddComponent<UnityEntry>();
            _unityEntry.PluginDir = _pluginDir;

            // 設定変更ハンドラ
            HIDE_LOCAL.SettingChanged       += (_, _2) => _unityEntry?.ApplyHideLocal();
            NEAR_CLIP.SettingChanged        += (_, _2) => { if (_unityEntry?.CameraComponent != null) _unityEntry.CameraComponent.nearClipPlane = NEAR_CLIP.Value; };
            FAR_CLIP.SettingChanged         += (_, _2) => { if (_unityEntry?.CameraComponent != null) _unityEntry.CameraComponent.farClipPlane  = FAR_CLIP.Value; };
            CUBEMAP_SIZE.SettingChanged     += (_, _2) => { if (_unityEntry?.cubeComponent   != null) _unityEntry.cubeComponent.CubemapSize     = (int)CUBEMAP_SIZE.Value; };
            PROJECTION_TYPE.SettingChanged  += (_, _2) => { if (_unityEntry?.cubeComponent   != null) _unityEntry.cubeComponent.ProjectionType  = PROJECTION_TYPE.Value; };
            RENDER_IN_STEREO.SettingChanged += (_, _2) => { if (_unityEntry?.cubeComponent   != null) _unityEntry.cubeComponent.RenderInStereo  = RENDER_IN_STEREO.Value; };
            SPOUT_ENABLE.SettingChanged     += (_, _2) => _unityEntry?.UpdateSpoutState();

            Logger.LogInfo("Reso360Spout2 Renderer plugin loaded.");
        }
    }

    // ---- MonoBehaviour: Unity レンダリング担当 ----------------------------------
    public class UnityEntry : MonoBehaviour
    {
        public Camera? CameraComponent;
        public CubemapToOtherProjection? cubeComponent;
        public IntPtr Plugin = IntPtr.Zero;
        public RenderTexture? SourceTexture;
        public Texture2D? SharedTexture;
        public string PluginDir = "";

        private SharedMemoryReader _sharedMem = new SharedMemoryReader();
        private GameObject? _cameraRoot;
        // Spout 初期化を遅延させる: D3D デバイスが完全に準備されてから呼ぶ
        private int _initDelayFrames = 1000;

        void Start()
        {
            // 共有メモリを開く
            if (!_sharedMem.TryOpen())
                Debug.LogWarning("[Reso360Spout2] Shared memory not yet available; will retry.");

            // 先にカメラ作成 (cubeComponent が作られる)、その後シェーダーをセット
            CreateCamera();
            LoadShaders();  // cubeComponent にシェーダーをセット

            cubeComponent!.CubemapSize    = (int)RendererPlugin.CUBEMAP_SIZE.Value;
            cubeComponent.ProjectionType  = RendererPlugin.PROJECTION_TYPE.Value;
            cubeComponent.RenderInStereo  = RendererPlugin.RENDER_IN_STEREO.Value;

            CameraComponent!.nearClipPlane = RendererPlugin.NEAR_CLIP.Value;
            CameraComponent.farClipPlane   = RendererPlugin.FAR_CLIP.Value;
            CameraComponent.cullingMask   &= ~(1 << 28);
            CameraComponent.cullingMask &= ~(1 << 29);
            CameraComponent.cullingMask &= ~(1 << 31);
            

            StartCoroutine(PostRenderLoop());

            // Spout の InitSpout() は Update() で遅延呼び出しする
            // (Start() 時点では D3D デバイスが Spout に未登録のため NULL device クラッシュが発生する)
        }

        void LateUpdate()
        {
            try
            {
                // 共有メモリ再試行
                if (!_sharedMem.IsOpen) _sharedMem.TryOpen();

                // Spout 初期化を数フレーム遅延 (D3D デバイス準備待ち)
                if (_initDelayFrames > 0)
                {
                    _initDelayFrames--;
                    if (_initDelayFrames == 0 && RendererPlugin.SPOUT_ENABLE.Value)
                    {
                        Debug.Log("[Reso360Spout2] Initializing Spout sender (deferred)...");
                        InitSpout();
                    }
                    return;
                }

                // ホストプロセスからカメラ状態を受け取る
                transform.position = _sharedMem.ReadPosition();
                transform.rotation = _sharedMem.ReadRotation();
                transform.localScale = _sharedMem.ReadScale();

            }
            catch (Exception e)
            {
                Debug.LogError("[Reso360Spout2] " + e);
            }
        }

        void OnDestroy() => _sharedMem.Dispose();

        IEnumerator PostRenderLoop()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame(); // 全てのカメラの描画が終わるのを待つ

                if (SourceTexture != null && Plugin != IntPtr.Zero)
                {
                    cubeComponent!.Rendering();
                    SendToSpout();
                }
                else
                {
                    Debug.Log("[Reso360Spout2] Waiting for SourceTexture to be created...");
                }
            }
        }

        private void LoadShaders()
        {
            var bundlePath = Path.Combine(PluginDir, "cubeto360");
            var bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null) { Debug.LogWarning($"[Reso360Spout2] AssetBundle not found: {bundlePath}"); return; }

            Shader? cubemapShader = null, cubemapRendererShader = null;
            foreach (var shader in bundle.LoadAllAssets<Shader>())
            {
                if (shader.name == "Unlit/CubemapToOtherProjection") cubemapShader = shader;
                else if (shader.name == "Unlit/CubemapRenderer")      cubemapRendererShader = shader;
            }
            // CubemapToOtherProjection に渡す（Start より前に設定が必要なため直接セット）
            if (cubeComponent != null)
            {
                cubeComponent.CubemapShader         = cubemapShader;
                cubeComponent.CubemapRendererShader = cubemapRendererShader;
            }
            Debug.Log("[Reso360Spout2] Shaders loaded.");
        }

        private void CreateCamera()
        {
            _cameraRoot = new GameObject("CameraRoot");
            _cameraRoot.transform.SetParent(transform, false);

            CameraComponent = _cameraRoot.AddComponent<Camera>();
            CameraComponent.depth            = -128;
            CameraComponent.fieldOfView      = 90f;
            CameraComponent.stereoTargetEye  = StereoTargetEyeMask.None;
            CameraComponent.stereoSeparation = 0.065f;

            cubeComponent = _cameraRoot.AddComponent<CubemapToOtherProjection>();
            Debug.Log("[Reso360Spout2] Camera created.");
        }

        private void InitSpout()
        {
            int w = RendererPlugin.OUTPUT_WIDTH.Value, h = RendererPlugin.OUTPUT_HEIGHT.Value;
            Plugin        = PluginEntry.CreateSender("VRCam", w, h);
            SourceTexture = new RenderTexture(w, h, 24);
            cubeComponent!.RenderTarget = SourceTexture;
        }

        private void SendToSpout()
        {
            if (Plugin == IntPtr.Zero)
            {
                    Debug.LogWarning("[Reso360Spout2] Spout sender not initialized; cannot send frame.");
                    return;
            }

            // ResoniteSpout と同様: まず Update イベントを発行してから SharedTexture を取得する
            SpoutUtil.IssueSenderPluginEvent(PluginEntry.Event.Update, Plugin);

            if (SharedTexture == null)
            {
                var ptr = PluginEntry.GetTexturePointer(Plugin);
                if (ptr != IntPtr.Zero)
                {
                    SharedTexture = Texture2D.CreateExternalTexture(
                        PluginEntry.GetTextureWidth(Plugin),
                        PluginEntry.GetTextureHeight(Plugin),
                        TextureFormat.ARGB32, false, false, ptr);
                    SharedTexture.hideFlags = HideFlags.DontSave;
                }
            }
            if (SharedTexture == null)
            {
                Debug.LogWarning("[Reso360Spout2] Failed to get shared texture pointer from Spout plugin.");
                return;
            } 

            // CommandBuffer の代わりに直接 Graphics 呼び出しを使用 (ResoniteSpout 方式)
            var tempRt = RenderTexture.GetTemporary(
                SharedTexture.width, SharedTexture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(SourceTexture, tempRt, new Vector2(1f, -1f), new Vector2(0f, 1f));
            Graphics.CopyTexture(tempRt, SharedTexture);
            RenderTexture.ReleaseTemporary(tempRt);
        }

        public void ApplyHideLocal()
        {
            if (CameraComponent == null) return;
            if (RendererPlugin.HIDE_LOCAL.Value)
            {
                CameraComponent.cullingMask &= ~((1 << 29) | (1 << 30) | (1 << 31));
                CameraComponent.cullingMask &= ~(1 << 28);
            }
            else
            {
                CameraComponent.cullingMask |= (1 << 29) | (1 << 30) | (1 << 31);
                CameraComponent.cullingMask &= ~(1 << 28);
            }
        }

        public void UpdateSpoutState()
        {
            if (RendererPlugin.SPOUT_ENABLE.Value)
            {
                int w = RendererPlugin.OUTPUT_WIDTH.Value, h = RendererPlugin.OUTPUT_HEIGHT.Value;
                Plugin        = PluginEntry.CreateSender("VRCam", w, h);
                SourceTexture = new RenderTexture(w, h, 24);
                cubeComponent!.RenderTarget = SourceTexture;
            }
            else
            {
                if (Plugin != IntPtr.Zero) SpoutUtil.IssueSenderPluginEvent(PluginEntry.Event.Dispose, Plugin);
                Plugin = IntPtr.Zero;
                SourceTexture = null;
                if (cubeComponent != null) cubeComponent.RenderTarget = null;
            }
        }
    }
}
