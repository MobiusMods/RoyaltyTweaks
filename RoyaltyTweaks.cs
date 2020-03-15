using RimWorld;
using HarmonyLib;
using Verse;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;

namespace TestMod
{
    [StaticConstructorOnStartup]
    public static class RoyaltyTweaks
    {        
        static RoyaltyTweaks() //our constructor
        {
            var harmony = new Harmony("com.mobius.royaltytweaks");
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(Pawn_RoyaltyTracker))]
        [HarmonyPatch(nameof(Pawn_RoyaltyTracker.CanRequireThroneroom))]
        static class Pawn_RoyaltyTracker_CanRequireThroneroom_Patch
        {
            static bool Prefix(Pawn_RoyaltyTracker __instance, Pawn ___pawn,  ref bool __result)
            {
                if(!LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().throneRoomTweaks || ___pawn.ownership.AssignedThrone != null)
                {
                    return true;
                }

                __result = ___pawn.IsFreeColonist && __instance.allowRoomRequirements && !___pawn.IsQuestLodger();
                int highestSeniority = 0;
                if (__result)
                {
                    if(__instance != null && __instance.MostSeniorTitle != null && __instance.MostSeniorTitle.def != null)
                    {
                        List<Map> maps = Find.Maps;
                        for (int i = 0; i < maps.Count; i++)
                        {
                            if (maps[i].IsPlayerHome)
                            {
                                foreach (Pawn pawn in maps[i].mapPawns.FreeColonistsSpawned)
                                {                                    
                                    if (pawn != ___pawn && pawn.royalty != null && pawn.royalty.MostSeniorTitle != null && pawn.royalty.MostSeniorTitle.def != null)
                                    {
                                        if(pawn.royalty.MostSeniorTitle.def.seniority > highestSeniority)
                                        {
                                            highestSeniority = pawn.royalty.MostSeniorTitle.def.seniority;
                                        }
                                    }
                                }
                            }
                        }
                        if (__instance.MostSeniorTitle.def.seniority < highestSeniority)
                        {
                            Pawn spouse = null;
                            if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().spouseWantsThroneroom){
                                spouse = ___pawn.GetSpouse();
                            }                            
                            if (spouse == null || spouse.royalty == null || spouse.royalty.MostSeniorTitle == null || spouse.royalty.MostSeniorTitle.def.seniority < highestSeniority)
                            {
                                __result = false;
                            }                                                        
                        }                       
                    }
                }               
                return false;
            }

            //public List<WorkTypeDef> GetDisabledWorkTypes(bool permanentOnly = false)
            [HarmonyPatch(typeof(Pawn))]
            [HarmonyPatch(nameof(Pawn.GetDisabledWorkTypes))]
            static class GetDisabledWorkTypes_Patch
            {
                static void Postfix(Pawn __instance,  ref List<WorkTypeDef> __result, bool permanentOnly = false)
                {                  
                    if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkPassionSkills && __instance.royalty != null)
                    {
                        var removeList = new List<WorkTypeDef>();
                        foreach (var workType in __result)
                        {
                            var skills = workType.relevantSkills;

                            if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkOnlyMajorPassionSkills)
                            {
                                // Will only work Major
                                if (!__instance.skills.skills.Any(a => a.passion == Passion.Major && skills.Contains(a.def)))
                                {
                                    removeList.Add(workType);
                                }
                            } else
                            {
                                // Will work all passion skills
                                if (!__instance.skills.skills.Any(a => a.passion != Passion.None && skills.Contains(a.def)))
                                {
                                    removeList.Add(workType);
                                }
                            }                           
                        }
                        if (!removeList.NullOrEmpty()) {
                            __result = removeList;
                        }
                    }
                }
            }

            [HarmonyPatch(typeof(Pawn))]
            [HarmonyPatch(nameof(Pawn.WorkTypeIsDisabled))]
            static class WorkTypeIsDisabled_Patch
            {
                static void Postfix(Pawn __instance, WorkTypeDef w, ref bool __result)
                {
                    // __result = true means the work is disabled and to check further now.
                    if (__result && LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkPassionSkills && __instance.royalty != null)
                    {
                        var skills = w.relevantSkills;

                        if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkOnlyMajorPassionSkills)
                        {
                            // Work only major passion skills
                            if (__instance.skills.skills.Any(a => a.passion == Passion.Major && skills.Contains(a.def)))
                            {
                                __result = false;
                            }
                        } else
                        {
                            // Work all passion skills.
                            if (__instance.skills.skills.Any(a => a.passion != Passion.None && skills.Contains(a.def)))
                            {
                                __result = false;
                            }
                        }                    
                    }
                }
            }

            // 		public WorkTags CombinedDisabledWorkTags
            [HarmonyPatch(typeof(Pawn))]
            [HarmonyPatch("CombinedDisabledWorkTags", MethodType.Getter)]
            static class Pawn_CombinedDisabledWorkTags_Patch
            {
                static bool Prefix(Pawn __instance, ref WorkTags __result)
                {
                    if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkPassionSkills && __instance.royalty != null)
                    {
                        var list = DefDatabase<WorkTypeDef>.AllDefsListForReading;

                        var namelist = new List<string>();
                        if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkOnlyMajorPassionSkills)
                        {
                            namelist = __instance.skills.skills.Where(a => a.passion == Passion.Major).Select(b => b.def.defName).ToList();
                        }
                        else
                        {
                            namelist = __instance.skills.skills.Where(a => a.passion != Passion.None).Select(b => b.def.defName).ToList();
                        }


                        if (namelist != null && namelist.Any())
                        {
                            var worklist = list.Where(a => a.relevantSkills != null && a.relevantSkills.Any() && namelist.Contains(a.relevantSkills.First().defName)).Select(b => b.workTags);

                            if (worklist != null && worklist.Any())
                            {
                                foreach (var worklistItem in worklist)
                                {
                                    __result &= ~worklistItem;
                                }
                            }
                        }

                        return false;
                    }
                    return true;
                }
            }
        }
    }

    public class RoyaltyTweaksSettings : ModSettings
    {
        /// <summary>
        /// The three settings our mod has.
        /// </summary>
        ///
        public bool throneRoomTweaks;
        public bool spouseWantsThroneroom;
        public bool willWorkPassionSkills;
        public bool willWorkOnlyMajorPassionSkills;
        //public float exampleFloat = 200f;
        //public List<Pawn> exampleListOfPawns = new List<Pawn>();

        /// <summary>
        /// The part that writes our settings to file. Note that saving is by ref.
        /// </summary>
        public override void ExposeData()
        {
            Scribe_Values.Look(ref throneRoomTweaks, "spouseWantsThroneroom", true);
            Scribe_Values.Look(ref spouseWantsThroneroom, "spouseWantsThroneroom", true);
            Scribe_Values.Look(ref willWorkPassionSkills, "willWorkPassionSkills", true);
            Scribe_Values.Look(ref willWorkOnlyMajorPassionSkills, "willWorkOnlyMajorPassionSkills", false);
            //Scribe_Values.Look(ref exampleFloat, "exampleFloat", 200f);
            //Scribe_Collections.Look(ref exampleListOfPawns, "exampleListOfPawns", LookMode.Reference);
            base.ExposeData();
        }
    }
    public class RoyaltyTweaksMod : Mod
    {
        /// <summary>
        /// A reference to our settings.
        /// </summary>
        RoyaltyTweaksSettings settings;

        /// <summary>
        /// A mandatory constructor which resolves the reference to our settings.
        /// </summary>
        /// <param name="content"></param>
        public RoyaltyTweaksMod(ModContentPack content) : base(content)
        {
            this.settings = GetSettings<RoyaltyTweaksSettings>();
        }

        /// <summary>
        /// The (optional) GUI part to set your settings.
        /// </summary>
        /// <param name="inRect">A Unity Rect with the size of the settings window.</param>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.Label("-=[ Throne Room Settings ]=-");
            listingStandard.CheckboxLabeled("Throne Rooms Required Only by Highest Titled Royals", ref settings.throneRoomTweaks, "Only the highest titled noble(s) will require/demand a throne room.");
            if (settings.throneRoomTweaks) { 
                listingStandard.CheckboxLabeled("Royal Spouse Wants Throne Room Too", ref settings.spouseWantsThroneroom, "The royal spouse of the highest titled royal will also demand a throneroom if their rank would normally call for it.");
            }
            listingStandard.Label("");
            listingStandard.Label("-=[ Royal Passion Skill Settings ]=-");
            listingStandard.CheckboxLabeled("Restore Disabled Royal Passion Skills", ref settings.willWorkPassionSkills, "Royals will continue to perform skills they are passionate about even after gaining royal ranks.");
            if (settings.willWorkPassionSkills)
            {
                if (listingStandard.RadioButton("Major and Minor", settings.willWorkOnlyMajorPassionSkills == false, 0f, "Royals will continue to perform both major and minor passion skills that would be disabled. This is the default mod behavior."))
                {
                    settings.willWorkOnlyMajorPassionSkills = false;

                }
                if (listingStandard.RadioButton("Only Major", settings.willWorkOnlyMajorPassionSkills == true, 0f, "Royals will continue to perform only major passion skills that would be disabled. Minor passion skills will be disabled with rank ups."))
                {
                    settings.willWorkOnlyMajorPassionSkills = true;

                }
            }            
            
            //listingStandard.Label("exampleFloatExplanation");
            //settings.exampleFloat = listingStandard.Slider(settings.exampleFloat, 100f, 300f);
            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        /// <summary>
        /// Override SettingsCategory to show up in the list of settings.
        /// Using .Translate() is optional, but does allow for localisation.
        /// </summary>
        /// <returns>The (translated) mod name.</returns>
        public override string SettingsCategory()
        {
            return "Royal Tweaks";
        }
    }
}
