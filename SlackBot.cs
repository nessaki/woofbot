using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SlackAPI;
using OpenMetaverse;

namespace BarkBot
{
    public class SlackBot : IRelay
    {
        const string API_KEY = "";
        Program MainProgram;
        public SlackServerInfo Conf;
        Configuration MainConf;
        public SlackSocketClient Client;

        public SlackBot(Program p, SlackServerInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;
        }

        public void Connect()
        {
            Client = new SlackSocketClient(Conf.APIKEY);
            Client.Connect((o) => { });
            Client.OnMessageReceived += Client_OnMessageReceived;
        }

        private void Client_OnMessageReceived(SlackAPI.WebSocketMessages.NewMessage msg)
        {
            if (msg.ok && Conf.Channels.Any(x => x.Value == Client.ChannelLookup[msg.channel].name))
            {
                string channel_name = Client.ChannelLookup[msg.channel].name;
                System.Console.WriteLine("(slack:{0}) {1}: {2}", channel_name, msg.user, msg.text);
                try
                {
                    List<BridgeInfo> bridges = MainConf.Bridges.FindAll((BridgeInfo b) =>
                    {
                        return
                            b.SlackServerConf == Conf &&
                            Conf.Channels[b.SlackChannelID] == channel_name;
                    });

                    foreach (BridgeInfo bridge in bridges)
                    {
                        if (bridge.Bot != null && bridge.GridGroup != UUID.Zero)
                        {
                            GridBot bot = MainProgram.GridBots.Find((GridBot b) => { return b.Conf == bridge.Bot; });
                            if (bot != null)
                            {
                                bot.RelayMessage(bridge,
                                    string.Format("(slack:{0}) {1}", channel_name, msg.user),
                                    msg.text);
                            }
                        }

                        if (bridge.IrcServerConf != null)
                        {
                            IrcBot bot = MainProgram.IrcBots.Find((IrcBot b) => { return b.Conf == bridge.IrcServerConf; });
                            if (bot != null)
                            {
                                bot.RelayMessage(bridge,
                                    string.Format("(slack:{0}) {1}", channel_name, msg.user),
                                    msg.text);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed relaying message: {0}", ex.Message);
                }
            }
        }

        public bool IsConnected
        {
            get { return Client.IsConnected; }
        }

        public void RelayMessage(BridgeInfo bridge, string from, string msg)
        {
            if (Client != null)
            {
                string confChan;
                SlackAPI.Channel channel = null;
                if (Conf.Channels.TryGetValue(bridge.SlackChannelID, out confChan))
                {
                    channel = Client.ChannelLookup.First(x => x.Value.name == confChan).Value;
                }
                if (channel != null)
                {
                    Client.PostMessage((o) => { if (!o.ok) Console.WriteLine("Slack - Failed to send message: " + o.error); }, channel.id, msg, from);
                }
            }
        }
    }
}