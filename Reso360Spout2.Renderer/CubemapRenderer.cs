using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reso360Spout2Renderer
{
    public enum GammaConvertType { None = 0, Linear_to_sRGB, Linear_to_BT709 }

    public class CubemapRenderer : IDisposable
    {
        private Shader? _shader;
        private Material? _material;
        private Mesh? _mesh;
        private readonly int _cubemapSize;
        private RenderTexture? _cubemap;
        private RenderTexture? _tempRT;
        // BuiltinRenderTextureType.CameraTarget はレンダラープロセスの VR コンテキストで
        // VR 共有テクスチャに解決されクラッシュするため、_tempRT 確定後に再構築する
        private CommandBuffer[]? _commandBuffers;

        struct FaceInfo
        {
            public Quaternion Rotate;
            public CubemapFace Face;
            public Vector3 PositionShift;
            public FaceInfo(Quaternion r, CubemapFace f, Vector3 p) { Rotate = r; Face = f; PositionShift = p; }
        }

        private static readonly FaceInfo[] _faces =
        {
            new FaceInfo(Quaternion.Euler(  0f,  90f, 0f), CubemapFace.PositiveX, new Vector3( 0f,  1f, 0f)),
            new FaceInfo(Quaternion.Euler(  0f, -90f, 0f), CubemapFace.NegativeX, new Vector3( 0f, -1f, 0f)),
            new FaceInfo(Quaternion.Euler(-90f,   0f, 0f), CubemapFace.PositiveY, Vector3.zero),
            new FaceInfo(Quaternion.Euler( 90f,   0f, 0f), CubemapFace.NegativeY, Vector3.zero),
            new FaceInfo(Quaternion.Euler(  0f,   0f, 0f), CubemapFace.PositiveZ, new Vector3( 1f,  0f, 0f)),
            new FaceInfo(Quaternion.Euler(  0f, 180f, 0f), CubemapFace.NegativeZ, new Vector3(-1f,  0f, 0f)),
        };

        private static readonly Vector3[] _meshVertices =
        {
            new Vector3( 1f,  1f, 0f),
            new Vector3(-1f,  1f, 0f),
            new Vector3(-1f, -1f, 0f),
            new Vector3( 1f, -1f, 0f),
        };
        private static readonly int[] _meshIndices = { 0, 1, 2, 2, 3, 0 };

        public CubemapRenderer(int cubemapSize, Shader cubemapRendererShader)
        {
            _cubemapSize = cubemapSize;
            _shader   = cubemapRendererShader;
            _material = new Material(_shader);
            _mesh = new Mesh { vertices = _meshVertices, triangles = _meshIndices };

            _cubemap           = new RenderTexture(cubemapSize, cubemapSize, 0, RenderTextureFormat.ARGB32);
            _cubemap.dimension = TextureDimension.Cube;
            _cubemap.Create();
            // コマンドバッファは _tempRT 確定後 (RebuildCommandBuffers) に作成する
        }

        public void Dispose()
        {
            DisposeCommandBuffers();
            if (_material != null) { UnityEngine.Object.Destroy(_material); _material = null; }
            if (_mesh     != null) { UnityEngine.Object.Destroy(_mesh);     _mesh     = null; }
            if (_tempRT   != null) { UnityEngine.Object.Destroy(_tempRT);   _tempRT   = null; }
            if (_cubemap  != null) { UnityEngine.Object.Destroy(_cubemap);  _cubemap  = null; }
        }

        private void DisposeCommandBuffers()
        {
            if (_commandBuffers == null) return;
            foreach (var c in _commandBuffers) c.Dispose();
            _commandBuffers = null;
        }

        /// <summary>
        /// _tempRT を直接参照するコマンドバッファを構築する。
        /// BuiltinRenderTextureType.CameraTarget の代わりに使うことで VR クラッシュを回避する。
        /// </summary>
        private void RebuildCommandBuffers()
        {
            DisposeCommandBuffers();
            var tid = Shader.PropertyToID("_MainTex");
            _commandBuffers = new CommandBuffer[_faces.Length];
            for (int i = 0; i < _faces.Length; i++)
            {
                var cb = new CommandBuffer();
                // _tempRT を直接指定 (VR 共有テクスチャへの誤解決を防ぐ)
                cb.SetGlobalTexture(tid, _tempRT);
                cb.SetRenderTarget(_cubemap, 0, _faces[i].Face);
                cb.DrawMesh(_mesh!, Matrix4x4.identity, _material!, 0, 0);
                _commandBuffers[i] = cb;
            }
        }

        public void RenderCubemap(Camera camera, int faceMask, float ipdOffset, GammaConvertType gammaConvert, bool correctPosition)
        {
            // _tempRT のフォーマットが変わった場合は再生成
            if (_tempRT != null)
            {
                var expected = camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
                if (_tempRT.format != expected) { UnityEngine.Object.Destroy(_tempRT); _tempRT = null; }
            }
            if (_tempRT == null)
            {
                _tempRT = new RenderTexture(_cubemapSize, _cubemapSize, 24,
                    camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);
                _tempRT.dimension = TextureDimension.Tex2D;
                _tempRT.Create();
                // _tempRT が新規作成/変更されたのでコマンドバッファを再構築
                RebuildCommandBuffers();
            }

            switch (gammaConvert)
            {
                case GammaConvertType.Linear_to_sRGB:  SetKeyword("LINEAR_TO_SRGB", true);  SetKeyword("LINEAR_TO_BT709", false); break;
                case GammaConvertType.Linear_to_BT709: SetKeyword("LINEAR_TO_SRGB", false); SetKeyword("LINEAR_TO_BT709", true);  break;
                default:                               SetKeyword("LINEAR_TO_SRGB", false); SetKeyword("LINEAR_TO_BT709", false); break;
            }

            var orgRot    = camera.transform.localRotation;
            var orgPos    = camera.transform.localPosition;
            var orgTarget = camera.targetTexture;
            var orgOrtho  = camera.orthographic;
            var orgAspect = camera.aspect;
            var orgFov    = camera.fieldOfView;

            camera.orthographic  = false;
            camera.aspect        = 1.0f;
            camera.fieldOfView   = 90.0f;
            camera.targetTexture = _tempRT;

            try
            {
                for (int i = 0; i < _commandBuffers!.Length; i++)
                {
                    if ((faceMask & (1 << (int)_faces[i].Face)) == 0) continue;
                    camera.AddCommandBuffer(CameraEvent.AfterEverything, _commandBuffers[i]);
                    try
                    {
                        camera.transform.localRotation = orgRot * _faces[i].Rotate;
                        camera.transform.localPosition = orgPos +
                            (correctPosition ? _faces[i].PositionShift * ipdOffset : Vector3.right * ipdOffset);
                        camera.Render();
                    }
                    finally { camera.RemoveCommandBuffer(CameraEvent.AfterEverything, _commandBuffers[i]); }
                }
            }
            finally
            {
                camera.transform.localRotation = orgRot;
                camera.transform.localPosition = orgPos;
                camera.targetTexture           = orgTarget;
                camera.orthographic            = orgOrtho;
                camera.aspect                  = orgAspect;
                camera.fieldOfView             = orgFov;
            }
        }

        public RenderTexture? Cubemap => _cubemap;

        private void SetKeyword(string keyword, bool flag)
        {
            if (_material == null) return;
            if (flag) _material.EnableKeyword(keyword); else _material.DisableKeyword(keyword);
        }
    }
}
