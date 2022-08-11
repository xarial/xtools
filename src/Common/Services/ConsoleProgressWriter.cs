﻿//*********************************************************************
//CAD+ Toolset
//Copyright(C) 2022 Xarial Pty Limited
//Product URL: https://cadplus.xarial.com
//License: https://cadplus.xarial.com/license/
//*********************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xarial.CadPlus.Plus.Services;

namespace Xarial.CadPlus.Common.Services
{
    public class ConsoleProgressWriter : IDisposable
    {
        private IReadOnlyList<IJobItem> m_Scope;

        private readonly IBatchJobBase m_Job;

        public ConsoleProgressWriter(IBatchJobBase job) 
        {
            m_Job = job;
            m_Job.Started += OnJobStarted;
            m_Job.Completed += OnJobCompleted;
            m_Job.Initialized += OnJobInitialized;
            m_Job.ItemProcessed += OnItemProcessed;
            m_Job.ProgressChanged += OnProgressChanged;
            m_Job.Log += OnLog;
        }

        private void OnJobStarted(IBatchJobBase sender, DateTime startTime) => SetStarted(startTime);
        private void OnJobCompleted(IBatchJobBase sender, TimeSpan duration, JobStatus_e status) => ReportCompleted(duration, status);
        private void OnJobInitialized(IBatchJobBase sender, IReadOnlyList<IJobItem> jobItems, IReadOnlyList<IJobItemOperationDefinition> operations) => SetJobScope(jobItems);
        private void OnItemProcessed(IBatchJobBase sender, IJobItem file) => ReportStatus(file);
        private void OnProgressChanged(IBatchJobBase sender, double progress) => ReportProgress(progress);
        private void OnLog(IBatchJobBase sender, string msg) => Log(msg);

        private void Log(string msg) => Console.WriteLine(msg);

        private void ReportProgress(double progress)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Progress: {(progress * 100).ToString("F")}%");
            Console.ResetColor();
        }

        private void SetStarted(DateTime startTime)
        {
            Console.WriteLine($"Started: {startTime}");
        }

        private void ReportStatus(IJobItem file)
        {
            Console.WriteLine($"Result: {file.State.Status}");
        }

        private void SetJobScope(IReadOnlyList<IJobItem> scope)
        {
            m_Scope = scope;
            Console.WriteLine($"Processing {scope.Count} file(s)");
        }

        private void ReportCompleted(TimeSpan duration, JobStatus_e status)
        {
            Console.WriteLine($"Operation completed: {duration}: {status}");
            Console.WriteLine($"Processed: {m_Scope.Count(j => j.State.Status == JobItemStateStatus_e.Succeeded)}");
            Console.WriteLine($"Warning: {m_Scope.Count(j => j.State.Status == JobItemStateStatus_e.Warning)}");
            Console.WriteLine($"Failed: {m_Scope.Count(j => j.State.Status == JobItemStateStatus_e.Failed)}");
        }

        public void Dispose()
        {
            m_Job.Completed -= OnJobCompleted;
            m_Job.Initialized -= OnJobInitialized;
            m_Job.ItemProcessed -= OnItemProcessed;
            m_Job.ProgressChanged -= OnProgressChanged;
            m_Job.Log -= OnLog;
        }
    }
}
