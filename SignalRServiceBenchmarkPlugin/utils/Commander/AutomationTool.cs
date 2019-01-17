﻿using Common;
using Medallion.Shell;
using Renci.SshNet;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Commander
{
    public class AutomationTool
    {
        // Executable information
        private readonly string _baseName = "publish";
        private readonly string _appLogFileName = "appserver.log";
        // App server information
        private readonly int _appserverPort;
        private readonly string _azureSignalRConnectionString;
        private readonly string _appserverLogDirPath;
        private readonly bool _notStartAppServer;
        private readonly bool _notStopAppServer;
        // Slave Information
        private readonly int _rpcPort;
        private readonly IList<string> _slaveList;

        // Local project path
        private readonly string _appserverProject;
        private readonly string _masterProject;
        private readonly string _slaveProject;

        // Remote executable files
        private readonly string _appserverTargetPath;
        private readonly string _masterTargetPath;
        private readonly string _slaveTargetPath;

        // Benchmark configuration
        private readonly FileInfo _benchmarkConfiguration;

        // Remote benchmark configuration
        private readonly string _benchmarkConfigurationTargetPath;

        // Scp/Ssh clients
        private RemoteClients _remoteClients;
        private volatile int _startedAppServer;

        public AutomationTool(ArgsOption argOption)
        {
            // Initialize
            _appserverProject = argOption.AppserverProject;
            _appserverLogDirPath = argOption.AppserverLogDirectory;
            _notStartAppServer = argOption.NotStartAppServer == 1;
            _notStopAppServer = argOption.NotStopAppServer == 1;
            _masterProject = argOption.MasterProject;
            _slaveProject = argOption.SlaveProject;
            _appserverTargetPath = argOption.AppserverTargetPath;
            _masterTargetPath = argOption.MasterTargetPath;
            _slaveTargetPath = argOption.SlaveTargetPath;
            _benchmarkConfiguration = new FileInfo(argOption.BenchmarkConfiguration);
            _benchmarkConfigurationTargetPath = argOption.BenchmarkConfigurationTargetPath;
            _slaveList = argOption.SlaveList;
            _rpcPort = argOption.RpcPort;
            _appserverPort = argOption.AppserverPort;
            _azureSignalRConnectionString = argOption.AzureSignalRConnectionString;
            var appServerCount = argOption.AppServerHostnames.Count();
            var appServerCountInUse = argOption.AppServerCountInUse;
            var appServers = argOption.AppServerHostnames?.Take(
                appServerCountInUse < appServerCount ?
                appServerCountInUse: appServerCount).ToList();
            // Create clients
            var slaveHostnames = (from slave in argOption.SlaveList select slave.Split(':')[0]).ToList();
            _remoteClients = new RemoteClients();
            _remoteClients.CreateAll(argOption.Username, argOption.Password, 
                appServers, argOption.MasterHostname, slaveHostnames);
        }

        public void Start()
        {
            try
            {
                // Clients connect to host
                _remoteClients.ConnectAll();

                // Publish dlls
                var (appserverExecutable, masterExecutable, slaveExecutable) = PubishExcutables();

                // Copy executables and configurations
                CopyExcutables(appserverExecutable, masterExecutable, slaveExecutable);

                // Unzip packages
                UnzipExecutables();

                // Run benchmark
                RunBenchmark();
            }
            finally
            {
                // Disconnect and dispose
                _remoteClients.DestroyAll();

                Log.Information("Finish");
            }
        }

        // Only for development
        public void StartDev()
        {
            try
            {
                // Clients connect to host
                _remoteClients.ConnectAll();

                FileInfo appserverExecutable = null, masterExecutable, slaveExecutable;
                if (!_notStartAppServer)
                {
                    appserverExecutable = File.Exists($"{_appserverProject}/{_baseName}.tgz") ?
                        new FileInfo($"{_appserverProject}/{_baseName}.tgz") :
                        new FileInfo($"{_appserverProject}/{_baseName}.zip");
                }
                masterExecutable = File.Exists($"{_masterProject}/{_baseName}.tgz") ?
                    new FileInfo($"{_masterProject}/{_baseName}.tgz") :
                    new FileInfo($"{_masterProject}/{_baseName}.zip");
                slaveExecutable = File.Exists($"{_slaveProject}/{_baseName}.tgz") ?
                    new FileInfo($"{_slaveProject}/{_baseName}.tgz") :
                    new FileInfo($"{_slaveProject}/{_baseName}.zip");

                // Copy executables and configurations
                CopyExcutables(appserverExecutable, masterExecutable, slaveExecutable);

                // Unzip packages
                UnzipExecutables();

                // Run benchmark
                RunBenchmark();
            }
            finally
            {
                // Disconnect and dispose
                _remoteClients.DestroyAll();
            }
        }

        private void OnAsyncSshCommandComplete(IAsyncResult result)
        {
            var command = (SshCommand)result.AsyncState;
            if (command.ExitStatus != 0)
            {
                Log.Error($"SshCommand '{command.CommandText}' occurs error: {command.Error}");
                throw new Exception(command.Error);
            }
        }

        private void OnStartAppAsyncSshCommandComplete(IAsyncResult result)
        {
            _startedAppServer++;
            var command = (SshCommand)result.AsyncState;
            if (command.ExitStatus != 0)
            {
                Log.Error($"SshCommand '{command.CommandText}' occurs error: {command.Error}");
                throw new Exception(command.Error);
            }
        }

        private string GetRemoteAppServerLogPath()
        {
            var appserverDirectory = Path.Combine(Path.GetDirectoryName(_appserverTargetPath),
                        Path.GetFileNameWithoutExtension(_appserverTargetPath), _baseName);
            var appLogFilePath = Path.Combine(appserverDirectory, _appLogFileName);
            return appLogFilePath;
        }

        private string GetLocalAppServerLogPath(string tag)
        {
            var fileName = $"{tag}_{_appLogFileName}";
            var path = Path.Combine(_appserverLogDirPath, fileName);
            return path;
        }

        private void WaitAppServerStarted()
        {
            var recheckTimeout = 600;
            var keyWords = "HttpConnection Started";
            CreateAppServerLogDirIfNotExist();
            var remoteAppLogFilePath = GetRemoteAppServerLogPath();
            string content = null;
            foreach (var client in _remoteClients.AppserverScpClients)
            {
                var host = client.ConnectionInfo.Host;
                var localAppServerLogPath = GetLocalAppServerLogPath(host);
                var recheck = 0;
                while (recheck < recheckTimeout)
                {
                    client.Download(remoteAppLogFilePath, new FileInfo(localAppServerLogPath));
                    using (StreamReader sr = new StreamReader(localAppServerLogPath))
                    {
                        content = sr.ReadToEnd();
                        if (content.Contains(keyWords))
                        {
                            Log.Information($"app server {host} started!");
                            break;
                        }
                    }
                    recheck++;
                    Log.Information($"waiting for appserver {host} starting...");
                    Task.Delay(TimeSpan.FromSeconds(1)).Wait();
                }
                if (recheck == recheckTimeout)
                {
                    Log.Error($"Fail to start server {host}!!!");
                    if (content != null)
                    {
                        Log.Information($"log content: {content}");
                    }
                }
            }
        }

        private void CreateAppServerLogDirIfNotExist()
        {
            if (!String.IsNullOrEmpty(_appserverLogDirPath) && !Directory.Exists(_appserverLogDirPath))
            {
                Directory.CreateDirectory(_appserverLogDirPath);
            }
        }

        private void CopyAppServerLog()
        {
            CreateAppServerLogDirIfNotExist();
            var remoteAppLogFilePath = GetRemoteAppServerLogPath();
            foreach (var client in _remoteClients.AppserverScpClients)
            {
                var host = client.ConnectionInfo.Host;
                var localAppServerLogPath = GetLocalAppServerLogPath(host);
                client.Download(remoteAppLogFilePath, new FileInfo(localAppServerLogPath));
            }
            // zip the applog because it may be big
            /*
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var zip = Command.Run("zip", new string[] { "-r", $"{logPrefix}.zip", $"{logPrefix}*.log" },
                    o => o.WorkingDirectory(_appserverLogDirPath));
                zip.Wait();
                if (!zip.Result.Success)
                {
                    Log.Error(zip.Result.StandardOutput);
                }
            }
            else
            {
                var tar = Command.Run("tar", new string[] { "zcvf", $"{logPrefix}.tgz", $"{logPrefix}*.log" },
                    o => o.WorkingDirectory(_appserverLogDirPath));
                tar.Wait();
                if (!tar.Result.Success)
                {
                    Log.Error(tar.Result.StandardOutput);
                }
                else
                {
                    Command.Run("rm", new string[] { $"{logPrefix}*.log" }, o => o.WorkingDirectory(_appserverLogDirPath));
                }
            }
            */
        }

        private void StartAppServer(bool relaunch=true)
        {
            var appserverDirectory = Path.Combine(Path.GetDirectoryName(_appserverTargetPath),
                        Path.GetFileNameWithoutExtension(_appserverTargetPath), _baseName);
            var appserverScript = "";
            var cmdPrefix = "dotnet exec AppServer.dll";
            var appLogFile = _appLogFileName;
            if (_azureSignalRConnectionString == null)
            {
                if (relaunch)
                {
                    appserverScript = $@"
#!/bin/bash
    isRun=`ps axu|grep '{cmdPrefix}'|wc -l`
    if [ $isRun -gt 1 ]
    then
        killall dotnet
    fi
    export useLocalSignalR=true
    cd {appserverDirectory}
    {cmdPrefix} --urls=http://*:{_appserverPort} > {appLogFile}
";
                }
                else
                {
                    appserverScript = $@"
#!/bin/bash
isRun=`ps axu|grep '{cmdPrefix}'|wc -l`
if [ $isRun -eq 1 ]
then
    export useLocalSignalR=true
    cd {appserverDirectory}
    {cmdPrefix} --urls=http://*:{_appserverPort} > {appLogFile}
else
    echo 'AppServer has started'
fi
";
                }
            }
            else
            {
                if (relaunch)
                {
                    appserverScript = $@"
#!/bin/bash
    isRun=`ps axu|grep '{cmdPrefix}'|wc -l`
    if [ $isRun -gt 1 ]
    then
        killall dotnet
    fi
    dotnet user-secrets set Azure:SignalR:ConnectionString '{_azureSignalRConnectionString}'
    cd {appserverDirectory}
    {cmdPrefix} --urls=http://*:{_appserverPort} > {appLogFile}
";
                }
                else
                {
                    appserverScript = $@"
#!/bin/bash
    isRun=`ps axu|grep '{cmdPrefix}'|wc -l`
    if [ $isRun -eq 1 ]
    then
        dotnet user-secrets set Azure:SignalR:ConnectionString '{_azureSignalRConnectionString}'
        cd {appserverDirectory}
        {cmdPrefix} --urls=http://*:{_appserverPort} > {appLogFile}
    else
        echo 'AppServer has started'
    fi
";
                }
            }
            // Copy script to start appserver to every app server VM
            var scriptName = "startAppServer.sh";
            var startAppServerScript = new FileInfo(scriptName);
            using (var writer = new StreamWriter(startAppServerScript.FullName, true))
            {
                writer.Write(appserverScript);
            }
            var remoteScriptPath = Path.Combine(appserverDirectory, scriptName);
            var startAppServerTasks = new List<Task>();
            startAppServerTasks.AddRange(from client in _remoteClients.AppserverScpClients
                         select Task.Run(() =>
                         {
                             try
                             {
                                 client.Upload(startAppServerScript, remoteScriptPath);
                                 Log.Information($"Successfully upload {startAppServerScript} to {client.ConnectionInfo.Host}/{remoteScriptPath}");
                             }
                             catch (Exception e)
                             {
                                 Log.Error($"Fail to upload startAppServer script: {e.Message}");
                             }
                         }));
            Task.WhenAll(startAppServerTasks).Wait();
            // launch those scripts
            var launchAppserverCmd = $"cd {appserverDirectory}; chmod +x {scriptName}; ./{scriptName}";
            var appserverSshCommands = (from client in _remoteClients.AppserverSshClients
                                        select client.CreateCommand(launchAppserverCmd.Replace('\\', '/'))).ToList();
            var appserverAsyncResults = (from command in appserverSshCommands
                                         select command.BeginExecute(OnStartAppAsyncSshCommandComplete, command)).ToList();
            Util.TimeoutCheckedTask(Task.Run(async ()=>
            {
                while (_startedAppServer < _remoteClients.AppserverSshClients.Count)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                Log.Information("All app server launched");
            }), 60000);
            Task.Delay(TimeSpan.FromSeconds(60)).Wait();
        }

        private void RunBenchmark()
        {
            try
            {
                Log.Information($"Run benchmark");

                // Kill all dotnet
                Log.Information($"Kill all dotnet processes");
                KillallDotnet();

                // Create SSH commands
                
                var slaveDirectory = Path.Combine(Path.GetDirectoryName(_slaveTargetPath),
                    Path.GetFileNameWithoutExtension(_slaveTargetPath), _baseName);
                var masterDirectory = Path.Combine(Path.GetDirectoryName(_masterTargetPath),
                    Path.GetFileNameWithoutExtension(_masterTargetPath), _baseName);
                var masterExecutablePath = Path.Combine(Path.GetDirectoryName(_masterTargetPath),
                    Path.GetFileNameWithoutExtension(_masterTargetPath), _baseName, "master.dll");
                var slaveExecutablePath = Path.Combine(Path.GetDirectoryName(_slaveTargetPath),
                    Path.GetFileNameWithoutExtension(_slaveTargetPath), _baseName, "slave.dll");

                var masterCommand = $"cd {masterDirectory}; " +
                                    $"dotnet exec {masterExecutablePath} -- " +
                                    $"--BenchmarkConfiguration=\"{_benchmarkConfigurationTargetPath}\" " +
                                    $"--SlaveList=\"{string.Join(',', _slaveList)}\"";
                var slaveCommand = $"cd {slaveDirectory}; dotnet exec {slaveExecutablePath} --HostName 0.0.0.0 --RpcPort {_rpcPort}";

                var masterSshCommand = _remoteClients.MasterSshClient.CreateCommand(masterCommand.Replace('\\', '/'));

                // Start app server
                if (!_notStartAppServer)
                {
                    Log.Information($"Start app server");
                    StartAppServer(!_notStopAppServer);
                    WaitAppServerStarted();
                }
                else
                {
                    Log.Information("Do not start appserver !");
                }
                // Start slaves
                // Check connection status
                foreach (var sshClient in _remoteClients.SlaveSshClients)
                {
                    if (!sshClient.IsConnected)
                    {
                        var endpoint = $"{sshClient.ConnectionInfo.Host}:{sshClient.ConnectionInfo.Port}";
                        Log.Warning($"a slave ssh {endpoint} dropped, and try reconnect");
                        try
                        {
                            sshClient.Connect();
                        }
                        catch (Exception e)
                        {
                            Log.Information($"reconnect failure: {e.Message}");
                            throw;
                        }
                    }
                }
                Log.Information("All slaves connected");
                var slaveSshCommands = (from client in _remoteClients.SlaveSshClients
                                        select client.CreateCommand(slaveCommand.Replace('\\', '/'))).ToList();
                Log.Information($"Start slaves");
                var slaveAsyncResults = (from command in slaveSshCommands
                                         select command.BeginExecute(OnAsyncSshCommandComplete, command)).ToList();

                // Start master
                Log.Information($"Start master");
                var masterResult = masterSshCommand.BeginExecute();
                using (var reader =
                   new StreamReader(masterSshCommand.OutputStream, Encoding.UTF8, true, 4096, true))
                {
                    while (!masterResult.IsCompleted || !reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        if (line != null)
                        {
                            Log.Information(line);
                        }
                    }
                }

                masterSshCommand.EndExecute(masterResult);
                if (!_notStartAppServer)
                {
                    CopyAppServerLog();
                }
            }
            finally
            {
                // Killall dotnet
                KillallDotnet();
            }
        }

        private void KillallDotnet()
        {
            void killDotnet(SshClient client)
            {
                var command = "killall dotnet";
                var sshCommand = client.CreateCommand(command);
                _ = sshCommand.Execute();
            }
            if (!_notStartAppServer && !_notStopAppServer)
            {
                _remoteClients.AppserverSshClients.ToList().ForEach(client => killDotnet(client));
            }
            killDotnet(_remoteClients.MasterSshClient);
            _remoteClients.SlaveSshClients.ToList().ForEach(client => killDotnet(client));
        }

        private void CopyExcutables(FileInfo appserverExecutable, FileInfo masterExecutable, FileInfo slaveExecutable)
        {
            Log.Information($"Copy executables to hosts...");

            string GetDir(string path) => Path.GetDirectoryName(path).Replace('\\', '/');

            // Make sure the remote path is available
            var makeDirTasks = new List<Task>();
            makeDirTasks.Add(Task.Run(() => _remoteClients.MasterSshClient.CreateCommand("mkdir -p " + GetDir(_masterTargetPath))));
            if (!_notStartAppServer)
            {
                makeDirTasks.AddRange(from client in _remoteClients.AppserverSshClients
                                      select Task.Run(() => client.CreateCommand(
                                          "mkdir -p " +
                                          GetDir(_appserverTargetPath)).Execute()));
            }
            
            makeDirTasks.AddRange(from client in _remoteClients.SlaveSshClients
                                  select Task.Run(() => client.CreateCommand(
                                      "mkdir -p " +
                                      GetDir(_slaveTargetPath)).Execute()));

            Task.WhenAll(makeDirTasks).Wait();

            // Copy executables
            var copyTasks = new List<Task>();
            if (!_notStartAppServer)
            {
                copyTasks.AddRange(from client in _remoteClients.AppserverScpClients
                                   select Task.Run(() =>
                                   client.Upload(appserverExecutable, _appserverTargetPath)));
            }
                
            copyTasks.Add(Task.Run(() => _remoteClients.MasterScpClient.Upload(masterExecutable, _masterTargetPath)));
            copyTasks.Add(Task.Run(() => _remoteClients.MasterScpClient.Upload(_benchmarkConfiguration, _benchmarkConfigurationTargetPath)));
            copyTasks.AddRange(from client in _remoteClients.SlaveScpClients
                                select Task.Run(() => client.Upload(slaveExecutable, _slaveTargetPath)));
            Task.WhenAll(copyTasks).Wait();
        }

        private (FileInfo appserverExecutable, FileInfo masterExecutable, FileInfo slaveExecutable) PubishExcutables()
        {
            Log.Information($"Generate executables...");

            FileInfo appserverExecutable = null;
            if (!_notStartAppServer)
            {
                appserverExecutable = PubishExcutable(_appserverProject, _baseName);
            }
            var masterExecutable = PubishExcutable(_masterProject, _baseName);
            var slaveExecutable = PubishExcutable(_slaveProject, _baseName);
            return (appserverExecutable, masterExecutable, slaveExecutable); 
        }

        private FileInfo PubishExcutable(string projectPath, string baseName)
        {
            Log.Information($"project path: {projectPath}");
            var publish = Command.Run("dotnet", $"publish -o {baseName}".Split(' '), o => o.WorkingDirectory(projectPath));
            publish.Wait();
            if (!publish.Result.Success) throw new Exception(publish.Result.StandardOutput);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var zip = Command.Run("zip", new string[] { "-r", $"{baseName}.zip", $"{baseName}/" }, o => o.WorkingDirectory(projectPath));
                zip.Wait();
                if (!zip.Result.Success) throw new Exception(zip.Result.StandardOutput);
                return new FileInfo(Path.Combine(projectPath, $"{baseName}.zip"));
            }
            else
            {
                var tar = Command.Run("tar", new string[] { "zcvf", $"{baseName}.tgz", $"{baseName}/" }, o => o.WorkingDirectory(projectPath));
                tar.Wait();
                if (!tar.Result.Success) throw new Exception(tar.Result.StandardOutput);
                return new FileInfo(Path.Combine(projectPath, $"{baseName}.tgz"));
            }
        }

        private void InstallZip(SshClient client)
        {
            var command = client.CreateCommand("sudo apt-get install -y zip");
            var result = command.Execute();
            if (command.Error != "")
            {
                Log.Error($"Install zip error on {client.ConnectionInfo.Host}: {result}, {command.Error}");
            }
        }

        private void Unzip(SshClient client, string targetPath)
        {
            SshCommand command = null;
            var directory = Path.GetDirectoryName(targetPath);
            var filenameWithoutExtension = Path.GetFileNameWithoutExtension(targetPath);
            var fileName = Path.GetFileName(targetPath);
            var ext = Path.GetExtension(targetPath);
            if (string.Equals("zip", ext, StringComparison.OrdinalIgnoreCase))
            {
                InstallZip(client);
                command = client.CreateCommand($"unzip -o -d {filenameWithoutExtension} {targetPath}");
            }
            else
            {
                command = client.CreateCommand($"cd {directory}; mkdir -p {filenameWithoutExtension}; tar zxvf {fileName} -C {directory}/{filenameWithoutExtension}");
            }

            var result = command.Execute();
            if (command.Error != "")
            {
                throw new Exception(command.Error);
            }
        }

        private void UnzipExecutables()
        {
            Log.Information($"Unzip executables");

            var tasks = new List<Task>();
            if (!_notStartAppServer)
            {
                tasks.AddRange(from client in _remoteClients.AppserverSshClients
                               select Task.Run(() => Unzip(client, _appserverTargetPath)));
            }
            tasks.Add(Task.Run(() => Unzip(_remoteClients.MasterSshClient, _masterTargetPath)));
            tasks.AddRange(from client in _remoteClients.SlaveSshClients
                           select Task.Run(() => Unzip(client, _slaveTargetPath)));
            Task.WhenAll(tasks).Wait();
        }
    }
}
