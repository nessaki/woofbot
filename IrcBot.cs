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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Meebey.SmartIrc4net;
using Nini.Config;
using OpenMetaverse;

namespace WoofBot
{
    public class IrcServerInfo : AChanStringsInfo
    {
        public string ServerHost;
        public int ServerPort = 6667;
        public string Nick;
        public string Username;

        public static IrcServerInfo Create(IniConfig conf, string id)
            => new IrcServerInfo()
            {
                ID = id,
                ServerHost = conf.GetString("irc_server"),
                ServerPort = conf.GetInt("irc_port", 6667),
                Nick = conf.GetString("irc_nick"),
                Username = conf.GetString(conf.Contains("irc_username") ? "irc_username" : "irc_nick")
            };
    }

    public class IrcBot : IDisposable, IRelay
    {
        Program MainProgram;
        public IrcServerInfo Conf;
        public AInfo GetConf() => Conf;
        Configuration MainConf;
        Thread IRCConnection;

        public IrcClient irc = new IrcClient()
        {
            SendDelay = 200,
            AutoReconnect = true,
            AutoRejoin = true,
            AutoRejoinOnKick = false,
            AutoRelogin = true,
            AutoRetry = true,
            AutoRetryDelay = 30,
            CtcpVersion = Program.Version,
            Encoding = Encoding.UTF8
        };

        public IrcBot(Program p, IrcServerInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;
            irc.OnRawMessage += Irc_OnRawMessage;
            irc.OnJoin += Irc_OnJoin;
            irc.OnConnected += Irc_OnConnected;
            irc.OnChannelAction += Irc_OnChannelAction;
            irc.OnChannelMessage += Irc_OnChannelMessage;
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
                        irc.Disconnect();
                    irc = null;
                }

                if (IRCConnection != null)
                {
                    if (IRCConnection.IsAlive)
                        IRCConnection.Abort();
                    IRCConnection = null;
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
                    bool emote = msg.StartsWith("/me ");
                    SplitMessage(emote ? msg.Substring(3) : msg).ForEach(line =>
                        irc.SendMessage(SendType.Action, chan, $"{from}{(emote ? "" : ":")} {line}"));
                }
            });

        void Irc_OnConnected(object sender, EventArgs e)
            => PrintMsg("IRC - " + Conf.ID, "Connected");

        void MessageRelayCommon(IrcEventArgs e, string actionmsg = null, bool action = false)
        {
            string from = $"(irc:{e.Data.Channel}) {e.Data.Nick}";
            string botfrom = from;
            string msg = action ? $"/me ${actionmsg}" : e.Data.Message;
            string botmsg = msg;

            Match m = Regex.Match(action ? actionmsg : msg, @"\(grid:(?<grid>[^)]*)\)\s*(?<first>\w+)\s*(?<last>\w+)[ :]*(?<msg>.*)");
            if (m.Success)
            {
                botfrom = $"(grid:{m.Groups["grid"]}) {m.Groups["first"]} {m.Groups["last"]}";
                botmsg = $"{(action ? "/me " : "")}{m.Groups["msg"]}";
            }

            MainProgram.RelayMessage(Program.EBridgeType.IRC,
                b => b.IrcServerConf == Conf && Conf.Channels[b.IrcChanID] == e.Data.Channel.ToLower(),
                from, msg, botfrom, botmsg);
        }

        void Irc_OnChannelMessage(object sender, IrcEventArgs e)
            => ThreadPool.QueueUserWorkItem(sync => MessageRelayCommon(e));

        void Irc_OnChannelAction(object sender, ActionEventArgs e)
            => ThreadPool.QueueUserWorkItem(sync => MessageRelayCommon(e, e.ActionMessage, true));

        void Irc_OnJoin(object sender, JoinEventArgs e)
            => PrintMsg("IRC - " + Conf.ID, $"{e.Channel} joined {e.Who}");

        void Irc_OnRawMessage(object sender, IrcEventArgs e)
        {
            if (e.Data.Type == ReceiveType.Unknown) return;
            PrintMsg(e.Data.Nick, $"({e.Data.Type}) {e.Data.Message}");
        }

        void PrintMsg(string from, string msg)
            => Console.WriteLine($"{from}: {msg}");

        public void Connect()
        {
            if (IRCConnection != null)
            {
                if (IRCConnection.IsAlive)
                    IRCConnection.Abort();
                IRCConnection = null;
            }

            IRCConnection = new Thread(new ParameterizedThreadStart(IrcThread))
            {
                Name = "IRC Thread",
                IsBackground = true
            };
            IRCConnection.Start(new object[] { Conf.ServerHost, Conf.ServerPort, Conf.Nick, Conf.Username, Conf.Channels.Values.ToArray() });
        }

        private void IrcThread(object param)
        {
            var args = (object[])param;
            var server = $"{args[0]}";
            int port = (int)args[1];
            var nick = $"{args[2]}";
            var username = $"{args[3]}";
            var chan = (string[])args[4];

            PrintMsg("IRC - " + Conf.ID, "Connecting...");

            try
            {
                irc.Connect(server, port);
                PrintMsg("System", "Logging in...");
                irc.Login(nick, "BarkBot System", 0, username);
                foreach (var c in chan)
                    irc.RfcJoin(c);
            }
            catch (Exception ex)
            {
                PrintMsg("System", "An error has occured: " + ex.Message);
            }

            try
            {
                irc.Listen();
                if (irc.IsConnected)
                {
                    irc.AutoReconnect = false;
                    irc.Disconnect();
                }
            }
            catch { }
        }
    }
}
