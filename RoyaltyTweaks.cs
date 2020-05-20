﻿using RimWorld;
using HarmonyLib;
using Verse;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;
using System.Runtime.Remoting.Messaging;

namespace TestMod
{
    [StaticConstructorOnStartup]
    public static class RoyaltyTweaks
    {
        static RoyaltyTweaks() //our constructor
        {
            //Harmony.DEBUG = true;
            var harmony = new Harmony("com.mobius.royaltytweaks");

            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(Pawn_RoyaltyTracker))]
        [HarmonyPatch(nameof(Pawn_RoyaltyTracker.CanRequireThroneroom))]
        static class Pawn_RoyaltyTracker_CanRequireThroneroom_Patch
        {
            static bool Prefix(Pawn_RoyaltyTracker __instance, Pawn ___pawn, ref bool __result)
            {
                if (!LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().throneRoomTweaks || ___pawn.ownership.AssignedThrone != null)
                {
                    return true;
                }

                __result = ___pawn.IsFreeColonist && __instance.allowRoomRequirements && !___pawn.IsQuestLodger() && (___pawn.MapHeld?.IsPlayerHome ?? false);

                int highestSeniority = 0;
                bool throneAssigned = false;
                if (__result)
                {
                    if (__instance != null && __instance.MostSeniorTitle != null && __instance.MostSeniorTitle.def != null)
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
                                        if (pawn.royalty.MostSeniorTitle.def.seniority > highestSeniority)
                                        {
                                            highestSeniority = pawn.royalty.MostSeniorTitle.def.seniority;
                                            if (pawn.ownership.AssignedThrone != null) throneAssigned = true;
                                        }
                                    }
                                }
                            }
                        }
                        if (__instance.MostSeniorTitle.def.seniority < highestSeniority)
                        {
                            Pawn spouse = null;
                            if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().spouseWantsThroneroom)
                            {
                                spouse = ___pawn.GetSpouse();
                            }
                            if (spouse == null || spouse.royalty == null || spouse.royalty.MostSeniorTitle == null || spouse.royalty.MostSeniorTitle.def.seniority < highestSeniority)
                            {
                                __result = false;
                            }
                        }
                        if (__instance.MostSeniorTitle.def.seniority == highestSeniority && throneAssigned)
                        {
                            __result = false;
                        }
                    }
                }
                return false;
            }
        }

        #region Speech Inspiration
        private static bool PositiveOutcome(ThoughtDef outcome)
        {
            return outcome == ThoughtDefOf.EncouragingSpeech || outcome == ThoughtDefOf.InspirationalSpeech;
        }

        // Token: 0x0600319B RID: 12699 RVA: 0x00113CE0 File Offset: 0x00111EE0
        [HarmonyPatch(typeof(LordJob_Joinable_Speech))]
        [HarmonyPatch("ApplyOutcome")]
        static class LordJob_JoinableSpeech_ApplyOutcome_Patch
        {
            static bool Prefix(LordJob_Joinable_Speech __instance, Pawn ___organizer, float progress)
            {
                if (!LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().speechesInspire)
                {
                    return true;
                }

                if (progress < 0.5f)
                {
                    return true;
                }
                ThoughtDef key = LordJob_Joinable_Speech.OutcomeThoughtChances.RandomElementByWeight(delegate (KeyValuePair<ThoughtDef, float> t)
                {
                    if (!PositiveOutcome(t.Key))
                    {
                        return LordJob_Joinable_Speech.OutcomeThoughtChances[t.Key];
                    }
                    return LordJob_Joinable_Speech.OutcomeThoughtChances[t.Key] * ___organizer.GetStatValue(StatDefOf.SocialImpact, true) * progress;
                }).Key;
                foreach (Pawn pawn in __instance.lord.ownedPawns)
                {
                    if (pawn != ___organizer && ___organizer.Position.InHorDistOf(pawn.Position, 18f))
                    {
                        pawn.needs.mood.thoughts.memories.TryGainMemory(key, ___organizer);
                        if (key == ThoughtDefOf.InspirationalSpeech && Rand.Value <= LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().speechesInspireChance)
                        {
                            InspirationDef randomAvailableInspirationDef = (from x in DefDatabase<InspirationDef>.AllDefsListForReading
                                                                            where x.Worker.InspirationCanOccur(pawn)
                                                                            select x).RandomElementByWeightWithFallback((InspirationDef x) => x.Worker.CommonalityFor(pawn), null);
                            if (randomAvailableInspirationDef != null)
                            {
                                pawn.mindState.inspirationHandler.TryStartInspiration_NewTemp(randomAvailableInspirationDef);
                            }
                        }

                    }
                }
                TaggedString taggedString = "LetterFinishedSpeech".Translate(___organizer.Named("ORGANIZER")).CapitalizeFirst() + " " + ("Letter" + key.defName).Translate();
                if (progress < 1f)
                {
                    taggedString += "\n\n" + "LetterSpeechInterrupted".Translate(progress.ToStringPercent(), ___organizer.Named("ORGANIZER"));
                }
                Find.LetterStack.ReceiveLetter(key.stages[0].LabelCap, taggedString, PositiveOutcome(key) ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent, ___organizer, null, null, null, null);
                Ability ability = ___organizer.abilities.GetAbility(AbilityDefOf.Speech);
                RoyalTitle mostSeniorTitle = ___organizer.royalty.MostSeniorTitle;
                if (ability != null && mostSeniorTitle != null)
                {
                    ability.StartCooldown(mostSeniorTitle.def.speechCooldown.RandomInRange);
                }
                return false;
            }
        }

        #endregion



        #region Conceited Disabled Work
        [HarmonyPatch(typeof(Pawn))]
        [HarmonyPatch("CombinedDisabledWorkTags", MethodType.Getter)]
        static class Pawn_CombinedDisabledWorkTags_Patch
        {
            private static string nameof(Action<float> applyOutcome)
            {
                throw new NotImplementedException();
            }

            static bool Prefix(Pawn __instance, ref WorkTags __result)
            {
                if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkPassionSkills)
                {
                    WorkTags workTags = (__instance.story != null) ? __instance.story.DisabledWorkTagsBackstoryAndTraits : WorkTags.None;
                    WorkTags disabledRoyalTags = WorkTags.None;

                    if (__instance.royalty?.MostSeniorTitle?.def?.seniority > 100)
                    {
                        foreach (RoyalTitle royalTitle in __instance.royalty.AllTitlesForReading)
                        {
                            if (royalTitle.conceited)
                            {
                                disabledRoyalTags |= royalTitle.def.disabledWorkTags;
                            }
                        }



                        var namelist = new List<string>();
                        if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkOnlyMajorPassionSkills)
                        {
                            namelist = __instance.skills.skills.Where(a => a.passion == Passion.Major).Select(b => b.def.defName).ToList();
                        }
                        else
                        {
                            namelist = __instance.skills.skills.Where(a => a.passion != Passion.None).Select(b => b.def.defName).ToList();
                        }

                        var list = DefDatabase<WorkTypeDef>.AllDefsListForReading;
                        if (namelist != null && namelist.Any())
                        {
                            var worklist = list.Where(a => a.relevantSkills != null && a.relevantSkills.Any() && namelist.Contains(a.relevantSkills.First().defName)).Select(b => b.workTags);

                            if (worklist != null && worklist.Any())
                            {
                                foreach (var worklistItem in worklist)
                                {
                                    disabledRoyalTags &= ~worklistItem;
                                }

                            }
                        }


                    }
                    workTags |= disabledRoyalTags;

                    if (__instance.health != null && __instance.health.hediffSet != null)
                    {
                        foreach (Hediff hediff in __instance.health.hediffSet.hediffs)
                        {
                            HediffStage curStage = hediff.CurStage;
                            if (curStage != null)
                            {
                                workTags |= curStage.disabledWorkTags;
                            }
                        }
                    }
                    __result = workTags;

                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Pawn))]
        [HarmonyPatch(nameof(Pawn.GetDisabledWorkTypes))]
        static class GetDisabledWorkTypes_Patch
        {
            static void Postfix(Pawn __instance, ref List<WorkTypeDef> __result, bool permanentOnly = false)
            {
                if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkPassionSkills
                    && __instance.IsColonist
                    && __instance.royalty?.MostSeniorTitle?.def?.seniority > 100
                    && __instance.royalty.MostSeniorTitle.conceited)
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
                        }
                        else
                        {
                            // Will work all passion skills
                            if (!__instance.skills.skills.Any(a => a.passion != Passion.None && skills.Contains(a.def)))
                            {
                                removeList.Add(workType);
                            }
                        }
                    }
                    if (!removeList.NullOrEmpty())
                    {
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
                if (__result && LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkPassionSkills
                    && __instance.IsColonist
                     && __instance.royalty?.MostSeniorTitle?.def?.seniority > 100
                     && __instance.royalty.MostSeniorTitle.conceited)
                {
                    var skills = w.relevantSkills;

                    if (LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>().willWorkOnlyMajorPassionSkills)
                    {
                        // Work only major passion skills
                        if (__instance.skills.skills.Any(a => a.passion == Passion.Major && skills.Contains(a.def)))
                        {
                            __result = false;
                        }
                    }
                    else
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
        #endregion
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
        public bool speechesInspire;
        public float speechesInspireChance;
        public float authorityFallPerDayMultiplier;
        //public float exampleFloat = 200f;
        //public List<Pawn> exampleListOfPawns = new List<Pawn>();

        /// <summary>
        /// The part that writes our settings to file. Note that saving is by ref.
        /// </summary>
        public override void ExposeData()
        {
            Scribe_Values.Look(ref throneRoomTweaks, "throneRoomTweaks", true, true);
            Scribe_Values.Look(ref spouseWantsThroneroom, "spouseWantsThroneroom", true, true);
            Scribe_Values.Look(ref willWorkPassionSkills, "willWorkPassionSkills", true, true);
            Scribe_Values.Look(ref willWorkOnlyMajorPassionSkills, "willWorkOnlyMajorPassionSkills", false, true);

            Scribe_Values.Look(ref speechesInspire, "speechesInspire", true, true);
            Scribe_Values.Look(ref speechesInspireChance, "inspiredChance", 0.5f, true);
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
            listingStandard.Label("-=[ " + "ThroneRoomSettings".Translate() + " ]=-");
            listingStandard.CheckboxLabeled("ThroneRoomRequiredByHighestCheckboxLabel".Translate(), ref settings.throneRoomTweaks, "ThroneRoomRequiredByHighestCheckboxTooltip".Translate());
            if (settings.throneRoomTweaks)
            {
                listingStandard.CheckboxLabeled("SpouseOptionLabel".Translate(), ref settings.spouseWantsThroneroom, "SpouseOptionTooltip".Translate());
            }
            listingStandard.Label("");
            listingStandard.Label("-=[ " + "RoyalPassionSkillSettings".Translate() + " ]=-");
            listingStandard.CheckboxLabeled("PassionSkillsCheckboxLabel".Translate(), ref settings.willWorkPassionSkills, "PassionSkillsCheckboxTooltip".Translate());
            if (settings.willWorkPassionSkills)
            {
                if (listingStandard.RadioButton_NewTemp("MajorAndMinorLabel".Translate(), settings.willWorkOnlyMajorPassionSkills == false, 0f, "MajorAndMinorTooltip".Translate()))
                {
                    settings.willWorkOnlyMajorPassionSkills = false;

                }
                if (listingStandard.RadioButton_NewTemp("OnlyMajorLabel".Translate(), settings.willWorkOnlyMajorPassionSkills == true, 0f, "OnlyMajorTooltip".Translate()))
                {
                    settings.willWorkOnlyMajorPassionSkills = true;

                }
            }
            listingStandard.Label("");
            listingStandard.Label("-=[ " + "SpeechSettings".Translate() + " ]=-");
            listingStandard.CheckboxLabeled("SpeechSettingsCheckboxLabel".Translate(), ref settings.speechesInspire, "SpeechSettingsCheckboxTooltip".Translate());
            if (settings.speechesInspire)
            {
                listingStandard.Label("SpeechInspireChanceLabel".Translate() + ": " + String.Format("{0:P2}", settings.speechesInspireChance), -1f, "SpeechInspireChanceTooltip".Translate());
                //settings.speechesInspireChance = Widgets.HorizontalSlider(new Rect(10, 10, 100, 10), settings.speechesInspireChance, 0.1f, 1f);                            
                settings.speechesInspireChance = listingStandard.Slider(settings.speechesInspireChance, 0.1f, 1f);
            }         

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
            return "RoyalTweaks".Translate();
        }
    }
}