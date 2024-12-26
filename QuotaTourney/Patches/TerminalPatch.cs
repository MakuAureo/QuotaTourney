using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static QuotaTournament.QuotaTournament;

namespace QuotaTourney.Patches
{
    [HarmonyPatch(typeof(Terminal))]
    internal class TerminalPatch
    {
        [HarmonyPatch("ParsePlayerSentence")]
        [HarmonyPrefix]
        static void ParsePlayerSentencePrePatch(Terminal __instance)
        {
            if (TimeOfDay.Instance.daysUntilDeadline < 3 || !StartOfRound.Instance.inShipPhase)
                return;

            string str = __instance.screenText.text.Substring(__instance.screenText.text.Length - __instance.textAdded);
            
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in str)
            {
                if (!char.IsPunctuation(c))
                    sb.Append(c);
            }
            str = sb.ToString().ToLower();

            string[] inp = str.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (inp[0] == "seed" && int.TryParse(inp[1], out int overrideSeed))
            {
                if (overrideSeed > 0 && overrideSeed < 100000000)
                {
                    SetSeed(overrideSeed);
                }
            }

            if (inp[0] == "freq")
            {
                if (inp[1] != "day" && inp[1] != "quota")
                    return;

                SetMoonFrequency(inp[1]);
            }
        }

        [HarmonyPatch("LoadNewNodeIfAffordable")]
        [HarmonyPostfix]
        static void LoadNewNodeIfAffordablePostPatch(ref Terminal __instance)
        {
            BuyFromShop("Item");
        }

        [HarmonyPatch("BuyVehicleClientRpc")]
        [HarmonyPostfix]
        static void BuyVehicleClientRpcPostPatch(ref Terminal __instance)
        {
            BuyFromShop("Cruiser");
        }

        public static void ForceItemSales(int force)
        {
            Terminal terminal = Object.FindObjectOfType<Terminal>();

            List<int> itemIndex = new List<int>();
            for (int i = 0; i < terminal.buyableItemsList.Length + terminal.buyableVehicles.Length; i++)
            {
                itemIndex.Add(i);
                itemIndex[i] = i;
            }

            System.Random saleRandom = new System.Random(StartOfRound.Instance.overrideSeedNumber);
            int numSales = 1 + saleRandom.Next(0, terminal.buyableItemsList.Length + terminal.buyableVehicles.Length)/force;
            for (int i = 0; i < numSales; i++)
            {
                int selectedItem = itemIndex[saleRandom.Next(0, itemIndex.Count)];
                int price = 100 - saleRandom.Next(20 * (3 - force), 80)/force;
                price = (int)System.Math.Round((double)price/10.0) * 10;

                terminal.itemSalesPercentages[selectedItem] = price;
                itemIndex.Remove(selectedItem);
            }
        }
    }
}
