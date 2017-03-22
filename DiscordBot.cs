using System;
using System.Collections.Generic;
using System.Linq;
using OpenMetaverse;
using Discord;
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
        Configuration MainConf;
        DiscordClient Client;

        public DiscordBot(Program p, DiscordServerInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;
        }

        async Task ConnectAsync()
        {
            for (;;)
            {
                try
                {
                    await Client.Connect(Conf.token, TokenType.Bot);
                    break;
                }
                catch (Exception ex)
                {
                    Client.Log.Error($"Login Failed", ex);
                    await Task.Delay(Client.Config.FailedReconnectDelay);
                }
            }
        }

        public void Connect()
        {
            var name = System.Reflection.Assembly.GetExecutingAssembly().GetName();
            Client = new DiscordClient(new DiscordConfigBuilder()
            {
                AppName = name.Name,
                AppVersion = name.Version.ToString(3),
                AppUrl = "https://bitbucket.org/alchemyviewer/barkbot",
                LogLevel = LogSeverity.Info
            });

#if log_discord // This is sooo spammy right now.
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
            Client.Log.Message += (s, e) => Logger.Log($"[Discord:{Conf.ID}] {e.Message}", ToLogLevel(e.Severity));
#endif
            Client.MessageReceived += Client_OnMessageReceived;
            Task.Run(ConnectAsync);
        }

        private void Client_OnMessageReceived(object s, MessageEventArgs msg)
        {
            ulong channel_id = msg.Channel.Id;
            User sender = msg.User;
            if (Conf.Channels.Values.Contains(channel_id) && sender.Id != Client.CurrentUser.Id)
            {
                string channel_name = msg.Channel.Name;
                string user_name = string.IsNullOrEmpty(sender.Nickname) ? sender.Name : sender.Nickname;
                var text = msg.Message.Text;
                string from = $"(discord:{channel_name}) {user_name}";
                Console.WriteLine($"{from}: {text}");
                try
                {
                    var bridges = MainConf.Bridges.FindAll(b =>
                        b.DiscordServerConf == Conf && Conf.Channels[b.DiscordChannelID] == channel_id);

                    Func<char, string, bool> begandend = (c, str) => c == str.First() && c == str.Last();
                    if (text.Length > 2 && (begandend('_', text) // Discord's /me support does this.
                        || begandend('*', text)))
                        text = $"/me {text.Substring(1, text.Length-2)}";

                    foreach (var m in msg.Message.Attachments)
                        text += (text.Length == 0 ? "" : "\n") + m.Url;

                    foreach (BridgeInfo bridge in bridges)
                    {
                        if (bridge.Bot != null && bridge.GridGroup != UUID.Zero)
                            MainProgram.GridBots.Find(b => b.Conf == bridge.Bot)?.RelayMessage(bridge, from, text);
                        if (bridge.IrcServerConf != null)
                            MainProgram.IrcBots.Find(b => b.Conf == bridge.IrcServerConf)?.RelayMessage(bridge, from, text);
                        if (bridge.SlackServerConf != null)
                            MainProgram.SlackBots.Find(b => b.Conf == bridge.SlackServerConf)?.RelayMessage(bridge, from, text);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed relaying message: {ex.Message}");
                }
            }
        }

        public bool IsConnected() => Client?.State == ConnectionState.Connected;

        async Task RelayMessageAsync(BridgeInfo bridge, Channel c, string msg)
        {
            if (c == null) return;
            var safes = bridge.DiscordServerConf.SafeRoles;
            bool has_safes = safes.Any();
            var devs = bridge.DiscordServerConf.DevRoles;
            bool has_devs = devs.Any();
            foreach (var r in c.Server.Roles)
            {
                if (r.IsMentionable && (!has_safes || !safes.Contains(r.Name)))
                    msg = msg.Replace($"@{r.Name}", r.Mention);
                if (has_devs && devs.Contains(r.Name))
                {
                    foreach (var m in r.Members)
                    {
                        msg = msg.Replace($"@{m.Name}", m.Mention);
                        if (!string.IsNullOrEmpty(m.Nickname))
                            msg = msg.Replace($"@{m.Nickname}", m.NicknameMention);
                    }
                }
            }
            await c.SendMessage(msg);
        }

        public void RelayMessage(BridgeInfo bridge, string from, string msg)
        {
            if (IsConnected())
            {
                if (Conf.Channels.TryGetValue(bridge.DiscordChannelID, out ulong confChan))
                {
                    bool action = msg.StartsWith("/me ") || msg.StartsWith("/me'");
                    Task.Run(() => RelayMessageAsync(bridge, Client.GetChannel(confChan), action ? $"*{from}{msg.Substring(3)}*" : $"{from}: {msg}"));
                }
            }
        }
    }
}
