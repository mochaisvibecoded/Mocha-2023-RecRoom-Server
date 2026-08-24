using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mocha2023.Classes
{
    public class ServerConfig
    {
        public static object Bracket => new List<object>();
        public static string BaseURL = "https://playmocha.xyz";
        public static int GameVersion = 20230406;

        public const string PhotonRealtimeAppId =
            "c73b99b5-eba7-4152-90c2-b3f7bac5fa00";

        public const string PhotonVoiceAppId =
            "88ed1211-b61c-451c-90f6-9e494ce7ef5f";

        public const string PhotonRegion = "us";
        public const string PhotonAppVersion = "20230427_prod";
        public static readonly string PhotonCustomAuthSecret =
            Program.LoadLocalSetting("PHOTON_CUSTOM_AUTH_SECRET");

        public static bool PhotonEnabled =>
            !string.IsNullOrWhiteSpace(PhotonRealtimeAppId);

        public static readonly Version MinModVersion = new Version(1, 0, 5);
    }
}
