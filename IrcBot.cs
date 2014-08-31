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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Meebey.SmartIrc4net;
using OpenMetaverse;

namespace Jarilo
{
    public class IrcBot : IDisposable, IRelay
    {
        Program MainProgram;
        public IrcServerInfo Conf;
        Configuration MainConf;
        Thread IRCConnection;

        public IrcClient irc;

        public IrcBot(Program p, IrcServerInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;

            irc = new IrcClient();
            irc.SendDelay = 200;
            irc.AutoReconnect = true;
            irc.AutoRejoin = true;
            irc.AutoRejoinOnKick = false;
            irc.AutoRelogin = true;
            irc.AutoRetry = true;
            irc.AutoRetryDelay = 30;
            irc.CtcpVersion = Program.Version;
            irc.Encoding = Encoding.UTF8;

            irc.OnRawMessage += new IrcEventHandler(irc_OnRawMessage);
            irc.OnJoin += new JoinEventHandler(irc_OnJoin);
            irc.OnConnected += new EventHandler(irc_OnConnected);
            irc.OnChannelAction += new ActionEventHandler(irc_OnChannelAction);
            irc.OnChannelMessage += new IrcEventHandler(irc_OnChannelMessage);
        }

        public void Dispose()
        {
            try
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
            catch { }
        }

        List<string> SplitMessage(string message)
        {
            List<string> ret = new List<string>();
            string[] lines = Regex.Split(message, "\n+");

            for (int l = 0; l < lines.Length; l++)
            {

                string[] words = lines[l].Split(' ');
                string outstr = string.Empty;

                for (int i = 0; i < words.Length; i++)
                {
                    outstr += words[i] + " ";
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
        {
            ThreadPool.QueueUserWorkItem(sync =>
                {
                    if (irc != null && irc.IsConnected)
                    {
                        string chan;
                        if (Conf.Channels.TryGetValue(bridge.IrcChanID, out chan))
                        {
                            bool emote = msg.StartsWith("/me ");
                            List<string> lines;

                            if (emote)
                                lines = SplitMessage(msg.Substring(3));
                            else
                                lines = SplitMessage(msg);

                            foreach (string line in lines)
                            {
                                if (emote)
                                    irc.SendMessage(SendType.Action, chan, string.Format("{0} {1}", from, line));
                                else
                                    irc.SendMessage(SendType.Message, chan, string.Format("{0}: {1}", from, line));
                            }

                        }
                    }
                });
        }

        void irc_OnConnected(object sender, EventArgs e)
        {
            PrintMsg("IRC - " + Conf.ID, string.Format("Connected"));
        }

        void irc_OnChannelMessage(object sender, IrcEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(sync =>
                {
                    try
                    {
                        List<BridgeInfo> bridges = MainConf.Bridges.FindAll((BridgeInfo b) =>
                        {
                            return
                                b.IrcServerConf == Conf &&
                                Conf.Channels[b.IrcChanID] == e.Data.Channel;
                        });

                        foreach (BridgeInfo bridge in bridges)
                        {
                            if (bridge.Bot != null && bridge.GridGroup != UUID.Zero)
                            {
                                GridBot bot = MainProgram.GridBots.Find((GridBot b) => { return b.Conf == bridge.Bot; });
                                if (bot != null)
                                {
                                    string from = string.Format("(irc:{0}) {1}", e.Data.Channel, e.Data.Nick);
                                    string msg = e.Data.Message;

                                    Match m = Regex.Match(msg, @"\(grid:(?<grid>[^)]*)\)\s*(?<first>\w+)\s*(?<last>\w+)[ :]*(?<msg>.*)");
                                    if (m.Success)
                                    {
                                        from = string.Format("(grid:{0}) {1} {2}",
                                            m.Groups["grid"],
                                            m.Groups["first"],
                                            m.Groups["last"]
                                            );
                                        msg = m.Groups["msg"].ToString();
                                    }

                                    bot.RelayMessage(bridge,
                                        from,
                                        msg);
                                }
                            }

                            if (bridge.XmppServerConf != null)
                            {
                                XmppBot bot = MainProgram.XmppBots.Find((XmppBot b) => { return b.Conf == bridge.XmppServerConf; });
                                if (bot != null)
                                {
                                    bot.RelayMessage(bridge,
                                        string.Format("(irc:{0}) {1}", e.Data.Channel, e.Data.Nick),
                                        e.Data.Message);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed relaying message: {0}", ex.Message);
                    }
                });

        }

        void irc_OnChannelAction(object sender, ActionEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(sync =>
            {
                try
                {

                    List<BridgeInfo> bridges = MainConf.Bridges.FindAll((BridgeInfo b) =>
                    {
                        return
                            b.IrcServerConf == Conf &&
                            Conf.Channels[b.IrcChanID] == e.Data.Channel;
                    });

                    foreach (BridgeInfo bridge in bridges)
                    {
                        if (bridge.Bot != null && bridge.GridGroup != UUID.Zero)
                        {
                            GridBot bot = MainProgram.GridBots.Find((GridBot b) => { return b.Conf == bridge.Bot; });
                            if (bot != null)
                            {
                                string from = string.Format("(irc:{0}) {1}", e.Data.Channel, e.Data.Nick);
                                string msg = string.Format("/me {0}", e.ActionMessage);

                                Match m = Regex.Match(e.ActionMessage, @"\(grid:(?<grid>[^)]*)\)\s*(?<first>\w+)\s*(?<last>\w+)[ :]*(?<msg>.*)");
                                if (m.Success)
                                {
                                    from = string.Format("(grid:{0}) {1} {2}",
                                        m.Groups["grid"],
                                        m.Groups["first"],
                                        m.Groups["last"]
                                        );
                                    msg = "/me " + m.Groups["msg"].ToString();
                                }

                                bot.RelayMessage(bridge,
                                    from,
                                    msg);
                            }
                        }

                        if (bridge.XmppServerConf != null)
                        {
                            XmppBot bot = MainProgram.XmppBots.Find((XmppBot b) => { return b.Conf == bridge.XmppServerConf; });
                            if (bot != null)
                            {
                                bot.RelayMessage(bridge,
                                    string.Format("(irc:{0}) {1}", e.Data.Channel, e.Data.Nick),
                                    string.Format("/me {0}", e.ActionMessage));
                            }
                        }

                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed relaying message: {0}", ex.Message);
                }
            });

        }

        void irc_OnJoin(object sender, JoinEventArgs e)
        {
            PrintMsg("IRC - " + Conf.ID, string.Format("{1} joined {0}", e.Channel, e.Who));
        }

        void irc_OnRawMessage(object sender, IrcEventArgs e)
        {
            if (e.Data.Type == ReceiveType.Unknown) return;
            PrintMsg(e.Data.Nick, string.Format("({0}) {1}", e.Data.Type, e.Data.Message));
        }

        void PrintMsg(string from, string msg)
        {
            Console.WriteLine("{0}: {1}", from, msg);
        }

        public void Connect()
        {
            if (IRCConnection != null)
            {
                if (IRCConnection.IsAlive)
                    IRCConnection.Abort();
                IRCConnection = null;
            }

            IRCConnection = new Thread(new ParameterizedThreadStart(IrcThread));
            IRCConnection.Name = "IRC Thread";
            IRCConnection.IsBackground = true;
            IRCConnection.Start(new object[] { Conf.ServerHost, Conf.ServerPort, Conf.Nick, Conf.Channels.Values.ToArray() });
        }

        private void IrcThread(object param)
        {
            object[] args = (object[])param;
            string server = (string)args[0];
            int port = (int)args[1];
            string nick = (string)args[2];
            string[] chan = (string[])args[3];

            PrintMsg("IRC - " + Conf.ID, "Connecting...");

            try
            {
                irc.Connect(server, port);
                PrintMsg("System", "Logging in...");
                irc.Login(nick, "Singularity SL Relay", 0, nick);
                for (int i = 0; i < chan.Length; i++)
                    irc.RfcJoin(chan[i]);
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
