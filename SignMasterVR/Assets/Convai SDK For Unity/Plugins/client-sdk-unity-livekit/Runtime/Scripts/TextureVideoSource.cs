using UnityEngine;
using LiveKit.Proto;
using UnityEngine.Rendering;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;
using System;

namespace LiveKit
{
    public class TextureVideoSource : RtcVideoSource
    {
        TextureFormat _textureFormat;
        // Readback-compatible intermediate. Lets AsyncGPUReadback run against a known
        // ReadPixels-compatible format even when the source texture's format isn't directly
        // readable, and decouples the copy from the source format (CopyTexture requires an
        // exact format match; Blit does not).
        private RenderTexture _readbackRT;

        public Texture Texture { get; }

        public override int GetWidth()
        {
            return Texture.width;
        }

        public override int GetHeight()
        {
            return Texture.height;
        }

        protected override VideoRotation GetVideoRotation()
        {
            return VideoRotation._0;
        }

        public TextureVideoSource(Texture texture, VideoBufferType bufferType = VideoBufferType.Rgba) : base(VideoStreamSource.Texture, bufferType)
        {
            Texture = texture;
            base.Init();
        }

        public TextureVideoSource(Texture texture, int targetFrameRate, VideoBufferType bufferType = VideoBufferType.Rgba)
            : base(VideoStreamSource.Texture, bufferType)
        {
            Texture = texture;
            TargetFrameRate = targetFrameRate > 0 ? targetFrameRate : 30;
            base.Init();
        }

        ~TextureVideoSource()
        {
            Dispose(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _readbackRT != null)
            {
                _readbackRT.Release();
                UnityEngine.Object.Destroy(_readbackRT);
                _readbackRT = null;
            }
            base.Dispose(disposing);
        }

        // Read the texture data into a native array asynchronously
        protected override bool ReadBuffer()
        {
            if (_reading)
                return false;
            _reading = true;
            var textureChanged = false;

            if (_previewTexture == null || _previewTexture.width != GetWidth() || _previewTexture.height != GetHeight()) {
                var compatibleFormat = SystemInfo.GetCompatibleFormat(Texture.graphicsFormat, FormatUsage.ReadPixels);
                _textureFormat = GraphicsFormatUtility.GetTextureFormat(compatibleFormat);
                _bufferType = GetVideoBufferType(_textureFormat);
                _captureBuffer = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(_bufferType), Allocator.Persistent);
                _previewTexture = new Texture2D(GetWidth(), GetHeight(), _textureFormat, false);
                if (_readbackRT != null)
                {
                    _readbackRT.Release();
                    UnityEngine.Object.Destroy(_readbackRT);
                }
                _readbackRT = new RenderTexture(GetWidth(), GetHeight(), 0, compatibleFormat);
                textureChanged = true;
            }
            // Straight (non-flipping) blit into a readback-compatible RT. Do NOT vertically flip
            // here: Convai vision frame sources (CameraVisionFrameSource / WebcamVisionFrameSource)
            // already own orientation — they apply the WebRTC top-down correction AND webcam
            // rotation/mirroring, which a flat flip here cannot. A flip at this stage double-flips
            // the published frame (frames arrive upside down at the backend vision model). The
            // intermediate RT is still required for format-compatible AsyncGPUReadback. If a
            // future LiveKit SDK bump reintroduces a flip on this Blit, remove it again. See git b692bd5d.
            Graphics.Blit(Texture, _readbackRT);
            Graphics.CopyTexture(_readbackRT, _previewTexture);
            AsyncGPUReadback.RequestIntoNativeArray(ref _captureBuffer, _readbackRT, 0, _textureFormat, OnReadback);
            return textureChanged;
        }
    }
}
