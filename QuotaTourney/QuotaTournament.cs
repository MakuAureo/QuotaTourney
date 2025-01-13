using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using QuotaTourney.Patches;
using LethalNetworkAPI;
using Unity.Netcode;
using BepInEx.Configuration;
using System;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.Linq;
using Random = System.Random;
using static TerminalApi.TerminalApi;
using TerminalApi.Classes;
using TerminalApi;

namespace QuotaTournament
{
    enum MoonIDs
    {
        Experimentation,
        Assurance,
        Vow,
        Company,
        March,
        Adamance,
        Rend,
        Dine,
        Offense,
        Titan,
        Artifice,
        Liquidation,
        Embrion
    }

    public class ModNetworkBehavior<T>
    {
        private T Value;

        private LNetworkMessage<T> server;
        private LNetworkMessage<T> client;

        public ModNetworkBehavior(string networkName, Action<T, ulong> serverResponse, Action<T> clientResponse)
        {
            server = LNetworkMessage<T>.Create(networkName, serverResponse);
            client = LNetworkMessage<T>.Connect(networkName, null, clientResponse);
        }

        public void SendClients(T data)
        {
            server.SendClients(data);
        }

        public void SendServer(T data)
        {
            client.SendServer(data);
        }

        public void SetValue(T value)
        {
            Value = value;
        }

        public T GetValue()
        {
            return Value;
        }
    }

    [BepInPlugin(modGuid, modName, modVersion)]
    [BepInDependency("LethalNetworkAPI")]
    [BepInDependency("atomic.terminalapi")]
    public class QuotaTournament : BaseUnityPlugin
    {
        private const string modGuid = "OreoM.QuotaTournament";
        private const string modName = "QuotaTournament";
        private const string modVersion = "1.4.2";

        private readonly Harmony harmony = new Harmony(modGuid);
        private static QuotaTournament Instance;

        internal ManualLogSource internalLogger;

        private static List<int> allowedMoons = new List<int>();

        public const int seedMax = 2147483647;
        public static bool seedHasBeenSet = false;
        public static string terminalInput = "";

        private static ConfigEntry<bool> allowShopBypassAnyDay;
        private static ConfigEntry<bool> speedrunBonus;
        private static ConfigEntry<int> wipeLossPercentage;

        private static ModNetworkBehavior<int> netSeed;
        private static ModNetworkBehavior<int> netScore;
        private static ModNetworkBehavior<string> netBuy;
        private static ModNetworkBehavior<string> netMoonFrequency;
        private static ModNetworkBehavior<string> netAllowedMoons;

        private static class TerminalKeywords
        {
            public static TerminalKeyword seed;
            public static TerminalKeyword freq;
            public static TerminalKeyword ban;
            public static TerminalKeyword blank;
        }
        private static class TerminalNodes
        {
            public static TerminalNode seed;
            public static TerminalNode freq;
            public static TerminalNode ban;
        }

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            //Configs
            allowShopBypassAnyDay = Config.Bind(
                "General",
                "Allow shop bypass any day",
                true,
                new ConfigDescription(
                    "Allow items and cruiser to be bought directly to ship any day instead of only before first day"));
            speedrunBonus = Config.Bind(
                "General",
                "Speedrun Bonus",
                false,
                new ConfigDescription(
                    "Get more score if ship leaves early"));
            wipeLossPercentage = Config.Bind(
                "General",
                "Wipe loss percentage",
                50,
                new ConfigDescription(
                    "What percentage of score is lost if all players die",
                    new AcceptableValueRange<int>(0, 100)));

            internalLogger = BepInEx.Logging.Logger.CreateLogSource(modGuid);
            internalLogger.LogInfo($"{modName} v{modVersion} is patching.");
            //Harmony Patches
            try
            { 
                harmony.PatchAll(typeof(QuotaTournament));
                harmony.PatchAll(typeof(GameNetworkManagerPatch));
                harmony.PatchAll(typeof(StartOfRoundPatch));
                harmony.PatchAll(typeof(TerminalPatch));

                internalLogger.LogInfo($"{modName} v{modVersion} is done patching.");
            }
            catch (System.Exception e)
            {
                internalLogger.LogInfo($"{modName} v{modVersion} failed to patch.");
                Debug.LogException(e);
            }

            internalLogger.LogInfo($"{modName} v{modVersion} is creating the Network");
            //NetworkApi
            try
            {
                netSeed = new ModNetworkBehavior<int>("netSeed", SetSeedClientRequest, SetSeedServerRequest);
                netScore = new ModNetworkBehavior<int>("netScore", SyncScoreClientRequest, SyncScoreServerRequest);
                netBuy = new ModNetworkBehavior<string>("netBuy", BuyFromShopClientRequest, BuyFromShopServerRequest);
                netMoonFrequency = new ModNetworkBehavior<string>("netMoonFrequency", SetMoonFrequencyClientRequest, SetMoonFrequencyServerRequest);
                netMoonFrequency.SetValue("day");
                netAllowedMoons = new ModNetworkBehavior<string>("netAllowedMoons", BanMoonClientRequest, BanMoonServerRequest);
                netAllowedMoons.SetValue("default");

                ResetAllowedMoonList();

                internalLogger.LogInfo($"{modName} v{modVersion} is done creating the Network");
            }
            catch (System.Exception e)
            {
                internalLogger.LogInfo($"{modName} v{modVersion} failed to create Network");
                Debug.LogException(e);
            }

            internalLogger.LogInfo($"{modName} v{modVersion} is implementing terminal commands");
            //Terminal Commands
            try
            {   
                TerminalNodes.seed = CreateTerminalNode("seedNode", true);
                TerminalNodes.freq = CreateTerminalNode("freqNode", true);
                TerminalNodes.ban = CreateTerminalNode("banNode", true);

                TerminalKeywords.blank = CreateTerminalKeyword("whyCanINotGetCustomInputWithoutThis???", true);
                TerminalKeywords.seed = CreateTerminalKeyword("seed", false, TerminalNodes.seed);
                TerminalKeywords.freq = CreateTerminalKeyword("freq", false, TerminalNodes.freq);
                TerminalKeywords.ban = CreateTerminalKeyword("ban", false, TerminalNodes.ban);

                TerminalKeywords.blank.AddCompatibleNoun(TerminalKeywords.seed, TerminalNodes.seed);
                TerminalKeywords.blank.AddCompatibleNoun(TerminalKeywords.freq, TerminalNodes.freq);
                TerminalKeywords.blank.AddCompatibleNoun(TerminalKeywords.ban, TerminalNodes.ban);

                TerminalKeywords.seed.defaultVerb = TerminalKeywords.blank;
                TerminalKeywords.freq.defaultVerb = TerminalKeywords.blank;
                TerminalKeywords.ban.defaultVerb = TerminalKeywords.blank;

                AddTerminalKeyword(TerminalKeywords.seed, new CommandInfo()
                {
                    Title = "Seed [num]",
                    TriggerNode = TerminalNodes.seed,
                    DisplayTextSupplier = SetSeed,
                    Category = "Other",
                    Description = "Type a seed to start the challenge"
                });
                AddTerminalKeyword(TerminalKeywords.freq, new CommandInfo()
                {
                    Title = "Freq [day/quota]",
                    TriggerNode = TerminalNodes.freq,
                    DisplayTextSupplier = SetMoonFrequency,
                    Category = "Other",
                    Description = "Change the frequency with which moon changes"
                });
                AddTerminalKeyword(TerminalKeywords.ban, new CommandInfo()
                {
                    Title = "Ban [moon]",
                    TriggerNode = TerminalNodes.ban,
                    DisplayTextSupplier = BanMoon,
                    Category = "Other",
                    Description = "Ban moon from being choosen"
                });

                internalLogger.LogInfo($"{modName} v{modVersion} is done implementing terminal commands");
            }
            catch (System.Exception e)
            {
                internalLogger.LogInfo($"{modName} v{modVersion} failed to implement terminal commands");
                Debug.LogException(e);
            }
        }

        public static void AddScore(int inputScore)
        {
            float multiplier = (speedrunBonus.Value) ? (2 - TimeOfDay.Instance.normalizedTimeOfDay) : (1);
            netScore.SetValue(netScore.GetValue() + (int)(inputScore*multiplier));
        }

        public static void LoseScoreToWipe()
        {
            netScore.SetValue((int)((float)netScore.GetValue()*(100-wipeLossPercentage.Value)/100f));
        }

        public static void ResetScore()
        {
            netScore.SetValue(0);
        }

        public static int GetScore()
        {
            return netScore.GetValue();
        }

        public static void SyncScore()
        {
            netScore.SendServer(0);
        }
        
        public static void SyncScoreClientRequest(int option, ulong clientID)
        {
            if (option == 0)
                netScore.SendClients(netScore.GetValue());
        }

        public static void SyncScoreServerRequest(int serverScore)
        {
            netScore.SetValue(serverScore);
        }

        public static string SetMoonFrequency()
        {
            if (TimeOfDay.Instance.daysUntilDeadline < 3 || !StartOfRound.Instance.inShipPhase)
                return "Moon frequency can only be changed before starting the quota";

            string[] inp = terminalInput.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            string config = inp[1];
            if (config != "quota" && config != "day")
                return $"{config} is an invalid option";

            netMoonFrequency.SendServer(config);
            return $"Moon will change every {config}";
        }

        public static void SetMoonFrequencyClientRequest(string config, ulong clientID)
        {
            netMoonFrequency.SendClients(config);
        }

        public static void SetMoonFrequencyServerRequest(string config)
        {
            netMoonFrequency.SetValue(config);
        }

        public static string GetMoonFrequencyValue()
        {
            return netMoonFrequency.GetValue();
        }

        public static string SetSeed()
        {
            if (TimeOfDay.Instance.daysUntilDeadline < 3 || !StartOfRound.Instance.inShipPhase)
                return "Seed can only be set before starting the quota";

            string[] inp = terminalInput.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (!int.TryParse(inp[1], out int seed))
                return $"{seed} is not a valid number";

            netSeed.SendServer(seed);
            return $"Seed {seed} is set";
        }

        public static void SetSeedClientRequest(int seed, ulong clientID)
        {
            StartOfRound.Instance.ChangeLevelServerRpc(
                allowedMoons[seed % allowedMoons.Count],
                Object.FindObjectOfType<Terminal>().groupCredits = new System.Random(seed + 9114523).Next(1000, 1600));

            netSeed.SendClients(seed);
        }

        public static void SetSeedServerRequest(int seed)
        {
            Debug.Log($"Seed set: {seed}");
            netSeed.SetValue(seed);
            seedHasBeenSet = true;

            StartOfRound startOfRound = StartOfRound.Instance;

            startOfRound.randomMapSeed = new Random(netSeed.GetValue()).Next(1, seedMax);
            startOfRound.SetPlanetsWeather();

            startOfRound.overrideRandomSeed = true;
            startOfRound.overrideSeedNumber = netSeed.GetValue();

            ResetScore();
            startOfRound.profitQuotaMonitorText.text = $"CURRENT SCORE:\n  ${GetScore().ToString()}";

            TerminalPatch.ForceItemSales(TimeOfDay.Instance.daysUntilDeadline);
        }
        
        public static void BuyFromShop(string buyType)
        {
            if ((allowShopBypassAnyDay.Value || TimeOfDay.Instance.daysUntilDeadline == 3) && StartOfRound.Instance.inShipPhase)
                netBuy.SendServer(buyType);
        }

        public static void BuyFromShopClientRequest(string buyType, ulong clientID)
        {
            Terminal terminal = Object.FindObjectOfType<Terminal>();

            if (buyType.Equals("Item"))
            {
                foreach (int i in terminal.orderedItemsFromTerminal)
                {
                    GameObject boughtItem = Object.Instantiate(
                            terminal.buyableItemsList[i].spawnPrefab,
                            new Vector3((float)i / 7f, 2f, -15f + (float)i / 7f),
                            Quaternion.identity,
                            RoundManager.Instance.spawnedScrapContainer);
                    NetworkObject netBoughtItem = boughtItem.GetComponent<NetworkObject>();
                    netBoughtItem.Spawn();
                }
                netBuy.SendClients(buyType);
            }

            if (buyType.Equals("Cruiser"))
            {
                if (terminal.orderedVehicleFromTerminal != 1 && !(bool)Object.FindObjectOfType<VehicleController>())
                {
                    GameObject boughtCruiser = Object.Instantiate(
                            terminal.buyableVehicles[0].vehiclePrefab,
                            new Vector3(3f, 3f, -21f),
                            new Quaternion(0f, 1f, 0f, 1f),
                            RoundManager.Instance.VehiclesContainer);
                    NetworkObject netCruiser = boughtCruiser.GetComponent<NetworkObject>();
                    netCruiser.Spawn();

                    StartOfRound.Instance.SetMagnetOn(true);
                }
                netBuy.SendClients(buyType);
            }
        }

        public static void BuyFromShopServerRequest(string buyType)
        {
            Terminal terminal = Object.FindObjectOfType<Terminal>();

            if (buyType.Equals("Item"))
            {
                terminal.ClearBoughtItems();
            }

            if (buyType.Equals("Cruiser"))
            {
                StartOfRound.Instance.magnetLever.boolValue = true;

                terminal.orderedVehicleFromTerminal = -1;
                terminal.vehicleInDropship = false;
            }
        }

        public static string BanMoon()
        {
            if (seedHasBeenSet)
                return "Moons can only be banned before setting the seed";

            string[] inp = terminalInput.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            string moon = inp[1];

            string moonName = Enum.GetNames(typeof(MoonIDs)).FirstOrDefault(s => s.StartsWith(moon, StringComparison.OrdinalIgnoreCase));
            if (moonName != null && allowedMoons.Contains((int)Enum.Parse(typeof(MoonIDs), moonName)))
            {
                netAllowedMoons.SendServer(moonName);
                return $"{moonName} has been banned from the list";
            }

            return $"{moon} is an invalid option";
        }

        public static void BanMoonClientRequest(string moonName, ulong clientID)
        {
            netAllowedMoons.SendClients(moonName);
        }

        public static void BanMoonServerRequest(string moonName)
        {
            netAllowedMoons.SetValue("modified");
            allowedMoons.Remove((int)Enum.Parse(typeof(MoonIDs), moonName));
        }

        public static void ResetAllowedMoonList()
        {
            allowedMoons.Clear();
            foreach (int i in Enum.GetValues(typeof(MoonIDs)))
            {
                if (i == 3 || i == 11)
                    continue;

                allowedMoons.Add(i);
            }
            netAllowedMoons.SetValue("default");
        }

        public static List<int> GetAllowedMoonList()
        {
            return allowedMoons;
        }
    };
}
