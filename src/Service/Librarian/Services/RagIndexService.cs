using System.Diagnostics;
using Agyo.Rag.Abstractions;
using Agyo.Service.Librarian.Models;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;
using RagApi = Agyo.Rag.Rag;

namespace Agyo.Service.Librarian.Services;

/// <summary>
/// Re-platformed indexing (AGYO-0003): discovers a repository via <c>RagApi.Discover</c> and ingests it into
/// <c>Rag.Corpus&lt;LibraryDoc&gt;()</c> under the project's partition. Vectors are written inline by Rag (no
/// bespoke ChunkVectorState outbox), and chunk provenance rides on the Rag chunks. The Librarian's Job
/// lifecycle wraps the ingest. Backs the <see cref="IndexProjectAsync"/> delegate.
/// </summary>
public sealed class RagIndexService
{
    private readonly ILogger<RagIndexService> _logger;

    public RagIndexService(ILogger<RagIndexService> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IndexingResult> IndexProjectAsync(
        string projectId, bool force, CancellationToken ct, IProgress<IndexingProgress>? progress)
    {
        var sw = Stopwatch.StartNew();
        var project = await Project.Get(projectId, ct)
            ?? throw new InvalidOperationException($"Project not found: {projectId}");

        // One active job per project unless forced.
        var active = (await Job.Query(j => j.ProjectId == projectId &&
            (j.Status == JobStatus.Pending || j.Status == JobStatus.Planning || j.Status == JobStatus.Indexing), ct))
            .FirstOrDefault();

        if (active is not null && !force)
        {
            return new IndexingResult(active.ProcessedFiles, active.ChunksCreated, active.VectorsSaved, active.Elapsed,
                new[] { new IndexingError("(system)", $"Indexing already in progress (Job {active.Id}). Use force=true.", "ConcurrencyConflict", null) });
        }

        if (active is not null)
        {
            await active.Cancel(ct);
            active.ErrorMessage = "Cancelled by force restart";
            await active.Save(ct);
        }

        var job = Job.Create(projectId, totalFiles: 0);
        await job.Save(ct);

        try
        {
            project.Status = IndexingStatus.Indexing;
            await project.Save(ct);

            var files = RagApi.Discover(project.RootPath).ToList();
            job.TotalFiles = files.Count;
            job.Status = JobStatus.Indexing;
            await job.Save(ct);

            RagIngestResult result;
            using (EntityContext.Partition(projectId))
            {
                result = await RagApi.Corpus<LibraryDoc>().Ingest(
                    files,
                    new Progress<RagIngestProgress>(p =>
                    {
                        job.UpdateProgress(p.ProcessedFiles, p.CurrentFileName);
                        progress?.Report(new IndexingProgress(
                            p.ProcessedFiles, p.TotalFiles, p.ProcessedChunks, p.ProcessedChunks, p.CurrentFileName));
                    }),
                    ct);
            }

            project.MarkIndexed(result.ChunksCreated, indexedBytes: 0);
            await project.Save(ct);

            job.ProcessedFiles = result.FilesProcessed;
            job.ChunksCreated = result.ChunksCreated;
            job.VectorsSaved = result.ChunksCreated;
            job.Complete();
            await job.Save(ct);

            sw.Stop();
            var errors = result.Errors
                .Select(e => new IndexingError(e.FileName, e.Reason, "IngestError", e.Exception?.StackTrace))
                .ToList();
            _logger.LogInformation(
                "Indexed project {ProjectId}: {Files} files, {Chunks} chunks via RagApi.Corpus<LibraryDoc> ({Errors} errors)",
                projectId, result.FilesProcessed, result.ChunksCreated, errors.Count);

            return new IndexingResult(result.FilesProcessed, result.ChunksCreated, result.ChunksCreated, sw.Elapsed, errors);
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            await job.Save(CancellationToken.None);
            project.Status = project.DocumentCount > 0 ? IndexingStatus.Ready : IndexingStatus.NotIndexed;
            await project.Save(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Re-platformed indexing failed for project {ProjectId}", projectId);
            job.Fail(ex.Message);
            await job.Save(CancellationToken.None);
            project.Status = IndexingStatus.Failed;
            project.LastError = ex.Message;
            await project.Save(CancellationToken.None);
            sw.Stop();
            return new IndexingResult(0, 0, 0, sw.Elapsed,
                new[] { new IndexingError("(system)", ex.Message, ex.GetType().Name, ex.StackTrace) });
        }
    }
}
