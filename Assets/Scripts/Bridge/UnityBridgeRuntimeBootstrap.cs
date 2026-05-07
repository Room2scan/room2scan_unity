using UnityEngine;

namespace Room2Scan.Bridge
{
    public static class UnityBridgeRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureBridgeExists()
        {
            UnityBridge.GetOrCreateInstance();
        }
    }
}
