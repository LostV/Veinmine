using BepInEx;
using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace Veinmine
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInProcess("valheim.exe")]
    public class VeinminePlugin : BaseUnityPlugin
    {
        private const string ModGUID = "com.LostV.veinmine";
        private const string ModName = "Veinmine";
        private const string ModVersion = "1.0.0";

        private readonly Harmony _harmony = new Harmony(ModGUID);

        public static VeinminePlugin Instance;
        public static ManualLogSource Log;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _harmony.PatchAll();
            Log.LogInfo($"{ModName} v{ModVersion} has loaded!");
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        [HarmonyPatch(typeof(MineRock5), "Damage")]
        static class MineRock5_Damage_Patch
        {
            static void Prefix(MineRock5 __instance, HitData hit)
            {
                if (!Input.GetKey(KeyCode.LeftAlt)) return;
                if (hit.GetAttacker() != Player.m_localPlayer) return;
                VeinMiner.AttemptVeinMine(__instance, hit);
            }
        }

        [HarmonyPatch(typeof(MineRock), "Damage")]
        static class MineRock_Damage_Patch
        {
            static void Prefix(MineRock __instance, HitData hit)
            {
                if (!Input.GetKey(KeyCode.LeftAlt)) return;
                if (hit.GetAttacker() != Player.m_localPlayer) return;
                VeinMiner.AttemptVeinMine(__instance, hit);
            }
        }

        [HarmonyPatch(typeof(Destructible), "Damage")]
        static class Destructible_Damage_Patch
        {
            static void Prefix(Destructible __instance, HitData hit)
            {
                if (!Input.GetKey(KeyCode.LeftAlt)) return;
                if (hit.GetAttacker() != Player.m_localPlayer) return;
                VeinMiner.AttemptVeinMine(__instance, hit);
            }
        }
    }

    public static class VeinMiner
    {
        private static bool isMining = false;

        public static void AttemptVeinMine(Component origin, HitData hit)
        {
            if (isMining) return;
            isMining = true;

            try
            {
                // Validate player
                if (hit.GetAttacker() != Player.m_localPlayer) return;

                // BFS State
                Queue<Collider> toSearch = new Queue<Collider>();
                HashSet<Collider> visited = new HashSet<Collider>();
                List<KeyValuePair<Component, HitData>> targetsToStrike = new List<KeyValuePair<Component, HitData>>();

                // Initialize with detection of the vein type
                string veinName = GetVeinName(origin);
                Type veinType = origin.GetType();

                if (hit.m_hitCollider != null)
                {
                    toSearch.Enqueue(hit.m_hitCollider);
                    visited.Add(hit.m_hitCollider);
                }
                else
                {
                    // Fallback using origin position if collider is missing (unlikely)
                    Collider[] initial = Physics.OverlapSphere(origin.transform.position, 1.0f);
                    foreach(var c in initial) 
                    {
                         if(c.GetComponentInParent(veinType) == origin) 
                         {
                             toSearch.Enqueue(c);
                             visited.Add(c);
                         }
                    }
                }

                // Tuning
                float searchRadius = 3.0f; // Increased for finding neighbor chunks with gaps
                int maxRecursion = 2000; // Increased limit
                int recursed = 0;

                while (toSearch.Count > 0 && recursed < maxRecursion)
                {
                    Collider current = toSearch.Dequeue();
                    recursed++;

                    // Add neighbors
                    Collider[] neighbors = Physics.OverlapSphere(current.bounds.center, searchRadius);
                    foreach (var neighbor in neighbors)
                    {
                        if (visited.Contains(neighbor)) continue;

                        // Check component match
                        Component comp = neighbor.GetComponentInParent(veinType);
                        if (comp != null)
                        {
                            // Check name match (prevent jumping from Copper to Stone)
                            if (GetVeinName(comp) == veinName)
                            {
                                visited.Add(neighbor);
                                toSearch.Enqueue(neighbor);

                                // Prepare hit
                                HitData newHit = hit.Clone();
                                newHit.m_damage = hit.m_damage;
                                newHit.m_point = neighbor.transform.position;
                                newHit.m_hitCollider = neighbor;
                                
                                targetsToStrike.Add(new KeyValuePair<Component, HitData>(comp, newHit));
                            }
                        }
                    }
                }

                Logger.LogInfo($"VeinMine: Found {targetsToStrike.Count} connected rocks via BFS.");

                // Execute Damage
                foreach (var pair in targetsToStrike)
                {
                    Component target = pair.Key;
                    HitData strike = pair.Value;

                    if (target is MineRock5 r5) r5.Damage(strike);
                    else if (target is MineRock r) r.Damage(strike);
                    else if (target is Destructible d) d.Damage(strike);
                }
            }
            finally
            {
                isMining = false;
            }
        }

        private static string GetVeinName(Component c)
        {
             // return filter name, usually the prefab name?
             // Using Object Name might be enough (e.g. rock4_copper(Clone))
             // Stripping (Clone) might be safer.
             string name = c.gameObject.name;
             if (name.EndsWith("(Clone)")) name = name.Substring(0, name.Length - 7);
             return name;
        }

        // Logger helper access
        private static ManualLogSource Logger => VeinminePlugin.Log;
    }
}


