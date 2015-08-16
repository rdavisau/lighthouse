// Copyright 2014-2015 Aaron Stannard, Petabridge LLC
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use 
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed 
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using ConfigurationException = Akka.Configuration.ConfigurationException;

namespace Lighthouse
{
    /// <summary>
    /// Launcher for the Lighthouse <see cref="ActorSystem"/>
    /// </summary>
    public static class LighthouseHostFactory
    {
        public static string CommandLineSystemName { get; set; }
        public static string CommandLinePublicHostName { get; set; }
        public static int? CommandLinePort { get; set; }
        public static List<string> CommandLineAdditionalSeedNodes { get; set; }
        public static List<string> CommandLineAdditionalRoles { get; set; } 

        public static ActorSystem LaunchLighthouse(string ipAddress = null, int? specifiedPort = null)
        {
            // the configuration of the actorSystem is based on the combination of app.config
            // and any given command-line options (systemname, hostname, port, seeds, roles) 
            var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
            var clusterConfig = section.AkkaConfig;

            // actorSystem: Commandline > Config > "lighthouse" default
            var systemName = CommandLineSystemName
                             ?? clusterConfig.GetConfig("lighthouse")?.GetString("actorSystem")
                             ?? "lighthouse";

            // public-hostname: Method arg > Commandline > Config > "localhost" default
            ipAddress = ipAddress
                        ?? CommandLinePublicHostName
                        ?? clusterConfig.GetConfig("akka.remote")?.GetString("helios.tcp.public-hostname");

            if (string.IsNullOrEmpty(ipAddress))
                throw new ConfigurationException("Need to specify an explicit hostname for Lighthouse. Specify a public hostname in App.config or on the command line using the -publicHostName parameter.");

            // port: Method arg > Commandline > Config; fail if missing
            var port = specifiedPort
                   ?? CommandLinePort
                   ?? clusterConfig.GetConfig("akka.remote")?.GetInt("helios.tcp.port");
                   
            if (port == 0)
                throw new ConfigurationException("Need to specify an explicit port for Lighthouse. Specify a port in App.config or on the command line using the -port parameter.");

            // seed-nodes: Combine .config and commandline seed node definitions
            var selfAddress = $"akka.tcp://{systemName}@{ipAddress}:{port}";
            var seeds = (clusterConfig.GetStringList("akka.cluster.seed-nodes") ?? new List<string>())
                .Concat(CommandLineAdditionalSeedNodes ?? new List<string>())
                .Concat(new[] {selfAddress})
                .Distinct();
            var seedParts = seeds.Select(s => $"\"{s}\"");
            var injectedClusterSeedNodesConfigString = $"akka.cluster.seed-nodes = [{string.Join(",", seedParts)}]";
            
            // roles: Combine .config and commandline seed node definitions
            var roles = (clusterConfig.GetStringList("akka.cluster.roles") ?? new List<string>())
                .Concat(CommandLineAdditionalRoles ?? new List<string>())
                .Distinct();
            var injectedClusterRoleConfigString = $"akka.cluster.roles = [{string.Join(",", roles)}]";

            var finalConfig = ConfigurationFactory.ParseString($@"akka.remote.helios.tcp.public-hostname = {ipAddress} akka.remote.helios.tcp.port = {port}")
                .WithFallback(ConfigurationFactory.ParseString(injectedClusterSeedNodesConfigString))
                .WithFallback(ConfigurationFactory.ParseString(injectedClusterRoleConfigString))
                .WithFallback(clusterConfig);

            return ActorSystem.Create(systemName, finalConfig);
        }
    }
}
