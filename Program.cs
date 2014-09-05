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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using OpenMetaverse;

namespace BarkBot
{
    public class Program
    {
        string cmd;
        public Configuration Conf;
        public List<GridBot> GridBots;
        public List<IrcBot> IrcBots;
        public List<XmppBot> XmppBots;

        public static string Version = "BarkBot 0.1";

        static void Main(string[] args)
        {
            Program p = new Program();
            p.Run(args);
        }

        public int OnlineBots()
        {
            int nr = 0;
            foreach (GridBot bot in GridBots)
            {
                if (bot.Connected)
                {
                    nr++;
                }
            }
            return nr;
        }

        public int TotalBots()
        {
            return GridBots.Count;
        }

        void DisplayPrompt()
        {
            System.Console.Write("[Bot {0} of {1} online]> ", OnlineBots(), TotalBots());
        }

        public void CmdStartup()
        {
            foreach (XmppBot bot in XmppBots)
            {
                if (!bot.IsConnected)
                {
                    bot.Connect();
                }
                else
                {
                    Console.WriteLine("XMPP bot {0} already connected, skipping", bot.Conf.ID);
                }
            }

            foreach (IrcBot bot in IrcBots)
            {
                if (!bot.irc.IsConnected)
                {
                    bot.Connect();
                }
                else
                {
                    Console.WriteLine("IRC bot {0} already connected, skipping", bot.Conf.ID);
                }
            }

            foreach (GridBot bot in GridBots)
            {
                if (!bot.Connected)
                {
                    System.Console.WriteLine("Logging in {0}...", bot.Conf.Name);
                    bot.Login();
                    bot.Persistant = true;
                }
                else
                {
                    System.Console.WriteLine("{0} already online, skipping.", bot.Conf.Name);
                }
            }
        }

        public void CmdShutdown()
        {
            foreach (XmppBot bot in XmppBots)
            {
                bot.Dispose();
            }

            foreach (IrcBot bot in IrcBots)
            {
                bot.Dispose();
            }

            foreach (GridBot bot in GridBots)
            {
                if (bot.Connected)
                {
                    System.Console.WriteLine("Logging out {0}...", bot.Conf.Name);
                    bot.Logout();
                    bot.CleanUp();
                }
                else
                {
                    System.Console.WriteLine("{0} already offline, skipping.", bot.Conf.Name);
                }
            }
        }

        void SetAppearance(bool rebake)
        {
            ThreadPool.QueueUserWorkItem(sync =>
            {
                foreach (GridBot bot in GridBots)
                {
                    if (bot.Connected)
                    {
                        bot.Client.Appearance.RequestSetAppearance(rebake);
                        System.Console.WriteLine("{0} is setting appearance.", bot.Conf.Name);
                    }
                    Thread.Sleep(1500);
                }
            });
        }

        public string CmdStatus()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Masters:");
            foreach (KeyValuePair<UUID, string> master in Conf.Masters)
            {
                sb.AppendLine(master.Value + " " + master.Key);
            }

            sb.AppendLine();
            sb.AppendLine("Bots:");

            foreach (GridBot bot in GridBots)
            {
                sb.Append(bot.Conf.Name);
                if (!bot.Connected)
                {
                    sb.AppendLine(" is offline.");
                }
                else
                {
                    sb.AppendLine(" is in " + bot.Client.Network.CurrentSim.Name + " at " + bot.Position);
                }
            }
            System.Console.WriteLine(sb.ToString());
            return sb.ToString();
        }

        void ProcessCommand(string line)
        {
            Regex splitter = new Regex(@" +");
            string[] args = splitter.Split(line.Trim());

            if (args.Length > 0 && args[0] != "")
            {
                cmd = args[0];

                switch (cmd)
                {
                    case "startup":
                        CmdStartup();
                        break;
                    case "shutdown":
                        CmdShutdown();
                        break;
                    case "status":
                        CmdStatus();
                        break;
                    case "appearance":
                        SetAppearance(false);
                        break;
                    case "rebake":
                        SetAppearance(true);
                        break;
                    case "help":
                        System.Console.WriteLine("Commands:\n"
                            + "\nhelp - display this message"
                            + "\nstartup - starts offline bots"
                            + "\nshutdown - logs all bots off"
                            + "\nstatus - gives the status of all bots"
                            /* Liru Note: These don't seem to work on SL anymore.
                            + "\nappearance - rebake me"
                            + "\nrebake - rebake me forcefully"
                             */
                            + "\nquit - shutdown and end program");
                        break;
                }
            }
        }

        void Run(string[] args)
        {
            Logger.Log(Version + " starting up", Helpers.LogLevel.Info);
            System.Console.WriteLine(Version + " starting up");

            try { Conf = new Configuration(@"./"); }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to read the configuration file: {0}", ex.Message);
                Environment.Exit(1);
            }

            System.Console.WriteLine("Configuration loaded:\n" + Conf);

            GridBots = new List<GridBot>();
            foreach (BotInfo b in Conf.Bots)
            {
                GridBots.Add(new GridBot(this, b, Conf));
            }

            IrcBots = new List<IrcBot>();
            foreach (IrcServerInfo b in Conf.IrcServers)
            {
                IrcBots.Add(new IrcBot(this, b, Conf));
            }

            XmppBots = new List<XmppBot>();
            foreach(XmppServerInfo b in Conf.XmppServers)
            {
                XmppBots.Add(new XmppBot(this, b, Conf));
            }


            if (args.Length > 0)
            {
                cmd = args[0];
                if (cmd == "startup")
                {
                    CmdStartup();
                }
            }

            while (cmd != "quit")
            {
                DisplayPrompt();
                string inputLine = System.Console.ReadLine();
                if (inputLine == null)
                {
                    cmd = "quit";
                }
                else
                {
                    ProcessCommand(inputLine);
                }
            }
            CmdShutdown();
        }
    }
}
