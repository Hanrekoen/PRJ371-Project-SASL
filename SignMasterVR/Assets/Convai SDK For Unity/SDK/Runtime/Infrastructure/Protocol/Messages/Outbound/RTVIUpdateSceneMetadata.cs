using System.Collections.Generic;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>Outbound scene metadata update message.</summary>
    public class RTVIUpdateSceneMetadata : RTVISendMessageBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RTVIUpdateSceneMetadata" /> class.
        /// </summary>
        /// <param name="sceneMetadata">Scene metadata entries to send.</param>
        public RTVIUpdateSceneMetadata(List<SceneMetadata> sceneMetadata)
        {
            Type = "update-scene-metadata";
            Data = sceneMetadata;
        }
    }
}
