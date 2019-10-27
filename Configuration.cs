// BSD 3-Clause License
// 
// Copyright (c) 2014-2019, Alchemy Development Group
// Copyright (c) 2010, Jarilo Development Team
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
//     * Redistributions of source code must retain the above copyright notice, this
//       list of conditions and the following disclaimer.
//
//     * Redistributions in binary form must reproduce the above copyright notice,
//       this list of conditions and the following disclaimer in the documentation
//       and/or other materials provided with the distribution.
//
//     * Neither the name of the copyright holder, nor the names of its
//       contributors may be used to endorse or promote products derived from
//       this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// $Id$
//

using OpenMetaverse;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace WoofBot
{
    /// <summary>
    /// Holds parsed config file configuration
    /// </summary>
    public abstract class AInfo
    {
        public string Id;
    }
    public abstract class AChannelsInfo<TV> : AInfo
    {
        public readonly Dictionary<string, TV> Channels = new Dictionary<string, TV>();

        public static void AddChannel<T>(TomlTable conf, string id, string serverId, ref List<T> servers) where T : AChannelsInfo<TV>
        {
            var si = servers.Find(s => s.Id == serverId);
            if (si == null)
                Console.WriteLine($"Warning, unknown server in section [ircchan.{id}]");
            else
                si.Channels[id] = (TV)Convert.ChangeType(((string)conf["channel"]).ToLower(), typeof(TV));
        }
    }
    public abstract class AChanStringsInfo : AChannelsInfo<string> { }

    public class BridgeInfo : AInfo
    {
        public BotInfo Bot;
        public UUID? GridGroup;
        public IrcServerInfo IrcServerConf;
        public string IrcChanId;
#if SLACK
        public SlackServerInfo SlackServerConf;
        public string SlackChannelID;
#endif
        public DiscordServerInfo DiscordServerConf;
        public string DiscordChannelId;

        public static BridgeInfo Create(TomlTable conf, string id)
        {
            UUID? groupId = null;
            if (conf.ContainsKey("grid_group"))
            {
                var str = (string)conf["grid_group"];
                if (!str.Equals("local"))
                    try { groupId = UUID.Parse(str); } catch { /* ignored */ }
            }
            else groupId = UUID.Zero;

            var bi = new BridgeInfo()
            {
                Id = id,
                IrcChanId = (string)conf["ircchan"],
                GridGroup = groupId,
#if SLACK
                SlackChannelID = conf.GetString("slackchannel"),
#endif
                DiscordChannelId = (string)conf["discordchannel"]
            };
            return bi;
        }
    }

    public class Configuration
    {
        public readonly Dictionary<UUID, string> Masters = new Dictionary<UUID, string>();
        public readonly List<BotInfo> Bots = new List<BotInfo>();
        public List<IrcServerInfo> IrcServers = new List<IrcServerInfo>();
#if SLACK
        public List<SlackServerInfo> SlackServers = new List<SlackServerInfo>();
#endif
        public List<DiscordServerInfo> DiscordServers = new List<DiscordServerInfo>();
        public readonly List<BridgeInfo> Bridges = new List<BridgeInfo>();

        private readonly ConcurrentDictionary<string, ulong> _regions = new ConcurrentDictionary<string, ulong>();
        private readonly DocumentSyntax _tomlDoc;

        public Configuration(string conf)
        {
            var filename = conf + "/woofbot.toml";
            _tomlDoc = Toml.Parse(File.ReadAllText(filename), filename);
            LoadConfFile();
        }

        private void TableParse(TomlTable table, string key, Action<string, TomlTable> action)
        {
            if (!table.ContainsKey(key)) return;
            
            try
            {
                if (!(table[key] is TomlTable subTable)) return;
                foreach (var (s, value) in subTable)
                {
                    try
                    {
                        action(s, value as TomlTable);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed parsing section {key}.{s}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed parsing section {key}: {ex.Message}");
            }
        }

        private void LoadConfFile()
        {
            Masters.Clear();
            Bots.Clear();

            var table = _tomlDoc.ToModel();

            TableParse(table, "master", (_, master) =>
            {
                if (!UUID.TryParse(master["uuid"] as string, out UUID masterId)) return;
                if (masterId != UUID.Zero)
                    Masters.Add(masterId, master["name"] as string);
            });

            TableParse(table, "bot", (id, conf) => Bots.Add(BotInfo.Create(conf, id)));

            TableParse(table, "ircserver", (id, conf) => IrcServers.Add(IrcServerInfo.Create(conf, id)));

            TableParse(table, "ircchan",
                (id, conf) => IrcServerInfo.AddChannel(conf, id, conf["irc_server"] as string, ref IrcServers));

#if SLACK
            TableParse(table, "slack", (id, conf) => SlackServers.Add(new SlackServerInfo(){ID = id, APIKEY = conf["slack_key"] as string}));

            TableParse(table, "slackchannel", (id, conf) => SlackServerInfo.AddChannel(conf, id, conf["slack_server"] as string, ref SlackServers));
#endif
            TableParse(table, "discord", (id, conf) =>
            {
                DiscordServers.Add(new DiscordServerInfo()
                {
                    Id = id,
                    token = conf["token"] as string,
                    SafeRoles = ((TomlArray)conf["saferoles"]).Select(e => e as string).ToList(),
                    DevRoles = ((TomlArray)conf["devroles"]).Select(e => e as string).ToList()
                });
            });

            TableParse(table, "discordchannel",
                (id, conf) =>
                    DiscordServerInfo.AddChannel(conf, id, (string) conf["discord_server"], ref DiscordServers));

            TableParse(table, "bridge", (id, conf) =>
            {
                var bi = BridgeInfo.Create(conf, id);
                bi.IrcServerConf = IrcServers.Find(s => s.Channels.ContainsKey(bi.IrcChanId));
                if (conf.ContainsKey("bot"))
                    bi.Bot = Bots.Find(b => b.Id == conf["bot"] as string);
#if SLACK
                bi.SlackServerConf = SlackServers.Find(slack => slack.Channels.ContainsKey(bi.SlackChannelID));
#endif
                bi.DiscordServerConf = DiscordServers.Find(d => d.Channels.ContainsKey(bi.DiscordChannelId));
                Bridges.Add(bi);
            });
        }

        internal ulong GetRegionHandle(string name)
            => _regions.TryGetValue(name.ToLower(), out ulong handle) ? handle : 0;

        internal void UpdateRegionHandle(string name, ulong handle)
            => _regions.TryAdd(name, handle);

        public override string ToString()
        {
            var sb = new StringBuilder($"Masters ({Masters.Count})\n");
            foreach (var master in Masters)
            {
                sb.Append($"{master.Key} ({master.Value})\n");
            }
            sb.Append($"Bots ({Bots.Count})\n");
            Bots.ForEach(b =>
            {
                sb.AppendFormat(b.Name);
                if (!string.IsNullOrEmpty(b.Sim))
                {
                    sb.Append($"; keep in region: {b.Sim}");
                }
                if (b.SitOn != UUID.Zero)
                {
                    sb.Append($"; sit on: {b.SitOn}");
                }
                sb.AppendLine();
            });
            return $"{sb}Region handles ({_regions.Count})\n\nSay `startup` to start, `help` for more commands, and `quit` to end.\n";
        }

        public bool IsMaster(UUID key) => Masters.ContainsKey(key);

        public bool IsMaster(string name) => Masters.ContainsValue(name);
    }
}
