#!/bin/bash

. ./jenkins_private_functions.sh
# input Jenkinw workspace directory
# this function is invoked in every job entry
function set_global_env() {
   local relative_dir="azure-signalr-bench"
   local Jenkins_Workspace_Root=$1
   if [ $# -eq 2 ]
   then
      relative_dir=$2
   fi
   export JenkinsRootPath="$Jenkins_Workspace_Root"
   export PluginScriptWorkingDir=$Jenkins_Workspace_Root/${relative_dir}/SignalRServiceBenchmarkPlugin/plugins/Plugin.Microsoft.Azure.SignalR.Benchmark/Scripts/BenchmarkConfigurationGenerator/
   export PluginRpcBuildWorkingDir=$Jenkins_Workspace_Root/${relative_dir}/SignalRServiceBenchmarkPlugin/framework/rpc/
   export ScriptWorkingDir=$Jenkins_Workspace_Root/${relative_dir}/Scripts/                     # folders to find all scripts
   export CurrentWorkingDir=$Jenkins_Workspace_Root/${relative_dir}/v2/JenkinsScript/     # workding directory
   export CommandWorkingDir=$Jenkins_Workspace_Root/${relative_dir}/SignalRServiceBenchmarkPlugin/utils/Commander
   export AppServerWorkingDir=$Jenkins_Workspace_Root/${relative_dir}/SignalRServiceBenchmarkPlugin/utils/AppServer
############# those configurations are shared in Jenkins folder #####
   export AgentConfig=$JenkinsRootPath'/agent.yaml'
   export PrivateIps=$JenkinsRootPath'/privateIps.yaml'
   export PublicIps=$JenkinsRootPath'/publicIps.yaml'
   export ServicePrincipal=$JenkinsRootPath'/servicePrincipal.yaml'

############ global static variables #########
   export RootFolder="$CurrentWorkingDir"
   export ConfigRoot="$RootFolder/signalr_perf_config_sample/config/"
   export ScenarioRoot="$RootFolder/signalr_perf_config_sample/scenarios/"
   export BenchConfig=$ConfigRoot'/bench.yaml'
   #export ResultFolderSuffix='suffix'
   export VMMgrDir=/tmp/VMMgr
   export nginx_root=/mnt/Data/NginxRoot
   export g_nginx_ns="ingress-nginx"
}

# depends on set_global_env
function set_job_env() {
   export result_root=`date +%Y%m%d%H%M%S`
   export DogFoodResourceGroup="hzatpf"`date +%M%S`
   export serverUrl=`awk '{print $2}' $JenkinsRootPath/JobConfig.yaml`
}
# require global env:
# ASRSEnv, DogFoodResourceGroup, ASRSLocation
function prepare_ASRS_creation() {
cd $ScriptWorkingDir
. ./az_signalr_service.sh

if [ "$ASRSEnv" == "dogfood" ]
then
  register_signalr_service_dogfood
  az_login_ASRS_dogfood
else
  az_login_signalr_dev_sub
fi
create_group_if_not_exist $DogFoodResourceGroup $ASRSLocation
}

# global env: ScriptWorkingDir, DogFoodResourceGroup, ASRSEnv
function clean_ASRS_group() {
############# remove SignalR Service Resource Group #########
cd $ScriptWorkingDir
delete_group $DogFoodResourceGroup
if [ "$ASRSEnv" == "dogfood" ]
then
  unregister_signalr_service_dogfood
fi
}

function register_exit_handler() {
  cd $CurrentWorkingDir
  if [ -d ${VMMgrDir} ]
  then
    rm -rf ${VMMgrDir}
  fi
  dotnet publish -c Release -f netcoreapp2.1 -o ${VMMgrDir} --self-contained -r linux-x64
  trap remove_resource_group EXIT
}

function run_all_units() {
 local user=$1
 local passwd="$2"
 local service
 local signalrServiceName
 for service in $bench_serviceunit_list
 do
   cd $ScriptWorkingDir
   ConnectionString="" # set it to be invalid first
   signalrServiceName="atpf"`date +%H%M%S`
   create_asrs $DogFoodResourceGroup $signalrServiceName $service
   if [ "$ConnectionString" == "" ]
   then
     echo "Skip the running on SignalR service unit'$service' since it was failed to create"
     continue
   fi

   run_benchmark $service $user "$passwd" "$ConnectionString"
   
   delete_signalr_service $signalrServiceName $DogFoodResourceGroup
 done
}