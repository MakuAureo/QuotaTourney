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

namespace QuotaTournament
{
    public class ModNetworkBehavior<T>
    {
        private T Value;

        private LNetworkMessage<T> server;
        private LNetworkMessage<T> client;

        public ModNetworkBehavior(string networkName,Action<T, ulong> serverResponse, Action<T> clientResponse)
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
    public class QuotaTournament : BaseUnityPlugin
    {
        private const string modGuid = "OreoM.QuotaTournament";
        private const string modName = "QuotaTournament";
        private const string modVersion = "1.3.5";

        private readonly Harmony harmony = new Harmony(modGuid);

        internal ManualLogSource logger;

        private static ConfigEntry<bool> allowShopBypassAnyDay;

        private static ModNetworkBehavior<int> netSeed;
        private static ModNetworkBehavior<string> netBuy;
        private static ModNetworkBehavior<string> netMoonFrequency;

        void Awake()
        {
            allowShopBypassAnyDay = Config.Bind(
                "General",
                "Allow shop bypass any day",
                true,
                new ConfigDescription(
                    "Allow items and cruiser to be bought directly to ship any day instead of only before first day"));
            
            logger = BepInEx.Logging.Logger.CreateLogSource(modGuid);
            logger.LogInfo($"{modName} v{modVersion} is patching.");
            try
            { 
                harmony.PatchAll(typeof(QuotaTournament));
                harmony.PatchAll(typeof(StartOfRoundPatch));
                harmony.PatchAll(typeof(TerminalPatch));

                logger.LogInfo($"{modName} v{modVersion} is done patching.");
            }
            catch (System.Exception e)
            {
                logger.LogInfo($"{modName} v{modVersion} failed to patch.");
                Debug.LogException(e);
            }

            logger.LogInfo($"{modName} v{modVersion} is creating the Network");
            try
            {
                netSeed = new ModNetworkBehavior<int>("netSeed", SetSeedClientRequest, SetSeedServerRequest);
                netBuy = new ModNetworkBehavior<string>("netBuy", BuyFromShopClientRequest, BuyFromShopServerRequest);
                netMoonFrequency = new ModNetworkBehavior<string>("netMoonFrequency", SetMoonFrequencyClientRequest, SetMoonFrequencyServerRequest);
                netMoonFrequency.SetValue("day");

                logger.LogInfo($"{modName} v{modVersion} is done creating the Network");
            }
            catch (System.Exception e)
            {
                logger.LogInfo($"{modName} v{modVersion} failed to create Network");
                Debug.LogException(e);
            }
        }

        public static void SetMoonFrequency(string config)
        {
            netMoonFrequency.SendServer(config);
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

        public static void SetSeed(int seed)
        {
            while ((seed ^ 0b10110001101110001100010111) % 13 == 11 ||
                   (seed ^ 0b10110001101110001100010111) % 13 == 3)
                seed = StartOfRoundPatch.NextSeed(seed);

            netSeed.SendServer(seed);
        }

        public static void SetSeedClientRequest(int seed, ulong clientID)
        {
            StartOfRound.Instance.ChangeLevelServerRpc(
                (seed ^ 0b10110001101110001100010111) % 13,
                Object.FindObjectOfType<Terminal>().groupCredits = new System.Random(seed + 9114523).Next(900, 1500));

            netSeed.SendClients(seed);
        }

        public static void SetSeedServerRequest(int seed)
        {
            Debug.Log($"Seed set: {seed}");
            netSeed.SetValue(seed);

            StartOfRound startOfRound = StartOfRound.Instance;

            startOfRound.randomMapSeed = (netSeed.GetValue() + 12814523) % 100000000;
            startOfRound.SetPlanetsWeather();

            startOfRound.overrideRandomSeed = true;
            startOfRound.overrideSeedNumber = netSeed.GetValue();

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
    };
}
