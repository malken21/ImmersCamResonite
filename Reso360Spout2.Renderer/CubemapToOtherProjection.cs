using System.Collections;
using UnityEngine;

namespace Reso360Spout2Renderer
{
    public enum ProjectionType
    {
        Equirectangular_360 = 0,
        Equirectangular_180,
        FishEye_Circumference,
        FishEye_Diagonal
    }

    public enum FishEyeType { Equidistance, EquisolidAngle, Orthogonal }

    [RequireComponent(typeof(Camera))]
    public class CubemapToOtherProjection : MonoBehaviour
    {
        public RenderTexture? RenderTarget;
        public int CubemapSize = 1024;
        public ProjectionType ProjectionType = ProjectionType.Equirectangular_360;
        public FishEyeType FishEyeType = FishEyeType.Equidistance;
        public bool UseUnityInternalCubemapRenderer = false;
        public GammaConvertType GammaConvertType = GammaConvertType.None;
        public bool RenderInStereo = false;
        public float StereoSeparation = 0.065f;
        public bool CorrectCameraPositionInStereoRendering = false;

        // シェーダーはレンダラープラグインから設定される
        public Shader? CubemapShader;
        public Shader? CubemapRendererShader;

        private Camera _camera = null!;
        private Material? _material;
        private RenderTexture? _cubemap;
        private CubemapRenderer? _cubemapRenderer;

        void Start()
        {
            _camera   = GetComponent<Camera>();
            _material = new Material(CubemapShader);

            _cubemap           = new RenderTexture(CubemapSize, CubemapSize, 24, RenderTextureFormat.ARGB32);
            _cubemap.dimension = UnityEngine.Rendering.TextureDimension.Cube;

            if (!UseUnityInternalCubemapRenderer)
                _cubemapRenderer = new CubemapRenderer(CubemapSize, CubemapRendererShader);
        }

        void LateUpdate()
        {
            if (RenderTarget != null) InternalUpdate();
        }

        IEnumerator InternalUpdateAsync()
        {
            yield return new WaitForEndOfFrame();
            InternalUpdate();
        }

        void InternalUpdate()
        {
            if (RenderTarget == null) return;

            switch (ProjectionType)
            {
                case ProjectionType.Equirectangular_360:
                    SetUV(Mathf.PI * 2f, Mathf.PI, 0f, 0f);
                    SetKeyword("PROJ_FISHEYE", false); SetKeyword("ANGLEFUNC_EQUISOLIDANGLE", false); SetKeyword("ANGLEFUNC_ORTHGONAL", false);
                    break;
                case ProjectionType.Equirectangular_180:
                    SetUV(Mathf.PI, Mathf.PI, Mathf.PI * 0.5f, 0f);
                    SetKeyword("PROJ_FISHEYE", false); SetKeyword("ANGLEFUNC_EQUISOLIDANGLE", false); SetKeyword("ANGLEFUNC_ORTHGONAL", false);
                    break;
                case ProjectionType.FishEye_Circumference:
                    SetUV(2f, 2f, -1f, -1f); SetKeyword("PROJ_FISHEYE", true); SetFishEyeScale(1.0f);
                    break;
                case ProjectionType.FishEye_Diagonal:
                    SetUV(2f, 2f, -1f, -1f); SetKeyword("PROJ_FISHEYE", true); SetFishEyeScale(1f / Mathf.Sqrt(2));
                    break;
            }

            switch (FishEyeType)
            {
                case FishEyeType.Equidistance:   SetKeyword("ANGLEFUNC_EQUISOLIDANGLE", false); SetKeyword("ANGLEFUNC_ORTHGONAL", false); break;
                case FishEyeType.EquisolidAngle: SetKeyword("ANGLEFUNC_EQUISOLIDANGLE", true);  SetKeyword("ANGLEFUNC_ORTHGONAL", false); break;
                case FishEyeType.Orthogonal:     SetKeyword("ANGLEFUNC_EQUISOLIDANGLE", false); SetKeyword("ANGLEFUNC_ORTHGONAL", true);  break;
            }

            if (RenderInStereo)
            {
                var tmpSep = _camera.stereoSeparation;
                var tmpEye = _camera.stereoTargetEye;
                _camera.stereoSeparation = StereoSeparation;
                _camera.stereoTargetEye  = StereoTargetEyeMask.None;

                if (ProjectionType == ProjectionType.Equirectangular_360)
                {
                    Render(Camera.MonoOrStereoscopicEye.Left,  1f, 0.5f,  0f, -0.5f);
                    Render(Camera.MonoOrStereoscopicEye.Right, 1f, 0.5f,  0f,  0.5f);
                }
                else
                {
                    Render(Camera.MonoOrStereoscopicEye.Left,  0.5f, 1f, -0.5f, 0f);
                    Render(Camera.MonoOrStereoscopicEye.Right, 0.5f, 1f,  0.5f, 0f);
                }

                _camera.stereoSeparation = tmpSep;
                _camera.stereoTargetEye  = tmpEye;
            }
            else
            {
                Render(Camera.MonoOrStereoscopicEye.Mono, 1f, 1f, 0f, 0f);
            }
        }

        void OnDestroy()
        {
            if (_material != null) { Destroy(_material); _material = null; }
            if (_cubemap  != null) { Destroy(_cubemap);  _cubemap  = null; }
            _cubemapRenderer?.Dispose(); _cubemapRenderer = null;
        }

        void Render(Camera.MonoOrStereoscopicEye eye, float sx, float sy, float ox, float oy)
        {
            if (UseUnityInternalCubemapRenderer)
            {
                ApplyGamma(); _camera.RenderToCubemap(_cubemap, 63, eye);
                SetPositionScale(sx, sy, ox, oy);
                _material!.SetMatrix("_Matrix", Matrix4x4.Rotate(Quaternion.identity));
                Graphics.Blit(_cubemap, RenderTarget, _material);
            }
            else
            {
                float ipd = eye == Camera.MonoOrStereoscopicEye.Left  ? -StereoSeparation / 2f
                          : eye == Camera.MonoOrStereoscopicEye.Right ?  StereoSeparation / 2f : 0f;
                _cubemapRenderer!.RenderCubemap(
                    _camera,
                    ProjectionType == ProjectionType.Equirectangular_360 ? 63 : 63 - (1 << (int)CubemapFace.NegativeZ),
                    ipd, GammaConvertType, CorrectCameraPositionInStereoRendering);
                SetPositionScale(sx, sy, ox, oy);
                _material!.SetMatrix("_Matrix", Matrix4x4.identity);
                SetKeyword("LINEAR_TO_SRGB", false); SetKeyword("LINEAR_TO_BT709", false);
                Graphics.Blit(_cubemapRenderer.Cubemap, RenderTarget, _material);
            }
        }

        void ApplyGamma()
        {
            switch (GammaConvertType)
            {
                case GammaConvertType.Linear_to_sRGB:  SetKeyword("LINEAR_TO_SRGB", true);  SetKeyword("LINEAR_TO_BT709", false); break;
                case GammaConvertType.Linear_to_BT709: SetKeyword("LINEAR_TO_SRGB", false); SetKeyword("LINEAR_TO_BT709", true);  break;
                default:                               SetKeyword("LINEAR_TO_SRGB", false); SetKeyword("LINEAR_TO_BT709", false); break;
            }
        }

        void SetUV(float sx, float sy, float ox, float oy)
            => _material?.SetVector("_UVScaleOffset", new Vector4(sx, sy, ox, oy));
        void SetPositionScale(float sx, float sy, float ox, float oy)
            => _material?.SetVector("_PositionScaleOffset", new Vector4(sx, sy, ox, oy));
        void SetFishEyeScale(float s)
            => _material?.SetFloat("_FishEyeDiameterScale", s);
        void SetKeyword(string keyword, bool flag)
        {
            if (_material == null) return;
            if (flag) _material.EnableKeyword(keyword); else _material.DisableKeyword(keyword);
        }
    }
}
