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

using IrcDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Tomlyn.Model;

namespace WoofBot
{
    public class IrcServerInfo : AChanStringsInfo
    {
        public string ServerHost;
        public int ServerPort = 6667;
        public string Nick;
        public string Username;

        public static IrcServerInfo Create(TomlTable conf, string id)
            => new IrcServerInfo()
            {
                ID = id,
                ServerHost = (string)conf["irc_server"],
                ServerPort = conf.ContainsKey("irc_port") ? (int)(long)conf["irc_port"] : 6667,
                Nick = (string)conf["irc_nick"],
                Username = (string)conf[conf.ContainsKey("irc_username") ? "irc_username" : "irc_nick"]
            };
    }

    public class IrcBot : IDisposable, IRelay
    {
        Program MainProgram;
        public IrcServerInfo Conf;
        public AInfo GetConf() => Conf;
        Configuration MainConf;


        /*
         *  SendDelay = 200,
            AutoReconnect = true,
            AutoRejoin = true,
            AutoRejoinOnKick = false,
            AutoRelogin = true,
            AutoRetry = true,
            AutoRetryDelay = 30,
         */

        public StandardIrcClient irc = new StandardIrcClient()
        {
            FloodPreventer = new IrcStandardFloodPreventer(4, 2000),
            TextEncoding = Encoding.UTF8
        };

        public IrcBot(Program p, IrcServerInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;
            irc.RawMessageReceived += Irc_OnRawMessage;
            irc.Connected += Irc_OnConnected;
            irc.Registered += Irc_Registered;
        }

        private void Irc_Registered(object sender, EventArgs e)
        {
            var client = (IrcClient)sender;

            // maybe handle notices?
            client.LocalUser.JoinedChannel += LocalUser_JoinedChannel;
            client.LocalUser.LeftChannel += LocalUser_LeftChannel;
        }

        private void LocalUser_JoinedChannel(object sender, IrcChannelEventArgs e)
        {
            e.Channel.MessageReceived += Irc_OnChannelMessage;
            e.Channel.UserJoined += Irc_OnChannelJoin;
        }

        private void LocalUser_LeftChannel(object sender, IrcChannelEventArgs e)
        {
            e.Channel.MessageReceived -= Irc_OnChannelMessage;
            e.Channel.UserJoined -= Irc_OnChannelJoin;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (irc != null)
                {
                    if (irc.IsConnected)
                        irc.Quit(1000, "quiting");
                    irc.Dispose();
                    irc = null;
                }
            }
        }

        public bool IsConnected() => irc != null && irc.IsConnected;

        List<string> SplitMessage(string message)
        {
            var ret = new List<string>();
            var lines = Regex.Split(message, "\n+");

            foreach (var line in lines)
            {
                var words = line.Split(' ');
                var outstr = string.Empty;

                foreach (var word in words)
                {
                    outstr += $"{word} ";
                    if (outstr.Length > 380)
                    {
                        ret.Add(outstr.Trim());
                        outstr = string.Empty;
                    }
                }
                ret.Add(outstr.Trim());
            }

            return ret;
        }

        public void RelayMessage(BridgeInfo bridge, string from, string msg)
            => ThreadPool.QueueUserWorkItem(sync =>
            {
                if (IsConnected() && Conf.Channels.TryGetValue(bridge.IrcChanID, out string chan))
                {
                    var channel = irc.Channels.First(c => c.Name.Equals(chan, StringComparison.OrdinalIgnoreCase));
                    bool emote = msg.StartsWith("/me ");
                    SplitMessage(emote ? msg.Substring(3) : msg).ForEach(line =>
                         irc.LocalUser.SendMessage(channel, $"{from}{(emote ? "" : ":")} {line}"));
                }
            });

        void Irc_OnConnected(object sender, EventArgs e)
            => PrintMsg("IRC - " + Conf.ID, "Connected");

        void MessageRelayCommon(IrcChannel channel, IrcMessageEventArgs e, string actionmsg = null, bool action = false)
        {
            if (e.Source is IrcUser)
            {
                string from = $"(irc:{channel.Name}) {e.Source.Name}";
                string botfrom = from;
                string msg = action ? $"/me ${actionmsg}" : e.Text;
                string botmsg = msg;

                Match m = Regex.Match(action ? actionmsg : msg, @"\(grid:(?<grid>[^)]*)\)\s*(?<first>\w+)\s*(?<last>\w+)[ :]*(?<msg>.*)");
                if (m.Success)
                {
                    botfrom = $"(grid:{m.Groups["grid"]}) {m.Groups["first"]} {m.Groups["last"]}";
                    botmsg = $"{(action ? "/me " : "")}{m.Groups["msg"]}";
                }

                MainProgram.RelayMessage(Program.EBridgeType.IRC,
                    b => b.IrcServerConf == Conf && Conf.Channels.Any(pair => pair.Key == b.IrcChanID && pair.Value.Equals(channel.Name, StringComparison.OrdinalIgnoreCase)),
                    from, msg, botfrom, botmsg);
            }
        }

        void Irc_OnChannelMessage(object sender, IrcMessageEventArgs e)
            => ThreadPool.QueueUserWorkItem(sync => MessageRelayCommon((IrcChannel)sender, e));

        void Irc_OnChannelJoin(object sender, IrcChannelUserEventArgs e)
            => PrintMsg("IRC - " + Conf.ID, $"{((IrcChannel)sender).Name} joined {e.ChannelUser.User.UserName}");

        void Irc_OnRawMessage(object sender, IrcRawMessageEventArgs e)
        {
            //PrintMsg(e.Message.Source.Name, $"({e.Message.Prefix}) {e.Message}");
        }

        void PrintMsg(string from, string msg)
            => Console.WriteLine($"{from}: {msg}");

        public void Connect()
        {
            IrcRegistrationInfo info = new IrcUserRegistrationInfo()
            {
                NickName = Conf.Nick,
                UserName = Conf.Username,
                RealName = "WoofBot"
            };

            PrintMsg("IRC - " + Conf.ID, "Connecting...");

            try
            {
                using (var connectedEvent = new ManualResetEventSlim(false))
                {
                    irc.Connected += (sender, e) => connectedEvent.Set();
                    // FIXME: IrcDotNet seems to only support IPv4...
                    var endpoint = new DnsEndPoint(Conf.ServerHost, Conf.ServerPort, System.Net.Sockets.AddressFamily.InterNetwork);
                    irc.Connect(endpoint, false, info);
                    PrintMsg("System", "Logging in...");
                    if (!connectedEvent.Wait(10000))
                    {
                        irc.Dispose();
                        irc = null;
                        PrintMsg("System", "Failed to login to irc server");
                        return;
                    }
                    foreach (var c in Conf.Channels.Values)
                        irc.Channels.Join(c);
                }
            }
            catch (Exception ex)
            {
                PrintMsg("System", "An error has occured: " + ex.Message);
            }
        }
    }
}
