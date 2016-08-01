﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Chatterino.Common
{
    public static class Badges
    {
        public static ConcurrentDictionary<string, TwitchEmote> FourtfGlobalBadges = new ConcurrentDictionary<string, TwitchEmote>();

        public static void LoadGlobalBadges()
        {
            // fourtf badges
            Task.Run(() =>
            {
                try
                {
                    var request = WebRequest.Create("https://fourtf.com/chatterino/badges.json");
                    using (var response = request.GetResponse())
                    using (var stream = response.GetResponseStream())
                    {
                        JsonParser parser = new JsonParser();
                        dynamic json = parser.Parse(stream);
                        foreach (dynamic badge in json["badges"])
                        {
                            var emote = new TwitchEmote { Url = badge["image"], Tooltip = badge["tooltip"] };
                            foreach (string user in badge["users"])
                            {
                                FourtfGlobalBadges[user] = emote;
                            }
                        }
                    }
                }
                catch { }
            });
        }
    }
}