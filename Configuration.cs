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
    class BotInfo
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Password { get; set; }
        public UUID SitOn { get; set; }
        public ulong SimHandle { get; set; }
        public string Sim { get; set; }
        public Vector3 PosInSim { get; set; }
        public string LoginURI { get; set; }

        public string Name
        {
            get { return FirstName + " " + LastName; }
        }

        public BotInfo()
        {
            LoginURI = "https://login.agni.lindenlab.com/cgi-bin/login.cgi";
        }
    }

    class Configuration
    {
        string confPath;
        public Dictionary<UUID, string> masters = new Dictionary<UUID,string>();
        public List<BotInfo> bots = new List<BotInfo>();
        public Dictionary<string, ulong> regions = new Dictionary<string, ulong>();
        IniConfigSource inifile;

        public Configuration(string conf)
        {
            confPath = conf;
            inifile = new IniConfigSource(confPath + "/jarilo.conf");
            inifile.CaseSensitive = false;
            LoadMastersAndBots();
            LoadRegions();
        }

        public void LoadRegions()
        {
            regions = new Dictionary<string,ulong>();

            try {
                StreamReader reader = new StreamReader(confPath + "/regions.txt");
                Regex splitter = new Regex(@"[\t ]*=[\t ]*", RegexOptions.Compiled);

                while (!reader.EndOfStream) {
                    string line = reader.ReadLine();
                    string[] args = splitter.Split(line.Trim(), 2);
                    if (args.Length >= 2) {
                        ulong handle = 0;
                        try {
                            handle = ulong.Parse(args[1]);

                        } catch (Exception) {
                        }
                        if (handle > 0 && !regions.ContainsKey(args[0])) {
                            regions.Add(args[0].ToLower(), handle);
                        }
                    }
                }
            } catch (Exception) { }
        }

        public void SaveRegions()
        {
            try {
                StreamWriter wr = new StreamWriter(confPath + "/regions.txt");
                foreach (KeyValuePair<string, ulong> r in regions) {
                    wr.WriteLine(r.Key + "=" + r.Value);
                }
                wr.Close();
            } catch (Exception) {
                Logger.Log("Failed to save regions handle cache.", Helpers.LogLevel.Warning);
            }
        }

        public ulong GetRegionHandle(string name)
        {
            ulong handle;
            if (regions.TryGetValue(name.ToLower(), out handle)) {
                return handle;
            } else {
                return 0;
            }
        }

        public void SaveCachedRegionHandle(string name, ulong handle)
        {
            lock (regions) {
                if (!regions.ContainsKey(name.ToLower())) {
                    regions.Add(name.ToLower(), handle);
                    SaveRegions();
                }
            }
        }

        public void LoadMastersAndBots()
        {
            masters.Clear();
            bots.Clear();

            foreach (IniConfig conf in inifile.Configs) {
                if (conf.Name.StartsWith("master", false, System.Globalization.CultureInfo.InvariantCulture)) {
                    try {
                        UUID masterId = (UUID)conf.Get("uuid");
                        string masterName = conf.Get("name");
                        if (masterId != UUID.Zero) {
                            masters.Add(masterId, masterName);
                        }
                    } catch (Exception) { }
                } else if (conf.Name.StartsWith("bot", false, System.Globalization.CultureInfo.InvariantCulture)) {
                    try {
                        BotInfo b = new BotInfo();
                        b.FirstName = conf.Get("first_name");
                        b.LastName = conf.Get("last_name");
                        b.Password = conf.Get("password");
                        if (string.IsNullOrEmpty(b.FirstName) || string.IsNullOrEmpty(b.LastName) || string.IsNullOrEmpty(b.Password)) throw new Exception("Incomplete bot information, fist_name, last_name and password are required");
                        b.SitOn = (UUID)conf.Get("sit_on");
                        b.Sim = conf.Get("sim");
                        try {
                            float pos_x = conf.GetFloat("pos_x");
                            float pos_y = conf.GetFloat("pos_y");
                            float pos_z = conf.GetFloat("pos_z");
                            if (!string.IsNullOrEmpty(b.Sim)) {
                                b.PosInSim = new Vector3(pos_x, pos_y, pos_z);
                            }
                        } catch (Exception) { }
                        if (conf.Contains("login_uri"))
                        {
                            b.LoginURI = conf.Get("login_uri");
                        }
                        bots.Add(b);
                    } catch (Exception) { }
                }
            }
        }


        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Masters ({0})\n", masters.Count);
            foreach (KeyValuePair<UUID, string> master in masters) {
                sb.AppendFormat("{1} ({0})\n", master.Key, master.Value);
            }
            sb.AppendFormat("Bots ({0})\n", bots.Count);
            foreach (BotInfo b in bots) {
                sb.AppendFormat("{0} {1}",b.FirstName, b.LastName);
                if (!string.IsNullOrEmpty(b.Sim)) {
                    sb.AppendFormat("; keep in region: {0}", b.Sim);
                }
                if (b.SitOn != UUID.Zero) {
                    sb.AppendFormat("; sit on: {0}", b.SitOn);
                }
                sb.AppendLine();
            }
            sb.AppendFormat("Region handles ({0})\n", regions.Count);
            return sb.ToString();
        }

        public bool IsMaster(UUID key)
        {
            return masters.ContainsKey(key);
        }

    }
}
