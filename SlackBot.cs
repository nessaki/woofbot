using System;
using System.Linq;
using SlackAPI;
using OpenMetaverse;

namespace WoofBot
{
    public class SlackServerInfo : AChanStringsInfo
    {
        public string APIKEY;
    }

    public class SlackBot : IRelay
    {
        Program MainProgram;
        public SlackServerInfo Conf;
        public AInfo GetConf() => Conf;
        Configuration MainConf;
        SlackSocketClient Client;

        public SlackBot(Program p, SlackServerInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;
        }

        public void Connect()
        {
            Client = new SlackSocketClient(Conf.APIKEY);
            Client.Connect(o => { });
            Client.OnMessageReceived += Client_OnMessageReceived;
        }

        private void Client_OnMessageReceived(SlackAPI.WebSocketMessages.NewMessage msg)
        {
            if (msg.ok && msg.subtype != "bot_message" && Conf.Channels.Any(x => x.Value == Client.ChannelLookup[msg.channel].name))
            {
                string channel_name = Client.ChannelLookup[msg.channel].name;
                string from = $"(slack:{channel_name}) {Client.UserLookup[msg.user].name}";
                Console.WriteLine($"{from}: {msg.text}");
                MainProgram.RelayMessage(Program.EBridgeType.SLACK,
                    b => b.SlackServerConf == Conf && Conf.Channels[b.SlackChannelID] == channel_name.ToLower(),
                    $from, msg.text);
            }
        }

        public bool IsConnected() => Client != null && Client.IsConnected;

        public void RelayMessage(BridgeInfo bridge, string from, string msg)
        {
            if (Client != null)
            {
                if (Conf.Channels.TryGetValue(bridge.SlackChannelID, out string confChan))
                {
                    var channel = Client.ChannelLookup.First(x => x.Value.name == confChan).Value;
                    if (channel != null)
                        Client.PostMessage(o => { if (!o.ok) Console.WriteLine("Slack - Failed to send message: " + o.error); }, channel.id, msg, from);
                }
            }
        }
    }
}
