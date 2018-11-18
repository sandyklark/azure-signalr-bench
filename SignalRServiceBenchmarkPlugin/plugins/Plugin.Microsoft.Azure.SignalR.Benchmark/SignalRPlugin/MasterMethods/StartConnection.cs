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
    public class StartConnection : IMasterMethod
    {
        public Task Do(IDictionary<string, object> stepParameters, IDictionary<string, object> pluginParameters, IList<IRpcClient> clients)
        {
            Log.Information($"Start connections...");

            // Get parameters
            stepParameters.TryGetTypedValue(SignalRConstants.ConcurrentConnection, out int concurrentConnection, Convert.ToInt32);

            if (concurrentConnection < clients.Count)
            {
                var message = $"Concurrent connection {concurrentConnection} should be larger than the number of slaves {clients.Count}";
                Log.Error(message);
                throw new Exception(message);
            }

            var packages = clients.Select((client, i) =>
            {
                int currentConcurrentConnection = Util.SplitNumber(concurrentConnection, i, clients.Count);
                var data = new Dictionary<string, object> { { SignalRConstants.ConcurrentConnection, currentConcurrentConnection } };
                // Add method and type
                PluginUtils.AddMethodAndType(data, stepParameters);
                return new { Client = client, Data = data };
            });

            var results = from package in packages select package.Client.QueryAsync(package.Data);

            return Task.WhenAll(results);
        }
    }
}
