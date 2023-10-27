using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;

using BombServerEmu_MNR.Src.Log;
using BombServerEmu_MNR.Src.DataTypes;
using BombServerEmu_MNR.Src.Services;
using BombServerEmu_MNR.Src.Helpers;
using System.Net;

namespace BombServerEmu_MNR.Src
{
    class Program
    {
        public static List<BombService> Services { get; } = new List<BombService>();

        public static string ClusterUuid { get; } = UUID.GenerateUUID();

        public static bool DisableTLS = false;

        public static string ApiURL = "http://127.0.0.1:10050/";

        private static string ip = "127.0.0.1";

        public static List<GameManagerGame> GamesMatchmaking = new List<GameManagerGame>(); 
        public static int GameIdIncrement = 0;

        static void Main(string[] args)
        {
            Logging.OpenLogFile();
            Logging.RealLog(typeof(Program), "BombServer  Copyright (C) 2021  derole\n" +
                "This program comes with ABSOLUTELY NO WARRANTY! This is free software, and you are welcome to redistribute it under certain conditions\n", LogType.Info);
            CheckArgs(args);
            
            WebApiIntegration.InitContentUpdates();

            Services.Add(new Directory(ip, 10501).Service);
            Services.Add(new Matchmaking(ip, 10510).Service);  //Made up port
            Services.Add(new GameManager(ip, 10505).Service); // Is this supposed to be 10412 actually?
            Services.Add(new GameBrowser(ip, 10412).Service); 
            Services.Add(new PlayGroup(ip, 10514).Service);  //Made up port
            Services.Add(new TextComm(ip, 10513).Service);  //Made up port
            Services.Add(new Stats(ip, 13452).Service);
            Services.Add(new GameServer(ip, 50002).Service);
        }

        static void CheckArgs(string[] args)
        {
            for (int i=0; i<args.Length; i++) {
                switch (args[i]) {
                    case "-loglevel":
                        Logging.logLevel = (LogLevel)Enum.Parse(typeof(LogLevel), args[i + 1]);
                        break;

                    case "-ip":
                        IPAddress Output;
                        if (!IPAddress.TryParse(args[i + 1], out Output))
                        {
                            Logging.RealLog(typeof(Program), $"{args[i + 1]} is not a valid ip address", LogType.Error);
                            Environment.Exit(0);
                        }
                        ip = args[i + 1];
                        break;

                    case "-notls":
                        DisableTLS = true;
                        Logging.RealLog(typeof(Program), "TLS has been disabled", LogType.Info);
                        break;

                    case "-url":
                        ApiURL = args[i + 1];
                        break;
                }
            }
        }
    }
}
