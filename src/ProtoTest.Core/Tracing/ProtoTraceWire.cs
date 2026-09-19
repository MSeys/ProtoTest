namespace ProtoTest.Core;

/// <summary>
/// Serializes a run into the two wire documents of the v2 tracer: a span tree (operations with their
/// events, sections and record items) and one state map (tracked items with their changes). The snapshot
/// model stays the source of truth; these documents are what the archive and any consumer read.
/// </summary>
internal static class ProtoTraceWire
{
    public static object Spans(ProtoTraceRun run)
    {
        var groups = new List<object>();
        foreach (var test in run.Tests)
        {
            groups.Add(ResourceGroup(
                new
                {
                    testId = test.TestId,
                    testName = test.Name,
                    testClass = test.ClassName,
                    testMethod = test.MethodName,
                    testOutcome = Lower(test.Outcome),
                    testDurationMs = test.Duration.TotalMilliseconds
                },
                test.Entries,
                test.Record,
                test.Artifacts));
        }

        groups.Add(ResourceGroup(
            RunAttributes(run),
            run.Entries ?? [],
            run.Record,
            run.Artifacts));

        return new { formatVersion = "2.0", resourceSpans = groups };
    }

    public static object State(ProtoTraceRun run) => new
    {
        // 1.1: tracked values carry the generic kind "value" with the domain type in the id
        // (`{type}:{identity}`); 1.0 wrote the domain type as the kind.
        formatVersion = "1.1",
        run = new { items = Items(run.Entities, run.Values) },
        tests = run.Tests.Select(test => new
        {
            testId = test.TestId,
            name = test.Name,
            items = Items(test.Entities, test.Values)
        }).ToArray()
    };

    /// <summary>
    /// The run's resource: its identity and timing, and the environment it ran in as flat
    /// <c>environment.*</c> attributes, the same string map every other resource uses.
    /// </summary>
    private static Dictionary<string, object?> RunAttributes(ProtoTraceRun run)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["runId"] = run.RunId,
            ["runStartedAtUtc"] = run.StartedAtUtc,
            ["runCompletedAtUtc"] = run.CompletedAtUtc
        };
        foreach (var (key, value) in run.Environment)
        {
            attributes[$"environment.{key}"] = value;
        }

        return attributes;
    }

    private static object ResourceGroup(
        object attributes,
        IReadOnlyList<ProtoTraceEntry> entries,
        ProtoTraceRecord? record,
        IReadOnlyList<ProtoTraceArtifact>? artifacts)
    {
        var events = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var orphans = new List<object>();
        // A timeline event with no operation above it - a run's gate verdicts - belongs to the scope, the
        // same place record items without an operation go; nothing on the timeline may drop off the wire.
        var operations = entries
            .Where(entry => entry.EntryKind == ProtoTraceEntryKind.Operation)
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        orphans.AddRange(entries
            .Where(entry => entry.EntryKind == ProtoTraceEntryKind.Event
                && (entry.ParentId is null || !operations.Contains(entry.ParentId)))
            .Select(TimelineEvent));
        AddRecordEvents(events, orphans, record);
        var spans = entries
            .Where(entry => entry.EntryKind == ProtoTraceEntryKind.Operation)
            .Select(entry => new
            {
                spanId = entry.Id,
                parentSpanId = entry.ParentId,
                name = entry.Name,
                kind = entry.Kind,
                source = entry.Source,
                phase = Lower(entry.Phase),
                status = Lower(entry.Outcome),
                error = entry.Error is null ? null : new { type = entry.Error.Type, message = entry.Error.Message },
                startedAtUtc = entry.TimestampUtc,
                durationMs = entry.Duration?.TotalMilliseconds,
                count = entry.Count == 1 ? (int?)null : entry.Count,
                entityKind = entry.EntityKind,
                entityId = entry.EntityId,
                attributes = entry.Attributes,
                sections = entry.Sections,
                events = ChildEvents(entry.Id, entries, events)
            })
            .ToArray();

        return new
        {
            resource = new { attributes },
            // The one declaration of each artifact; attachment events refer to it by artifactId.
            artifacts = (artifacts ?? []).Select(artifact => new
            {
                id = artifact.Id,
                name = artifact.Name,
                mediaType = artifact.MediaType,
                description = artifact.Description,
                archivePath = artifact.ArchivePath,
                sizeBytes = artifact.SizeBytes,
                error = artifact.Error
            }).ToArray(),
            scopeSpans = new[]
            {
                new
                {
                    scope = new { name = "ProtoTest", version = "1.0" },
                    spans,
                    events = orphans.Count == 0 ? null : orphans
                }
            }
        };
    }

    private static object[] ChildEvents(
        string spanId,
        IReadOnlyList<ProtoTraceEntry> entries,
        IReadOnlyDictionary<string, List<object>> recordEvents)
    {
        var events = new List<object>();
        foreach (var entry in entries)
        {
            if (entry.EntryKind != ProtoTraceEntryKind.Event
                || !string.Equals(entry.ParentId, spanId, StringComparison.Ordinal))
            {
                continue;
            }

            events.Add(TimelineEvent(entry));
        }

        if (recordEvents.TryGetValue(spanId, out var recorded))
        {
            events.AddRange(recorded);
        }

        return [.. events];
    }

    private static object TimelineEvent(ProtoTraceEntry entry) => new
    {
        name = entry.Name,
        atUtc = entry.TimestampUtc,
        kind = entry.Kind,
        source = entry.Source,
        outcome = Lower(entry.Outcome),
        error = entry.Error is null ? null : new { type = entry.Error.Type, message = entry.Error.Message },
        entityKind = entry.EntityKind,
        entityId = entry.EntityId,
        attributes = entry.Attributes,
        sections = entry.Sections
    };

    private static void AddRecordEvents(
        Dictionary<string, List<object>> byOperation,
        List<object> orphans,
        ProtoTraceRecord? record)
    {
        void Add(string? operationId, object value)
        {
            if (string.IsNullOrEmpty(operationId))
            {
                orphans.Add(value);
                return;
            }

            if (!byOperation.TryGetValue(operationId, out var list))
            {
                byOperation[operationId] = list = [];
            }

            list.Add(value);
        }

        foreach (var observation in record?.Observations ?? [])
        {
            Add(observation.OperationId, new
            {
                record = "observation",
                name = observation.TargetName,
                atUtc = observation.AtUtc,
                kind = observation.Kind,
                identifier = observation.Identifier,
                data = observation.Data,
                metadata = observation.Metadata
            });
        }

        foreach (var attachment in record?.Attachments ?? [])
        {
            Add(attachment.OperationId, new
            {
                record = "attachment",
                name = attachment.Name,
                atUtc = attachment.AtUtc,
                // Media type, path, size and any capture error live on the artifact this refers to.
                artifactId = attachment.ArtifactId
            });
        }

        foreach (var finding in record?.Findings ?? [])
        {
            Add(finding.OperationId, new
            {
                record = "finding",
                name = finding.Message,
                atUtc = finding.AtUtc,
                status = finding.Status,
                category = finding.Category,
                targetName = finding.TargetName,
                tags = finding.Tags,
                metadata = finding.Metadata
            });
        }
    }

    private static object[] Items(
        IReadOnlyList<ProtoTraceEntity>? entities,
        IReadOnlyList<ProtoTraceValue>? values)
    {
        var items = new Dictionary<string, (string Kind, string Id, string Name, string? Scope, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc, IReadOnlyDictionary<string, string?> State, List<object> Changes)>(StringComparer.Ordinal);

        foreach (var entity in entities ?? [])
        {
            items[Key(entity.Kind, entity.Id)] = (
                entity.Kind,
                entity.Id,
                entity.Name,
                entity.Scope,
                entity.FirstSeenUtc,
                entity.LastSeenUtc,
                entity.State,
                [.. (entity.Versions ?? []).Select(Change)]);
        }

        foreach (var value in values ?? [])
        {
            var changes = value.Versions.Select(Change).ToList();
            var key = Key(value.Kind, value.Id);
            if (items.TryGetValue(key, out var existing))
            {
                existing.Changes.AddRange(changes);
                continue;
            }

            items[key] = (
                value.Kind,
                value.Id,
                value.Name,
                value.Scope,
                value.FirstSeenUtc,
                value.LastSeenUtc,
                value.Versions.LastOrDefault()?.State ?? new Dictionary<string, string?>(),
                changes);
        }

        return [.. items.Values.Select(item => new
        {
            kind = item.Kind,
            id = item.Id,
            name = item.Name,
            scope = item.Scope,
            firstSeenUtc = item.FirstSeenUtc,
            lastSeenUtc = item.LastSeenUtc,
            state = item.State,
            changes = item.Changes
        })];
    }

    private static object Change(ProtoTraceVersion version) => new
    {
        atUtc = version.AtUtc,
        operationId = version.OperationId,
        change = version.Change,
        state = version.State,
        source = Lower(version.Source),
        inferred = version.Inferred
    };

    private static string Key(string kind, string id) => $"{kind}\u001f{id}";

    private static string Lower<T>(T value) where T : struct, Enum
        => value.ToString().ToLowerInvariant();
}
