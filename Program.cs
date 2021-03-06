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
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using OpenMetaverse;

namespace WoofBot
{
    public class Program
    {
        private string _cmd;
        private Configuration _conf;
        private List<GridBot> _gridBots;
        private List<IrcBot> _ircBots;
        private List<DiscordBot> _discordBots;

        // FIXME: make this dynamicly generated by the build process
        public static string Version = "WoofBot 0.4";

        private static void Main(string[] args) => new Program().Run(args);

        private int OnlineBots() => _gridBots.Count(bot => bot.IsConnected());

        private int TotalBots() => _gridBots.Count;

        private void DisplayPrompt()
            => Console.Write($"[Bot {OnlineBots()} of {TotalBots()} online]> ");

        private void StartRelay<T>(string type, List<T> bots) where T : IRelay
            => bots.ForEach(bot =>
            {
                if (!bot.IsConnected())
                    bot.Connect();
                else
                    Console.WriteLine($"{type} bot {bot.GetConf().Id} already connected, skipping");
            });

        public void CmdStartup()
        {
            StartRelay("Discord", _discordBots);
            StartRelay("IRC", _ircBots);
            StartRelay("Metaverse", _gridBots);
        }

        public void CmdShutdown()
        {
            _ircBots.ForEach(bot => bot.Dispose());

            _gridBots.ForEach(bot =>
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

        public enum EBridgeType
        {
            Grid,
            Irc,
            Discord,
        }

        public void RelayMessage(EBridgeType type, Predicate<BridgeInfo> criteria, string from, string text, string botFrom = null, string botText = null)
        {
            try
            {
                var gridFrom = botFrom ?? from;
                var gridText = botText ?? text;
                foreach (var bridge in _conf.Bridges.FindAll(criteria))
                {
                    if (type != EBridgeType.Grid && bridge.Bot != null && (bridge.GridGroup == null || bridge.GridGroup != UUID.Zero))
                        _gridBots.Find(b => b.Conf == bridge.Bot)?.RelayMessage(bridge, gridFrom, gridText);
                    if (type != EBridgeType.Irc && bridge.IrcServerConf != null)
                        _ircBots.Find(b => b.Conf == bridge.IrcServerConf)?.RelayMessage(bridge, from, text);
                    if (type != EBridgeType.Discord && bridge.DiscordServerConf != null)
                        _discordBots.Find(b => b.Conf == bridge.DiscordServerConf)?.RelayMessage(bridge, from, text);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed relaying message: {ex.Message}");
            }
        }

        private void SetAppearance(bool rebake)
            => ThreadPool.QueueUserWorkItem(sync => _gridBots.ForEach(bot =>
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
            foreach (var (uuid, name) in _conf.Masters)
                sb.AppendLine($"{name} {uuid}");

            sb.AppendLine("\nBots:");

            _gridBots.ForEach(bot =>
                sb.AppendLine($"{bot.Conf.Name} is {(bot.IsConnected() ? $"in {bot.Client.Network.CurrentSim.Name} at {bot.Position}" : "offline")}."));
            return sb.ToString();
        }

        private void ProcessCommand(string line)
        {
            var splitter = new Regex(@" +");
            var args = splitter.Split(line.Trim());

            if (args.Length <= 0 || args[0].Length == 0) return;
            
            _cmd = args[0];

            switch (_cmd)
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

        private void Run(IReadOnlyList<string> args)
        {
            Logger.Log(Version + " starting up", Helpers.LogLevel.Info);
            Console.WriteLine(Version + " starting up");

            var confAutoLifeTime = Environment.GetEnvironmentVariable("WOOFBOT_AUTO_LIFETIME") ?? "false";
            var confDirectory = Environment.GetEnvironmentVariable("WOOFBOT_CONFIG_PATH") ?? "./";

            try { _conf = new Configuration(confDirectory); }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read the configuration file: {ex.Message}");
                Environment.Exit(1);
            }

            Console.WriteLine("Configuration loaded:\n" + _conf);

            _gridBots = new List<GridBot>();
            _conf.Bots.ForEach(b => _gridBots.Add(new GridBot(this, b, _conf)));

            _ircBots = new List<IrcBot>();
            _conf.IrcServers.ForEach(b => _ircBots.Add(new IrcBot(this, b, _conf)));

            _discordBots = new List<DiscordBot>();
            _conf.DiscordServers.ForEach(b => _discordBots.Add(new DiscordBot(this, b)));

            if (!confAutoLifeTime.Equals("true", StringComparison.OrdinalIgnoreCase)
                && !confAutoLifeTime.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Count > 0 && (_cmd = args[0]) == "startup")
                    CmdStartup();

                try
                {
                    while (_cmd != "quit")
                    {
                        DisplayPrompt();
                        string inputLine = Console.ReadLine();
                        if (inputLine == null)
                        {
                            _cmd = "quit";
                        }
                        else
                        {
                            ProcessCommand(inputLine);
                        }
                    }
                }
                catch
                {
                    // do nothing and fallthrough
                }
            }
            else // full lifetime management via normal daemon process usage.
            {
                var loadContext = AssemblyLoadContext.GetLoadContext(typeof(Program).GetTypeInfo().Assembly);
                var exitEvent = new ManualResetEvent(false);

                Console.CancelKeyPress += (sender, eventArgs) => { eventArgs.Cancel = true; exitEvent.Set(); };
                loadContext.Unloading += _ => exitEvent.Set(); 

                CmdStartup();
                exitEvent.WaitOne();
                // fallthrough once a cancel event is triggered.
            }
            CmdShutdown();
        }
    }
}
