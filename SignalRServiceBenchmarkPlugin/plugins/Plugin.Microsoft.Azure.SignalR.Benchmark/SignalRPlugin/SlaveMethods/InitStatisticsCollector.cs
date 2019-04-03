﻿using Plugin.Base;
using Serilog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Plugin.Microsoft.Azure.SignalR.Benchmark.SlaveMethods
{
    public class InitStatisticsCollector : InitStatisticsCollectorBase, ISlaveMethod
    {
        public Task<IDictionary<string, object>> Do(
            IDictionary<string, object> stepParameters,
            IDictionary<string, object> pluginParameters)
        {
            Log.Information($"Init statistic collector...");
            return Run(stepParameters, pluginParameters, RegisterLatencyStatistics);
        }
    }
}
