using System;
using System.Collections.Generic;
using System.Linq;
using OpenMetaverse;
using Discord;
using System.Threading.Tasks;
using Discord.WebSocket;

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
        Configuration MainConf;
        DiscordSocketClient Client;

        public DiscordBot(Program p, DiscordServerInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;
        }

        private async Task Log(LogMessage msg)
        {
            Func<LogSeverity, Helpers.LogLevel> ToLogLevel = s =>
            {
                switch (s)
                {
                    case LogSeverity.Verbose: case LogSeverity.Debug: return Helpers.LogLevel.Debug;
                    case LogSeverity.Error: return Helpers.LogLevel.Error;
                    case LogSeverity.Warning: return Helpers.LogLevel.Warning;
                    case LogSeverity.Info: return Helpers.LogLevel.Info;
                }
                return Helpers.LogLevel.None;
            };
            // FIXME: Hacked to be a task because Discord.Net wants it.
            await Task.Run(() => Logger.Log($"[Discord:{Conf.ID}] {msg.Message}", ToLogLevel(msg.Severity)));
            //return Task.CompletedTask;
        }

        async Task ConnectAsync()
        {
            Client.Log += Log;
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

            Client.MessageReceived += Client_OnMessageReceived;
            Task.Run(ConnectAsync);
        }

        private async Task Client_OnMessageReceived(SocketMessage msg)
        {
            ulong channel_id = msg.Channel.Id;
            SocketUser sender = msg.Author;
            if (sender.IsBot || sender.Id == Client.CurrentUser.Id) return;
            if (Conf.Channels.Values.Contains(channel_id))
            {
                // FIXME: Hacked to be a task because Discord.Net wants it.
                await Task.Run(() => Relay(msg));
                // return Task.CompletedTask;
            }
            // TODO: Commands?
            //else if (owners.Contains(msg.Author.Id)) await Command(msg);
        }

        private void Relay(SocketMessage msg)
        {
            var text = msg is SocketUserMessage && msg.Tags.Any() ? (msg as SocketUserMessage).Resolve() : msg.Content; // Resolve 'em if you got 'em.
            string from = $"(discord:{msg.Channel.Name}) {(msg.Author as SocketGuildUser)?.Nickname ?? msg.Author.Username}";
            Console.WriteLine($"{from}: {text}");
            Func<char, string, bool> begandend = (c, str) => c == str.First() && c == str.Last();
            if (text.Length > 2 && (begandend('_', text) // Discord's /me support does this.
                || begandend('*', text)))
                text = $"/me {text.Substring(1, text.Length-2)}";

            foreach (var m in msg.Attachments)
                text += (text.Length == 0 ? "" : "\n") + m.Url;

            MainProgram.RelayMessage(Program.EBridgeType.DISCORD,
                b => b.DiscordServerConf == Conf && Conf.Channels[b.DiscordChannelID] == msg.Channel.Id,
                from, text);
        }

        public bool IsConnected() => Client?.LoginState == LoginState.LoggedIn;

        async Task RelayMessageAsync(BridgeInfo bridge, SocketTextChannel c, string msg)
        {
            if (c == null) return;
            var safes = bridge.DiscordServerConf.SafeRoles;
            bool has_safes = safes.Any();
            var devs = bridge.DiscordServerConf.DevRoles;
            bool has_devs = devs.Any();
            foreach (var r in c.Guild.Roles)
            {
                if (r.IsMentionable && (!has_safes || !safes.Contains(r.Name)))
                    msg = msg.Replace($"@{r.Name}", r.Mention);
                if (has_devs && devs.Contains(r.Name))
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
                if (Conf.Channels.TryGetValue(bridge.DiscordChannelID, out ulong confChan))
                {
                    bool action = msg.StartsWith("/me ") || msg.StartsWith("/me'");
                    Task.Run(() => RelayMessageAsync(bridge, (SocketTextChannel)Client.GetChannel(confChan), action ? $"*{from}{msg.Substring(3)}*" : $"{from}: {msg}"));
                }
            }
        }
    }
}
