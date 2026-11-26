using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewUAFGJ;

internal sealed record ReplacementOperation(string Replacement, long? PathId = null, string? FileId = null,
    string? TypeTreeSource = null, bool Raw = false, bool ResizeAtlas = false,
    bool SyncAtlasMaterials = false, string? Entry = null, string? Context = null)
{
    internal void Validate()
    {
        if (FileId is not (null or "-" or "0"))
            throw new ArgumentException("FileID must be '-' or '0'. External file references are unsupported.");
        if (Raw && (ResizeAtlas || TypeTreeSource is not null))
            throw new ArgumentException("--raw cannot be combined with an atlas resize or a TypeTree source.");
        if (SyncAtlasMaterials && !ResizeAtlas)
            throw new ArgumentException("--sync-atlas-materials requires --resize-atlas.");
        if (Entry is "")
            throw new ArgumentException("A serialized entry name cannot be empty.");
    }
}

internal sealed record SourceBatch(string Source, ReplacementOperation[] Operations, string? Output = null,
    string? Name = null, bool DeferCompression = false, string? DiscoveryPath = null);

internal sealed record BatchPlan(SourceBatch[] Sources, string[]? TypeTreeRoots = null, int MaxWorkers = 1,
    double SameSourceDelaySeconds = 0.0);

internal static class BatchCommand
{
    internal static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private const string ResultPrefix = "[BATCH] ";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static int Run(string planPath, bool printProfile, long started)
    {
        planPath = Path.GetFullPath(planPath);
        string directory = Path.GetDirectoryName(planPath)!;
        using var input = File.OpenRead(planPath);
        BatchPlan plan = JsonSerializer.Deserialize<BatchPlan>(input, Json)
            ?? throw new InvalidDataException("The batch plan is empty.");
        if (plan.Sources is not { Length: > 0 })
            throw new InvalidDataException("The batch plan requires at least one source.");
        if (plan.MaxWorkers < 1)
            throw new ArgumentOutOfRangeException(nameof(plan.MaxWorkers), "Worker count must be positive.");
        if (!double.IsFinite(plan.SameSourceDelaySeconds) || plan.SameSourceDelaySeconds is < 0 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(plan.SameSourceDelaySeconds), "Cooldown must be between 0 and 3600 seconds.");
        TimeSpan cooldown = TimeSpan.FromSeconds(plan.SameSourceDelaySeconds);

        string FullPath(string path) => Path.GetFullPath(path, directory);
        var inputs = new HashSet<string>(PathComparer);
        var outputs = new HashSet<string>(PathComparer);
        var jobs = new SourceBatch[plan.Sources.Length];
        for (int index = 0; index < plan.Sources.Length; index++)
        {
            var job = plan.Sources[index] ?? throw new InvalidDataException("A batch source cannot be null.");
            if (job.Operations is not { Length: > 0 })
                throw new InvalidDataException($"The batch for '{job.Source}' requires at least one replacement.");
            string source = FullPath(job.Source);
            string output = job.Output is null ? source : FullPath(job.Output);
            if (!inputs.Add(source) || !outputs.Add(output))
                throw new InvalidDataException($"The batch contains a duplicate source or output for '{source}'.");
            jobs[index] = job with
            {
                Source = source,
                Output = output,
                DiscoveryPath = job.DiscoveryPath is { } original ? FullPath(original) : null,
                Operations = job.Operations.Select(operation =>
                {
                    if (operation is null)
                        throw new InvalidDataException($"A replacement for '{source}' cannot be null.");
                    return operation with
                    {
                        Replacement = FullPath(operation.Replacement),
                        TypeTreeSource = operation.TypeTreeSource is { } tree ? FullPath(tree) : null
                    };
                }).ToArray()
            };
        }

        foreach (SourceBatch job in jobs)
        {
            if (!PathComparer.Equals(job.Source, job.Output!) && inputs.Contains(job.Output!))
                throw new InvalidDataException($"A batch output '{job.Output}' is another source.");
            foreach (ReplacementOperation operation in job.Operations)
            {
                if (outputs.Contains(operation.Replacement) ||
                    (operation.TypeTreeSource is { } tree && outputs.Contains(tree) && !inputs.Contains(tree)))
                {
                    throw new InvalidDataException($"A batch output would overwrite an input to '{job.Source}'.");
                }
            }
        }

        string[] roots = plan.TypeTreeRoots?.Select(FullPath).ToArray()
            ?? jobs.Select(job => ReplacementTypeTree.GameRoot(job.Source)).Distinct(PathComparer).ToArray();
        var aliases = jobs.Where(job => job.DiscoveryPath is not null)
            .ToDictionary(job => job.DiscoveryPath!, job => job.Source, PathComparer);
        var resolver = new ReplacementTypeTree(roots, jobs.SelectMany(job => job.Operations)
            .Select(operation => operation.TypeTreeSource).OfType<string>(), aliases);
        var textures = new TextureEncodingCache(jobs.SelectMany(job => job.Operations)
            .Where(operation => !operation.Raw).GroupBy(operation => operation.Replacement, PathComparer)
            .Where(group => group.Skip(1).Any()).Select(group => group.Key));
        var session = new ReplacementSession(resolver, textures);
        double initialization = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        int failures = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = plan.MaxWorkers };
        Parallel.For(0, jobs.Length, options, (index, state) =>
        {
            SourceBatch job = jobs[index];
            string label = job.Name ?? job.Source;
            string prefix = $"[{index + 1}/{jobs.Length}] {label}";
            Console.WriteLine($"{prefix} ({job.Operations.Length} replacements)");
            var profile = new PerformanceProfile(Stopwatch.GetTimestamp());
            PerformanceProfile.Current = profile;
            bool success = false;
            string? failure = null;
            try
            {
                AssetReplacement.ReplaceBatch(job.Source, job.Output!, job.Operations, session, cooldown,
                    (operationIndex, operation) =>
                    {
                        string pathId = operation.PathId?.ToString() ?? "auto";
                        string fileId = operation.FileId is not (null or "-") ? $", FileID {operation.FileId}" : "";
                        Console.WriteLine($"{prefix} [{operationIndex + 1}/{job.Operations.Length}] "
                            + $"PathID {pathId}{fileId} ← {Path.GetFileName(operation.Replacement)}");
                    }, job.DeferCompression);
                success = true;
            }
            catch (Exception error)
            {
                failure = error.Message;
                Console.Error.WriteLine($"[FATAL] {label}{Environment.NewLine}{failure}");
                Interlocked.Increment(ref failures);
                state.Stop();
            }
            finally
            {
                PrintResult(label, success, job.Operations.Length, profile, printProfile, failure);
                PerformanceProfile.Current = null;
            }
        });
        Console.WriteLine(FormattableString.Invariant($"Process initialization {initialization:F2} ms"));
        return failures == 0 ? 0 : 1;
    }

    internal static int Repack(string source, string reference, bool printProfile, long started)
    {
        var profile = new PerformanceProfile(started);
        PerformanceProfile.Current = profile;
        profile.Record(PerformanceProfile.Initialization, started);
        bool success = false;
        try
        {
            AssetReplacement.MatchBundleCompression(source, reference);
            success = true;
            return 0;
        }
        finally
        {
            PrintResult(source, success, 0, profile, printProfile);
            PerformanceProfile.Current = null;
        }
    }

    private static void PrintResult(string source, bool success, int operations, PerformanceProfile profile,
        bool printProfile, string? error = null)
    {
        var report = new
        {
            source, success, operations, error,
            totalMs = profile.TotalMilliseconds,
            timings = profile.Timings,
            details = printProfile ? profile.Details : null
        };
        Console.WriteLine(ResultPrefix + JsonSerializer.Serialize(report, Json));
    }
}

internal sealed record ReplacementSession(ReplacementTypeTree TypeTrees, TextureEncodingCache? Textures = null);
