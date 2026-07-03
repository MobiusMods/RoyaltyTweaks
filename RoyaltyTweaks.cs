using RimWorld;
using HarmonyLib;
using Verse;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;

// The namespace is part of the serialized settings contract: RimWorld writes
// <ModSettings Class="TestMod.RoyaltyTweaksSettings"> into every user's config
// file, so renaming the namespace (or the settings class) makes existing
// installs log a resolve error on startup. It stays TestMod forever.
namespace TestMod
{

	// Token: 0x02000002 RID: 2
	[StaticConstructorOnStartup]
	public static class RoyaltyTweaks
	{
		private static RoyaltyTweaksSettings cachedSettings;

		// Token: 0x06000001 RID: 1 RVA: 0x00002050 File Offset: 0x00000250
		static RoyaltyTweaks()
		{
			cachedSettings = LoadedModManager.GetMod<RoyaltyTweaksMod>().GetSettings<RoyaltyTweaksSettings>();
			new Harmony("com.mobius.royaltytweaks").PatchAll();
		}

		// A "senior royal" for these tweaks: any title above the base rank.
		private static bool IsSeniorRoyal(Pawn pawn)
		{
			RoyalTitleDef def = pawn.royalty?.MostSeniorTitle?.def;
			return def != null && def.seniority > 100;
		}

		// OR of disabledWorkTags across ALL conceited titles the pawn holds (not just
		// the most senior one — a lesser title from a second faction counts too).
		private static WorkTags ConceitedTitleDisabledTags(Pawn pawn)
		{
			WorkTags tags = WorkTags.None;
			if (pawn.royalty != null)
			{
				foreach (RoyalTitle title in pawn.royalty.AllTitlesForReading)
				{
					if (title.conceited)
					{
						tags |= title.def.disabledWorkTags;
					}
				}
			}
			return tags;
		}

		// Passion in ANY of the work type's relevant skills (not just the first).
		private static bool HasPassionInAnyRelevantSkill(Pawn pawn, WorkTypeDef workType, bool onlyMajor)
		{
			List<SkillDef> relevant = workType.relevantSkills;
			if (relevant == null || relevant.Count == 0 || pawn.skills == null)
			{
				return false;
			}
			List<SkillRecord> pawnSkills = pawn.skills.skills;
			for (int i = 0; i < pawnSkills.Count; i++)
			{
				SkillRecord sr = pawnSkills[i];
				if (relevant.Contains(sr.def) && (onlyMajor ? sr.passion == Passion.Major : sr.passion != Passion.None))
				{
					return true;
				}
			}
			return false;
		}

		// Should this otherwise-disabled work type be re-enabled for the pawn?
		// Only work the pawn's conceited TITLE disabled — never work disabled by
		// backstory/traits/genes — and only when they have a passion in it.
		private static bool PassionOverridesDisable(Pawn pawn, WorkTypeDef workType)
		{
			WorkTags royalTags = ConceitedTitleDisabledTags(pawn);
			if (royalTags == WorkTags.None || (workType.workTags & royalTags) == WorkTags.None)
			{
				return false;
			}
			WorkTags storyTags = pawn.story?.DisabledWorkTagsBackstoryTraitsAndGenes ?? WorkTags.None;
			if ((workType.workTags & storyTags) != WorkTags.None)
			{
				return false;
			}
			return HasPassionInAnyRelevantSkill(pawn, workType, cachedSettings.willWorkOnlyMajorPassionSkills);
		}

		// Token: 0x02000005 RID: 5
		[HarmonyPatch(typeof(Pawn_RoyaltyTracker))]
		[HarmonyPatch("CanRequireThroneroom")]
		private static class Pawn_RoyaltyTracker_CanRequireThroneroom_Patch
		{
			// Token: 0x06000007 RID: 7 RVA: 0x000022C0 File Offset: 0x000004C0
			private static bool Prefix(Pawn_RoyaltyTracker __instance, Pawn ___pawn, ref bool __result)
			{
				if (!cachedSettings.throneRoomTweaks || ___pawn.ownership.AssignedThrone != null)
				{
					return true;
				}
				bool flag;
				if (___pawn.IsFreeColonist && __instance.allowRoomRequirements && !QuestUtility.IsQuestLodger(___pawn))
				{
					Map mapHeld = ___pawn.MapHeld;
					flag = (mapHeld != null && mapHeld.IsPlayerHome);
				}
				else
				{
					flag = false;
				}
				__result = flag;
				int highestSeniority = 0;
				bool throneAssigned = false;
				if (__result && __instance != null && __instance.MostSeniorTitle != null && __instance.MostSeniorTitle.def != null)
				{
					foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
					{
						if (pawn != ___pawn && pawn.royalty != null && pawn.royalty.MostSeniorTitle != null && pawn.royalty.MostSeniorTitle.def != null)
						{
							int seniority = pawn.royalty.MostSeniorTitle.def.seniority;
							// throneAssigned must track pawns AT the current max, not just the
							// one that raised it — reset it when the bar rises, and let any
							// pawn tied at the bar set it (order-independent).
							if (seniority > highestSeniority)
							{
								highestSeniority = seniority;
								throneAssigned = pawn.ownership.AssignedThrone != null;
							}
							else if (seniority == highestSeniority && pawn.ownership.AssignedThrone != null)
							{
								throneAssigned = true;
							}
						}
					}
					if (__instance.MostSeniorTitle.def.seniority < highestSeniority)
					{
						bool spouseHasHighTitle = false;
						if (cachedSettings.spouseWantsThroneroom)
						{
							List<DirectPawnRelation> relations = ___pawn.relations?.DirectRelations;
							if (relations != null)
							{
								for (int i = 0; i < relations.Count; i++)
								{
									DirectPawnRelation rel = relations[i];
									if (rel.def == PawnRelationDefOf.Spouse && rel.otherPawn != null && !rel.otherPawn.Dead)
									{
										Pawn spouse = rel.otherPawn;
										if (spouse.royalty?.MostSeniorTitle?.def != null &&
											spouse.royalty.MostSeniorTitle.def.seniority >= highestSeniority)
										{
											spouseHasHighTitle = true;
											break;
										}
									}
								}
							}
						}
						if (!spouseHasHighTitle)
						{
							__result = false;
						}
					}
					if (__instance.MostSeniorTitle.def.seniority == highestSeniority && throneAssigned)
					{
						__result = false;
					}
				}
				return false;
			}
		}

		// Token: 0x02000006 RID: 6
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("CombinedDisabledWorkTags", MethodType.Getter)]
		private static class Pawn_CombinedDisabledWorkTags_Patch
		{
			// Token: 0x06000009 RID: 9 RVA: 0x000024D4 File Offset: 0x000006D4
			private static bool Prefix(Pawn __instance, ref WorkTags __result)
			{
				if (cachedSettings.willWorkPassionSkills)
				{
                    WorkTags workTags = __instance.story?.DisabledWorkTagsBackstoryTraitsAndGenes ?? WorkTags.None;
                    workTags |= __instance.kindDef.disabledWorkTags;

                    #region Royalty Tweaks
                    WorkTags disabledRoyalTags = WorkTags.None;
                    if (IsSeniorRoyal(__instance))
                    {
                        disabledRoyalTags = ConceitedTitleDisabledTags(__instance);
                        if (disabledRoyalTags != WorkTags.None)
                        {
                            bool onlyMajor = cachedSettings.willWorkOnlyMajorPassionSkills;
                            List<WorkTypeDef> allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
                            for (int i = 0; i < allWorkTypes.Count; i++)
                            {
                                WorkTypeDef workType = allWorkTypes[i];
                                if (HasPassionInAnyRelevantSkill(__instance, workType, onlyMajor))
                                {
                                    disabledRoyalTags &= ~workType.workTags;
                                }
                            }
                        }
                    }
                    workTags |= disabledRoyalTags;
					#endregion

                    if (ModsConfig.IdeologyActive && __instance.Ideo != null)
                    {
                        Precept_Role role = __instance.Ideo.GetRole(__instance);
                        if (role != null)
                        {
                            workTags |= role.def.roleDisabledWorkTags;
                        }
                    }
                    if (__instance.health?.hediffSet != null)
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
                    foreach (QuestPart_WorkDisabled item2 in QuestUtility.GetWorkDisabledQuestPart(__instance))
                    {
                        workTags |= item2.disabledWorkTags;
                    }
                    if (__instance.IsMutant)
                    {
                        workTags |= __instance.mutant.Def.workDisables;
                        if (!__instance.mutant.IsPassive)
                        {
                            workTags &= ~WorkTags.Violent;
                        }
                    }
                    __result = workTags;
					return false;
				}
				return true;
			}
		}

		// Token: 0x02000007 RID: 7
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("GetDisabledWorkTypes")]
		private static class GetDisabledWorkTypes_Patch
		{
			private static void Postfix(Pawn __instance, ref List<WorkTypeDef> __result)
			{
				if (!cachedSettings.willWorkPassionSkills || !__instance.IsColonist || !IsSeniorRoyal(__instance))
				{
					return;
				}
				// Copy-on-write filter: only allocate when something is actually
				// re-enabled, and never mutate the list vanilla handed us.
				List<WorkTypeDef> kept = null;
				for (int i = 0; i < __result.Count; i++)
				{
					WorkTypeDef workType = __result[i];
					if (PassionOverridesDisable(__instance, workType))
					{
						if (kept == null)
						{
							kept = new List<WorkTypeDef>(__result.Count);
							for (int j = 0; j < i; j++)
							{
								kept.Add(__result[j]);
							}
						}
					}
					else
					{
						kept?.Add(workType);
					}
				}
				if (kept != null)
				{
					__result = kept;
				}
			}
		}

		// Token: 0x02000008 RID: 8
		[HarmonyPatch(typeof(Pawn))]
		[HarmonyPatch("WorkTypeIsDisabled")]
		private static class WorkTypeIsDisabled_Patch
		{
			private static void Postfix(Pawn __instance, WorkTypeDef w, ref bool __result)
			{
				if (__result && cachedSettings.willWorkPassionSkills && __instance.IsColonist
					&& IsSeniorRoyal(__instance) && PassionOverridesDisable(__instance, w))
				{
					__result = false;
				}
			}
		}

		[HarmonyPatch(typeof(Pawn_RoyaltyTracker))]
		[HarmonyPatch("CanRequireBedroom")]
		private static class Pawn_RoyaltyTracker_CanRequireBedroom_Patch
		{
			private static bool Prefix(ref bool __result)
			{
				if (cachedSettings.disableBedroomRequirements)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(ThoughtWorker_RoyalTitleApparelRequirementNotMet))]
		[HarmonyPatch("CurrentStateInternal")]
		private static class ThoughtWorker_RoyalTitleApparelRequirementNotMet_Patch
		{
			private static void Postfix(ref ThoughtState __result)
			{
				if (cachedSettings.disableApparelRequirements)
				{
					__result = ThoughtState.Inactive;
				}
			}
		}

		[HarmonyPatch(typeof(ThoughtWorker_RoyalTitleApparelMinQualityNotMet))]
		[HarmonyPatch("CurrentStateInternal")]
		private static class ThoughtWorker_RoyalTitleApparelMinQualityNotMet_Patch
		{
			private static void Postfix(ref ThoughtState __result)
			{
				if (cachedSettings.disableApparelRequirements)
				{
					__result = ThoughtState.Inactive;
				}
			}
		}
	}

	// Token: 0x02000003 RID: 3
	public class RoyaltyTweaksSettings : ModSettings
	{
		// Token: 0x06000002 RID: 2 RVA: 0x00002064 File Offset: 0x00000264
		public override void ExposeData()
		{
			Scribe_Values.Look<bool>(ref this.throneRoomTweaks, "throneRoomTweaks", true, true);
			Scribe_Values.Look<bool>(ref this.spouseWantsThroneroom, "spouseWantsThroneroom", true, true);
			Scribe_Values.Look<bool>(ref this.willWorkPassionSkills, "willWorkPassionSkills", true, true);
			Scribe_Values.Look<bool>(ref this.willWorkOnlyMajorPassionSkills, "willWorkOnlyMajorPassionSkills", false, true);
			Scribe_Values.Look<bool>(ref this.disableBedroomRequirements, "disableBedroomRequirements", false, true);
			Scribe_Values.Look<bool>(ref this.disableApparelRequirements, "disableApparelRequirements", false, true);
			base.ExposeData();
		}

		// Field initializers are the REAL fresh-install defaults: RimWorld only calls
		// ExposeData() when a settings file already exists on disk, so the defaults
		// passed to Scribe_Values.Look only apply to keys missing from an old file.
		public bool throneRoomTweaks = true;

		public bool spouseWantsThroneroom = true;

		public bool willWorkPassionSkills = true;

		public bool willWorkOnlyMajorPassionSkills;

		public bool disableBedroomRequirements;

		public bool disableApparelRequirements;
	}
	// Token: 0x02000004 RID: 4
	public class RoyaltyTweaksMod : Mod
	{
		// Token: 0x06000004 RID: 4 RVA: 0x000020C7 File Offset: 0x000002C7
		public RoyaltyTweaksMod(ModContentPack content) : base(content)
		{
			this.settings = base.GetSettings<RoyaltyTweaksSettings>();
		}

		// Token: 0x06000005 RID: 5 RVA: 0x000020DC File Offset: 0x000002DC
		public override void DoSettingsWindowContents(Rect inRect)
		{
			Listing_Standard listingStandard = new Listing_Standard();
			listingStandard.Begin(inRect);
			listingStandard.Label("-=[ " + Translator.Translate("ThroneRoomSettings") + " ]=-", -1f, null);
			listingStandard.CheckboxLabeled(Translator.Translate("ThroneRoomRequiredByHighestCheckboxLabel"), ref this.settings.throneRoomTweaks, Translator.Translate("ThroneRoomRequiredByHighestCheckboxTooltip"));
			if (this.settings.throneRoomTweaks)
			{
				listingStandard.CheckboxLabeled(Translator.Translate("SpouseOptionLabel"), ref this.settings.spouseWantsThroneroom, Translator.Translate("SpouseOptionTooltip"));
			}
			listingStandard.Label((TaggedString)"", -1f, (string)null);
			listingStandard.Label("-=[ " + Translator.Translate("RoyalPassionSkillSettings") + " ]=-", -1f, null);
			listingStandard.CheckboxLabeled(Translator.Translate("PassionSkillsCheckboxLabel"), ref this.settings.willWorkPassionSkills, Translator.Translate("PassionSkillsCheckboxTooltip"));
			if (this.settings.willWorkPassionSkills)
			{
				if (listingStandard.RadioButton(Translator.Translate("MajorAndMinorLabel"), !this.settings.willWorkOnlyMajorPassionSkills, 0f, Translator.Translate("MajorAndMinorTooltip"), null))
				{
					this.settings.willWorkOnlyMajorPassionSkills = false;
				}
				if (listingStandard.RadioButton(Translator.Translate("OnlyMajorLabel"), this.settings.willWorkOnlyMajorPassionSkills, 0f, Translator.Translate("OnlyMajorTooltip"), null))
				{
					this.settings.willWorkOnlyMajorPassionSkills = true;
				}
			}
			listingStandard.Label((TaggedString)"", -1f, (string)null);
			listingStandard.Label("-=[ " + Translator.Translate("RoyalRequirementSettings") + " ]=-", -1f, null);
			listingStandard.CheckboxLabeled(Translator.Translate("DisableBedroomRequirementsLabel"), ref this.settings.disableBedroomRequirements, Translator.Translate("DisableBedroomRequirementsTooltip"));
			listingStandard.CheckboxLabeled(Translator.Translate("DisableApparelRequirementsLabel"), ref this.settings.disableApparelRequirements, Translator.Translate("DisableApparelRequirementsTooltip"));
			listingStandard.End();
			base.DoSettingsWindowContents(inRect);
		}

		// Token: 0x06000006 RID: 6 RVA: 0x000022AE File Offset: 0x000004AE
		public override string SettingsCategory()
		{
			return Translator.Translate("RoyalTweaks");
		}

		// Token: 0x04000006 RID: 6
		private RoyaltyTweaksSettings settings;
	}
}
