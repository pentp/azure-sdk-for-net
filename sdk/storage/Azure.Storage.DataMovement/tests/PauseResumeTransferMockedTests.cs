﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Core.Pipeline;
using Moq;
using NUnit.Framework;
using static Azure.Storage.DataMovement.Tests.MemoryTransferCheckpointer;

namespace Azure.Storage.DataMovement.Tests;

public class PauseResumeTransferMockedTests
{
    private (StepProcessor<TransferJobInternal> JobProcessor, StepProcessor<JobPartInternal> PartProcessor, StepProcessor<Func<Task>> ChunkProcessor) StepProcessors()
        => (new(), new(), new());

    private Dictionary<DataTransferState, int> GetJobsStateCount(List<DataTransfer> transfers, MemoryTransferCheckpointer checkpointer)
    {
        Dictionary<DataTransferState, int> jobsStateCount = new();
        // initialize jobsStateCount
        foreach (DataTransferState state in Enum.GetValues(typeof(DataTransferState)))
        {
            jobsStateCount[state] = 0;
        }
        // populate jobsStateCount
        foreach (DataTransfer transfer in transfers)
        {
            Job job = checkpointer.Jobs[transfer.Id];
            ++jobsStateCount[job.Status.State];
        }
        return jobsStateCount;
    }

    private Dictionary<DataTransferState, int> GetJobPartsStateCount(List<DataTransfer> transfers, MemoryTransferCheckpointer checkpointer)
    {
        Dictionary<DataTransferState, int> jobPartsStateCount = new();
        // initialize jobPartsStateCount
        foreach (DataTransferState state in Enum.GetValues(typeof(DataTransferState)))
        {
            jobPartsStateCount[state] = 0;
        }
        // populate jobPartsStateCount
        foreach (DataTransfer transfer in transfers)
        {
            Job job = checkpointer.Jobs[transfer.Id];
            foreach (var jobPartEntry in job.Parts)
            {
                JobPart jobPart = jobPartEntry.Value;
                ++jobPartsStateCount[jobPart.Status.State];
            }
        }
        return jobPartsStateCount;
    }

    private int GetEnumerationCompleteCount(List<DataTransfer> transfers, MemoryTransferCheckpointer checkpointer)
    {
        return transfers.Count(transfer => checkpointer.Jobs[transfer.Id].EnumerationComplete);
    }

    private void AssertJobProcessingSuccessful(int numJobs, List<DataTransfer> transfers, MemoryTransferCheckpointer checkpointer)
    {
        int inProgressJobsCount1 = GetJobsStateCount(transfers, checkpointer)[DataTransferState.InProgress];
        int enumerationCompleteCount1 = GetEnumerationCompleteCount(transfers, checkpointer);
        Assert.That(enumerationCompleteCount1, Is.EqualTo(numJobs), "Error: all jobs should have finished enumerating");
        Assert.That(inProgressJobsCount1, Is.EqualTo(numJobs), "Error: all jobs should be in InProgress state after Job Processing");
    }

    private void AssertPartProcessingSuccessful(int numJobParts, List<DataTransfer> transfers, MemoryTransferCheckpointer checkpointer)
    {
        int inProgressJobPartsCount = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.InProgress];
        Assert.That(inProgressJobPartsCount, Is.EqualTo(numJobParts), "Error: all job parts should be in InProgress state after Part Processing");
    }

    private void AssertAllJobsAndPartsCompleted(int numJobs, int numJobParts, List<DataTransfer> transfers, MemoryTransferCheckpointer checkpointer)
    {
        int completedJobsCount = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Completed];
        int completedPartsCount = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Completed];
        Assert.That(completedJobsCount, Is.EqualTo(numJobs), "Error in transitioning all jobs to Completed state");
        Assert.That(completedPartsCount, Is.EqualTo(numJobParts), "Error in transitioning all job parts to Completed state");
        foreach (DataTransfer transfer in transfers)
        {
            Job job = checkpointer.Jobs[transfer.Id];
            Assert.That(job.Status.State, Is.EqualTo(DataTransferState.Completed), "Error in transitioning all jobs to Completed state");
            foreach (var jobPart in job.Parts)
            {
                Assert.That(jobPart.Value.Status.State, Is.EqualTo(DataTransferState.Completed), "Error in transitioning all job parts to Completed state");
            }
        }
    }

    private async Task AssertResumeTransfer(
        int numJobs,
        int numJobParts,
        int numChunks,
        int chunksPerPart,
        List<DataTransfer> resumedTransfers,
        MemoryTransferCheckpointer checkpointer,
        StepProcessor<TransferJobInternal> jobsProcessor,
        StepProcessor<JobPartInternal> partsProcessor,
        StepProcessor<Func<Task>> chunksProcessor)
    {
        await Task.Delay(50);
        int pausedJobsCount = GetJobsStateCount(resumedTransfers, checkpointer)[DataTransferState.Paused];
        Assert.That(pausedJobsCount, Is.EqualTo(numJobs));

        // process jobs on resume
        Assert.That(await jobsProcessor.StepAll(), Is.EqualTo(numJobs), "Error job processing on resume");

        await Task.Delay(50);
        AssertJobProcessingSuccessful(numJobs, resumedTransfers, checkpointer);

        // process job parts on resume
        Assert.That(await partsProcessor.StepAll(), Is.EqualTo(numJobParts), "Error job part processing on resume");

        await Task.Delay(50);
        AssertPartProcessingSuccessful(numJobParts, resumedTransfers, checkpointer);

        // process chunks on resume
        int chunksStepped = await chunksProcessor.StepAll();
        // Check if all chunks stepped through
        if (chunksPerPart > 1)
        {
            // Multichunk transfer sends a completion chunk after all the other chunks stepped through.
            await Task.Delay(50);
            Assert.That(await chunksProcessor.StepAll() + chunksStepped, Is.EqualTo(numChunks + numJobParts));
        }
        else
        {
            Assert.That(chunksStepped, Is.EqualTo(numChunks));
        }
        AssertAllJobsAndPartsCompleted(numJobs, numJobParts, resumedTransfers, checkpointer);
    }

    [Test]
    [Combinatorial]
    public async Task PauseResumeDuringJobProcessing_ItemTransfer(
        [Values(2, 6)] int items,
        [Values(333, 500, 1024)] int itemSize,
        [Values(333, 1024)] int chunkSize,
        [Values(PauseLocation.PauseProcessHalfway,
            PauseLocation.PauseProcessStart)] PauseLocation pauseLocation)
    {
        int chunksPerPart = (int)Math.Ceiling((float)itemSize / chunkSize);
        // TODO: below should be only `items * chunksPerPart` but can't in some cases due to
        //       a bug in how work items are processed on multipart uploads.
        int numChunks = Math.Max(chunksPerPart - 1, 1) * items;

        Uri srcUri = new("file:///foo/bar");
        Uri dstUri = new("https://example.com/fizz/buzz");

        (var jobsProcessor, var partsProcessor, var chunksProcessor) = StepProcessors();
        JobBuilder jobBuilder = new(ArrayPool<byte>.Shared, default, new ClientDiagnostics(ClientOptions.Default));
        MemoryTransferCheckpointer checkpointer = new();

        var resources = Enumerable.Range(0, items).Select(_ =>
        {
            Mock<StorageResourceItem> srcResource = new(MockBehavior.Strict);
            Mock<StorageResourceItem> dstResource = new(MockBehavior.Strict);

            (srcResource, dstResource).BasicSetup(srcUri, dstUri, itemSize);

            return (Source: srcResource, Destination: dstResource);
        }).ToList();

        List<StorageResourceProvider> resumeProviders = new() { new MockStorageResourceProvider(checkpointer) };

        await using TransferManager transferManager = new(
            jobsProcessor,
            partsProcessor,
            chunksProcessor,
            jobBuilder,
            checkpointer,
            resumeProviders);

        List<DataTransfer> transfers = new();

        // queue jobs
        foreach ((Mock<StorageResourceItem> srcResource, Mock<StorageResourceItem> dstResource) in resources)
        {
            DataTransfer transfer = await transferManager.StartTransferAsync(
                srcResource.Object,
                dstResource.Object,
                new()
                {
                    InitialTransferSize = chunkSize,
                    MaximumTransferChunkSize = chunkSize,
                });
            transfers.Add(transfer);

            // Assert that job plan file is created properly
            Assert.That(checkpointer.Jobs.ContainsKey(transfer.Id), Is.True, "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.Queued), "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Parts.Count, Is.EqualTo(0), "Job Part files should not exist before job processing");
        }
        Assert.That(checkpointer.Jobs.Count, Is.EqualTo(transfers.Count), "Error during Job plan file creation.");
        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(items), "Error during initial Job queueing.");

        // Setup PauseProcessHalfway & PauseProcessStart before issuing pause
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            // Let half of the jobs process without Pause
            Assert.That(await jobsProcessor.StepMany(items / 2), Is.EqualTo(items / 2), "Error in job processing half");
            Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(items / 2), "Error in job processing half");
        }
        else
        {
            Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(items), "Error in job processing all");
        }

        // Issue Pause for all of the transfers
        foreach (DataTransfer transfer in transfers)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
        }

        // Process (the rest of) jobs
        await jobsProcessor.StepAll();

        await Task.Delay(50);
        int pausedJobsCount = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        int queuedPartsCount = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Queued];
        int jobPartsCreatedCount = transfers.Sum(transfer => checkpointer.Jobs[transfer.Id].Parts.Count);
        int enumerationCompleteCount = GetEnumerationCompleteCount(transfers, checkpointer);

        // Assert that we properly paused for PauseProcessHalfway & PauseProcessStart
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            Assert.That(pausedJobsCount, Is.EqualTo(items / 2), "Error in Pausing half");
            Assert.That(queuedPartsCount, Is.EqualTo(items / 2), "Error in Pausing half");
            Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(items / 2), "Error in Pausing half");
            Assert.That(jobPartsCreatedCount, Is.EqualTo(items / 2), "Error in Pausing half");
            Assert.That(enumerationCompleteCount, Is.EqualTo(items / 2), "Error: half of the jobs should have finished enumerating");
        }
        else
        {
            Assert.That(pausedJobsCount, Is.EqualTo(items), "Error in Pausing all");
            Assert.That(queuedPartsCount, Is.EqualTo(0), "Error in Pausing all");
            Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(0), "Error in Pausing all");
            Assert.That(jobPartsCreatedCount, Is.EqualTo(0), "Error in Pausing all");
            Assert.That(enumerationCompleteCount, Is.EqualTo(0), "Error: none of the jobs should have finished enumerating");
        }

        // At this point, we are continuing with the leftovers from PauseProcessHalfway
        // Process parts
        await partsProcessor.StepAll();

        await Task.Delay(50);
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            int pausedJobsCount2 = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
            int pausedPartsCount2 = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Paused];
            Assert.That(pausedJobsCount2, Is.EqualTo(items), "Error in transitioning all jobs to Paused state");
            // only half of the job part checkpointers are created in PauseProcessHalfway so only half will be in Paused state
            Assert.That(pausedPartsCount2, Is.EqualTo(items / 2), "Error in transitioning all created job parts to Paused state");
            Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(0), "Error: no items should proceed to chunking");
        }

        // START RESUME TRANSFERS
        List<DataTransfer> resumedTransfers = await transferManager.ResumeAllTransfersAsync(new()
        {
            InitialTransferSize = chunkSize,
            MaximumTransferChunkSize = chunkSize,
        });

        await AssertResumeTransfer(items, items, numChunks, chunksPerPart, resumedTransfers, checkpointer, jobsProcessor, partsProcessor, chunksProcessor);
    }

    [Test]
    [Combinatorial]
    public async Task PauseResumeDuringPartProcessing_ItemTransfer(
        [Values(2, 6)] int items,
        [Values(333, 500, 1024)] int itemSize,
        [Values(333, 1024)] int chunkSize,
        [Values(PauseLocation.PauseProcessHalfway,
            PauseLocation.PauseProcessStart)] PauseLocation pauseLocation)
    {
        int chunksPerPart = (int)Math.Ceiling((float)itemSize / chunkSize);
        // TODO: below should be only `items * chunksPerPart` but can't in some cases due to
        //       a bug in how work items are processed on multipart uploads.
        int numChunks = Math.Max(chunksPerPart - 1, 1) * items;

        Uri srcUri = new("file:///foo/bar");
        Uri dstUri = new("https://example.com/fizz/buzz");

        (var jobsProcessor, var partsProcessor, var chunksProcessor) = StepProcessors();
        JobBuilder jobBuilder = new(ArrayPool<byte>.Shared, default, new ClientDiagnostics(ClientOptions.Default));
        MemoryTransferCheckpointer checkpointer = new();

        var resources = Enumerable.Range(0, items).Select(_ =>
        {
            Mock<StorageResourceItem> srcResource = new(MockBehavior.Strict);
            Mock<StorageResourceItem> dstResource = new(MockBehavior.Strict);

            (srcResource, dstResource).BasicSetup(srcUri, dstUri, itemSize);

            return (Source: srcResource, Destination: dstResource);
        }).ToList();

        List<StorageResourceProvider> resumeProviders = new() { new MockStorageResourceProvider(checkpointer) };

        await using TransferManager transferManager = new(
            jobsProcessor,
            partsProcessor,
            chunksProcessor,
            jobBuilder,
            checkpointer,
            resumeProviders);

        List<DataTransfer> transfers = new();

        // queue jobs
        foreach ((Mock<StorageResourceItem> srcResource, Mock<StorageResourceItem> dstResource) in resources)
        {
            DataTransfer transfer = await transferManager.StartTransferAsync(
                srcResource.Object,
                dstResource.Object,
                new()
                {
                    InitialTransferSize = chunkSize,
                    MaximumTransferChunkSize = chunkSize,
                });
            transfers.Add(transfer);

            // Assert that job plan file is created properly
            Assert.That(checkpointer.Jobs.ContainsKey(transfer.Id), Is.True, "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.Queued), "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Parts.Count, Is.EqualTo(0), "Job Part files should not exist before job processing");
        }
        Assert.That(checkpointer.Jobs.Count, Is.EqualTo(transfers.Count), "Error during Job plan file creation.");
        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(items), "Error during initial Job queueing.");

        // Process jobs
        Assert.That(await jobsProcessor.StepAll(), Is.EqualTo(items), "Error during job processing");

        // At this point, job part files should be created for each transfer item
        foreach (DataTransfer transfer in transfers)
        {
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.InProgress), "Error transitioning Job to InProgress after job processing");
            var partsDict = checkpointer.Jobs[transfer.Id].Parts;
            Assert.That(partsDict.Count, Is.EqualTo(1), "Error during Job part file creation.");
            Assert.That(partsDict.First().Value.Status.State, Is.EqualTo(DataTransferState.Queued), "Error during Job part file creation.");
        }

        int enumerationCompleteCount = GetEnumerationCompleteCount(transfers, checkpointer);
        Assert.That(enumerationCompleteCount, Is.EqualTo(items), "Error: all jobs should have finished enumerating");

        // Setup PauseProcessHalfway & PauseProcessStart before issuing pause
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            // Let half of the parts process without Pause
            Assert.That(await partsProcessor.StepMany(items / 2), Is.EqualTo(items / 2), "Error in part processing half");
            Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(items / 2), "Error in part processing half");
        }
        else
        {
            Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(items), "Error in part processing all");
        }

        // Issue Pause for all of the transfers
        foreach (DataTransfer transfer in transfers)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
        }

        // Process (the rest of) parts
        await partsProcessor.StepAll();

        await Task.Delay(50);
        int pausedJobsCount = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        int pausedPartsCount = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Paused];

        // Assert that we properly paused for PauseProcessHalfway & PauseProcessStart
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            Assert.That(pausedJobsCount, Is.EqualTo(items / 2), "Error in Pausing half"); // cus jobs is 1:1 with job parts in item transfer
            Assert.That(pausedPartsCount, Is.EqualTo(items / 2), "Error in Pausing half");
            Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(numChunks / 2), "Error in Pausing half");
        }
        else
        {
            Assert.That(pausedJobsCount, Is.EqualTo(items), "Error in Pausing all");
            Assert.That(pausedPartsCount, Is.EqualTo(items), "Error in Pausing all");
            Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(0), "Error in Pausing all");
        }

        // At this point, we are continuing with the leftovers from PauseProcessHalfway
        // Process chunks
        await chunksProcessor.StepAll();

        await Task.Delay(50);
        // All jobs and job parts should be paused now
        int pausedJobsCount2 = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        int pausedPartsCount2 = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        Assert.That(pausedJobsCount2, Is.EqualTo(items), "Error in transitioning all jobs to Paused state");
        Assert.That(pausedPartsCount2, Is.EqualTo(items), "Error in transitioning all job parts to Paused state");

        // START RESUME TRANSFERS
        List<DataTransfer> resumedTransfers = await transferManager.ResumeAllTransfersAsync();

        await AssertResumeTransfer(items, items, numChunks, chunksPerPart, resumedTransfers, checkpointer, jobsProcessor, partsProcessor, chunksProcessor);
    }

    [Test]
    [Combinatorial]
    public async Task PauseResumeDuringChunkProcessing_ItemTransfer(
        [Values(2, 6)] int items,
        [Values(1024)] int itemSize,
        [Values(1024)] int chunkSize,
        [Values(PauseLocation.PauseProcessHalfway,
            PauseLocation.PauseProcessStart)] PauseLocation pauseLocation)
    {
        int chunksPerPart = (int)Math.Ceiling((float)itemSize / chunkSize);
        // TODO: below should be only `items * chunksPerPart` but can't in some cases due to
        //       a bug in how work items are processed on multipart uploads.
        int numChunks = Math.Max(chunksPerPart - 1, 1) * items;

        Uri srcUri = new("file:///foo/bar");
        Uri dstUri = new("https://example.com/fizz/buzz");

        (var jobsProcessor, var partsProcessor, var chunksProcessor) = StepProcessors();
        JobBuilder jobBuilder = new(ArrayPool<byte>.Shared, default, new ClientDiagnostics(ClientOptions.Default));
        MemoryTransferCheckpointer checkpointer = new();

        var resources = Enumerable.Range(0, items).Select(_ =>
        {
            Mock<StorageResourceItem> srcResource = new(MockBehavior.Strict);
            Mock<StorageResourceItem> dstResource = new(MockBehavior.Strict);

            (srcResource, dstResource).BasicSetup(srcUri, dstUri, itemSize);

            return (Source: srcResource, Destination: dstResource);
        }).ToList();

        List<StorageResourceProvider> resumeProviders = new() { new MockStorageResourceProvider(checkpointer) };

        await using TransferManager transferManager = new(
            jobsProcessor,
            partsProcessor,
            chunksProcessor,
            jobBuilder,
            checkpointer,
            resumeProviders);

        List<DataTransfer> transfers = new();

        // queue jobs
        foreach ((Mock<StorageResourceItem> srcResource, Mock<StorageResourceItem> dstResource) in resources)
        {
            DataTransfer transfer = await transferManager.StartTransferAsync(
                srcResource.Object,
                dstResource.Object,
                new()
                {
                    InitialTransferSize = chunkSize,
                    MaximumTransferChunkSize = chunkSize,
                });
            transfers.Add(transfer);

            // Assert that job plan file is created properly
            Assert.That(checkpointer.Jobs.ContainsKey(transfer.Id), Is.True, "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.Queued), "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Parts.Count, Is.EqualTo(0), "Job Part files should not exist before job processing");
        }
        Assert.That(checkpointer.Jobs.Count, Is.EqualTo(transfers.Count), "Error during Job plan file creation.");
        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(items), "Error during initial Job queueing.");

        // Process jobs
        Assert.That(await jobsProcessor.StepAll(), Is.EqualTo(items), "Error during job processing");

        // At this point, job part files should be created for each transfer item
        foreach (DataTransfer transfer in transfers)
        {
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.InProgress), "Error transitioning Job to InProgress after job processing");
            var partsDict = checkpointer.Jobs[transfer.Id].Parts;
            Assert.That(partsDict.Count, Is.EqualTo(1), "Error during Job part file creation.");
            Assert.That(partsDict.First().Value.Status.State, Is.EqualTo(DataTransferState.Queued), "Error during Job part file creation.");
        }

        int enumerationCompleteCount = GetEnumerationCompleteCount(transfers, checkpointer);
        Assert.That(enumerationCompleteCount, Is.EqualTo(items), "Error: all jobs should have finished enumerating");

        // Process parts
        Assert.That(await partsProcessor.StepAll(), Is.EqualTo(items), "Error in part processing");

        foreach (DataTransfer transfer in transfers)
        {
            var partsDict = checkpointer.Jobs[transfer.Id].Parts;
            Assert.That(partsDict.First().Value.Status.State, Is.EqualTo(DataTransferState.InProgress), "Error transitioning Job Part to InProgress");
        }

        // Setup PauseProcessHalfway & PauseProcessStart before issuing pause
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            // Let half of the chunks process without Pause
            Assert.That(await chunksProcessor.StepMany(numChunks / 2), Is.EqualTo(numChunks / 2), "Error in chunk processing half");
            Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(numChunks / 2), "Error in chunk processing half");
        }
        else
        {
            Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(numChunks), "Error in chunk processing all");
        }

        // Issue Pause for all of the transfers
        foreach (DataTransfer transfer in transfers)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            if (pauseLocation == PauseLocation.PauseProcessStart)
            {
                Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
            }
            else
            {
                // the half that processed before the pause will be in Completed state
                Assert.That(transfer.TransferStatus.State,
                    Is.AnyOf(DataTransferState.Pausing, DataTransferState.Completed),
                    "Error: Transfer state for PauseProcessHalfway should be either Pausing or Completed");
            }
        }

        // Process (the rest of) chunks
        await chunksProcessor.StepAll();

        await Task.Delay(50);
        var jobsStateCount = GetJobsStateCount(transfers, checkpointer);
        int pausedJobsCount = jobsStateCount[DataTransferState.Paused];
        int completedJobsCount = jobsStateCount[DataTransferState.Completed];
        var partsStateCount = GetJobPartsStateCount(transfers, checkpointer);
        int pausedPartsCount = partsStateCount[DataTransferState.Paused];
        int completedPartsCount = partsStateCount[DataTransferState.Completed];

        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            // Because for this test, 1 chunk = 1 job part = 1 job/transfer
            // so pausing half of the chunks should yield half of the jobs and job parts in Paused state
            Assert.That(pausedJobsCount, Is.EqualTo(items / 2), "Error in Pausing half");
            Assert.That(completedJobsCount, Is.EqualTo(items / 2), "Error in Pausing half");
            Assert.That(pausedPartsCount, Is.EqualTo(items / 2), "Error in Pausing half");
            Assert.That(completedPartsCount, Is.EqualTo(items / 2), "Error in Pausing half");
        }
        else
        {
            Assert.That(pausedJobsCount, Is.EqualTo(items), "Error in Pausing all");
            Assert.That(completedJobsCount, Is.EqualTo(0), "Error in Pausing all");
            Assert.That(pausedPartsCount, Is.EqualTo(items), "Error in Pausing all");
            Assert.That(completedPartsCount, Is.EqualTo(0), "Error in Pausing all");
        }

        // START RESUME TRANSFERS
        List<DataTransfer> resumedTransfers = await transferManager.ResumeAllTransfersAsync();

        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            await AssertResumeTransfer(items / 2, items / 2, numChunks / 2, chunksPerPart / 2, resumedTransfers, checkpointer, jobsProcessor, partsProcessor, chunksProcessor);
        }
        else
        {
            await AssertResumeTransfer(items, items, numChunks, chunksPerPart, resumedTransfers, checkpointer, jobsProcessor, partsProcessor, chunksProcessor);
        }
    }

    [Test]
    [Combinatorial]
    public async Task PauseResumeDuringJobProcessing_ContainerTransfer(
    [Values(2, 6)] int numJobs,
    [Values(333, 500, 1024)] int itemSize,
    [Values(333, 1024)] int chunkSize,
    [Values(PauseLocation.PauseProcessHalfway,
            PauseLocation.PauseProcessStart)] PauseLocation pauseLocation)
    {
        static int GetItemCountFromContainerIndex(int i) => i * 2;

        int numJobParts = Enumerable.Range(1, numJobs).Select(GetItemCountFromContainerIndex).Sum();
        int chunksPerPart = (int)Math.Ceiling((float)itemSize / chunkSize);
        // TODO: below should be only `items * chunksPerPart` but can't in some cases due to
        //       a bug in how work items are processed on multipart uploads.
        int numChunks = Math.Max(chunksPerPart - 1, 1) * numJobParts;

        Uri srcUri = new("file:///foo/bar");
        Uri dstUri = new("https://example.com/fizz/buzz");

        (var jobsProcessor, var partsProcessor, var chunksProcessor) = StepProcessors();
        JobBuilder jobBuilder = new(ArrayPool<byte>.Shared, default, new ClientDiagnostics(ClientOptions.Default));
        MemoryTransferCheckpointer checkpointer = new();

        var resources = Enumerable.Range(1, numJobs).Select(i =>
        {
            Mock<StorageResourceContainer> srcResource = new(MockBehavior.Strict);
            Mock<StorageResourceContainer> dstResource = new(MockBehavior.Strict);
            (srcResource, dstResource).BasicSetup(srcUri, dstUri, GetItemCountFromContainerIndex(i), itemSize);
            return (Source: srcResource, Destination: dstResource);
        }).ToList();

        List<StorageResourceProvider> resumeProviders = new() { new MockStorageResourceProvider(checkpointer) };

        await using TransferManager transferManager = new(
            jobsProcessor,
            partsProcessor,
            chunksProcessor,
            jobBuilder,
            checkpointer,
            resumeProviders);

        List<DataTransfer> transfers = new();

        // queue jobs
        foreach ((Mock<StorageResourceContainer> srcResource, Mock<StorageResourceContainer> dstResource) in resources)
        {
            DataTransfer transfer = await transferManager.StartTransferAsync(
                srcResource.Object,
                dstResource.Object,
                new()
                {
                    InitialTransferSize = chunkSize,
                    MaximumTransferChunkSize = chunkSize,
                });
            transfers.Add(transfer);

            // Assert that job plan file is created properly
            Assert.That(checkpointer.Jobs.ContainsKey(transfer.Id), Is.True, "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.Queued), "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Parts.Count, Is.EqualTo(0), "Job Part files should not exist before job processing");
        }
        Assert.That(checkpointer.Jobs.Count, Is.EqualTo(transfers.Count), "Error during Job plan file creation.");
        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(numJobs), "Error during initial Job queueing.");

        // Setup PauseProcessHalfway & PauseProcessStart before issuing pause
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            // Let half of the jobs process without Pause
            Assert.That(await jobsProcessor.StepMany(numJobs / 2), Is.EqualTo(numJobs / 2), "Error in job processing half");
            Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(numJobs / 2), "Error in job processing half");
        }
        else
        {
            Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(numJobs), "Error in job processing all");
        }

        // Issue Pause for all of the transfers
        foreach (DataTransfer transfer in transfers)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
        }

        // Process (the rest of) jobs
        await jobsProcessor.StepAll();

        await Task.Delay(50);
        int pausedJobsCount = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        int queuedPartsCount = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Queued];
        int expectedPartsCreatedCount = Enumerable.Range(1, numJobs/2).Select(GetItemCountFromContainerIndex).Sum();
        int jobPartsCreatedCount = transfers.Sum(transfer => checkpointer.Jobs[transfer.Id].Parts.Count);
        int enumerationCompleteCount = GetEnumerationCompleteCount(transfers, checkpointer);

        // Assert that we properly paused for PauseProcessHalfway & PauseProcessStart
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            Assert.That(pausedJobsCount, Is.EqualTo(numJobs / 2), "Error in Pausing half");
            Assert.That(queuedPartsCount, Is.EqualTo(expectedPartsCreatedCount), "Error in Pausing half");
            Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(expectedPartsCreatedCount), "Error in Pausing half");
            Assert.That(jobPartsCreatedCount, Is.EqualTo(expectedPartsCreatedCount), "Error in Pausing half");
            Assert.That(enumerationCompleteCount, Is.EqualTo(numJobs / 2), "Error: half of the jobs should have finished enumerating");
        }
        else
        {
            Assert.That(pausedJobsCount, Is.EqualTo(numJobs), "Error in Pausing all");
            Assert.That(queuedPartsCount, Is.EqualTo(0), "Error in Pausing all");
            Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(0), "Error in Pausing all");
            Assert.That(jobPartsCreatedCount, Is.EqualTo(0), "Error in Pausing all");
            Assert.That(enumerationCompleteCount, Is.EqualTo(0), "Error: none of the jobs should have finished enumerating");
        }

        // At this point, we are continuing with the leftovers from PauseProcessStart
        // Process parts
        await partsProcessor.StepAll();

        await Task.Delay(50);
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            int pausedJobsCount2 = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
            int pausedPartsCount2 = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Paused];
            Assert.That(pausedJobsCount2, Is.EqualTo(numJobs), "Error in transitioning all jobs to Paused state");
            Assert.That(pausedPartsCount2, Is.EqualTo(expectedPartsCreatedCount), "Error in transitioning all created job parts to Paused state");
            Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(0), "Error: no items should proceed to chunking");
        }

        // START RESUME TRANSFERS
        List<DataTransfer> resumedTransfers = await transferManager.ResumeAllTransfersAsync(new()
        {
            InitialTransferSize = chunkSize,
            MaximumTransferChunkSize = chunkSize,
        });

        await AssertResumeTransfer(numJobs, numJobParts, numChunks, chunksPerPart, resumedTransfers, checkpointer, jobsProcessor, partsProcessor, chunksProcessor);
    }

    [Test]
    [Combinatorial]
    public async Task PauseResumeDuringPartProcessing_ContainerTransfer(
        [Values(2, 6)] int numJobs,
        [Values(333, 500, 1024)] int itemSize,
        [Values(333, 1024)] int chunkSize,
        [Values(PauseLocation.PauseProcessHalfway,
            PauseLocation.PauseProcessStart)] PauseLocation pauseLocation)
    {
        static int GetItemCountFromContainerIndex(int i) => i * 2;

        int numJobParts = Enumerable.Range(1, numJobs).Select(GetItemCountFromContainerIndex).Sum();
        int chunksPerPart = (int)Math.Ceiling((float)itemSize / chunkSize);
        // TODO: below should be only `items * chunksPerPart` but can't in some cases due to
        //       a bug in how work items are processed on multipart uploads.
        int numChunks = Math.Max(chunksPerPart - 1, 1) * numJobParts;

        Uri srcUri = new("file:///foo/bar");
        Uri dstUri = new("https://example.com/fizz/buzz");

        (var jobsProcessor, var partsProcessor, var chunksProcessor) = StepProcessors();
        JobBuilder jobBuilder = new(ArrayPool<byte>.Shared, default, new ClientDiagnostics(ClientOptions.Default));
        MemoryTransferCheckpointer checkpointer = new();

        var resources = Enumerable.Range(1, numJobs).Select(i =>
        {
            Mock<StorageResourceContainer> srcResource = new(MockBehavior.Strict);
            Mock<StorageResourceContainer> dstResource = new(MockBehavior.Strict);
            (srcResource, dstResource).BasicSetup(srcUri, dstUri, GetItemCountFromContainerIndex(i), itemSize);
            return (Source: srcResource, Destination: dstResource);
        }).ToList();

        List<StorageResourceProvider> resumeProviders = new() { new MockStorageResourceProvider(checkpointer) };

        await using TransferManager transferManager = new(
            jobsProcessor,
            partsProcessor,
            chunksProcessor,
            jobBuilder,
            checkpointer,
            resumeProviders);

        List<DataTransfer> transfers = new();

        // queue jobs
        foreach ((Mock<StorageResourceContainer> srcResource, Mock<StorageResourceContainer> dstResource) in resources)
        {
            DataTransfer transfer = await transferManager.StartTransferAsync(
                srcResource.Object,
                dstResource.Object,
                new()
                {
                    InitialTransferSize = chunkSize,
                    MaximumTransferChunkSize = chunkSize,
                });
            transfers.Add(transfer);

            // Assert that job plan file is created properly
            Assert.That(checkpointer.Jobs.ContainsKey(transfer.Id), Is.True, "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.Queued), "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Parts.Count, Is.EqualTo(0), "Job Part files should not exist before job processing");
        }
        Assert.That(checkpointer.Jobs.Count, Is.EqualTo(transfers.Count), "Error during Job plan file creation.");
        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(numJobs), "Error during initial Job queueing.");

        // process jobs
        Assert.That(await jobsProcessor.StepAll(), Is.EqualTo(numJobs), "Error during job processing");

        // At this point, job part files should be created for each transfer item
        for (int i = 0; i < transfers.Count; i++)
        {
            DataTransfer transfer = transfers[i];
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.InProgress), "Error transitioning Job to InProgress after job processing");
            int expectedItemCount = GetItemCountFromContainerIndex(i + 1);
            var partsDict = checkpointer.Jobs[transfer.Id].Parts;
            Assert.That(partsDict.Count, Is.EqualTo(expectedItemCount), "Error during Job part file creation.");
            Assert.That(partsDict.Values.All(part => part.Status.State == DataTransferState.Queued),
                Is.True, "Error during Job part file creation: Not all parts are in the Queued state.");
        }

        int enumerationCompleteCount = GetEnumerationCompleteCount(transfers, checkpointer);
        Assert.That(enumerationCompleteCount, Is.EqualTo(numJobs), "Error: all jobs should have finished enumerating");

        // Setup PauseProcessHalfway & PauseProcessStart before issuing pause
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            // Let half of the parts process without Pause
            Assert.That(await partsProcessor.StepMany(numJobParts / 2), Is.EqualTo(numJobParts / 2), "Error in part processing half");
            Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(numJobParts / 2), "Error in part processing half");
        }
        else
        {
            Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(numJobParts), "Error in part processing all");
        }

        // Issue Pause for all of the transfers
        foreach (DataTransfer transfer in transfers)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
        }

        // Process (the rest of) parts
        await partsProcessor.StepAll();

        await Task.Delay(50);
        int pausedPartsCount = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Paused];

        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            Assert.That(pausedPartsCount, Is.EqualTo(numJobParts / 2), "Error in Pausing half");
            Assert.That(chunksProcessor.ItemsInQueue, Is.AtLeast(1), "Error in Pausing half");
        }
        else
        {
            Assert.That(pausedPartsCount, Is.EqualTo(numJobParts), "Error in Pausing all");
            Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(0), "Error in Pausing all");
        }

        // At this point, we are continuing with the leftovers from PauseProcessHalfway
        // Process chunks
        await chunksProcessor.StepAll();

        await Task.Delay(50);
        // All jobs and job parts should be paused now
        int pausedJobsCount2 = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        int pausedPartsCount2 = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        Assert.That(pausedJobsCount2, Is.EqualTo(numJobs), "Error in transitioning all jobs to Paused state");
        Assert.That(pausedPartsCount2, Is.EqualTo(numJobParts), "Error in transitioning all parts to Paused state");

        // START RESUME TRANSFERS
        List<DataTransfer> resumedTransfers = await transferManager.ResumeAllTransfersAsync();

        await AssertResumeTransfer(numJobs, numJobParts, numChunks, chunksPerPart, resumedTransfers, checkpointer, jobsProcessor, partsProcessor, chunksProcessor);
    }

    [Test]
    [Combinatorial]
    public async Task PauseResumeDuringChunkProcessing_ContainerTransfer(
    [Values(2, 6)] int numJobs,
    [Values(1024)] int itemSize,
    [Values(1024)] int chunkSize,
    [Values(PauseLocation.PauseProcessHalfway,
            PauseLocation.PauseProcessStart)] PauseLocation pauseLocation)
    {
        static int GetItemCountFromContainerIndex(int i) => i * 2;

        int numJobParts = Enumerable.Range(1, numJobs).Select(GetItemCountFromContainerIndex).Sum();
        int chunksPerPart = (int)Math.Ceiling((float)itemSize / chunkSize);
        // TODO: below should be only `items * chunksPerPart` but can't in some cases due to
        //       a bug in how work items are processed on multipart uploads.
        int numChunks = Math.Max(chunksPerPart - 1, 1) * numJobParts;

        Uri srcUri = new("file:///foo/bar");
        Uri dstUri = new("https://example.com/fizz/buzz");

        (var jobsProcessor, var partsProcessor, var chunksProcessor) = StepProcessors();
        JobBuilder jobBuilder = new(ArrayPool<byte>.Shared, default, new ClientDiagnostics(ClientOptions.Default));
        MemoryTransferCheckpointer checkpointer = new();

        var resources = Enumerable.Range(1, numJobs).Select(i =>
        {
            Mock<StorageResourceContainer> srcResource = new(MockBehavior.Strict);
            Mock<StorageResourceContainer> dstResource = new(MockBehavior.Strict);
            (srcResource, dstResource).BasicSetup(srcUri, dstUri, GetItemCountFromContainerIndex(i), itemSize);
            return (Source: srcResource, Destination: dstResource);
        }).ToList();

        List<StorageResourceProvider> resumeProviders = new() { new MockStorageResourceProvider(checkpointer) };

        await using TransferManager transferManager = new(
            jobsProcessor,
            partsProcessor,
            chunksProcessor,
            jobBuilder,
            checkpointer,
            resumeProviders);

        List<DataTransfer> transfers = new();

        // queue jobs
        foreach ((Mock<StorageResourceContainer> srcResource, Mock<StorageResourceContainer> dstResource) in resources)
        {
            DataTransfer transfer = await transferManager.StartTransferAsync(
                srcResource.Object,
                dstResource.Object,
                new()
                {
                    InitialTransferSize = chunkSize,
                    MaximumTransferChunkSize = chunkSize,
                });
            transfers.Add(transfer);

            // Assert that job plan file is created properly
            Assert.That(checkpointer.Jobs.ContainsKey(transfer.Id), Is.True, "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.Queued), "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Parts.Count, Is.EqualTo(0), "Job Part files should not exist before job processing");
        }
        Assert.That(checkpointer.Jobs.Count, Is.EqualTo(transfers.Count), "Error during Job plan file creation.");
        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(numJobs), "Error during initial Job queueing.");

        // Process jobs
        Assert.That(await jobsProcessor.StepAll(), Is.EqualTo(numJobs), "Error processing jobs");

        // At this point, job part files should be created for each transfer item
        for (int i = 0; i < transfers.Count; i++)
        {
            DataTransfer transfer = transfers[i];
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.InProgress), "Error transitioning Job to InProgress after job processing");
            int expectedItemCount = GetItemCountFromContainerIndex(i + 1);
            var partsDict = checkpointer.Jobs[transfer.Id].Parts;
            Assert.That(partsDict.Count, Is.EqualTo(expectedItemCount), "Error during Job part file creation.");
            Assert.That(partsDict.Values.All(part => part.Status.State == DataTransferState.Queued),
                Is.True, "Error during Job part file creation: Not all parts are in the Queued state.");
        }

        int enumerationCompleteCount = GetEnumerationCompleteCount(transfers, checkpointer);
        Assert.That(enumerationCompleteCount, Is.EqualTo(numJobs), "Error: all jobs should have finished enumerating");

        // Process parts
        Assert.That(await partsProcessor.StepAll(), Is.EqualTo(numJobParts), "Error processing job parts");

        foreach (DataTransfer transfer in transfers)
        {
            var partsDict = checkpointer.Jobs[transfer.Id].Parts;
            Assert.That(partsDict.Values.All(part => part.Status.State == DataTransferState.InProgress),
                Is.True, "Error transitioning each Job Part to InProgress");
        }

        // Setup PauseProcessHalfway & PauseProcessStart before issuing pause
        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            // Let half of the chunks process without Pause
            Assert.That(await chunksProcessor.StepMany(numChunks / 2), Is.EqualTo(numChunks / 2), "Error in chunk processing half");
            Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(numChunks / 2), "Error in chunk processing half");
        }
        else
        {
            Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(numChunks), "Error in chunk processing all");
        }

        // Issue Pause for all transfers
        foreach (DataTransfer transfer in transfers)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            if (pauseLocation == PauseLocation.PauseProcessStart)
            {
                Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
            }
            else
            {
                Assert.That(transfer.TransferStatus.State,
                    Is.AnyOf(DataTransferState.Pausing, DataTransferState.Completed),
                    "Error: Transfer state for PauseProcessHalfway should be either Pausing or Completed");
            }
        }

        // Process (the rest of) chunks
        await chunksProcessor.StepAll();

        await Task.Delay(50);
        var jobsStateCount = GetJobsStateCount(transfers, checkpointer);
        int pausedJobsCount = jobsStateCount[DataTransferState.Paused];
        int completedJobsCount = jobsStateCount[DataTransferState.Completed];
        var partsStateCount = GetJobPartsStateCount(transfers, checkpointer);
        int pausedPartsCount = partsStateCount[DataTransferState.Paused];
        int completedPartsCount = partsStateCount[DataTransferState.Completed];

        int expectedAlreadyCompletedJobsCount_half = 0;
        for (int i = 1, numChunksCompleted = numChunks / 2; i <= numJobParts / 2 && numChunksCompleted > 0; ++i)
        {
            numChunksCompleted -= i * 2;
            if (numChunksCompleted >= 0)
            {
                ++expectedAlreadyCompletedJobsCount_half;
            }
        }

        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            int expectedPausedJobsCount = numJobs - expectedAlreadyCompletedJobsCount_half;
            // For this test, job parts is 1:1 with job chunks
            Assert.That(pausedJobsCount, Is.EqualTo(expectedPausedJobsCount), "Error in Pausing half");
            Assert.That(completedJobsCount, Is.EqualTo(expectedAlreadyCompletedJobsCount_half), "Error in Pausing half");
            Assert.That(pausedPartsCount, Is.EqualTo(numJobParts / 2), "Error in Pausing half");
            Assert.That(completedPartsCount, Is.EqualTo(numJobParts / 2), "Error in Pausing half");
        }
        else
        {
            Assert.That(pausedJobsCount, Is.EqualTo(numJobs), "Error in Pausing all");
            Assert.That(completedJobsCount, Is.EqualTo(0), "Error in Pausing all");
            Assert.That(pausedPartsCount, Is.EqualTo(numJobParts), "Error in Pausing all");
            Assert.That(completedPartsCount, Is.EqualTo(0), "Error in Pausing all");
        }

        // START RESUME TRANSFERS
        List<DataTransfer> resumedTransfers = await transferManager.ResumeAllTransfersAsync();

        if (pauseLocation == PauseLocation.PauseProcessHalfway)
        {
            int expectedJobsCount = numJobs - expectedAlreadyCompletedJobsCount_half;
            int expectedPartsCount = Enumerable.Range(numJobs + 1 - expectedJobsCount, expectedJobsCount)
                .Sum(i => i * 2);
            await AssertResumeTransfer(expectedJobsCount, expectedPartsCount, expectedPartsCount, chunksPerPart, resumedTransfers, checkpointer, jobsProcessor, partsProcessor, chunksProcessor);
        }
        else
        {
            await AssertResumeTransfer(numJobs, numJobParts, numChunks, chunksPerPart, resumedTransfers, checkpointer, jobsProcessor, partsProcessor, chunksProcessor);
        }
    }

    [Test]
    [Combinatorial]
    public async Task MultiplePausesAndResumes_ItemTransfer(
        [Values(2, 6)] int items,
        [Values(333, 500, 1024)] int itemSize,
        [Values(333, 1024)] int chunkSize)
    {
        int chunksPerPart = (int)Math.Ceiling((float)itemSize / chunkSize);
        // TODO: below should be only `items * chunksPerPart` but can't in some cases due to
        //       a bug in how work items are processed on multipart uploads.
        int numChunks = Math.Max(chunksPerPart - 1, 1) * items;

        Uri srcUri = new("file:///foo/bar");
        Uri dstUri = new("https://example.com/fizz/buzz");

        (var jobsProcessor, var partsProcessor, var chunksProcessor) = StepProcessors();
        JobBuilder jobBuilder = new(ArrayPool<byte>.Shared, default, new ClientDiagnostics(ClientOptions.Default));
        MemoryTransferCheckpointer checkpointer = new();

        var resources = Enumerable.Range(0, items).Select(_ =>
        {
            Mock<StorageResourceItem> srcResource = new(MockBehavior.Strict);
            Mock<StorageResourceItem> dstResource = new(MockBehavior.Strict);

            (srcResource, dstResource).BasicSetup(srcUri, dstUri, itemSize);

            return (Source: srcResource, Destination: dstResource);
        }).ToList();

        List<StorageResourceProvider> resumeProviders = new() { new MockStorageResourceProvider(checkpointer) };

        await using TransferManager transferManager = new(
            jobsProcessor,
            partsProcessor,
            chunksProcessor,
            jobBuilder,
            checkpointer,
            resumeProviders);

        List<DataTransfer> transfers = new();

        // queue jobs
        foreach ((Mock<StorageResourceItem> srcResource, Mock<StorageResourceItem> dstResource) in resources)
        {
            DataTransfer transfer = await transferManager.StartTransferAsync(
                srcResource.Object,
                dstResource.Object,
                new()
                {
                    InitialTransferSize = chunkSize,
                    MaximumTransferChunkSize = chunkSize,
                });
            transfers.Add(transfer);

            // Assert that job plan file is created properly
            Assert.That(checkpointer.Jobs.ContainsKey(transfer.Id), Is.True, "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.Queued), "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Parts.Count, Is.EqualTo(0), "Job Part files should not exist before job processing");
        }
        Assert.That(checkpointer.Jobs.Count, Is.EqualTo(transfers.Count), "Error during Job plan file creation.");
        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(items), "Error during initial Job queueing.");

        // Pause transfers #1
        foreach (DataTransfer transfer in transfers)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
        }

        // Process jobs
        await jobsProcessor.StepAll();

        await Task.Delay(50);
        // Assert that we properly paused
        int pausedJobsCount0 = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        int queuedPartsCount0 = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Queued];
        int jobPartsCreatedCount0 = transfers.Sum(transfer => checkpointer.Jobs[transfer.Id].Parts.Count);
        int enumerationCompleteCount0 = GetEnumerationCompleteCount(transfers, checkpointer);
        Assert.That(pausedJobsCount0, Is.EqualTo(items), "Error in Pausing all");
        Assert.That(queuedPartsCount0, Is.EqualTo(0), "Error in Pausing all");
        Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(0), "Error in Pausing all");
        Assert.That(jobPartsCreatedCount0, Is.EqualTo(0), "Error in Pausing all");
        Assert.That(enumerationCompleteCount0, Is.EqualTo(0), "Error: none of the jobs should have finished enumerating");

        // Resume transfers #1
        List<DataTransfer> resumedTransfers1 = await transferManager.ResumeAllTransfersAsync(new()
        {
            InitialTransferSize = chunkSize,
            MaximumTransferChunkSize = chunkSize,
        });

        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(items), "Error: job processing on resume #1");

        // process jobs on resume #1
        await jobsProcessor.StepAll();

        await Task.Delay(50);
        AssertJobProcessingSuccessful(items, resumedTransfers1, checkpointer);
        Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(items), "Error: job part processing on resume #1");

        // Pause transfers #2
        foreach (DataTransfer transfer in resumedTransfers1)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
        }

        // process job parts on resume #1
        await partsProcessor.StepAll();

        await Task.Delay(50);
        // Assert that we properly paused
        int pausedJobsCount1 = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        int pausedPartsCount1 = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        Assert.That(pausedJobsCount1, Is.EqualTo(items), "Error in Pausing all");
        Assert.That(pausedPartsCount1, Is.EqualTo(items), "Error in Pausing all");
        Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(0), "Error in Pausing all");

        // Resume transfers #2
        List<DataTransfer> resumedTransfers2 = await transferManager.ResumeAllTransfersAsync(new()
        {
            InitialTransferSize = chunkSize,
            MaximumTransferChunkSize = chunkSize,
        });

        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(items), "Error: job processing on resume #2");

        // Process jobs on resume #2
        await jobsProcessor.StepAll();

        await Task.Delay(50);
        AssertJobProcessingSuccessful(items, resumedTransfers2, checkpointer);
        Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(items), "Error: part processing on resume #2");

        // Process parts on resume #2
        await partsProcessor.StepAll();

        await Task.Delay(50);
        AssertPartProcessingSuccessful(items, resumedTransfers2, checkpointer);
        Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(numChunks), "Error: chunk processing on resume #2");

        // Pause transfers #3
        foreach (DataTransfer transfer in resumedTransfers2)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
        }

        // Process chunks on resume #2
        await chunksProcessor.StepAll();

        await Task.Delay(50);
        // Assert that we properly paused
        var jobsStateCount2 = GetJobsStateCount(transfers, checkpointer);
        int pausedJobsCount2 = jobsStateCount2[DataTransferState.Paused];
        int completedJobsCount2 = jobsStateCount2[DataTransferState.Completed];
        var partsStateCount2 = GetJobPartsStateCount(transfers, checkpointer);
        int pausedPartsCount2 = partsStateCount2[DataTransferState.Paused];
        int completedPartsCount2 = partsStateCount2[DataTransferState.Completed];
        Assert.That(pausedJobsCount2, Is.EqualTo(items), "Error in Pausing all");
        Assert.That(completedJobsCount2, Is.EqualTo(0), "Error in Pausing all");
        Assert.That(pausedPartsCount2, Is.EqualTo(items), "Error in Pausing all");
        Assert.That(completedPartsCount2, Is.EqualTo(0), "Error in Pausing all");

        // Resume transfers #3
        List<DataTransfer> resumedTransfers3 = await transferManager.ResumeAllTransfersAsync(new()
        {
            InitialTransferSize = chunkSize,
            MaximumTransferChunkSize = chunkSize,
        });

        await AssertResumeTransfer(items, items, numChunks, chunksPerPart, resumedTransfers3, checkpointer, jobsProcessor, partsProcessor, chunksProcessor);
    }

    [Test]
    [Combinatorial]
    public async Task MultiplePausesAndResumes_ContainerTransfer(
        [Values(2, 6)] int numJobs,
        [Values(333, 500, 1024)] int itemSize,
        [Values(333, 1024)] int chunkSize)
    {
        static int GetItemCountFromContainerIndex(int i) => i * 2;

        int numJobParts = Enumerable.Range(1, numJobs).Select(GetItemCountFromContainerIndex).Sum();
        int chunksPerPart = (int)Math.Ceiling((float)itemSize / chunkSize);
        // TODO: below should be only `items * chunksPerPart` but can't in some cases due to
        //       a bug in how work items are processed on multipart uploads.
        int numChunks = Math.Max(chunksPerPart - 1, 1) * numJobParts;

        Uri srcUri = new("file:///foo/bar");
        Uri dstUri = new("https://example.com/fizz/buzz");

        (var jobsProcessor, var partsProcessor, var chunksProcessor) = StepProcessors();
        JobBuilder jobBuilder = new(ArrayPool<byte>.Shared, default, new ClientDiagnostics(ClientOptions.Default));
        MemoryTransferCheckpointer checkpointer = new();

        var resources = Enumerable.Range(1, numJobs).Select(i =>
        {
            Mock<StorageResourceContainer> srcResource = new(MockBehavior.Strict);
            Mock<StorageResourceContainer> dstResource = new(MockBehavior.Strict);
            (srcResource, dstResource).BasicSetup(srcUri, dstUri, GetItemCountFromContainerIndex(i), itemSize);
            return (Source: srcResource, Destination: dstResource);
        }).ToList();

        List<StorageResourceProvider> resumeProviders = new() { new MockStorageResourceProvider(checkpointer) };

        await using TransferManager transferManager = new(
            jobsProcessor,
            partsProcessor,
            chunksProcessor,
            jobBuilder,
            checkpointer,
            resumeProviders);

        List<DataTransfer> transfers = new();

        // queue jobs
        foreach ((Mock<StorageResourceContainer> srcResource, Mock<StorageResourceContainer> dstResource) in resources)
        {
            DataTransfer transfer = await transferManager.StartTransferAsync(
                srcResource.Object,
                dstResource.Object,
                new()
                {
                    InitialTransferSize = chunkSize,
                    MaximumTransferChunkSize = chunkSize,
                });
            transfers.Add(transfer);

            // Assert that job plan file is created properly
            Assert.That(checkpointer.Jobs.ContainsKey(transfer.Id), Is.True, "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Status.State, Is.EqualTo(DataTransferState.Queued), "Error during Job plan file creation.");
            Assert.That(checkpointer.Jobs[transfer.Id].Parts.Count, Is.EqualTo(0), "Job Part files should not exist before job processing");
        }
        Assert.That(checkpointer.Jobs.Count, Is.EqualTo(transfers.Count), "Error during Job plan file creation.");
        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(numJobs), "Error during initial Job queueing.");

        // Pause transfers #1
        foreach (DataTransfer transfer in transfers)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
        }

        // Process jobs
        await jobsProcessor.StepAll();

        await Task.Delay(50);
        // Assert that we properly paused
        int pausedJobsCount0 = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        int queuedPartsCount0 = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Queued];
        int jobPartsCreatedCount0 = transfers.Sum(transfer => checkpointer.Jobs[transfer.Id].Parts.Count);
        int enumerationCompleteCount0 = GetEnumerationCompleteCount(transfers, checkpointer);
        Assert.That(pausedJobsCount0, Is.EqualTo(numJobs), "Error in Pausing all");
        Assert.That(queuedPartsCount0, Is.EqualTo(0), "Error in Pausing all");
        Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(0), "Error in Pausing all");
        Assert.That(jobPartsCreatedCount0, Is.EqualTo(0), "Error in Pausing all");
        Assert.That(enumerationCompleteCount0, Is.EqualTo(0), "Error: none of the jobs should have finished enumerating");

        // Resume transfers #1
        List<DataTransfer> resumedTransfers1 = await transferManager.ResumeAllTransfersAsync(new()
        {
            InitialTransferSize = chunkSize,
            MaximumTransferChunkSize = chunkSize,
        });

        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(numJobs), "Error: job processing on resume #1");

        // process jobs on resume #1
        await jobsProcessor.StepAll();

        await Task.Delay(50);
        AssertJobProcessingSuccessful(numJobs, resumedTransfers1, checkpointer);
        Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(numJobParts), "Error: job part processing on resume #1");

        // Pause transfers #2
        foreach (DataTransfer transfer in resumedTransfers1)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
        }

        // process job parts on resume #1
        await partsProcessor.StepAll();

        await Task.Delay(50);
        // Assert that we properly paused
        int pausedJobsCount1 = GetJobsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        int pausedPartsCount1 = GetJobPartsStateCount(transfers, checkpointer)[DataTransferState.Paused];
        Assert.That(pausedJobsCount1, Is.EqualTo(numJobs), "Error in Pausing all");
        Assert.That(pausedPartsCount1, Is.EqualTo(numJobParts), "Error in Pausing all");
        Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(0), "Error in Pausing all");

        // Resume transfers #2
        List<DataTransfer> resumedTransfers2 = await transferManager.ResumeAllTransfersAsync(new()
        {
            InitialTransferSize = chunkSize,
            MaximumTransferChunkSize = chunkSize,
        });

        Assert.That(jobsProcessor.ItemsInQueue, Is.EqualTo(numJobs), "Error: job processing on resume #2");

        // Process jobs on resume #2
        await jobsProcessor.StepAll();

        await Task.Delay(50);
        AssertJobProcessingSuccessful(numJobs, resumedTransfers2, checkpointer);
        Assert.That(partsProcessor.ItemsInQueue, Is.EqualTo(numJobParts), "Error: part processing on resume #2");

        // Process parts on resume #2
        await partsProcessor.StepAll();

        await Task.Delay(50);
        AssertPartProcessingSuccessful(numJobParts, resumedTransfers2, checkpointer);
        Assert.That(chunksProcessor.ItemsInQueue, Is.EqualTo(numChunks), "Error: chunk processing on resume #2");

        // Pause transfers #3
        foreach (DataTransfer transfer in resumedTransfers2)
        {
            Task pauseTask = transferManager.PauseTransferIfRunningAsync(transfer.Id);
            Assert.That(DataTransferState.Pausing, Is.EqualTo(transfer.TransferStatus.State), "Error in transitioning to Pausing state");
        }

        // Process chunks on resume #2
        await chunksProcessor.StepAll();

        await Task.Delay(50);
        // Assert that we properly paused
        var jobsStateCount2 = GetJobsStateCount(transfers, checkpointer);
        int pausedJobsCount2 = jobsStateCount2[DataTransferState.Paused];
        int completedJobsCount2 = jobsStateCount2[DataTransferState.Completed];
        var partsStateCount2 = GetJobPartsStateCount(transfers, checkpointer);
        int pausedPartsCount2 = partsStateCount2[DataTransferState.Paused];
        int completedPartsCount2 = partsStateCount2[DataTransferState.Completed];
        Assert.That(pausedJobsCount2, Is.EqualTo(numJobs), "Error in Pausing all");
        Assert.That(completedJobsCount2, Is.EqualTo(0), "Error in Pausing all");
        Assert.That(pausedPartsCount2, Is.EqualTo(numJobParts), "Error in Pausing all");
        Assert.That(completedPartsCount2, Is.EqualTo(0), "Error in Pausing all");

        // Resume transfers #3
        List<DataTransfer> resumedTransfers3 = await transferManager.ResumeAllTransfersAsync(new()
        {
            InitialTransferSize = chunkSize,
            MaximumTransferChunkSize = chunkSize,
        });

        await AssertResumeTransfer(numJobs, numJobParts, numChunks, chunksPerPart, resumedTransfers3, checkpointer, jobsProcessor, partsProcessor, chunksProcessor);
    }

    public enum PauseLocation
    {
        PauseProcessHalfway,
        PauseProcessStart
    }
}
