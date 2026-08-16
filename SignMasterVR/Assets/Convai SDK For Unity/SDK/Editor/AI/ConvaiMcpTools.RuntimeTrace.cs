using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Convai.Editor.AI
{
    public static partial class ConvaiMcpTools
    {
        private const string TraceRuntimeEventsTool = "Convai.TraceRuntimeEvents";

        [McpTool(TraceRuntimeEventsTool, "Starts, reads, clears, or stops a bounded editor-only Convai runtime event trace. Transcript capture is off by default.", "Trace Convai Runtime Events", Groups = new[] { "convai", "runtime", "diagnostics" }, EnabledByDefault = true)]
        public static object TraceRuntimeEvents(JObject parameters) =>
            TraceRuntimeEvents(Parse<ConvaiRuntimeTraceRequest>(parameters));

        public static object TraceRuntimeEvents(ConvaiRuntimeTraceRequest request) =>
            ConvaiRuntimeEventTrace.Execute(request);

        [McpSchema(TraceRuntimeEventsTool)]
        public static object TraceRuntimeEventsInputSchema() => ObjectSchema(
            new Dictionary<string, object>
            {
                ["operation"] = EnumProperty("Trace operation.", ConvaiRuntimeTraceOperation.Read),
                ["managerInstanceId"] = IntegerProperty("Optional active-scene manager instance ID.", 0),
                ["characterInstanceId"] = IntegerProperty("Optional active-scene character instance ID filter.", 0),
                ["eventFilters"] = new { type = "array", items = new { type = "string" }, description = "Optional event type or category filters." },
                ["limit"] = new { type = "integer", minimum = 1, maximum = ConvaiRuntimeEventTrace.Capacity, @default = 100 },
                ["captureTranscripts"] = BooleanProperty("Capture transcript events and text. Off by default.", false)
            });

        [McpOutputSchema(TraceRuntimeEventsTool)]
        public static object TraceRuntimeEventsOutputSchema() => StandardResponseSchema();
    }
}
