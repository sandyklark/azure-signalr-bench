using System;
using System.Collections.Generic;
using Bench.Common;
using Bench.Common.Config;
using Bench.RpcSlave.Worker.Counters;
using Bench.RpcSlave.Worker.Savers;
using Microsoft.AspNetCore.SignalR.Client;

namespace Bench.RpcSlave.Worker
{
    public class WorkerToolkit
    {
        public JobConfig JobConfig { get; set; }
        public List<HubConnection> Connections { get; set; }
        public List<IDisposable> ConnectionCallbacks { get; set; } = new List<IDisposable>();
        public Stat.Types.State State { get; set; } = Stat.Types.State.WorkerUnexist;
        public Counter Counters { get; set; } = new Counter(new LocalFileSaver());
        public Common.BenchmarkCellConfig BenchmarkCellConfig { get; set; }
        public int ServerCount { get; set; }

        public ConnectionConfigList ConnectionConfigList { get; set; }
        public Range ConnectionRange { get; set; }

        public Dictionary<string, bool> Init { get; set; } = new Dictionary<string, bool>();

        public List<string> ConnectionIds { get; set; } = new List<string>();

        // serverless mode needs connection string
        public string ConnectionString { get; set; }
    }
}