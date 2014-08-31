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
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using OpenMetaverse;
using Nini.Config;

namespace Jarilo
{
    /// <summary>
    /// Holds parsed config file configuration
    /// </summary>
    public class BotInfo
    {
        public string ID { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Password { get; set; }
        public UUID SitOn { get; set; }
        public ulong SimHandle { get; set; }
        public string Sim { get; set; }
        public Vector3 PosInSim { get; set; }
        public string LoginURI { get; set; }
        public string GridName { get; set; }
        public string Name
        {
            get { return FirstName + " " + LastName; }
        }

        public BotInfo()
        {
            LoginURI = "https://login.agni.lindenlab.com/cgi-bin/login.cgi";
            GridName = "agni";
        }
    }

    public class IrcServerInfo
    {
        public string ID { get; set; }
        public string ServerHost { get; set; }
        public int ServerPort { get; set; }
        public string Nick { get; set; }
        public string Username { get; set; }
        public Dictionary<string, string> Channels { get; set; }

        public IrcServerInfo()
        {
            ServerPort = 6667;
            Channels = new Dictionary<string, string>();
        }
    }

    public class XmppServerInfo
    {
        public string ID { get; set; }
        public string User { get; set; }
        public string Domain { get; set; }
        public string Password { get; set; }
        public string NetworkHost { get; set; }
        public string Nick { get; set; }
        public Dictionary<string, string> Conferences { get; set; }

        public XmppServerInfo()
        {
            Conferences = new Dictionary<string, string>();
        }
    }

    public class BridgeInfo
    {
        public string ID { get; set; }
        public BotInfo Bot { get; set; }
        public UUID GridGroup { get; set; }
        public IrcServerInfo IrcServerConf { get; set; }
        public string IrcChanID { get; set; }
        public XmppServerInfo XmppServerConf { get; set; }
        public string XmppConferenceID { get; set; }
    }

    public class Configuration
    {
        string ConfPath;
        public Dictionary<UUID, string> Masters = new Dictionary<UUID, string>();
        public List<BotInfo> Bots = new List<BotInfo>();
        public List<IrcServerInfo> IrcServers = new List<IrcServerInfo>();
        public List<XmppServerInfo> XmppServers = new List<XmppServerInfo>();
        public List<BridgeInfo> Bridges = new List<BridgeInfo>();

        public Dictionary<string, ulong> Regions = new Dictionary<string, ulong>();
        IniConfigSource inifile;

        public Configuration(string conf)
        {
            ConfPath = conf;
            inifile = new IniConfigSource(ConfPath + "/jarilo.conf");
            inifile.CaseSensitive = false;
            LoadConfFile();
            LoadRegions();
        }

        public void LoadConfFile()
        {
            Masters.Clear();
            Bots.Clear();

            foreach (IniConfig conf in inifile.Configs)
            {
                string[] head = Regex.Split(conf.Name, @"\s*:\s*");
                if (head.Length < 2)
                {
                    Console.WriteLine("Invalid section name {0}, expected format type:id", conf.Name);
                    continue;
                }

                string type = head[0];
                string id = head[1];

                if (type == "master")
                {
                    try
                    {
                        UUID masterId = (UUID)conf.Get("uuid");
                        string masterName = conf.Get("name");
                        if (masterId != UUID.Zero)
                        {
                            Masters.Add(masterId, masterName);
                        }
                    }
                    catch (Exception) { }
                }
                else if (type == "bot")
                {
                    try
                    {
                        BotInfo b = new BotInfo();
                        b.ID = id;
                        b.FirstName = conf.Get("first_name");
                        b.LastName = conf.Get("last_name");
                        b.Password = conf.Get("password");
                        if (string.IsNullOrEmpty(b.FirstName) || string.IsNullOrEmpty(b.LastName) || string.IsNullOrEmpty(b.Password)) throw new Exception("Incomplete bot information, fist_name, last_name and password are required");
                        b.SitOn = (UUID)conf.Get("sit_on");
                        b.Sim = conf.Get("sim");
                        try
                        {
                            float pos_x = conf.GetFloat("pos_x");
                            float pos_y = conf.GetFloat("pos_y");
                            float pos_z = conf.GetFloat("pos_z");
                            if (!string.IsNullOrEmpty(b.Sim))
                            {
                                b.PosInSim = new Vector3(pos_x, pos_y, pos_z);
                            }
                        }
                        catch (Exception) { }
                        if (conf.Contains("login_uri"))
                        {
                            b.LoginURI = conf.Get("login_uri");
                        }
                        if (conf.Contains("grid_name"))
                        {
                            b.GridName = conf.GetString("grid_name");
                        }
                        else
                        {
                            b.GridName = "agni";
                        }
                        Bots.Add(b);
                    }
                    catch (Exception) { }
                }
                else if (type == "ircserver")
                {
                    try
                    {
                        IrcServerInfo si = new IrcServerInfo();
                        si.ID = id;
                        si.ServerHost = conf.GetString("irc_server");
                        si.ServerPort = conf.GetInt("irc_port", 6667);
                        si.Nick = conf.GetString("irc_nick");
                        si.Username = si.Nick;
                        if (conf.Contains("irc_username"))
                            si.Username = conf.GetString("irc_username");
                        IrcServers.Add(si);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed parsing section {0}: {1}", conf.Name, ex.Message);
                    }
                }
                else if (type == "ircchan")
                {
                    try
                    {
                        string server_id = conf.GetString("irc_server");
                        IrcServerInfo si = IrcServers.Find((IrcServerInfo s) => { return s.ID == server_id; });
                        if (si == null)
                        {
                            Console.WriteLine("Warning, unknown server in section [{0}]", conf.Name);
                            continue;
                        }
                        si.Channels[id] = conf.GetString("chan_name");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed parsing section {0}: {1}", conf.Name, ex.Message);
                    }
                }
                else if (type == "xmppserver")
                {
                    try
                    {
                        XmppServerInfo si = new XmppServerInfo();
                        si.ID = id;
                        si.User = conf.GetString("xmpp_user");
                        si.Domain = conf.GetString("xmpp_domain");
                        si.NetworkHost = conf.GetString("xmpp_server");
                        si.Password = conf.GetString("xmpp_password");
                        si.Nick = conf.GetString("xmpp_nick", si.User);
                        XmppServers.Add(si);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed parsing section {0}: {1}", conf.Name, ex.Message);
                    }
                }
                else if (type == "xmppconference")
                {
                    try
                    {
                        string server_id = conf.GetString("xmpp_server");
                        XmppServerInfo si = XmppServers.Find((XmppServerInfo s) => { return s.ID == server_id; });
                        if (si == null)
                        {
                            Console.WriteLine("Warning, unknown server in section [{0}]", conf.Name);
                            continue;
                        }
                        si.Conferences[id] = conf.GetString("conference");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed parsing section {0}: {1}", conf.Name, ex.Message);
                    }
                }
                else if (type == "bridge")
                {
                    try
                    {
                        BridgeInfo bi = new BridgeInfo();
                        bi.ID = id;
                        bi.IrcChanID = conf.GetString("ircchan");
                        bi.IrcServerConf = IrcServers.Find((IrcServerInfo s) => { return s.Channels.ContainsKey(bi.IrcChanID); });
                        UUID groupID;
                        UUID.TryParse(conf.GetString("grid_group"), out groupID);
                        bi.GridGroup = groupID;
                        bi.Bot = Bots.Find((BotInfo b) => { return b.ID == conf.GetString("bot"); });
                        bi.XmppConferenceID = conf.GetString("xmppconference");
                        bi.XmppServerConf = XmppServers.Find((XmppServerInfo xmpp) => { return xmpp.Conferences.ContainsKey(bi.XmppConferenceID); });
                        Bridges.Add(bi);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed parsing section {0}: {1}", conf.Name, ex.Message);
                    }
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
                        ulong handle = 0;
                        try
                        {
                            handle = ulong.Parse(args[1]);

                        }
                        catch (Exception)
                        {
                        }
                        if (handle > 0 && !Regions.ContainsKey(args[0]))
                        {
                            Regions.Add(args[0].ToLower(), handle);
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        public void SaveRegions()
        {
            try
            {
                StreamWriter wr = new StreamWriter(ConfPath + "/regions.txt");
                foreach (KeyValuePair<string, ulong> r in Regions)
                {
                    wr.WriteLine(r.Key + "=" + r.Value);
                }
                wr.Close();
            }
            catch (Exception)
            {
                Logger.Log("Failed to save regions handle cache.", Helpers.LogLevel.Warning);
            }
        }

        public ulong GetRegionHandle(string name)
        {
            ulong handle;
            if (Regions.TryGetValue(name.ToLower(), out handle))
            {
                return handle;
            }
            else
            {
                return 0;
            }
        }

        public void SaveCachedRegionHandle(string name, ulong handle)
        {
            lock (Regions)
            {
                if (!Regions.ContainsKey(name.ToLower()))
                {
                    Regions.Add(name.ToLower(), handle);
                    SaveRegions();
                }
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Masters ({0})\n", Masters.Count);
            foreach (KeyValuePair<UUID, string> master in Masters)
            {
                sb.AppendFormat("{1} ({0})\n", master.Key, master.Value);
            }
            sb.AppendFormat("Bots ({0})\n", Bots.Count);
            foreach (BotInfo b in Bots)
            {
                sb.AppendFormat("{0} {1}", b.FirstName, b.LastName);
                if (!string.IsNullOrEmpty(b.Sim))
                {
                    sb.AppendFormat("; keep in region: {0}", b.Sim);
                }
                if (b.SitOn != UUID.Zero)
                {
                    sb.AppendFormat("; sit on: {0}", b.SitOn);
                }
                sb.AppendLine();
            }
            sb.AppendFormat("Region handles ({0})\n", Regions.Count);
            return sb.ToString() + "\nSay `startup` to start, `help` for more commands, and `quit` to end.\n";
        }

        public bool IsMaster(UUID key)
        {
            return Masters.ContainsKey(key);
        }

        public bool IsMaster(string name)
        {
            return Masters.ContainsValue(name);
        }

    }
}
