using System;
using System.IO.MemoryMappedFiles;
using UnityEngine;

namespace Reso360Spout2Renderer
{
    /// <summary>
    /// ホストプロセスが書き込んだカメラ状態を共有メモリから読み取る。
    /// 構造: 10 floats (40 bytes)
    ///   [0-11]  position (x, y, z)
    ///   [12-27] rotation (x, y, z, w)
    ///   [28-39] scale    (x, y, z)
    /// </summary>
    public class SharedMemoryReader : IDisposable
    {
        public const string MAP_NAME = "Reso360Spout2_Camera";
        private const int   MAP_SIZE = 40;

        private MemoryMappedFile?         _mmf;
        private MemoryMappedViewAccessor? _view;
        private bool _disposed;

        public bool IsOpen => _view != null;

        public bool TryOpen()
        {
            if (IsOpen) return true;
            try
            {
                _mmf  = MemoryMappedFile.CreateOrOpen(MAP_NAME, MAP_SIZE);
                _view = _mmf.CreateViewAccessor(0, MAP_SIZE, MemoryMappedFileAccess.ReadWrite);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public Vector3 ReadPosition()
        {
            if (_view == null) return Vector3.zero;
            return new Vector3(
                _view.ReadSingle(0),
                _view.ReadSingle(4),
                _view.ReadSingle(8));
        }

        public Quaternion ReadRotation()
        {
            if (_view == null) return Quaternion.identity;
            var q = new Quaternion(
                _view.ReadSingle(12),
                _view.ReadSingle(16),
                _view.ReadSingle(20),
                _view.ReadSingle(24));
            // 未初期化(全0)の場合は identity を返す
            return (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f)
                ? Quaternion.identity : q;
        }

        public Vector3 ReadScale()
        {
            if (_view == null) return Vector3.one;
            var s = new Vector3(
                _view.ReadSingle(28),
                _view.ReadSingle(32),
                _view.ReadSingle(36));
            // 未初期化(全0)の場合は Vector3.one を返す
            return s.sqrMagnitude < 1e-6f ? Vector3.one : s;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _view?.Dispose();
            _mmf?.Dispose();
        }
    }
}
