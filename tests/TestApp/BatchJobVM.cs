﻿using Moq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TestApp.Properties;
using Xarial.CadPlus.Plus.Services;
using Xarial.CadPlus.Plus.Shared.Helpers;
using Xarial.CadPlus.Plus.Shared.Services;
using Xarial.CadPlus.Plus.Shared.ViewModels;
using Xarial.XCad.Base;
using Xarial.XToolkit.Services;
using Xarial.XToolkit.Wpf;
using Xarial.XToolkit.Wpf.Extensions;

namespace TestApp
{
    public class MyAsyncBatchJob : IAsyncBatchJob
    {
        public event BatchJobStartedDelegate Started;
        public event BatchJobInitializedDelegate Initialized;
        public event BatchJobCompletedDelegate Completed;
        public event BatchJobItemProcessedDelegate ItemProcessed;
        public event BatchJobLogDelegateDelegate Log;

        public IReadOnlyList<IBatchJobItem> JobItems => m_JobItems;
        public IReadOnlyList<string> LogEntries => m_LogEntries;
        public IReadOnlyList<IBatchJobItemOperationDefinition> OperationDefinitions => m_OperationDefinitions;

        public IBatchJobState State => m_State;

        private List<MyJobItem> m_JobItems;
        private List<string> m_LogEntries;
        private List<IBatchJobItemOperationDefinition> m_OperationDefinitions;

        private readonly BatchJobState m_State;

        public MyAsyncBatchJob()
        {
            m_JobItems = new List<MyJobItem>();
            m_LogEntries = new List<string>();
            m_OperationDefinitions = new List<IBatchJobItemOperationDefinition>();
            m_State = new BatchJobState();
        }

        public async Task TryExecuteAsync(CancellationToken cancellationToken)
            => await this.HandleJobExecuteAsync(
                t => Started?.Invoke(this, t),
                t => m_State.StartTime = t,
                Init,
                c => m_State.TotalItemsCount = c,
                () => Initialized?.Invoke(this, m_JobItems, m_OperationDefinitions),
                DoWork,
                d => Completed?.Invoke(this, d, m_State.Status),
                d => m_State.Duration = d,
                s => m_State.Status = s, cancellationToken, Mock.Of<IXLogger>());

        private async Task<int> Init(CancellationToken cancellationToken)
        {
            var icon1 = Resources.icon1.ToBitmapImage(true);
            var icon2 = Resources.icon2.ToBitmapImage(true);
            var icon3 = Resources.icon3.ToBitmapImage(true);
            var icon4 = Resources.icon4.ToBitmapImage(true);

            var opers = new MyJobItemOperationDefinition[]
            {
                new MyJobItemOperationDefinition("Operation #1", icon1),
                new MyJobItemOperationDefinition("Operation #2", icon2),
                new MyJobItemOperationDefinition("Operation #3", icon1),
                new MyJobItemOperationDefinition("Operation #4", null)
            };

            var items = new MyJobItem[]
            {
                new MyJobItem(icon3, Resources.preview.ToBitmapImage(true), "Item1", "First Item", null, opers, null),
                new MyJobItem(icon4, null, "Item2", "Second Item", () => MessageBox.Show("Item2 is clicked"), opers, null),
                new MyJobItem(icon3, null, "Item3", "Third Item", null, opers, null),
                new MyJobItem(null, null, "Item4", "Fourth Item", null, opers, null)
            };

            var startTime = DateTime.Now;

            //Initializing
            await Task.Delay(TimeSpan.FromSeconds(2));

            m_JobItems.AddRange(items);

            m_OperationDefinitions.AddRange(opers);

            return m_JobItems.Count;
        }

        private async Task DoWork(CancellationToken cancellationToken)
        {
            async Task ProcessJobItemOperation(MyJobItemOperation oper, BatchJobItemStateStatus_e res, object userRes, string[] issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                oper.Update(BatchJobItemStateStatus_e.InProgress, null, null);
                await Task.Delay(TimeSpan.FromSeconds(0.5));
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(1));
                cancellationToken.ThrowIfCancellationRequested();
                oper.Update(res, issues?.Select(i => new BatchJobItemIssue(res == BatchJobItemStateStatus_e.Failed ? BatchJobItemIssueType_e.Error : BatchJobItemIssueType_e.Information, i)).ToArray(), userRes);
            }

            //item1
            m_JobItems[0].Update(BatchJobItemStateStatus_e.InProgress, null);

            Log?.Invoke(this, "Processing item1oper1");
            await ProcessJobItemOperation(m_JobItems[0].Operations[0], BatchJobItemStateStatus_e.Succeeded, null, null);

            Log?.Invoke(this, "Processing item1oper2");
            await ProcessJobItemOperation(m_JobItems[0].Operations[1], BatchJobItemStateStatus_e.Succeeded, 10, null);

            Log?.Invoke(this, "Processing item1oper3");
            await ProcessJobItemOperation(m_JobItems[0].Operations[2], BatchJobItemStateStatus_e.Succeeded, 5, null);

            Log?.Invoke(this, "Processing item1oper4");
            await ProcessJobItemOperation(m_JobItems[0].Operations[3], BatchJobItemStateStatus_e.Succeeded, 5, null);

            m_JobItems[0].Update(m_JobItems[0].ComposeStatus(), null);

            m_State.SucceededItemsCount++;
            ItemProcessed?.Invoke(this, m_JobItems[0]);
            m_State.Progress = 1d / 4d;

            //item2
            m_JobItems[1].Update(BatchJobItemStateStatus_e.InProgress, null);

            Log?.Invoke(this, "Processing item2oper1");
            await ProcessJobItemOperation(m_JobItems[1].Operations[0], BatchJobItemStateStatus_e.Failed, "Failed Result", new string[] { "Some Error 1", "Some Error 2" });

            Log?.Invoke(this, "Processing item2oper2");
            await ProcessJobItemOperation(m_JobItems[1].Operations[1], BatchJobItemStateStatus_e.Succeeded, "Test Result", new string[] { "Some Info 1" });

            Log?.Invoke(this, "Processing item2oper3");
            await ProcessJobItemOperation(m_JobItems[1].Operations[2], BatchJobItemStateStatus_e.Warning, null, null);

            Log?.Invoke(this, "Processing item2oper4");
            await ProcessJobItemOperation(m_JobItems[1].Operations[3], BatchJobItemStateStatus_e.Succeeded, "", null);

            m_JobItems[1].Update(m_JobItems[1].ComposeStatus(), new IBatchJobItemIssue[] { new BatchJobItemIssue(BatchJobItemIssueType_e.Warning, "Some Warning") });

            m_State.WarningItemsCount++;
            ItemProcessed?.Invoke(this, m_JobItems[1]);
            m_State.Progress = 2d / 4d;

            //item3
            m_JobItems[2].Update(BatchJobItemStateStatus_e.InProgress, null);

            Log?.Invoke(this, "Processing item3oper1");
            await ProcessJobItemOperation(m_JobItems[2].Operations[0], BatchJobItemStateStatus_e.Failed, null, null);

            Log?.Invoke(this, "Processing item3oper2");
            await ProcessJobItemOperation(m_JobItems[2].Operations[1], BatchJobItemStateStatus_e.Failed, null, new string[] { "Some Error 3" });

            Log?.Invoke(this, "Processing item3oper3");
            await ProcessJobItemOperation(m_JobItems[2].Operations[2], BatchJobItemStateStatus_e.Failed, null, null);

            Log?.Invoke(this, "Processing item3oper4");
            await ProcessJobItemOperation(m_JobItems[2].Operations[3], BatchJobItemStateStatus_e.Failed, null, null);

            m_JobItems[2].Update(m_JobItems[2].ComposeStatus(), null);

            m_State.FailedItemsCount++;
            ItemProcessed?.Invoke(this, m_JobItems[2]);
            m_State.Progress = 3d / 4d;

            //item4
            m_JobItems[3].Update(BatchJobItemStateStatus_e.InProgress, null);

            Log?.Invoke(this, "Processing item4oper1");
            await ProcessJobItemOperation(m_JobItems[3].Operations[0], BatchJobItemStateStatus_e.Warning, null, null);

            Log?.Invoke(this, "Processing item4oper2");
            await ProcessJobItemOperation(m_JobItems[3].Operations[1], BatchJobItemStateStatus_e.Warning, null, null);

            Log?.Invoke(this, "Processing item4oper3");
            await ProcessJobItemOperation(m_JobItems[3].Operations[2], BatchJobItemStateStatus_e.Warning, null, null);

            Log?.Invoke(this, "Processing item4oper4");
            await ProcessJobItemOperation(m_JobItems[3].Operations[3], BatchJobItemStateStatus_e.Succeeded, null, null);

            m_JobItems[3].Update(m_JobItems[3].ComposeStatus(), null);

            m_State.FailedItemsCount++;
            ItemProcessed?.Invoke(this, m_JobItems[3]);
            m_State.Progress = 4d / 4d;
        }

        public void Dispose()
        {
        }
    }

    public class MyJobItemOperationDefinition : IBatchJobItemOperationDefinition
    {
        public string Name { get; }
        public BitmapImage Icon { get; }

        public MyJobItemOperationDefinition(string name, BitmapImage icon)
        {
            Name = name;
            Icon = icon;
        }
    }

    public class MyJobItem : IBatchJobItem
    {
        public event BatchJobItemNestedItemsInitializedDelegate NestedItemsInitialized;

        IReadOnlyList<IBatchJobItemOperation> IBatchJobItem.Operations => Operations;

        public BitmapImage Icon { get; }
        public BitmapImage Preview { get; }
        public string Title { get; }
        public string Description { get; }
        public Action Link { get; }
        public IReadOnlyList<MyJobItemOperation> Operations { get; }
        public IReadOnlyList<IBatchJobItem> Nested { get; }

        public IBatchJobItemState State => m_State;

        private readonly BatchJobItemState m_State;

        public MyJobItem(BitmapImage icon, BitmapImage preview, string title, string description,
            Action link, MyJobItemOperationDefinition[] operationDefs, IBatchJobItem[] nested)
        {
            Icon = icon;
            Preview = preview;
            Title = title;
            Description = description;
            Link = link;
            Operations = operationDefs.Select(d => new MyJobItemOperation(d, this)).ToArray();
            Nested = nested;

            m_State = new BatchJobItemState(this);
        }

        public void Update(BatchJobItemStateStatus_e status, IBatchJobItemIssue[] issues)
        {
            m_State.Status = status;
            m_State.Issues = issues;
        }
    }

    public class MyJobItemOperation : IBatchJobItemOperation
    {
        public event BatchJobItemOperationUserResultChangedDelegate UserResultChanged;

        public IBatchJobItemOperationDefinition Definition { get; }
        
        public object UserResult 
        {
            get => m_UserResult;
            private set 
            {
                m_UserResult = value;
                UserResultChanged?.Invoke(this, UserResult);
            }
        }

        public IBatchJobItemOperationState State => m_State;

        private BatchJobItemOperationState m_State;

        private readonly IBatchJobItem m_Item;

        private object m_UserResult;

        public MyJobItemOperation(IBatchJobItemOperationDefinition def, IBatchJobItem item) 
        {
            Definition = def;
            
            m_Item = item;

            m_State = new BatchJobItemOperationState(m_Item, this);
        }

        public void Update(BatchJobItemStateStatus_e status, IBatchJobItemIssue[] issues, object userRes) 
        {
            m_State.Status = status;
            m_State.Issues = issues;

            UserResult = userRes;
        }
    }

    public class BatchJobVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public BatchJobBaseVM Result 
        {
            get => m_Result;
            private set 
            {
                m_Result = value;
                this.NotifyChanged();
            }
        }

        public ICommand RunJobCommand { get; }
        public ICommand CancelJobCommand { get; }

        private BatchJobBaseVM m_Result;

        private CancellationTokenSource m_CancellationTokenSource;

        public BatchJobVM() 
        {
            RunJobCommand = new RelayCommand(RunJob, () => m_CancellationTokenSource == null);
        }

        private async void RunJob()
        {
            m_CancellationTokenSource = new CancellationTokenSource();

            var res = new AsyncBatchJobVM(new MyAsyncBatchJob(), new GenericMessageService(), Mock.Of<IXLogger>(), m_CancellationTokenSource, null, null);

            Result = res;

            await res.TryRunBatchAsync();

            MessageBox.Show($"Completed: {res.Status}");

            m_CancellationTokenSource = null;
        }
    }
}
