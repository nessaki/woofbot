// 
// Jarilo
// Copyright (c) 2010, Jarilo Development Team
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of the application "Jarilo", nor the names of its
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Nini.Config;
using OpenMetaverse;

namespace WoofBot
{
    /// <summary>
    /// Holds parsed config file configuration
    /// </summary>
    public abstract class AInfo
    {
        public string ID;
    }
    public abstract class AChannelsInfo<val> : AInfo
    {
        public Dictionary<string, val> Channels = new Dictionary<string, val>();

        public static void AddChannel<T>(IniConfig conf, string id, string server_id, ref List<T> servers) where T:AChannelsInfo<val>
        {
            var si = servers.Find(s => s.ID == server_id);
            if (si == null)
                Console.WriteLine($"Warning, unknown server in section [{conf.Name}]");
            else
                si.Channels[id] = (val)Convert.ChangeType(conf.GetString("channel").ToLower(), typeof(val));
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

        public static BridgeInfo Create(IniConfig conf, string id)
        {
            UUID? groupID = null;
            if (conf.Contains("grid_group"))
            {
                var str = conf.GetString("grid_group");
                if (!str.Equals("local"))
                    try { groupID = UUID.Parse(str); } catch { }
            }
            else groupID = UUID.Zero;

            var bi = new BridgeInfo()
            {
                ID = id,
                IrcChanID = conf.GetString("ircchan"),
                GridGroup = groupID,
#if SLACK
                SlackChannelID = conf.GetString("slackchannel"),
#endif
                DiscordChannelID = conf.GetString("discordchannel")
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

        internal Dictionary<string, ulong> Regions = new Dictionary<string, ulong>();
        IniConfigSource inifile;

        public Configuration(string conf)
        {
            ConfPath = conf;
            inifile = new IniConfigSource(ConfPath + "/jarilo.conf")
            {
                CaseSensitive = false
            };
            LoadConfFile();
            LoadRegions();
        }

        public void LoadConfFile()
        {
            Masters.Clear();
            Bots.Clear();

            foreach (IniConfig conf in inifile.Configs)
            {
                var head = Regex.Split(conf.Name, @"\s*:\s*");
                if (head.Length < 2)
                {
                    Console.WriteLine($"Invalid section name {conf.Name}, expected format type:id");
                    continue;
                }

                string type = head[0];
                string id = head[1];

                try
                {
                    if (type == "master")
                    {
                        try
                        {
                            var masterId = (UUID)conf.Get("uuid");
                            if (masterId != UUID.Zero)
                                Masters.Add(masterId, conf.Get("name"));
                        }
                        catch { }
                    }
                    else if (type == "bot")
                        Bots.Add(BotInfo.Create(conf, id));
                    else if (type == "ircserver")
                        IrcServers.Add(IrcServerInfo.Create(conf, id));
                    else if (type == "ircchan")
                        IrcServerInfo.AddChannel(conf, id, conf.GetString("irc_server"), ref IrcServers);
#if SLACK
                    else if (type == "slack")
                        SlackServers.Add(new SlackServerInfo(){ID = id, APIKEY = conf.GetString("slack_key")});
                    else if (type == "slackchannel")
                        SlackServerInfo.AddChannel(conf, id, conf.GetString("slack_server"), ref SlackServers);
#endif
                    else if (type == "discord")
                        DiscordServers.Add(new DiscordServerInfo()
                        {
                            ID = id, token = conf.GetString("token"),
                            SafeRoles = new List<string>(conf.GetString("saferoles").Split(',')),
                            DevRoles = new List<string>(conf.GetString("devroles").Split(','))
                        });
                    else if (type == "discordchannel")
                        DiscordServerInfo.AddChannel(conf, id, conf.GetString("discord_server"), ref DiscordServers);
                    else if (type == "bridge")
                    {
                        var bi = BridgeInfo.Create(conf, id);
                        bi.IrcServerConf = IrcServers.Find(s => s.Channels.ContainsKey(bi.IrcChanID));
                        if (conf.Contains("bot")) bi.Bot = Bots.Find(b => b.ID == conf.GetString("bot"));
#if SLACK
                        bi.SlackServerConf = SlackServers.Find(slack => slack.Channels.ContainsKey(bi.SlackChannelID));
#endif
                        bi.DiscordServerConf = DiscordServers.Find(d => d.Channels.ContainsKey(bi.DiscordChannelID));
                        Bridges.Add(bi);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed parsing section {conf.Name}: {ex.Message}");
                }
            }
        }

        public void LoadRegions()
        {
            Regions = new Dictionary<string, ulong>();

            try
            {
                StreamReader reader = new StreamReader(ConfPath + "/regions.txt");
                Regex splitter = new Regex(@"[\t ]*=[\t ]*", RegexOptions.Compiled);

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    string[] args = splitter.Split(line.Trim(), 2);
                    if (args.Length >= 2)
                    {
                        if (ulong.TryParse(args[1], out ulong handle) && handle > 0)
                        {
                            var arg = args[0].ToLower();
                            if (!Regions.ContainsKey(arg))
                                Regions.Add(arg, handle);
                        }
                    }
                }
            }
            catch { }
        }

        public void SaveRegions()
        {
            try
            {
                StreamWriter wr = new StreamWriter(ConfPath + "/regions.txt");
                foreach (var r in Regions)
                    wr.WriteLine($"{r.Key}={r.Value}");
                wr.Close();
            }
            catch
            {
                Logger.Log("Failed to save regions handle cache.", Helpers.LogLevel.Warning);
            }
        }

        internal ulong GetRegionHandle(string name)
            => Regions.TryGetValue(name.ToLower(), out ulong handle) ? handle : 0;

        internal void SaveCachedRegionHandle(string name, ulong handle)
        {
            lock (Regions)
            {
                var str = name.ToLower();
                if (!Regions.ContainsKey(str))
                {
                    Regions.Add(str, handle);
                    SaveRegions();
                }
            }
        }

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
