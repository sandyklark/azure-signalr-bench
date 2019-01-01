﻿using Common;
using Plugin.Base;
using Rpc.Service;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin.Microsoft.Azure.SignalR.Benchmark.MasterMethods
{
    public class Reconnect : IMasterMethod
    {
        public Task Do(IDictionary<string, object> stepParameters, IDictionary<string, object> pluginParameters, IList<IRpcClient> clients)
        {
            Log.Information($"Reconnect connections...");

            // Get parameters
            stepParameters.TryGetTypedValue(SignalRConstants.ConnectionTotal, out int connectionTotal, Convert.ToInt32);
            stepParameters.TryGetTypedValue(SignalRConstants.HubUrls, out string hubUrl, Convert.ToString);
            stepParameters.TryGetTypedValue(SignalRConstants.TransportType, out string transportType, Convert.ToString);
            stepParameters.TryGetTypedValue(SignalRConstants.HubProtocol, out string hubProtocol, Convert.ToString);
            stepParameters.TryGetTypedValue(SignalRConstants.ConcurrentConnection, out int concurrentConnection, Convert.ToInt32);

            // Prepare configuration for each clients
            var packages = clients.Select((client, i) =>
            {
                int currentConcurrentConnection = Util.SplitNumber(concurrentConnection, i, clients.Count);
                (int beg, int end) = Util.GetConnectionRange(connectionTotal, i, clients.Count);
                var data = new Dictionary<string, object>
                {
                    { SignalRConstants.HubUrls, hubUrl },
                    { SignalRConstants.TransportType, transportType },
                    { SignalRConstants.HubProtocol, hubProtocol },
                    // Make sure concurrent connection is at least 1
                    { SignalRConstants.ConcurrentConnection, currentConcurrentConnection > 0 ? currentConcurrentConnection : 1}
                };
                // Add method and type
                PluginUtils.AddMethodAndType(data, stepParameters);
                return new { Client = client, Data = data };
            });

            // Process on clients
            var results = from package in packages select package.Client.QueryAsync(package.Data);
            var task = Task.WhenAll(results);
            if (stepParameters.TryGetValue(SignalRConstants.Duration, out object value))
            {
                stepParameters.TryGetTypedValue(SignalRConstants.Duration, out long duration, Convert.ToInt64);
                return Util.TimeoutCheckedTask(task, duration * 2, nameof(Reconnect));
            }
            else
            {
                return Util.TimeoutCheckedTask(task, SignalRConstants.MillisecondsToWait, nameof(Reconnect));
            }
        }
    }
}
