namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Immutable options for video track publishing configuration.
    /// </summary>
    /// <remarks>
    ///     Use the fluent <c>WithXxx</c> methods to create modified copies with different values.
    /// </remarks>
    public readonly struct VideoPublishOptions
    {
        /// <summary>
        ///     Default publish options: "unity-scene" track, 1 Mbps, 15 fps, no simulcast.
        /// </summary>
        public static readonly VideoPublishOptions Default = new(
            "unity-scene",
            1_000_000,
            15,
            false,
            VideoTrackSource.ScreenShare
        );

        /// <summary>
        ///     Low bandwidth preset: 500 Kbps, 10 fps.
        /// </summary>
        public static readonly VideoPublishOptions LowBandwidth = new(
            "unity-scene",
            500_000,
            10,
            false,
            VideoTrackSource.ScreenShare
        );

        /// <summary>
        ///     High quality preset: 2 Mbps, 30 fps.
        /// </summary>
        public static readonly VideoPublishOptions HighQuality = new(
            "unity-scene",
            2_000_000,
            30,
            false,
            VideoTrackSource.ScreenShare
        );

        /// <summary>
        ///     Gets the name of the video track as it will appear in the room.
        /// </summary>
        public string TrackName { get; }

        /// <summary>
        ///     Gets the maximum bitrate in bits per second for video encoding.
        /// </summary>
        public int MaxBitrate { get; }

        /// <summary>
        ///     Gets the maximum frame rate for video encoding.
        /// </summary>
        public int MaxFrameRate { get; }

        /// <summary>
        ///     Gets a value indicating whether simulcast is enabled.
        /// </summary>
        public bool Simulcast { get; }

        /// <summary>
        ///     Gets the semantic source of the video track (camera vs screenshare).
        /// </summary>
        public VideoTrackSource Source { get; }

        /// <summary>
        ///     Gets the preferred codec for publishing, when supported by the underlying transport.
        /// </summary>
        public VideoCodec Codec { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="VideoPublishOptions" /> struct.
        /// </summary>
        /// <param name="trackName">Name of the video track.</param>
        /// <param name="maxBitrate">Maximum bitrate in bits per second.</param>
        /// <param name="maxFrameRate">Maximum frame rate.</param>
        /// <param name="simulcast">Whether to enable simulcast.</param>
        /// <param name="source">Semantic source type.</param>
        /// <param name="codec">Preferred codec.</param>
        public VideoPublishOptions(
            string trackName,
            int maxBitrate,
            int maxFrameRate,
            bool simulcast,
            VideoTrackSource source = VideoTrackSource.Unknown,
            VideoCodec codec = VideoCodec.VP8)
        {
            TrackName = trackName ?? "unity-scene";
            MaxBitrate = maxBitrate > 0 ? maxBitrate : 1_000_000;
            MaxFrameRate = maxFrameRate > 0 ? maxFrameRate : 15;
            Simulcast = simulcast;
            Source = source;
            Codec = codec;
        }

        /// <summary>
        ///     Creates a new options instance with a different track name.
        /// </summary>
        public VideoPublishOptions WithTrackName(string trackName) =>
            new(trackName, MaxBitrate, MaxFrameRate, Simulcast, Source, Codec);

        /// <summary>
        ///     Creates a new options instance with a different max bitrate.
        /// </summary>
        public VideoPublishOptions WithMaxBitrate(int maxBitrate) =>
            new(TrackName, maxBitrate, MaxFrameRate, Simulcast, Source, Codec);

        /// <summary>
        ///     Creates a new options instance with a different max frame rate.
        /// </summary>
        public VideoPublishOptions WithMaxFrameRate(int maxFrameRate) =>
            new(TrackName, MaxBitrate, maxFrameRate, Simulcast, Source, Codec);

        /// <summary>
        ///     Creates a new options instance with simulcast enabled or disabled.
        /// </summary>
        public VideoPublishOptions WithSimulcast(bool simulcast) =>
            new(TrackName, MaxBitrate, MaxFrameRate, simulcast, Source, Codec);

        /// <summary>
        ///     Creates a new options instance with a different semantic source.
        /// </summary>
        public VideoPublishOptions WithSource(VideoTrackSource source) =>
            new(TrackName, MaxBitrate, MaxFrameRate, Simulcast, source, Codec);

        /// <summary>
        ///     Creates a new options instance with a different preferred codec.
        /// </summary>
        public VideoPublishOptions WithCodec(VideoCodec codec) =>
            new(TrackName, MaxBitrate, MaxFrameRate, Simulcast, Source, codec);

        /// <inheritdoc />
        public override string ToString() =>
            $"VideoPublishOptions(TrackName={TrackName}, MaxBitrate={MaxBitrate}, MaxFrameRate={MaxFrameRate}, Simulcast={Simulcast}, Source={Source}, Codec={Codec})";
    }

    /// <summary>
    ///     Types of video track sources.
    /// </summary>
    public enum VideoTrackSource
    {
        /// <summary>Camera input.</summary>
        Camera,

        /// <summary>Screen share.</summary>
        ScreenShare,

        /// <summary>Unknown or other source.</summary>
        Unknown
    }

    /// <summary>
    ///     Supported video codecs.
    /// </summary>
    public enum VideoCodec
    {
        /// <summary>VP8 codec.</summary>
        VP8,

        /// <summary>VP9 codec.</summary>
        VP9,

        /// <summary>H.264 codec.</summary>
        H264,

        /// <summary>AV1 codec.</summary>
        AV1
    }
}
