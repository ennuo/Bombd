using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Xml;
using System.Net.Http;
using BombServerEmu_MNR.Src.Log;
using BombServerEmu_MNR.Src.DataTypes;

namespace BombServerEmu_MNR.Src.Helpers
{
    class WebApiIntegration {
        static string ContentUpdatesURL = "";

        private static string GetStringFromURL(string URL) {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", ".NET Foundation Repository Reporter");

            string result = "";
            Task.Run(async () => {
                try {
                    result = await client.GetStringAsync(URL);
                }
                catch (Exception e) {
                    Logging.Log(typeof(WebApiIntegration), e.ToString(), LogType.Error);
                }
            }).Wait();

            return result;
        }

        public static void InitContentUpdates() {
            string ContentUpdateXML = "";
            ContentUpdateXML = GetStringFromURL($"{Program.ApiURL}resources/content_update.latest.xml");

            Logging.Log(typeof(WebApiIntegration), ContentUpdateXML, LogType.Info);

            XmlDocument ContentUpdatesResource = new XmlDocument();
            try {
                ContentUpdatesResource.LoadXml(ContentUpdateXML);
            }
            catch (Exception e) {
                Logging.Log(typeof(WebApiIntegration), e.ToString(), LogType.Error);
            }

            try {
                ContentUpdatesURL = ContentUpdatesResource.GetElementsByTagName("request")[0].Attributes["url"].InnerText;
            }
            catch (Exception e) {
                Logging.Log(typeof(WebApiIntegration), e.ToString(), LogType.Error);
            }
        }

        public static List<CircleOfInfluence.Event> GetContentUpdate(string Type)
        {
            var eventList = new List<CircleOfInfluence.Event>();

            XmlDocument ContentUpdate = new XmlDocument();

            ContentUpdate.LoadXml(GetStringFromURL($"{Program.ApiURL}{ContentUpdatesURL}?platform=PS3&content_update_type={Type}"));

            var ServerEventList = new XmlDocument();

            if (ContentUpdate.GetElementsByTagName("data").Count != 0)
            {
                try {
                    ServerEventList.LoadXml(Encoding.UTF8.GetString(Convert.FromBase64String(ContentUpdate.GetElementsByTagName("data")[0].InnerText)));
                }
                catch (Exception e) {
                    Logging.Log(typeof(WebApiIntegration), e.ToString(), LogType.Error);
                }
            }

            foreach (XmlElement serverEvent in ServerEventList.GetElementsByTagName("event"))
            {
                eventList.Add(new CircleOfInfluence.Event
                {
                    Name = serverEvent.Attributes["name"].InnerText,
                    Id = int.Parse(serverEvent.Attributes["id"].InnerText),
                    Laps = int.Parse(serverEvent.Attributes["laps"].InnerText),
                    Description = serverEvent.Attributes["description"].InnerText
                });
            }

            if (eventList.Count == 0)
            {
                eventList.Add(new CircleOfInfluence.Event
                {
                    Name = "T1_ModCircuit",
                    Id = 288,
                    Laps = 3,
                    Description = ""
                });
            }
            
            return eventList;
        }
    }
}