﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.SignalRBench.Common;
using k8s;
using k8s.Models;

namespace Azure.SignalRBench.Coordinator
{
    public class K8sProvider : IK8sProvider
    {
        private const string _default = "default";
        private const string _appserver = "appserver";
        private const string _client = "client";
        private Kubernetes? _k8s;

        public Kubernetes K8s => _k8s ?? throw new InvalidOperationException();

        public void Initialize(string config)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(config));
            _k8s = new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(stream));
        }

        public async Task<string> CreateServerPodsAsync(string testId, int nodePoolIndex, string[] asrsConnectionStrings, CancellationToken cancellationToken)
        {
            var name = _appserver + "-" + testId;
            var service = new V1Service()
            {
                ApiVersion = "v1",
                Kind = "Service",
                Metadata = new V1ObjectMeta()
                {
                    Name = name
                },
                Spec = new V1ServiceSpec()
                {
                    Ports = new List<V1ServicePort> { new V1ServicePort(port: 6379, targetPort: 6379) },
                    Selector = new Dictionary<string, string>()
                    {
                        ["app"] = name
                    }
                }
            };
            await _k8s.CreateNamespacedServiceAsync(service, _default,cancellationToken: cancellationToken);

            V1Deployment deployment = new V1Deployment()
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Metadata = new V1ObjectMeta()
                {
                    Name = name,
                    Labels = new Dictionary<string, string>()
                    {
                        [Constants.ConfigurationKeys.TestIdKey] = testId
                    }
                },
                Spec = new V1DeploymentSpec
                {
                    Replicas = 1,
                    Selector = new V1LabelSelector()
                    {
                        MatchLabels = new Dictionary<string, string>
                        {
                            { "app", name }
                        }
                    },
                    Template = new V1PodTemplateSpec()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            CreationTimestamp = null,
                            Labels = new Dictionary<string, string>
                            {
                                ["app"] = name,
                            }
                        },
                        Spec = new V1PodSpec
                        {
                            NodeSelector = new Dictionary<String, String>()
                            {
                                ["agentpool"] = AksProvider.ToPoolName(nodePoolIndex)
                            },
                            Containers = new List<V1Container>()
                            {
                            new V1Container()
                            {
                                Name = name,
                                Image = "signalrbenchmark/perf:1.3",
                                Resources=new V1ResourceRequirements()
                                {
                                    Requests=new Dictionary<string, ResourceQuantity>()
                                    {
                                        ["cpu"]=new ResourceQuantity("100m"),
                                        ["memory"]=new ResourceQuantity("128Mi")
                                    },
                                    Limits=new Dictionary<string, ResourceQuantity>()
                                    {
                                        ["cpu"]=new ResourceQuantity("250m"),
                                        ["memory"]=new ResourceQuantity("256Mi")
                                    }
                                },
                                VolumeMounts=new List<V1VolumeMount>()
                                {
                                    new V1VolumeMount("/mnt/perf","volume")
                                },
                                Command=new List<string>()
                                {
                                    "/bin/sh", "-c"
                                },
                                Args=new List<String>()
                                {
                                    "cp /mnt/perf/manifest/AppServer/AppServer.zip /home ; cd /home ; unzip AppServer.zip ;exec ./AppServer;"
                                },
                                Env=new List<V1EnvVar>()
                                {
                                    new V1EnvVar(Constants.ConfigurationKeys.TestIdKey,testId),
                                    new V1EnvVar(Constants.ConfigurationKeys.ConnectionString,string.Join(",",asrsConnectionStrings))
                                }
                            },
                            },
                            Volumes = new List<V1Volume>()
                            {
                                new V1Volume("volume")
                                {
                                    AzureFile=new V1AzureFileVolumeSource("azure-secret","perf",false)
                                }
                            }
                        },

                    }
                }
            };
            await _k8s.CreateNamespacedDeploymentAsync(deployment, _default,cancellationToken:cancellationToken);
            return name;
        }

        public async Task CreateClientPodsAsync(string testId, int nodePoolIndex, string url, CancellationToken cancellationToken)
        {
            var name = _client + '-' + testId;
            V1Deployment deployment = new V1Deployment()
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Metadata = new V1ObjectMeta()
                {
                    Name = name,
                    Labels = new Dictionary<string, string>()
                    {
                        [Constants.ConfigurationKeys.TestIdKey] = testId
                    }
                },
                Spec = new V1DeploymentSpec
                {
                    Replicas = 1,
                    Selector = new V1LabelSelector()
                    {
                        MatchLabels = new Dictionary<string, string>
                        {
                            { "app", name }
                        }
                    },
                    Template = new V1PodTemplateSpec()
                    {
                        Metadata = new V1ObjectMeta()
                        {
                            CreationTimestamp = null,
                            Labels = new Dictionary<string, string>
                            {
                                ["app"] = name,
                            }
                        },
                        Spec = new V1PodSpec
                        {
                            NodeSelector = new Dictionary<String, String>()
                            {
                                ["agentpool"] = AksProvider.ToPoolName(nodePoolIndex)
                            },
                            Containers = new List<V1Container>()
                            {
                            new V1Container()
                            {
                                Name = name,
                                Image = "signalrbenchmark/perf:1.3",
                                Resources=new V1ResourceRequirements()
                                {
                                    Requests=new Dictionary<string, ResourceQuantity>()
                                    {
                                        ["cpu"]=new ResourceQuantity("100m"),
                                        ["memory"]=new ResourceQuantity("128Mi")
                                    },
                                    Limits=new Dictionary<string, ResourceQuantity>()
                                    {
                                        ["cpu"]=new ResourceQuantity("250m"),
                                        ["memory"]=new ResourceQuantity("256Mi")
                                    }
                                },
                                VolumeMounts=new List<V1VolumeMount>()
                                {
                                    new V1VolumeMount("/mnt/perf","volume")
                                },
                                Command=new List<string>()
                                {
                                    "/bin/sh", "-c"
                                },
                                Args=new List<String>()
                                {
                                    "cp /mnt/perf/manifest/Client/Client.zip /home ; cd /home ; unzip Client.zip ; exec ./Client"
                                },
                                Env=new List<V1EnvVar>()
                                {
                                    new V1EnvVar(Constants.ConfigurationKeys.TestIdKey,testId),
                                    new V1EnvVar(Constants.ConfigurationKeys.AppServerUrl,url)
                                }
                            },
                            },
                            Volumes = new List<V1Volume>()
                            {
                                new V1Volume("volume")
                                {
                                    AzureFile=new V1AzureFileVolumeSource("azure-secret","perf",false)
                                }
                            }
                        },

                    }
                }
            };
            await _k8s.CreateNamespacedDeploymentAsync(deployment, _default,cancellationToken:cancellationToken);
        }

        public async Task DeleteClientPodsAsync(string testId, int nodePoolIndex)
        {
            string name = _client + '-' + testId;
            await _k8s.DeleteNamespacedDeploymentAsync(name, _default);
        }

        public async Task DeleteServerPodsAsync(string testId, int nodePoolIndex)
        {
            string name = _appserver + '-' + testId;
            await _k8s.DeleteNamespacedServiceAsync(name, _default);
            await _k8s.DeleteNamespacedDeploymentAsync(name, _default);
        }
    }
}