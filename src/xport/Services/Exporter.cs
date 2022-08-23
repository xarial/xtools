﻿//*********************************************************************
//CAD+ Toolset
//Copyright(C) 2022 Xarial Pty Limited
//Product URL: https://cadplus.xarial.com
//License: https://cadplus.xarial.com/license/
//*********************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xarial.CadPlus.Common.Services;
using Xarial.CadPlus.Plus.Extensions;
using Xarial.CadPlus.Plus.Services;
using Xarial.CadPlus.Plus.Shared.Helpers;
using Xarial.CadPlus.Plus.Shared.Services;
using Xarial.CadPlus.Xport.Core;
using Xarial.XCad.Base;
using Xarial.XToolkit;
using Xarial.XToolkit.Wpf.Utils;

namespace Xarial.CadPlus.Xport.Services
{
    public class JobItemFile : IBatchJobItem
    {
        public event BatchJobItemNestedItemsInitializedDelegate NestedItemsInitialized;

        private const string EDRW_FORMAT = ".e";

        IReadOnlyList<IBatchJobItemOperation> IBatchJobItem.Operations => Operations;
        IBatchJobItemState IBatchJobItem.State => State;

        public string FilePath { get; }

        internal JobItemFile(string filePath, string outDir, JobItemExportFormatDefinition[] formatDefs)
        {
            FilePath = filePath;
            
            Title = Path.GetFileName(filePath);
            Description = filePath;
            Link = TryOpenInFileExplorer;
            State = new BatchJobItemState(this);

            var outFiles = new JobItemExportFormat[formatDefs.Length];

            for (int i = 0; i < formatDefs.Length; i++)
            {
                var formatDef = formatDefs[i];

                var ext = formatDef.Extension;

                if (ext.Equals(EDRW_FORMAT, StringComparison.CurrentCultureIgnoreCase))
                {
                    switch (Path.GetExtension(filePath).ToLower())
                    {
                        case ".sldprt":
                            ext = ".eprt";
                            break;

                        case ".sldasm":
                            ext = ".easm";
                            break;

                        case ".slddrw":
                            ext = ".edrw";
                            break;

                        default:
                            throw new ArgumentException($"{EDRW_FORMAT} format is only applicable for SOLIDWORKS files");
                    }
                }

                outFiles[i] = new JobItemExportFormat(this, Path.Combine(!string.IsNullOrEmpty(outDir) ? outDir : Path.GetDirectoryName(filePath),
                    Path.GetFileNameWithoutExtension(filePath) + ext), formatDef);
            }

            Operations = outFiles;
        }

        public BitmapImage Icon { get; }
        public BitmapImage Preview { get; }
        public string Title { get; }
        public string Description { get; }

        public Action Link { get; }

        public BatchJobItemState State { get; }

        public IReadOnlyList<JobItemExportFormat> Operations { get; }

        public IReadOnlyList<IBatchJobItem> Nested { get; }

        private void TryOpenInFileExplorer()
        {
            try
            {
                FileSystemUtils.BrowseFileInExplorer(FilePath);
            }
            catch
            {
            }
        }
    }

    public class JobItemExportFormatDefinition : IBatchJobItemOperationDefinition
    {
        public string Name { get; }
        public BitmapImage Icon { get; }

        public string Extension { get; }

        public JobItemExportFormatDefinition(string ext)
        {
            Extension = ext;

            Name = ext;
        }
    }

    public class JobItemExportFormat : IBatchJobItemOperation
    {
        public event BatchJobItemOperationUserResultChangedDelegate UserResultChanged;

        IBatchJobItemOperationState IBatchJobItemOperation.State => State;

        public string OutputFilePath { get; }
        public IBatchJobItemOperationDefinition Definition { get; }

        public BatchJobItemOperationState State { get; }
        public object UserResult { get; }

        public JobItemExportFormat(JobItemFile file, string outFilePath, JobItemExportFormatDefinition def)
        {
            OutputFilePath = outFilePath;
            Definition = def;
            State = new BatchJobItemOperationState(file, this);
        }
    }

    public class Exporter : IAsyncBatchJob
    {
        private readonly IJobProcessManager m_JobMgr;

        public event BatchJobStartedDelegate Started;
        public event BatchJobInitializedDelegate Initialized;
        public event BatchJobItemProcessedDelegate ItemProcessed;
        public event BatchJobLogDelegateDelegate Log;
        public event BatchJobCompletedDelegate Completed;

        private readonly ExportOptions m_Opts;

        public IReadOnlyList<IBatchJobItem> JobItems => m_JobFiles;
        public IReadOnlyList<IBatchJobItemOperationDefinition> OperationDefinitions { get; private set; }
        public IReadOnlyList<string> LogEntries => m_LogEntries;

        public IBatchJobState State => m_State;

        private readonly List<string> m_LogEntries;

        private readonly IXLogger m_Logger;

        private JobItemFile[] m_JobFiles;

        private readonly BatchJobState m_State;

        public Exporter(IJobProcessManager jobMgr, ExportOptions opts, IXLogger logger)
        {
            m_JobMgr = jobMgr;
            m_Opts = opts;
            m_Logger = logger;

            m_LogEntries = new List<string>();
            m_State = new BatchJobState();
        }

        public async Task TryExecuteAsync(CancellationToken cancellationToken)
            => await this.HandleJobExecuteAsync(
                t => Started?.Invoke(this, t),
                t => m_State.StartTime = t,
                InitAsync,
                c => m_State.TotalItemsCount = c,
                () => Initialized?.Invoke(this, JobItems, OperationDefinitions),
                DoWorkAsync,
                d => Completed?.Invoke(this, d, m_State.Status),
                d => m_State.Duration = d,
                s => m_State.Status = s,
                cancellationToken, m_Logger);

        private Task<int> InitAsync(CancellationToken arg)
        {
            AddLogEntry($"Exporting Started");

            m_JobFiles = ParseOptions(m_Opts, out var formats);

            OperationDefinitions = formats;

            return Task.FromResult(m_JobFiles.Length);
        }

        private async Task DoWorkAsync(CancellationToken cancellationToken)
        {
            for (int i = 0; i < m_JobFiles.Length; i++)
            {
                var file = m_JobFiles[i];

                await file.HandleJobItemAsync(
                    (f, s) => f.State.Status = s,
                    f => ExportFormats(f, cancellationToken),
                    (f, e) => f.State.ReportError(e),
                    f => m_State.IncrementItemsCount(f),
                    () => m_State.Progress = (i + 1) / (double)m_JobFiles.Length,
                    f => ItemProcessed?.Invoke(this, f));
            }
        }

        private async Task ExportFormats(JobItemFile file, CancellationToken cancellationToken)
        {
            foreach (var outFile in file.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await outFile.HandleJobItemOperationAsync(
                    (f, s) => f.State.Status = s,
                    f => ExportFormat(f, file.FilePath, cancellationToken),
                    (f, e) =>
                    {
                        f.State.ReportError(e);

                        if (!m_Opts.ContinueOnError)
                        {
                            throw e;
                        }
                    });
            }
        }

        private async Task ExportFormat(JobItemExportFormat outFile, string srcFilePath, CancellationToken cancellationToken)
        {
            var desFile = GetAvailableDestinationFile(outFile);

            var prcStartInfo = new ProcessStartInfo()
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                FileName = typeof(StandAloneExporter.Program).Assembly.Location,
                Arguments = $"\"{srcFilePath}\" \"{desFile}\" {m_Opts.Version}"
            };

            var tcs = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (m_Opts.Timeout > 0)
            {
                tcs.CancelAfter(TimeSpan.FromSeconds(m_Opts.Timeout));
            }

            var res = await StartWaitProcessAsync(prcStartInfo, tcs.Token).ConfigureAwait(false);

            if (res)
            {
                outFile.State.Status = BatchJobItemStateStatus_e.Succeeded;
            }
            else
            {
                throw new Exception("Failed to process the file");
            }
        }

        private string GetAvailableDestinationFile(JobItemExportFormat outFile)
        {
            var desFile = outFile.OutputFilePath;

            int index = 0;

            while (File.Exists(desFile))
            {
                var outDir = Path.GetDirectoryName(outFile.OutputFilePath);
                var fileName = Path.GetFileNameWithoutExtension(outFile.OutputFilePath);
                var ext = Path.GetExtension(outFile.OutputFilePath);

                fileName = $"{fileName} ({++index})";

                desFile = Path.Combine(outDir, fileName + ext);
            }

            return desFile;
        }

        private Task<bool> StartWaitProcessAsync(ProcessStartInfo prcStartInfo,
            CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var process = new Process();

            var isCancelled = false;

            process.StartInfo = prcStartInfo;
            process.EnableRaisingEvents = true;
            prcStartInfo.RedirectStandardOutput = true;
            process.OutputDataReceived += (sender, e) =>
            {
                var tag = StandAloneExporter.Program.LOG_MESSAGE_TAG;
                if (e.Data?.StartsWith(tag) == true)
                {
                    AddLogEntry(e.Data.Substring(tag.Length));
                }
            };

            process.Exited += (sender, args) =>
            {
                if (!isCancelled)
                {
                    tcs.SetResult(process.ExitCode == 0);
                }
            };

            if (cancellationToken != default)
            {
                cancellationToken.Register(() =>
                {
                    isCancelled = true;
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                    tcs.TrySetCanceled();
                });
            }

            process.Start();
            m_JobMgr.AddProcess(process);
            process.BeginOutputReadLine();
            return tcs.Task;
        }

        private JobItemFile[] ParseOptions(ExportOptions opts, out JobItemExportFormatDefinition[] formatDefs)
        {
            var outDir = opts.OutputDirectory;

            if (!string.IsNullOrEmpty(outDir))
            {
                if (!Directory.Exists(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }
            }

            var filter = opts.Filter;

            if (string.IsNullOrEmpty(filter))
            {
                filter = "*.*";
            }

            formatDefs = new JobItemExportFormatDefinition[opts.Format.Length];

            for (int i = 0; i < opts.Format.Length; i++)
            {
                var ext = opts.Format[i];

                if (!ext.StartsWith("."))
                {
                    ext = "." + ext;
                }

                formatDefs[i] = new JobItemExportFormatDefinition(ext);
            }

            var files = new List<string>();

            foreach (var input in opts.Input)
            {
                if (Directory.Exists(input))
                {
                    files.AddRange(Directory.GetFiles(input, filter, SearchOption.AllDirectories).ToList());
                }
                else if (File.Exists(input))
                {
                    files.Add(input);
                }
                else
                {
                    throw new Exception("Specify input file or directory");
                }
            }

            var jobs = new List<JobItemFile>();

            foreach (var file in files)
            {
                var jobItemFile = new JobItemFile(file, outDir, formatDefs);
                jobs.Add(jobItemFile);
            }

            return jobs.ToArray();
        }

        private void AddLogEntry(string msg)
        {
            m_LogEntries.Add(msg);
            Log?.Invoke(this, msg);
        }

        public void Dispose()
        {
        }
    }
}