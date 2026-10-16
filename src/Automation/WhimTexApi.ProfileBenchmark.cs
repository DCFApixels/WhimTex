using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        // Routed by Profile for all benchmark* actions. The response is independent of capture/baseline:
        // {apiVersion:1, success:true, benchmark:null|{benchmarkId,documentId,state,phase,running,
        // mode,repetitions,warmups,maxSize,completedSamples,totalSamples,completedWarmups,stopReason,error,
        // elapsedMs,partial,metadata,cold:{samplesMs,statistics},warm:{samplesMs,statistics}}}.
        // state: running|completed|cancelled|timedOut|interrupted|failed. phase: snapshot|cold|warmup|warm.
        // statistics: null or {count,medianMs,minMs,maxMs,meanMs,p95Ms,stddevMs}.
        // Failed runs remain successful API reads; inspect benchmark.state/error for execution failures.
        private static JObject ProfileBenchmark(JObject request, string action)
        {
            JToken benchmark;
            switch (action)
            {
                case "benchmarkStart":
                    Keys(request, "apiVersion", "action", "documentId", "mode", "repetitions", "warmups", "maxSize");
                    benchmark = WhimTexBenchmark.Start(Text(request, "documentId"), Text(request, "mode", "both"),
                        Int(request, "repetitions", WhimTexBenchmark.DefaultRepetitions, 1, WhimTexBenchmark.MaximumRepetitions),
                        Int(request, "warmups", WhimTexBenchmark.DefaultWarmups, 0, WhimTexBenchmark.MaximumWarmups),
                        Int(request, "maxSize", WhimTexBenchmark.DefaultMaxSize, 1, WhimTexBenchmark.MaximumSize));
                    break;
                case "benchmarkStatus":
                    Keys(request, "apiVersion", "action", "benchmarkId");
                    benchmark = WhimTexBenchmark.Status(Text(request, "benchmarkId"));
                    break;
                case "benchmarkCancel":
                case "benchmarkRead":
                    Keys(request, "apiVersion", "action", "benchmarkId");
                    string id = Text(request, "benchmarkId");
                    Require(!string.IsNullOrWhiteSpace(id), "benchmarkId is required.");
                    benchmark = action == "benchmarkCancel" ? WhimTexBenchmark.Cancel(id) : WhimTexBenchmark.Status(id);
                    break;
                default:
                    throw new WhimTexApiException("invalid_request", "Unknown benchmark action. Use benchmarkStart, benchmarkStatus, benchmarkCancel or benchmarkRead.");
            }
            var result = Success(); result["benchmark"] = benchmark;
            return result;
        }
    }
}
