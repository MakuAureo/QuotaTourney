using System;
using HarmonyLib;
using UnityEngine;
using static QuotaTournament.QuotaTournament;
using Object = UnityEngine.Object;

namespace QuotaTourney.Patches
{
    [HarmonyPatch(typeof(StartOfRound))]
    internal class StartOfRoundPatch
    {
        [HarmonyPatch("SetShipReadyToLand")]
        [HarmonyPostfix]
        static void SetShipReadyToLandPostPatch(ref StartOfRound __instance)
        {
            if (seedHasBeenSet)
            {
                if (TimeOfDay.Instance.daysUntilDeadline == 0)
                {
                    RoundManager.Instance.DespawnPropsAtEndOfRound(true);
                    __instance.ResetShip();
                    __instance.profitQuotaMonitorText.text = $"TOTAL SCORE:\n  ${GetScore().ToString()}";

                    ResetScore();
                    ResetAllowedMoonList();

                    if (QuotaTournament.QuotaTournament.GetMoonFrequencyValue() == "quota")
                        __instance.ChangeLevelServerRpc(GetAllowedMoonList()[__instance.overrideSeedNumber % GetAllowedMoonList().Count],
                            Object.FindObjectOfType<Terminal>().groupCredits);
                }

                if (QuotaTournament.QuotaTournament.GetMoonFrequencyValue() == "day")
                    __instance.ChangeLevelServerRpc(GetAllowedMoonList()[__instance.overrideSeedNumber % GetAllowedMoonList().Count],
                        Object.FindObjectOfType<Terminal>().groupCredits);

                Debug.Log($"Next seed: {__instance.overrideSeedNumber}");
            }
        }

        [HarmonyPatch("StartGame")]
        [HarmonyPrefix]
        static void StartGamePrePatch(ref StartOfRound __instance)
        {
            if (TimeOfDay.Instance.daysUntilDeadline == 1 && seedHasBeenSet)
            {
                __instance.isChallengeFile = true;
            }
        }

        [HarmonyPatch("ShipHasLeft")]
        [HarmonyPostfix]
        static void ShipHasLeftPostPatch(ref StartOfRound __instance)
        {
            if (seedHasBeenSet)
            {
                __instance.overrideSeedNumber = NextSeed(__instance.overrideSeedNumber);
                __instance.randomMapSeed = NextSeed(__instance.randomMapSeed);
                __instance.isChallengeFile = false;
            }
        }

        [HarmonyPatch("ChangeLevel")]
        [HarmonyPrefix]
        static void ChangeLevelPrePatch(ref StartOfRound __instance)
        {
            Debug.Log($"Current random seed: {__instance.randomMapSeed}");
            __instance.SetPlanetsWeather();
        }

        [HarmonyPatch("PassTimeToNextDay")]
        [HarmonyPostfix]
        static void PassTimeToNextDayPostPatch(ref StartOfRound __instance)
        {
            if (seedHasBeenSet)
            {
                if (__instance.livingPlayers == 0)
                    LoseScoreToWipe();
                else
                    AddScore(__instance.GetValueOfAllScrap(true, true));

                __instance.profitQuotaMonitorText.text = $"CURRENT SCORE:\n  ${GetScore().ToString()}";
                TerminalPatch.ForceItemSales(TimeOfDay.Instance.daysUntilDeadline);
            }
        }

        public static int NextSeed(int seed)
        {
            return new System.Random(Math.Abs(seed + 84936687)).Next(1, seedMax);
        }
    }
}
