﻿using System.Collections.Generic;
using System.IO;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Tests.Common;
using Xunit;

namespace Xels.Bitcoin.Features.RPC.Tests
{
    public class RPCSettingsTest : TestBase
    {
        public RPCSettingsTest() : base(KnownNetworks.TestNet)
        {
        }

        [Fact]
        public void Load_ValidNodeSettings_UpdatesRpcSettingsFromNodeSettings()
        {
            string dir = CreateTestDir(this);
            string confFile = Path.Combine(dir, "bitcoin.conf");
            var configLines = new List<string>()
            {
                "server=true",
                "rpcport=1378",
                "rpcuser=testuser",
                "rpcpassword=testpassword",
                "rpcallowip=0.0.0.0",
                "rpcbind=127.0.0.1"
            };

            WriteConfigurationToFile(confFile, configLines);

            var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

            var rpcSettings = new RpcSettings(nodeSettings);

            Assert.True(rpcSettings.Server);
            Assert.Equal(1378, rpcSettings.RPCPort);
            Assert.Equal("testuser", rpcSettings.RpcUser);
            Assert.Equal("testpassword", rpcSettings.RpcPassword);
            Assert.NotEmpty(rpcSettings.Bind);
            Assert.Equal("127.0.0.1:1378", rpcSettings.Bind[0].ToString());
            Assert.NotEmpty(rpcSettings.DefaultBindings);
            Assert.Equal("127.0.0.1:1378", rpcSettings.DefaultBindings[0].ToString());
            Assert.NotEmpty(rpcSettings.AllowIp);
            Assert.Equal("0.0.0.0", rpcSettings.AllowIp[0].ToString());
        }

        [Fact]
        public void Load_DefaultConfiguration_UsesDefaultNodeSettings()
        {
            string dir = CreateTestDir(this);
            string confFile = Path.Combine(dir, "bitcoin.conf");
            var configLines = new List<string>() { "" };
            WriteConfigurationToFile(confFile, configLines);

            var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

            var rpcSettings = new RpcSettings(nodeSettings);

            Assert.False(rpcSettings.Server);
            Assert.Equal(18332, rpcSettings.RPCPort);
            Assert.Null(rpcSettings.RpcUser);
            Assert.Null(rpcSettings.RpcPassword);
            Assert.Empty(rpcSettings.Bind);
            Assert.Empty(rpcSettings.DefaultBindings);
            Assert.Empty(rpcSettings.AllowIp);
        }

        [Fact]
        public void Load_InvalidRpcAllowIp_ThrowsConfigurationException()
        {
            Assert.Throws<ConfigurationException>(() =>
            {
                string dir = CreateTestDir(this);
                string confFile = Path.Combine(dir, "bitcoin.conf");
                var configLines = new List<string>()
                {
                    "server=true",
                    "rpcallowip=0....121.12000.00",
                };

                WriteConfigurationToFile(confFile, configLines);

                var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

                var rpcSettings = new RpcSettings(nodeSettings);
            });
        }

        [Fact]
        public void Load_InvalidRpcBindIp_ThrowsConfigurationException()
        {
            Assert.Throws<ConfigurationException>(() =>
            {
                string dir = CreateTestDir(this);
                string confFile = Path.Combine(dir, "bitcoin.conf");
                var configLines = new List<string>()
                {
                    "server=true",
                    "rpcbind=0....121.12000.00",
                };

                WriteConfigurationToFile(confFile, configLines);

                var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

                var rpcSettings = new RpcSettings(nodeSettings);
            });
        }

        [Fact]
        public void Load_RpcUserNameWithoutRpcPassword_ThrowsConfigurationException()
        {
            Assert.Throws<ConfigurationException>(() =>
            {
                string dir = CreateTestDir(this);
                string confFile = Path.Combine(dir, "bitcoin.conf");
                var configLines = new List<string>()
                {
                    "server=true",
                    "rpcport=1378",
                    "rpcuser=testuser",
                    "rpcallowip=0.0.0.0",
                    "rpcbind=127.0.0.1"
                };

                WriteConfigurationToFile(confFile, configLines);

                var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

                var rpcSettings = new RpcSettings(nodeSettings);
            });
        }

        [Fact]
        public void Load_RpcPasswordWithoutRpcUserName_ThrowsConfigurationException()
        {
            Assert.Throws<ConfigurationException>(() =>
            {
                string dir = CreateTestDir(this);
                string confFile = Path.Combine(dir, "bitcoin.conf");
                var configLines = new List<string>()
                {
                    "server=true",
                    "rpcport=1378",
                    "rpcpassword=testpassword",
                    "rpcallowip=0.0.0.0",
                    "rpcbind=127.0.0.1"
                };

                WriteConfigurationToFile(confFile, configLines);

                var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

                var rpcSettings = new RpcSettings(nodeSettings);
            });
        }

        [Fact]
        public void GetUrls_BindsConfigured_ReturnsBindUrls()
        {
            string dir = CreateTestDir(this);
            string confFile = Path.Combine(dir, "bitcoin.conf");
            var configLines = new List<string>()
            {
                "server=true",
                "rpcport=1378",
                "rpcuser=testuser",
                "rpcpassword=testpassword",
                "rpcallowip=0.0.0.0",
                "rpcbind=127.0.0.1"
            };

            WriteConfigurationToFile(confFile, configLines);

            var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

            var rpcSettings = new RpcSettings(nodeSettings);
            string[] urls = rpcSettings.GetUrls();

            Assert.NotEmpty(urls);
            Assert.Equal("http://127.0.0.1:1378/", urls[0]);
        }

        [Fact]
        public void GetUrls_NoBindsConfigured_ReturnsEmptyArray()
        {
            string dir = CreateTestDir(this);
            string confFile = Path.Combine(dir, "bitcoin.conf");
            var configLines = new List<string>()
            {
                ""
            };

            WriteConfigurationToFile(confFile, configLines);

            var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

            var rpcSettings = new RpcSettings(new NodeSettings(this.Network));
            string[] urls = rpcSettings.GetUrls();

            Assert.Empty(urls);
        }

        [Fact]
        public void GetUrls_NoBindsConfigured_AllowIp_Configured_Returns_DefaultBindings()
        {
            string dir = CreateTestDir(this);
            string confFile = Path.Combine(dir, "bitcoin.conf");
            var configLines = new List<string>()
            {
                "server=true",
                "rpcuser=testuser",
                "rpcpassword=testpassword",
                "rpcallowip=0.0.0.0"
            };

            WriteConfigurationToFile(confFile, configLines);

            var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

            var rpcSettings = new RpcSettings(nodeSettings);
            string[] urls = rpcSettings.GetUrls();

            Assert.Equal("http://[::]:18332/", urls[0]);
            Assert.Equal("http://0.0.0.0:18332/", urls[1]);
        }

        [Fact]
        public void GetUrls_NoBindsConfigured_NoUserPassword_AllowIp_Configured_Returns_DefaultBindings()
        {
            string dir = CreateTestDir(this);
            string confFile = Path.Combine(dir, "bitcoin.conf");
            var configLines = new List<string>()
            {
                "server=true",
                "rpcallowip=0.0.0.0"
            };

            WriteConfigurationToFile(confFile, configLines);

            var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

            var rpcSettings = new RpcSettings(nodeSettings);
            string[] urls = rpcSettings.GetUrls();

            Assert.Equal("http://[::]:18332/", urls[0]);
            Assert.Equal("http://0.0.0.0:18332/", urls[1]);
        }
        
        [Fact]
        public void Load_ValidNodeSettings_UpdatesRpcSettingsFromNodeSettings_Empty_Username_And_Password()
        {
            string dir = CreateTestDir(this);
            string confFile = Path.Combine(dir, "bitcoin.conf");
            var configLines = new List<string>()
            {
                "server=true",
                "rpcport=1378",
                "rpcallowip=0.0.0.0",
                "rpcbind=127.0.0.1"
            };

            WriteConfigurationToFile(confFile, configLines);

            var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=" + confFile });

            var rpcSettings = new RpcSettings(nodeSettings);

            Assert.True(rpcSettings.Server);
            Assert.Equal(1378, rpcSettings.RPCPort);
            Assert.Null(rpcSettings.RpcUser);
            Assert.Null(rpcSettings.RpcPassword);
            Assert.NotEmpty(rpcSettings.Bind);
            Assert.Equal("127.0.0.1:1378", rpcSettings.Bind[0].ToString());
            Assert.NotEmpty(rpcSettings.DefaultBindings);
            Assert.Equal("127.0.0.1:1378", rpcSettings.DefaultBindings[0].ToString());
            Assert.NotEmpty(rpcSettings.AllowIp);
            Assert.Equal("0.0.0.0", rpcSettings.AllowIp[0].ToString());
        }

        private static void WriteConfigurationToFile(string confFile, List<string> configurationFileLines)
        {
            File.WriteAllLines(confFile, configurationFileLines.ToArray());
        }
    }
}
