﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.SignalRBench.Common;
using Azure.SignalRBench.Coordinator.Entities;
using Azure.SignalRBench.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Portal.Controllers
{
    [Route("teststatus")]
    [ApiController]
    public class TestStatusContorller : ControllerBase
    {
        private IPerfStorage _perfStorage;
        private ILogger<TestStatusContorller> _logger;

        public TestStatusContorller(IPerfStorage perfStorage,ILogger<TestStatusContorller> logger)
        {
            _perfStorage = perfStorage;
            _logger = logger;
        }

        [HttpGet("list/{key?}")]
        public async Task<IEnumerable<TestStatusEntity>> Get(string key)
        {
            try
            {
                var table = await _perfStorage.GetTableAsync<TestStatusEntity>(Constants.TableNames.TestStatus);
                var rows = await table.QueryAsync(string.IsNullOrEmpty(key)
                    ? table.Rows
                    : from row in table.Rows where row.PartitionKey == key select row).ToListAsync();
                rows.Sort((a, b) =>
                    b.Timestamp.CompareTo(a.Timestamp)
                );
                return rows;
            }
            catch (Exception e)
            {
                _logger.LogError(e,"Get test status error");
                throw;
            }
        }
    }
}