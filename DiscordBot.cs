// BSD 3-Clause License
//
// Copyright (c) 2017-2019, Alchemy Development Group
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

using Discord;
using Discord.WebSocket;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WoofBot
{
    public class DiscordServerInfo : AChannelsInfo<ulong>
    {
        public string token;
        public List<string> SafeRoles;
        public List<string> DevRoles;
    }

    public class DiscordBot : IRelay
    {
        Program MainProgram;
        public DiscordServerInfo Conf;
        public AInfo GetConf() => Conf;
        DiscordSocketClient Client;

        public DiscordBot(Program p, DiscordServerInfo c)
        {
            MainProgram = p;
            Conf = c;
        }

        private Task Log(LogMessage msg)
        {
            Helpers.LogLevel ToLogLevel(LogSeverity s)
            {
                switch (s)
                {
                    case LogSeverity.Verbose:
                    case LogSeverity.Debug:
                        return Helpers.LogLevel.Debug;
                    case LogSeverity.Error:
                        return Helpers.LogLevel.Error;
                    case LogSeverity.Warning:
                        return Helpers.LogLevel.Warning;
                    case LogSeverity.Info:
                        return Helpers.LogLevel.Info;
                }

                return Helpers.LogLevel.None;
            }

            // Run the log in it's own thread
            Task.Run(() => Logger.Log($"[Discord:{Conf.Id}] {msg.Message}", ToLogLevel(msg.Severity)));
            return Task.CompletedTask;
        }

        async Task ConnectAsync()
        {
            await Client.LoginAsync(TokenType.Bot, Conf.token);
            await Client.StartAsync();

            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        public void Connect()
        {
            //var name = System.Reflection.Assembly.GetExecutingAssembly().GetName();
            Client = new DiscordSocketClient(new DiscordSocketConfig()
            {
                /*AppName = name.Name,
                Version = name.Version.ToString(3),
                AppUrl = "https://bitbucket.org/alchemyviewer/woofbot",*/
                LogLevel = LogSeverity.Info
            });

            Client.Log += Log;
            Client.MessageReceived += Client_OnMessageReceived;
            Client.Disconnected += Client_Disconnected;
            Task.Run(ConnectAsync);
        }

        private Task Client_Disconnected(Exception arg)
        {
            Task.Run(() => Logger.Log($"[Discord:{Conf.Id}] Disconnected ({arg.Message})", Helpers.LogLevel.Warning));
            return Task.CompletedTask;
        }

        private Task Client_OnMessageReceived(SocketMessage msg)
        {
            ulong channel_id = msg.Channel.Id;
            SocketUser sender = msg.Author;
            if (sender.IsBot || sender.Id == Client.CurrentUser.Id)
                return Task.CompletedTask;

            if (Conf.Channels.Values.Contains(channel_id))
            {
                // run on it's own thread.
                Task.Run(() => Relay(msg));
            }
            // TODO: Commands?
            //else if (owners.Contains(msg.Author.Id)) await Command(msg);

            return Task.CompletedTask;
        }

        private void Relay(SocketMessage msg)
        {
            var text = msg is SocketUserMessage && msg.Tags.Any() ? (msg as SocketUserMessage).Resolve() : msg.Content; // Resolve 'em if you got 'em.
            string from = $"(discord:{msg.Channel.Name}) {(msg.Author as SocketGuildUser)?.Nickname ?? msg.Author.Username}";
            Console.WriteLine($"{from}: {text}");
            Func<char, string, bool> begandend = (c, str) => c == str.First() && c == str.Last();
            if (text.Length > 2 && (begandend('_', text) // Discord's /me support does this.
                || begandend('*', text)))
                text = $"/me {text.Substring(1, text.Length - 2)}";

            foreach (var m in msg.Attachments)
                text += (text.Length == 0 ? "" : "\n") + m.Url;

            MainProgram.RelayMessage(Program.EBridgeType.Discord,
                b => b.DiscordServerConf == Conf && Conf.Channels.Any(c => c.Key == b.DiscordChannelId && c.Value == msg.Channel.Id),
                from, text);
        }

        public bool IsConnected() => Client?.LoginState == LoginState.LoggedIn && Client?.ConnectionState == ConnectionState.Connected;

        async Task RelayMessageAsync(BridgeInfo bridge, SocketTextChannel c, string msg)
        {
            if (c == null) return;
            var safes = bridge.DiscordServerConf.SafeRoles;
            var devs = bridge.DiscordServerConf.DevRoles;
            foreach (var r in c.Guild.Roles)
            {
                if (r.IsMentionable && (!safes.Contains(r.Name)))
                    msg = msg.Replace($"@{r.Name}", r.Mention);
                if (devs.Contains(r.Name))
                {
                    foreach (var m in r.Members)
                    {
                        msg = msg.Replace($"@{m.Username}", m.Mention);
                        if (!string.IsNullOrEmpty(m.Nickname))
                            msg = msg.Replace($"@{m.Nickname}", m.Mention);
                    }
                }
            }
            await c.SendMessageAsync(msg);
        }

        public void RelayMessage(BridgeInfo bridge, string from, string msg)
        {
            if (IsConnected())
            {
                if (Conf.Channels.TryGetValue(bridge.DiscordChannelId, out ulong confChan))
                {
                    bool action = msg.StartsWith("/me ") || msg.StartsWith("/me'");
                    Task.Run(() => RelayMessageAsync(bridge, (SocketTextChannel)Client.GetChannel(confChan), action ? $"*{from}{msg.Substring(3)}*" : $"{from}: {msg}"));
                }
            }
        }
    }
}
