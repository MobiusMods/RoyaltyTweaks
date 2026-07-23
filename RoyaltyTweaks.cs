using RimWorld;
using HarmonyLib;
using Verse;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

// The namespace is part of the serialized settings contract: RimWorld writes
// <ModSettings Class="Mobius.RoyaltyTweaks.RoyaltyTweaksSettings"> into every
// user's config file. The mod shipped as "TestMod" for years, so the
// RoyaltyTweaksMod constructor migrates old config files in place BEFORE the
// settings are read (see MigrateSettingsNamespace). Any future rename needs
// the same treatment or existing installs log a resolve error on startup.
namespace Mobius.RoyaltyTweaks
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
		// the most senior one - a lesser title from a second faction counts too).
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
		// Only work the pawn's conceited TITLE disabled - never work disabled by
		// backstory/traits/genes - and only when they have a passion in it.
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

		// Highest most-senior-title seniority among the OTHER free colonists, and
		// whether any pawn tied at that bar has a throne assigned.
		private static void HighestOtherTitleSeniority(Pawn pawn, out int highestSeniority, out bool throneAssigned)
		{
			highestSeniority = 0;
			throneAssigned = false;
			foreach (Pawn other in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
			{
				if (other != pawn && other.royalty != null && other.royalty.MostSeniorTitle != null && other.royalty.MostSeniorTitle.def != null)
				{
					int seniority = other.royalty.MostSeniorTitle.def.seniority;
					// throneAssigned must track pawns AT the current max, not just the
					// one that raised it - reset it when the bar rises, and let any
					// pawn tied at the bar set it (order-independent).
					if (seniority > highestSeniority)
					{
						highestSeniority = seniority;
						throneAssigned = other.ownership.AssignedThrone != null;
					}
					else if (seniority == highestSeniority && other.ownership.AssignedThrone != null)
					{
						throneAssigned = true;
					}
				}
			}
		}

		private static bool SpouseHasSeniorityAtLeast(Pawn pawn, int seniority)
		{
			List<DirectPawnRelation> relations = pawn.relations?.DirectRelations;
			if (relations != null)
			{
				for (int i = 0; i < relations.Count; i++)
				{
					DirectPawnRelation rel = relations[i];
					if (rel.def == PawnRelationDefOf.Spouse && rel.otherPawn != null && !rel.otherPawn.Dead)
					{
						Pawn spouse = rel.otherPawn;
						if (spouse.royalty?.MostSeniorTitle?.def != null &&
							spouse.royalty.MostSeniorTitle.def.seniority >= seniority)
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		// The core throne-room tweak, shared by every patch that gates something on
		// throne room requirements: is the requirement waived for this pawn when
		// judged at `seniority`? (The pawn's current title's seniority, or the
		// prospective title's when a promotion quest is being evaluated.) Waived
		// when a higher-titled pawn exists (unless the spouse rule keeps it), or
		// when a pawn tied at the top already has a throne assigned.
		private static bool ThroneroomRequirementWaived(Pawn pawn, int seniority)
		{
			int highestSeniority;
			bool throneAssigned;
			HighestOtherTitleSeniority(pawn, out highestSeniority, out throneAssigned);
			if (seniority < highestSeniority)
			{
				return !(cachedSettings.spouseWantsThroneroom && SpouseHasSeniorityAtLeast(pawn, highestSeniority));
			}
			return seniority == highestSeniority && throneAssigned;
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
				if (__result && __instance.MostSeniorTitle != null && __instance.MostSeniorTitle.def != null
					&& ThroneroomRequirementWaived(___pawn, __instance.MostSeniorTitle.def.seniority))
				{
					__result = false;
				}
				return false;
			}
		}

		// The bestowing-ceremony quest never calls CanRequireThroneroom - it checks
		// the PROSPECTIVE title's throneRoomRequirements directly, in two places:
		// QuestPart_RequirementsToAcceptThroneRoom.CanAccept blocks accepting the
		// quest, and JobDriver_BestowingCeremony.AnalyzeThroneRoom blocks starting
		// the ceremony (both the gizmo and the job call it). Waive both under the
		// same rule as the patch above, judged at the prospective title's seniority.
		[HarmonyPatch(typeof(QuestPart_RequirementsToAcceptThroneRoom))]
		[HarmonyPatch("CanAccept")]
		private static class QuestPart_RequirementsToAcceptThroneRoom_CanAccept_Patch
		{
			private static void Postfix(QuestPart_RequirementsToAcceptThroneRoom __instance, ref AcceptanceReport __result)
			{
				if (!__result.Accepted && cachedSettings.throneRoomTweaks
					&& __instance.forPawn != null && __instance.forTitle != null
					&& ThroneroomRequirementWaived(__instance.forPawn, __instance.forTitle.seniority))
				{
					__result = true;
				}
			}
		}

		[HarmonyPatch(typeof(JobDriver_BestowingCeremony))]
		[HarmonyPatch("AnalyzeThroneRoom")]
		private static class JobDriver_BestowingCeremony_AnalyzeThroneRoom_Patch
		{
			private static void Postfix(Pawn bestower, Pawn target, ref bool __result)
			{
				if (__result || !cachedSettings.throneRoomTweaks || target == null || target.royalty == null || bestower == null)
				{
					return;
				}
				RoyalTitleDef title = target.royalty.GetTitleAwardedWhenUpdating(bestower.Faction, target.royalty.GetFavor(bestower.Faction));
				if (title != null && ThroneroomRequirementWaived(target, title.seniority))
				{
					__result = true;
				}
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
			MigrateSettingsNamespace(content);
			this.settings = base.GetSettings<RoyaltyTweaksSettings>();
		}

		// Builds before the namespace rename serialized the settings class as
		// "TestMod.RoyaltyTweaksSettings" - the full type name lives in each user's
		// config file. Rewrite the old name in place BEFORE GetSettings reads the
		// file, so the rename is invisible: no startup error, no lost settings.
		// (Mod constructors run before any settings read and before the
		// StaticConstructorOnStartup pass, so this is early enough.)
		private static void MigrateSettingsNamespace(ModContentPack content)
		{
			try
			{
				// Mirrors LoadedModManager.GetSettingsFilename (private): the file is
				// keyed by the mod's folder name and this class's name.
				string file = Path.Combine(GenFilePaths.ConfigFolderPath,
					GenText.SanitizeFilename(string.Format("Mod_{0}_{1}.xml", content.FolderName, nameof(RoyaltyTweaksMod))));
				if (!File.Exists(file))
				{
					return;
				}
				string xml = File.ReadAllText(file);
				if (!xml.Contains("\"TestMod.RoyaltyTweaksSettings\""))
				{
					return; // already migrated (or a fresh install)
				}
				File.WriteAllText(file, xml.Replace(
					"\"TestMod.RoyaltyTweaksSettings\"",
					"\"Mobius.RoyaltyTweaks.RoyaltyTweaksSettings\""));
			}
			catch (Exception e)
			{
				// Worst case RimWorld logs one resolve error and falls back to the new
				// type - settings still load, so never let the migration itself throw.
				Log.Warning("[Royalty Tweaks] settings migration failed: " + e);
			}
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
