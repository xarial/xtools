﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xarial.CadPlus.XBatch.MDI
{
    public interface IJobResult
    {
        IJobResultLog Log { get; }
        IJobResultSummary Summary { get; }
    }
}
