using System;
using LiveKit;
using UnityEngine;

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeTextureVideoSource : ITextureVideoSource
    {
        private bool _disposed;
        private int _targetFrameRate = 30;
        private Coroutine _updateCoroutine;

        public NativeTextureVideoSource(Texture texture, int targetFrameRate = 30, string name = null)
        {
            SourceTexture = texture ?? throw new ArgumentNullException(nameof(texture));
            TargetFrameRate = targetFrameRate;
            Name = string.IsNullOrWhiteSpace(name) ? texture.name : name;
            UnderlyingSource = new TextureVideoSource(texture, TargetFrameRate);
        }

        internal TextureVideoSource UnderlyingSource { get; private set; }

        public string Name { get; }

        public bool IsCapturing { get; private set; }

        public int Width => SourceTexture != null ? SourceTexture.width : 0;

        public int Height => SourceTexture != null ? SourceTexture.height : 0;

        public Texture SourceTexture { get; private set; }

        public int TargetFrameRate
        {
            get => _targetFrameRate;
            set
            {
                _targetFrameRate = value > 0 ? value : 30;

                if (UnderlyingSource != null)
                    UnderlyingSource.TargetFrameRate = _targetFrameRate;
            }
        }

        public void SetTexture(Texture texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));

            if (ReferenceEquals(SourceTexture, texture)) return;

            bool wasCapturing = IsCapturing;
            StopCapture();

            UnderlyingSource?.Dispose();
            SourceTexture = texture;
            UnderlyingSource = new TextureVideoSource(texture, TargetFrameRate);

            if (wasCapturing) StartCapture();
        }

        public void StartCapture()
        {
            ThrowIfDisposed();

            if (IsCapturing) return;

            UnderlyingSource.TargetFrameRate = TargetFrameRate;
            UnderlyingSource.Start();
            _updateCoroutine = NativeCoroutineRunner.Run(UnderlyingSource.Update());
            IsCapturing = true;
        }

        public void StopCapture()
        {
            if (!IsCapturing) return;

            UnderlyingSource.Stop();
            NativeCoroutineRunner.Stop(_updateCoroutine);
            _updateCoroutine = null;
            IsCapturing = false;
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopCapture();
            UnderlyingSource?.Dispose();
            UnderlyingSource = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeTextureVideoSource));
        }
    }
}
