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

namespace BombServerEmu_MNR.Src
{
    class Program
    {
        public static List<BombService> Services { get; } = new List<BombService>();

        public static string ClusterUuid { get; } = UUID.GenerateUUID();

        static void Main(string[] args)
        {
            Logging.OpenLogFile();
            Logging.RealLog(typeof(Program), "BombServer  Copyright (C) 2021  derole\n" +
                "This program comes with ABSOLUTELY NO WARRANTY! This is free software, and you are welcome to redistribute it under certain conditions\n", LogType.Info);
            CheckArgs(args);

            Services.Add(new Directory("127.0.0.1", 10501).Service);

            Services.Add(new Matchmaking("127.0.0.1", 10510).Service);  //Made up port
            Services.Add(new GameManager("127.0.0.1", 10505).Service);
            Services.Add(new GameBrowser("127.0.0.1", 10412).Service);
            Services.Add(new GameServer(50002).Service);

            Services.Add(new TextComm("127.0.0.1", 10513).Service);  //Made up port
            Services.Add(new PlayGroup("127.0.0.1", 10514).Service);  //Made up port
            Services.Add(new Stats("127.0.0.1", 13452).Service);

            // TEST
            //new GameServer(1234);

            //Services.Add(new Directory("127.0.0.1", 11501).Service);

            //Services.Add(new Matchmaking("127.0.0.1", 11510).Service);
            //Services.Add(new GameManager("127.0.0.1", 11511).Service);
            //Services.Add(new GameBrowser("127.0.0.1", 11512).Service);

            //Services.Add(new TextComm("127.0.0.1", 11513).Service);
            //Services.Add(new PlayGroup("127.0.0.1", 11514).Service);
            //Services.Add(new Stats("127.0.0.1", 11515).Service);
        }

        static void CheckArgs(string[] args)
        {
            for (int i=0; i<args.Length; i++) {
                switch (args[i]) {
                    case "-loglevel":
                        Logging.logLevel = (LogLevel)Enum.Parse(typeof(LogLevel), args[i + 1]);
                        break;
                }
            }
        }
    }
}
