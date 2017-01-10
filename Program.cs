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
using System.Linq;

namespace WoofBot
{
    public class Program
    {
        string cmd;
        public Configuration Conf;
        public List<GridBot> GridBots;
        public List<IrcBot> IrcBots;
        public List<SlackBot> SlackBots;
        public List<DiscordBot> DiscordBots;

        public static string Version = "WoofBot 0.2";

        static void Main(string[] args) => new Program().Run(args);

        public int OnlineBots() => GridBots.Count(bot => bot.IsConnected());

        public int TotalBots() => GridBots.Count;

        void DisplayPrompt()
            => Console.Write($"[Bot {OnlineBots()} of {TotalBots()} online]> ");

	    public void StartRelay<T>(string type, List<T> Bots) where T:IRelay
            => Bots.ForEach(bot =>
            {
                if (!bot.IsConnected())
                    bot.Connect();
                else
                    Console.WriteLine($"{type} bot {bot.GetConf().ID} already connected, skipping");
            });

        public void CmdStartup()
        {
            StartRelay("Discord", DiscordBots);
            StartRelay("Slack", SlackBots);
            StartRelay("IRC", IrcBots);
            StartRelay("Metaverse", GridBots);
        }

        public void CmdShutdown()
        {
            IrcBots.ForEach(bot => bot.Dispose());

            GridBots.ForEach(bot =>
            {
                if (bot.IsConnected())
                {
                    Console.WriteLine($"Logging out {bot.Conf.Name}...");
                    bot.Dispose();
                }
                else
                {
                    Console.WriteLine($"{bot.Conf.Name} already offline, skipping.");
                }
            });
        }

        void SetAppearance(bool rebake)
            => ThreadPool.QueueUserWorkItem(sync => GridBots.ForEach(bot =>
            {
                if (bot.IsConnected())
                {
                    bot.Client.Appearance.RequestSetAppearance(rebake);
                    Console.WriteLine($"{bot.Conf.Name} is setting appearance.");
                }
                Thread.Sleep(1500);
            }));

        public string CmdStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Masters:");
            foreach (var master in Conf.Masters)
                sb.AppendLine($"{master.Value} {master.Key}");

            sb.AppendLine("\nBots:");

            GridBots.ForEach(bot =>
                sb.AppendLine($"{bot.Conf.Name} is {(bot.IsConnected() ? $"in {bot.Client.Network.CurrentSim.Name} at {bot.Position}" : "offline")}."));
            return sb.ToString();
        }

        void ProcessCommand(string line)
        {
            Regex splitter = new Regex(@" +");
            string[] args = splitter.Split(line.Trim());

            if (args.Length > 0 && args[0].Length != 0)
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
                        Console.WriteLine(CmdStatus());
                        break;
                    case "appearance":
                        SetAppearance(false);
                        break;
                    case "rebake":
                        SetAppearance(true);
                        break;
                    case "help":
                        Console.WriteLine("Commands:\n"
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
            Console.WriteLine(Version + " starting up");

            try { Conf = new Configuration(@"./"); }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read the configuration file: {ex.Message}");
                Environment.Exit(1);
            }

            Console.WriteLine("Configuration loaded:\n" + Conf);

            GridBots = new List<GridBot>();
            Conf.Bots.ForEach(b => GridBots.Add(new GridBot(this, b, Conf)));

            IrcBots = new List<IrcBot>();
            Conf.IrcServers.ForEach(b => IrcBots.Add(new IrcBot(this, b, Conf)));

            SlackBots = new List<SlackBot>();
            Conf.SlackServers.ForEach(b => SlackBots.Add(new SlackBot(this, b, Conf)));

            DiscordBots = new List<DiscordBot>();
            Conf.DiscordServers.ForEach(b => DiscordBots.Add(new DiscordBot(this, b, Conf)));

            if (args.Length > 0 && (cmd = args[0]) == "startup")
                CmdStartup();

            while (cmd != "quit")
            {
                DisplayPrompt();
                string inputLine = Console.ReadLine();
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
