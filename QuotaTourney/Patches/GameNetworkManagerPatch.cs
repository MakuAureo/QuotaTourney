using UnityEngine;
using HarmonyLib;
using static QuotaTournament.QuotaTournament;

namespace QuotaTourney.Patches
{
    [HarmonyPatch(typeof(GameNetworkManager))]
    internal class GameNetworkManagerPatch
    {
        [HarmonyPatch("Disconnect")]
        [HarmonyPrefix]
        static void DisconnectPrePatch(ref GameNetworkManager __instance)
        {
            seedHasBeenSet = false;
            ResetAllowedMoonList();
            ResetScore();
        }
    }
}
