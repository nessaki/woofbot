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
        public string ID;
    }
    public abstract class AChannelsInfo<V> : AInfo
    {
        public Dictionary<string, V> Channels = new Dictionary<string, V>();

        public static void AddChannel<T>(TomlTable conf, string id, string server_id, ref List<T> servers) where T : AChannelsInfo<V>
        {
            var si = servers.Find(s => s.ID == server_id);
            if (si == null)
                Console.WriteLine($"Warning, unknown server in section [ircchan.{id}]");
            else
                si.Channels[id] = (V)Convert.ChangeType(((string)conf["channel"]).ToLower(), typeof(V));
        }
    }
    public abstract class AChanStringsInfo : AChannelsInfo<string> { }

    public class BridgeInfo : AInfo
    {
        public BotInfo Bot;
        public UUID? GridGroup;
        public IrcServerInfo IrcServerConf;
        public string IrcChanID;
#if SLACK
        public SlackServerInfo SlackServerConf;
        public string SlackChannelID;
#endif
        public DiscordServerInfo DiscordServerConf;
        public string DiscordChannelID;

        public static BridgeInfo Create(TomlTable conf, string id)
        {
            UUID? groupID = null;
            if (conf.ContainsKey("grid_group"))
            {
                var str = (string)conf["grid_group"];
                if (!str.Equals("local"))
                    try { groupID = UUID.Parse(str); } catch { }
            }
            else groupID = UUID.Zero;

            var bi = new BridgeInfo()
            {
                ID = id,
                IrcChanID = (string)conf["ircchan"],
                GridGroup = groupID,
#if SLACK
                SlackChannelID = conf.GetString("slackchannel"),
#endif
                DiscordChannelID = (string)conf["discordchannel"]
            };
            return bi;
        }
    }

    public class Configuration
    {
        string ConfPath;
        public Dictionary<UUID, string> Masters = new Dictionary<UUID, string>();
        public List<BotInfo> Bots = new List<BotInfo>();
        public List<IrcServerInfo> IrcServers = new List<IrcServerInfo>();
#if SLACK
        public List<SlackServerInfo> SlackServers = new List<SlackServerInfo>();
#endif
        public List<DiscordServerInfo> DiscordServers = new List<DiscordServerInfo>();
        public List<BridgeInfo> Bridges = new List<BridgeInfo>();

        internal ConcurrentDictionary<string, ulong> Regions = new ConcurrentDictionary<string, ulong>();
        DocumentSyntax tomlDoc;

        public Configuration(string conf)
        {
            ConfPath = conf;
            var filename = ConfPath + "/woofbot.toml";
            tomlDoc = Toml.Parse(File.ReadAllText(filename), filename);
            LoadConfFile();
        }

        private void TableParse(TomlTable table, string key, Action<string, TomlTable> action)
        {
            if (table.ContainsKey(key))
            {
                try
                {
                    var subTable = table[key] as TomlTable;
                    foreach (var pair in subTable)
                    {
                        try
                        {
                            action(pair.Key, pair.Value as TomlTable);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed parsing section {key}.{pair.Key}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed parsing section {key}: {ex.Message}");
                }
            }
        }

        public void LoadConfFile()
        {
            Masters.Clear();
            Bots.Clear();

            var table = tomlDoc.ToModel();

            TableParse(table, "master", (_, master) =>
            {
                if (UUID.TryParse(master["uuid"] as string, out UUID masterId))
                {
                    if (masterId != UUID.Zero)
                        Masters.Add(masterId, master["name"] as string);
                }
            });

            TableParse(table, "bot", (id, conf) => Bots.Add(BotInfo.Create(conf, id)));

            TableParse(table, "ircserver", (id, conf) => IrcServers.Add(IrcServerInfo.Create(conf, id)));

            TableParse(table, "ircchan", (id, conf) => IrcServerInfo.AddChannel(conf, id, conf["irc_server"] as string, ref IrcServers));

#if SLACK
            TableParse(table, "slack", (id, conf) => SlackServers.Add(new SlackServerInfo(){ID = id, APIKEY = conf["slack_key"] as string}));

            TableParse(table, "slackchannel", (id, conf) => SlackServerInfo.AddChannel(conf, id, conf["slack_server"] as string, ref SlackServers));
#endif
            TableParse(table, "discord", (id, conf) =>
            {
                DiscordServers.Add(new DiscordServerInfo()
                {
                    ID = id,
                    token = conf["token"] as string,
                    SafeRoles = ((TomlArray)conf["saferoles"]).Select(e => e as string).ToList(),
                    DevRoles = ((TomlArray)conf["devroles"]).Select(e => e as string).ToList()
                });
            });

            TableParse(table, "discordchannel", (id, conf) => DiscordServerInfo.AddChannel(conf, id, (string)conf["discord_server"], ref DiscordServers));

            TableParse(table, "bridge", (id, conf) =>
            {
                var bi = BridgeInfo.Create(conf, id);
                bi.IrcServerConf = IrcServers.Find(s => s.Channels.ContainsKey(bi.IrcChanID));
                if (conf.ContainsKey("bot"))
                    bi.Bot = Bots.Find(b => b.ID == conf["bot"] as string);
#if SLACK
                bi.SlackServerConf = SlackServers.Find(slack => slack.Channels.ContainsKey(bi.SlackChannelID));
#endif
                bi.DiscordServerConf = DiscordServers.Find(d => d.Channels.ContainsKey(bi.DiscordChannelID));
                Bridges.Add(bi);
            });
        }

        internal ulong GetRegionHandle(string name)
            => Regions.TryGetValue(name.ToLower(), out ulong handle) ? handle : 0;

        internal void UpdateRegionHandle(string name, ulong handle)
            => Regions.TryAdd(name, handle);

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
            return $"{sb}Region handles ({Regions.Count})\n\nSay `startup` to start, `help` for more commands, and `quit` to end.\n";
        }

        public bool IsMaster(UUID key) => Masters.ContainsKey(key);

        public bool IsMaster(string name) => Masters.ContainsValue(name);
    }
}
