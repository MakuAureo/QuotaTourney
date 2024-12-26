using HarmonyLib;
using UnityEngine;
using static QuotaTournament.QuotaTournament;

namespace QuotaTourney.Patches
{
    [HarmonyPatch(typeof(StartOfRound))]
    internal class StartOfRoundPatch
    {
        [HarmonyPatch("SetShipReadyToLand")]
        [HarmonyPostfix]
        static void SetShipReadyToLandPostPatch(ref StartOfRound __instance)
        {
            if (__instance.overrideRandomSeed)
            {
                if (TimeOfDay.Instance.daysUntilDeadline == 0)
                {
                    RoundManager.Instance.DespawnPropsAtEndOfRound(true);
                    __instance.ResetShip();
                    __instance.profitQuotaMonitorText.text = $"TOTAL SCORE:\n  ${GetScore().ToString()}";

                    ResetScore();

                    if (QuotaTournament.QuotaTournament.GetMoonFrequencyValue() == "quota")
                        __instance.ChangeLevelServerRpc((__instance.overrideSeedNumber ^ 0b10110001101110001100010111) % 13,
                            Object.FindObjectOfType<Terminal>().groupCredits);
                }

                if (QuotaTournament.QuotaTournament.GetMoonFrequencyValue() == "day")
                    __instance.ChangeLevelServerRpc((__instance.overrideSeedNumber ^ 0b10110001101110001100010111) % 13,
                        Object.FindObjectOfType<Terminal>().groupCredits);

                Debug.Log($"Next seed: {__instance.overrideSeedNumber}");
            }
        }

        [HarmonyPatch("StartGame")]
        [HarmonyPrefix]
        static void StartGamePrePatch(ref StartOfRound __instance)
        {
            if (TimeOfDay.Instance.daysUntilDeadline == 1 && __instance.overrideRandomSeed)
            {
                __instance.isChallengeFile = true;
            }
        }

        [HarmonyPatch("ShipHasLeft")]
        [HarmonyPostfix]
        static void ShipHasLeftPostPatch(ref StartOfRound __instance)
        {
            if (__instance.overrideRandomSeed)
            {
                do
                    __instance.overrideSeedNumber = NextSeed(__instance.overrideSeedNumber);
                while ((__instance.overrideSeedNumber ^ 0b10110001101110001100010111) % 13 == 11 ||
                       (__instance.overrideSeedNumber ^ 0b10110001101110001100010111) % 13 == 3);

                __instance.randomMapSeed = NextSeed(__instance.randomMapSeed);
            }
            __instance.isChallengeFile = false;
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
            SetScore(__instance.GetValueOfAllScrap(true, false));
            __instance.profitQuotaMonitorText.text = $"CURRENT SCORE:\n  ${GetScore().ToString()}";

            TerminalPatch.ForceItemSales(TimeOfDay.Instance.daysUntilDeadline);
        }

        public static int NextSeed(int seed)
        {
            return new System.Random(seed + 84936687).Next(1,100000000);
        }
    }
}
